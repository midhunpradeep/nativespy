using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using NativeSpy.Agent.WinForms;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.TestTarget.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var handleTable = new TestManagedHandleTable();
        var form = new MainForm();
        var adapter = new WinFormsCurrentHwndAdapter(
            handleTable.Issue,
            handleTable.Resolve,
            handleTable.GetTypeId);
        var bridge = new TestTargetBridge(form, adapter, handleTable);
        form.Shown += (_, _) => bridge.SignalReady();
        bridge.Start();
        Application.Run(form);
    }
}

internal sealed class MainForm : Form
{
    public MainForm()
    {
        Name = "NativeSpyTestForm";
        Text = "NativeSpy Test Target";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 180);

        TestButton = new Button
        {
            Name = "NativeSpyTestButton",
            Text = "NativeSpy Test Button",
            AccessibleName = "NativeSpy Test Button",
            AutoSize = true,
            Location = new Point(120, 60)
        };
        Controls.Add(TestButton);
    }

    public Button TestButton { get; }
}

internal sealed class TestManagedHandleTable
{
    private const string SessionId = "i1-test-session";
    private const string BoundaryId = "i1-test-target";
    private readonly Dictionary<object, HandleRefDto> _handlesByObject =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, IssuedHandle> _objectsByHandle = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _typeIds = new();
    private int _nextHandleId;
    private int _nextTypeId;

    public HandleRefDto Issue(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_handlesByObject.TryGetValue(target, out var existing))
        {
            return existing;
        }

        var handle = new HandleRefDto(
            SessionId,
            $"test-object-{++_nextHandleId}",
            generation: 1,
            HandleKind.ClrObject,
            BoundaryId);
        _handlesByObject.Add(target, handle);
        _objectsByHandle.Add(handle.HandleId, new IssuedHandle(handle, target));
        return handle;
    }

    public string GetTypeId(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_typeIds.TryGetValue(type, out var existing))
        {
            return existing;
        }

        var typeId = $"i1-type-{++_nextTypeId}";
        _typeIds.Add(type, typeId);
        return typeId;
    }

    public object? Resolve(HandleRefDto handle)
    {
        if (!_objectsByHandle.TryGetValue(handle?.HandleId ?? string.Empty, out var issued)
            || !IsValidHandle(handle, issued.Handle))
        {
            return null;
        }

        return issued.Target;
    }

    public string ReadButtonText(HandleRefDto handle)
    {
        var target = Resolve(handle)
            ?? throw new InvalidOperationException("The test handle does not resolve to a live target object.");
        if (target is not Button button)
        {
            throw new InvalidOperationException(
                $"The resolved target type was '{target.GetType().FullName}', not '{typeof(Button).FullName}'.");
        }

        return button.Text;
    }

    private static bool IsValidHandle(HandleRefDto? supplied, HandleRefDto issued)
    {
        return supplied is not null
            && string.Equals(supplied.SessionId, issued.SessionId, StringComparison.Ordinal)
            && string.Equals(supplied.HandleId, issued.HandleId, StringComparison.Ordinal)
            && supplied.Generation == issued.Generation
            && supplied.Kind == issued.Kind
            && string.Equals(supplied.BoundaryId, issued.BoundaryId, StringComparison.Ordinal)
            && string.Equals(supplied.SessionId, SessionId, StringComparison.Ordinal)
            && string.Equals(supplied.BoundaryId, BoundaryId, StringComparison.Ordinal)
            && supplied.Kind == HandleKind.ClrObject
            && supplied.Generation > 0
            && !string.IsNullOrWhiteSpace(supplied.HandleId);
    }

    private sealed record IssuedHandle(HandleRefDto Handle, object Target);
}

internal sealed class TestTargetBridge
{
    private const int MaximumLineLength = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly MainForm _form;
    private readonly WinFormsCurrentHwndAdapter _adapter;
    private readonly TestManagedHandleTable _handleTable;
    private readonly object _outputGate = new();
    private volatile bool _stopping;
    private Thread? _readerThread;

