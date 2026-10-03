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
        var adapter = new WinFormsCurrentHwndAdapter(handleTable.Issue, handleTable.Resolve);
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
    private readonly Dictionary<string, object> _objectsByHandle = new(StringComparer.Ordinal);
    private int _nextHandleId;

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
        _objectsByHandle.Add(handle.HandleId, target);
        return handle;
    }

    public object? Resolve(HandleRefDto handle)
    {
        if (!IsValidHandle(handle))
        {
            return null;
        }

        return _objectsByHandle.TryGetValue(handle.HandleId, out var target)
            ? target
            : null;
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

    private static bool IsValidHandle(HandleRefDto? handle)
    {
        return handle is not null
            && string.Equals(handle.SessionId, SessionId, StringComparison.Ordinal)
            && string.Equals(handle.BoundaryId, BoundaryId, StringComparison.Ordinal)
            && handle.Kind == HandleKind.ClrObject
            && handle.Generation > 0
            && !string.IsNullOrWhiteSpace(handle.HandleId);
    }
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
                    Write(new BridgeResponse(request.Id, true, null, null, null, null));
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
                        null,
                        null));
                    break;
            }
        }
        catch (Exception exception)
        {
            Write(new BridgeResponse(request.Id, false, exception.Message, null, null, null));
        }
    }

    private BridgeResponse ExecuteCorrelation(BridgeRequest request, bool revalidate)
    {
        if (request.Hwnd is null or 0)
        {
            return new BridgeResponse(request.Id, false, "A positive HWND is required.", null, null, null);
        }

        if (revalidate && request.CandidateHandle is null)
        {
            return new BridgeResponse(request.Id, false, "A candidate handle is required for revalidation.", null, null, null);
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
        return new BridgeResponse(request.Id, true, null, wire, null, null);
    }

    private BridgeResponse ExecuteReadText(BridgeRequest request)
    {
        if (request.CandidateHandle is null)
        {
            return new BridgeResponse(request.Id, false, "A candidate handle is required.", null, null, null);
        }

        var text = InvokeOnUi(() => _handleTable.ReadButtonText(ToHandle(request.CandidateHandle)));
        return new BridgeResponse(request.Id, true, null, null, text, null);
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
        HandleWire? candidateHandle = null;
        string? typeId = null;
        string? typeFullName = null;
        string? assemblySimpleName = null;
        string? boundaryId = null;
        if (evidence.CandidateTarget?.Managed is { } managed)
        {
            candidateHandle = ToWire(managed.Handle);
            typeId = managed.TypeIdentity?.TypeId;
            typeFullName = managed.TypeIdentity?.FullName;
            assemblySimpleName = managed.TypeIdentity?.AssemblySimpleName;
            boundaryId = managed.TypeIdentity?.BoundaryId;
        }

        return new TargetEvidenceWire(
            evidence.AdapterId,
            evidence.ProcessId,
            candidateHandle,
            typeId,
            typeFullName,
            assemblySimpleName,
            boundaryId,
            evidence.EvidenceFacts
                .Select(fact => new FactWire(fact.Name, (int)fact.Outcome, (int)fact.EvidenceKind, fact.Detail))
                .ToArray(),
            evidence.ValidationFacts
                .Select(fact => new ValidationWire(fact.Name, (int)fact.Outcome, fact.Detail))
                .ToArray(),
            evidence.OperationError is null
                ? null
                : new ErrorWire((int)evidence.OperationError.Code, evidence.OperationError.Message));
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
        string? Text,
        object? Reserved);

    private sealed record TargetEvidenceWire(
        string AdapterId,
        int ProcessId,
        HandleWire? CandidateHandle,
        string? TypeId,
        string? TypeFullName,
        string? AssemblySimpleName,
        string? BoundaryId,
        FactWire[] EvidenceFacts,
        ValidationWire[] ValidationFacts,
        ErrorWire? Error);

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