    public TestTargetBridge(
        MainForm form,
        WinFormsCurrentHwndAdapter adapter,
        TestManagedHandleTable handleTable)
    {
        _form = form;
        _adapter = adapter;
        _handleTable = handleTable;
    }

    public void Start()
    {
        _readerThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "NativeSpy I1 test bridge reader"
        };
        _readerThread.Start();
    }

    public void SignalReady()
    {
        Write(new ReadyMessage("ready", Environment.ProcessId, ToHwnd(_form.Handle)));
    }

    private void ReadLoop()
    {
        try
        {
            while (!_stopping)
            {
                string? line;
                try
                {
                    line = ReadBoundedLine();
                }
                catch (InvalidDataException exception)
                {
                    Console.Error.WriteLine($"I1 test bridge rejected an oversized request: {exception.Message}");
                    continue;
                }

                if (line is null)
                {
                    break;
                }

                HandleLine(line);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"I1 test bridge reader failed: {exception}");
        }
    }

    private void HandleLine(string line)
    {
        BridgeRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(line, JsonOptions);
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"I1 test bridge received malformed JSON: {exception.Message}");
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Command))
        {
            Console.Error.WriteLine("I1 test bridge received an invalid request.");
            return;
        }

        try
        {
            switch (request.Command)
            {
                case "begin":
                    Write(ExecuteCorrelation(request, revalidate: false));
                    break;
                case "revalidate":
                    Write(ExecuteCorrelation(request, revalidate: true));
                    break;
                case "readText":
                    Write(ExecuteReadText(request));
                    break;
                case "shutdown":
                    Write(new BridgeResponse(request.Id, true, null, null, null));
                    _stopping = true;
                    InvokeOnUi(() =>
                    {
                        _form.Close();
                        return 0;
                    });
                    break;
                default:
                    Write(new BridgeResponse(
                        request.Id,
                        false,
                        $"Unknown command '{request.Command}'.",
                        null,
                        null));
                    break;
            }
        }
        catch (Exception exception)
        {
            Write(new BridgeResponse(request.Id, false, exception.Message, null, null));
        }
    }

    private BridgeResponse ExecuteCorrelation(BridgeRequest request, bool revalidate)
    {
        if (request.Hwnd is null or 0)
        {
            return new BridgeResponse(request.Id, false, "A positive HWND is required.", null, null);
        }

        if (revalidate && request.CandidateHandle is null)
        {
            return new BridgeResponse(request.Id, false, "A candidate handle is required for revalidation.", null, null);
        }

        var wire = InvokeOnUi(() =>
        {
            FrameworkCorrelationEvidenceDto evidence;
            if (revalidate)
            {
                evidence = _adapter.RevalidateCurrentHwnd(
                    request.Hwnd.Value,
                    ToHandle(request.CandidateHandle!));
            }
            else
            {
                evidence = _adapter.BeginCurrentHwnd(request.Hwnd.Value);
            }

            return ToWire(evidence);
        });
        return new BridgeResponse(request.Id, true, null, wire, null);
    }

    private BridgeResponse ExecuteReadText(BridgeRequest request)
    {
        if (request.CandidateHandle is null)
        {
            return new BridgeResponse(request.Id, false, "A candidate handle is required.", null, null);
        }

        var text = InvokeOnUi(() => _handleTable.ReadButtonText(ToHandle(request.CandidateHandle)));
        return new BridgeResponse(request.Id, true, null, null, text);
    }

    private T InvokeOnUi<T>(Func<T> callback)
    {
        return (T)_form.Invoke(callback)!;
    }

    private void Write<T>(T message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        if (json.Length > MaximumLineLength)
        {
            throw new InvalidOperationException("The test bridge response exceeded its line-size bound.");
        }

        lock (_outputGate)
        {
            Console.Out.WriteLine(json);
            Console.Out.Flush();
        }
    }

    private static string? ReadBoundedLine()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var value = Console.In.Read();
            if (value < 0)
            {
                return buffer.Length == 0 ? null : buffer.ToString();
            }

            if (value == '\n')
            {
                return buffer.ToString();
            }

            if (value == '\r')
            {
                continue;
            }

            if (buffer.Length == MaximumLineLength)
            {
                while (value >= 0 && value != '\n')
                {
                    value = Console.In.Read();
                }

                throw new InvalidDataException("The request line exceeded the configured bound.");
            }

            buffer.Append((char)value);
        }
    }

    private static TargetEvidenceWire ToWire(FrameworkCorrelationEvidenceDto evidence)
    {
        return new TargetEvidenceWire(
            evidence.AdapterId,
            evidence.ProcessId,
            ToWire(evidence.CandidateTarget),
            evidence.EvidenceFacts
                .Select(fact => new FactWire(fact.Name, (int)fact.Outcome, (int)fact.EvidenceKind, fact.Detail))
                .ToArray(),
            evidence.ValidationFacts
                .Select(fact => new ValidationWire(fact.Name, (int)fact.Outcome, fact.Detail))
                .ToArray(),
            ToWire(evidence.Effects),
            evidence.AdapterMetadata.Select(ToWire).ToArray(),
            evidence.Limitations.Select(ToWire).ToArray(),
            evidence.OperationError is null
                ? null
                : new ErrorWire((int)evidence.OperationError.Code, evidence.OperationError.Message));
    }

    private static TargetWire? ToWire(CorrelationTargetRefDto? target)
    {
        if (target is null)
        {
            return null;
        }

        return new TargetWire(
            (int)target.TargetKind,
            target.Managed is null ? null : ToWire(target.Managed));
    }

    private static ManagedObjectWire ToWire(ManagedObjectRefDto managed)
    {
        return new ManagedObjectWire(
            ToWire(managed.Handle),
            managed.TypeIdentity is null ? null : ToWire(managed.TypeIdentity),
            managed.BoundaryId,
            managed.ContextId);
    }

    private static TypeIdentityWire ToWire(TypeIdentityDto type)
    {
        return new TypeIdentityWire(
            type.TypeId,
            type.FullName,
            type.AssemblySimpleName,
            type.AssemblyVersion,
            type.AssemblyCulture,
            type.PublicKeyToken,
            type.ModuleVersionId,
            type.BoundaryId,
            type.DeclaringType is null ? null : ToWire(type.DeclaringType),
            type.GenericDefinition is null ? null : ToWire(type.GenericDefinition),
            type.GenericArguments.Select(ToWire).ToArray(),
            type.ArrayRank,
            type.ArrayShape?.ToArray(),
            type.PointerElementType is null ? null : ToWire(type.PointerElementType),
            type.ByRefElementType is null ? null : ToWire(type.ByRefElementType),
            type.NullableUnderlyingType is null ? null : ToWire(type.NullableUnderlyingType),
            type.IsValueType,
            type.BaseType is null ? null : ToWire(type.BaseType),
            type.Interfaces.Select(ToWire).ToArray(),
            type.DynamicIdentity);
    }

    private static TypeRefWire ToWire(TypeRefDto type)
    {
        return new TypeRefWire(type.TypeId, type.BoundaryId);
    }

    private static EffectWire ToWire(CorrelationEffectSummaryDto effects)
    {
        return new EffectWire(
            effects.Categories.Select(category => (int)category).ToArray(),
            (int)effects.FrameworkState,
            (int)effects.ApplicationCallbacks,
            effects.CallbackDetails
                .Select(detail => new CallbackWire(detail.Name, detail.Count, detail.CountKnown))
                .ToArray(),
            (int)effects.VisibleMutation,
            effects.Operations.ToArray());
    }

    private static MetadataWire ToWire(AdapterMetadataDto metadata)
    {
        return new MetadataWire(
            metadata.AdapterId,
            metadata.SchemaId,
            metadata.SchemaVersion,
            ToWire(metadata.Payload));
    }

    private static MetadataValueWire ToWire(DetachedMetadataValueDto value)
    {
        return new MetadataValueWire(
            (int)value.Kind,
            value.BooleanValue,
            value.IntegerValue,
            value.DecimalValue,
            value.FloatingPointValue,
            value.StringValue,
            value.ArrayValue?.Select(ToWire).ToArray(),
            value.ObjectValue?
                .Select(property => new MetadataPropertyWire(property.Name, ToWire(property.Value)))
                .ToArray());
    }

    private static LimitationWire ToWire(CorrelationLimitationDto limitation)
    {
        return new LimitationWire(limitation.Code, limitation.Detail);
    }

    private static HandleWire ToWire(HandleRefDto handle)
    {
        return new HandleWire(
            handle.SessionId,
            handle.HandleId,
            handle.Generation,
            (int)handle.Kind,
            handle.BoundaryId);
    }

    private static HandleRefDto ToHandle(HandleWire wire)
    {
        return new HandleRefDto(
            wire.SessionId,
            wire.HandleId,
            wire.Generation,
            (HandleKind)wire.Kind,
            wire.BoundaryId);
    }

    private static ulong ToHwnd(IntPtr handle)
    {
        return unchecked((ulong)handle.ToInt64());
    }

    private sealed record ReadyMessage(string Kind, int ProcessId, ulong Hwnd);

    private sealed record BridgeRequest(
        string Id,
        string Command,
        ulong? Hwnd = null,
        HandleWire? CandidateHandle = null);

    private sealed record BridgeResponse(
        string Id,
        bool Ok,
        string? Error,
        TargetEvidenceWire? Evidence,
        string? Text);

    private sealed record TargetEvidenceWire(
        string AdapterId,
        int ProcessId,
        TargetWire? CandidateTarget,
        FactWire[] EvidenceFacts,
        ValidationWire[] ValidationFacts,
        EffectWire Effects,
        MetadataWire[] AdapterMetadata,
        LimitationWire[] Limitations,
        ErrorWire? Error);

    private sealed record TargetWire(int TargetKind, ManagedObjectWire? Managed);

    private sealed record ManagedObjectWire(
        HandleWire Handle,
        TypeIdentityWire? TypeIdentity,
        string? BoundaryId,
        string? ContextId);

    private sealed record TypeIdentityWire(
        string TypeId,
        string FullName,
        string AssemblySimpleName,
        string? AssemblyVersion,
        string? AssemblyCulture,
        string? PublicKeyToken,
        string? ModuleVersionId,
        string BoundaryId,
        TypeRefWire? DeclaringType,
        TypeRefWire? GenericDefinition,
        TypeRefWire[] GenericArguments,
        int? ArrayRank,
        int[]? ArrayShape,
        TypeRefWire? PointerElementType,
        TypeRefWire? ByRefElementType,
        TypeRefWire? NullableUnderlyingType,
        bool IsValueType,
        TypeRefWire? BaseType,
        TypeRefWire[] Interfaces,
        string? DynamicIdentity);

    private sealed record TypeRefWire(string TypeId, string BoundaryId);

    private sealed record EffectWire(
        int[] Categories,
        int FrameworkState,
        int ApplicationCallbacks,
        CallbackWire[] CallbackDetails,
        int VisibleMutation,
        string[] Operations);

    private sealed record CallbackWire(string Name, int? Count, bool CountKnown);

    private sealed record MetadataWire(
        string AdapterId,
        string SchemaId,
        int SchemaVersion,
        MetadataValueWire Payload);

    private sealed record MetadataValueWire(
        int Kind,
        bool? BooleanValue,
        long? IntegerValue,
        decimal? DecimalValue,
        double? FloatingPointValue,
        string? StringValue,
        MetadataValueWire[]? ArrayValue,
        MetadataPropertyWire[]? ObjectValue);

    private sealed record MetadataPropertyWire(string Name, MetadataValueWire Value);

    private sealed record LimitationWire(string Code, string? Detail);

    private sealed record FactWire(string Name, int Outcome, int EvidenceKind, string? Detail);

    private sealed record ValidationWire(string Name, int Outcome, string? Detail);

    private sealed record ErrorWire(int Code, string? Message);

    private sealed record HandleWire(
        string SessionId,
        string HandleId,
        long Generation,
        int Kind,
        string? BoundaryId);
}
