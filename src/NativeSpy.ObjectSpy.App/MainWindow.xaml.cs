using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NativeSpy.Client.NamedPipes;
using NativeSpy.FlaUI;
using NativeSpy.ObjectSpy;
using NativeSpy.Protocol.Clr;
using NativeSpy.Protocol.Common;
using NativeSpy.Protocol.Correlation;

namespace NativeSpy.ObjectSpy.App;

public partial class MainWindow : Window
{
    private readonly WpfSelectionOverlay _overlay = new();
    private DispatcherTimer? _finderTimer;
    private ControlledTargetSession? _target;
    private FlaUiAutomationSession? _flaUi;
    private ObjectSpyCoordinator? _coordinator;
    private System.Drawing.Point _lastScreenPoint;
    private int _finderActive;
    private int _previewBusy;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ConnectionText.Text = "Starting controlled target...";
            _target = await Task.Run(ControlledTargetSession.Start).ConfigureAwait(true);
            ConnectionText.Text = "Attaching UIA3...";
            _flaUi = await Task.Run(() => FlaUiAutomationSession.Attach(_target.ProcessIdentity))
                .ConfigureAwait(true);
            _coordinator = new ObjectSpyCoordinator(
                _target.ProcessIdentity,
                _flaUi,
                _target.Session,
                _target.Session,
                _overlay);
            _coordinator.StateChanged += CoordinatorOnStateChanged;
            _finderTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _finderTimer.Tick += FinderTimerOnTick;
            ConnectionText.Text = $"Connected to PID {_target.ProcessIdentity.ProcessId}";
            RenderState();
        }
        catch (Exception exception)
        {
            ConnectionText.Text = "Target unavailable";
            ErrorText.Text = exception.Message;
        }
    }

    private void FinderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_coordinator is null)
        {
            return;
        }

        _finderActive = _finderActive == 0 ? 1 : 0;
        if (_finderActive != 0)
        {
            FinderButton.Content = "Stop finder";
            FixButton.IsEnabled = true;
            _finderTimer?.Start();
        }
        else
        {
            FinderButton.Content = "Activate finder";
            FixButton.IsEnabled = false;
            _finderTimer?.Stop();
            _coordinator.AbandonPreview();
        }
    }

    private async void FixButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_coordinator is null || _finderActive == 0)
        {
            return;
        }

        _finderActive = 0;
        _finderTimer?.Stop();
        FinderButton.Content = "Activate finder";
        FixButton.IsEnabled = false;
        try
        {
            await _coordinator.FreezeAsync(_lastScreenPoint);
        }
        finally
        {
            FixButton.IsEnabled = true;
        }
    }

    private void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        _coordinator?.Back();
    }

    private async void FinderTimerOnTick(object? sender, EventArgs e)
    {
        if (_coordinator is null || _finderActive == 0 || Interlocked.Exchange(ref _previewBusy, 1) != 0)
        {
            return;
        }

        try
        {
            if (GetCursorPos(out var point))
            {
                _lastScreenPoint = new System.Drawing.Point(point.X, point.Y);
                await _coordinator.PreviewAsync(_lastScreenPoint).ConfigureAwait(true);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _previewBusy, 0);
        }
    }

    private void CoordinatorOnStateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RenderState);
            return;
        }

        RenderState();
    }

    private void RenderState()
    {
        var state = _coordinator?.State;
        if (state is null)
        {
            StateText.Text = "No target session";
            return;
        }

        StateText.Text = $"Selection: {state.SelectionState}; CLR: {state.ClrState}";
        var observation = state.PreviewObservation ?? state.SelectionObservation;
        NameText.Text = observation?.Name ?? "—";
        ControlTypeText.Text = observation?.ControlType ?? "—";
        AutomationIdText.Text = observation?.AutomationId ?? "—";
        ClassNameText.Text = observation?.ClassName ?? "—";
        CandidateHwndText.Text = FormatHwnd(observation?.CandidateHwnd);
        RootHwndText.Text = FormatHwnd(observation?.RootHwnd);
        ProcessIdText.Text = observation?.CandidateProcessId?.ToString() ?? "—";

        CorrelationText.Text = state.Correlation is null
            ? "No correlation yet."
            : $"{state.Correlation.Status} / {state.Correlation.Direction}"
              + (state.ExternalEvidence is null
                  ? string.Empty
                  : $"\nObservation: {state.ExternalEvidence.Source.ObservationId}\nCapture: {state.ExternalEvidence.Source.CaptureId}");
        ManagedTypeText.Text = state.Description?.Object.TypeIdentity?.FullName is { } typeName
            ? typeName
            : "CLR type unavailable until Exact correlation.";
        BackButton.IsEnabled = state.NavigationDepth > 0;
        ErrorText.Text = state.Error ?? observation?.Limitation ?? string.Empty;
        RenderClrPanel(state);
    }

    private void RenderClrPanel(ObjectSpyViewState state)
    {
        ClrPanel.Children.Clear();
        if (state.ClrState != ObjectSpyClrState.Ready)
        {
            ClrPanel.Children.Add(new TextBlock
            {
                Text = state.ClrState == ObjectSpyClrState.Unavailable
                    ? "CLR inspection is available only after Exact correlation."
                    : $"CLR inspection: {state.ClrState}"
            });
            return;
        }

        if (state.NextContinuationToken is not null)
        {
            var moreButton = new Button
            {
                Content = "Load next member page",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6)
            };
            moreButton.Click += LoadNextMemberPageButtonOnClick;
            ClrPanel.Children.Add(moreButton);
        }

        AddHeading("Public instance fields");
        var fieldResults = state.FieldResults.ToDictionary(result => result.Member.MemberId, StringComparer.Ordinal);
        foreach (var member in state.Members.Where(member => member.Kind == ClrMemberKind.Field))
        {
            fieldResults.TryGetValue(member.Member.MemberId, out var result);
            AddMemberRow(member, result, allowFollow: true);
        }

        AddHeading("Readable public properties");
        foreach (var member in state.Members.Where(member => member.Kind == ClrMemberKind.Property))
        {
            var result = state.LastReadResult?.Member.MemberId == member.Member.MemberId
                ? state.LastReadResult
                : null;
            AddMemberRow(member, result, allowFollow: true);
        }
    }

    private void AddHeading(string text)
    {
        ClrPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4)
        });
    }

    private void AddMemberRow(MemberDescriptorDto member, MemberReadResultDto? result, bool allowFollow)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        var line = new DockPanel();
        line.Children.Add(new TextBlock
        {
            Text = member.DisplaySignature ?? member.Name,
            ToolTip = member.ValueType.TypeId,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (member.Kind == ClrMemberKind.Property)
        {
            var readButton = new Button
            {
                Content = "Read",
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(8, 1, 8, 1),
                Tag = member
            };
            readButton.Click += ReadPropertyButtonOnClick;
            DockPanel.SetDock(readButton, Dock.Right);
            line.Children.Add(readButton);
        }
        panel.Children.Add(line);

        if (result is not null)
        {
            panel.Children.Add(CreateResultPanel(result, allowFollow));
        }

        ClrPanel.Children.Add(panel);
    }

    private FrameworkElement CreateResultPanel(MemberReadResultDto result, bool allowFollow)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var text = new TextBlock
        {
            Text = FormatResult(result),
            Margin = new Thickness(16, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(text);
        if (allowFollow
            && result.Outcome == ClrReadOutcome.Available
            && result.Value?.Kind == ClrValueKind.ObjectReference)
        {
            var follow = new Button
            {
                Content = "Follow",
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(8, 1, 8, 1),
                Tag = result.Value
            };
            follow.Click += FollowButtonOnClick;
            panel.Children.Add(follow);
        }

        return panel;
    }

    private async void LoadNextMemberPageButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (_coordinator is not null)
        {
            await _coordinator.LoadNextMemberPageAsync();
        }
    }

    private async void ReadPropertyButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MemberDescriptorDto member } && _coordinator is not null)
        {
            await _coordinator.ReadPropertyAsync(member);
        }
    }

    private async void FollowButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ClrValueDto value } && _coordinator is not null)
        {
            await _coordinator.FollowObjectReferenceAsync(value);
        }
    }

    private static string FormatResult(MemberReadResultDto result)
    {
        return result.Outcome switch
        {
            ClrReadOutcome.Available => FormatValue(result.Value!),
            ClrReadOutcome.TargetFailed => $"TargetFailed: {result.TargetException?.ExceptionType.FullName ?? "unknown exception"}",
            _ => result.ErrorCode?.ToString() ?? result.Outcome.ToString()
        };
    }

    private static string FormatValue(ClrValueDto value)
    {
        return value.Kind switch
        {
            ClrValueKind.Null => "null",
            ClrValueKind.Boolean => value.BooleanValue!.Value ? "true" : "false",
            ClrValueKind.Integer => value.IntegerValue!,
            ClrValueKind.FloatingPoint => value.FloatingPointDisplay ?? value.FloatingPointBits!,
            ClrValueKind.Decimal => value.DecimalDisplay ?? (value.DecimalBits is null ? "decimal" : string.Join(",", value.DecimalBits)),
            ClrValueKind.Char => $"U+{value.CharCodeUnit!.Value:X4}",
            ClrValueKind.String => value.StringTruncated == true ? $"\"{value.StringValue}\" (truncated)" : $"\"{value.StringValue}\"",
            ClrValueKind.Guid => value.GuidValue!,
            ClrValueKind.DateTime => $"{value.DateTimeTicks} ({value.DateTimeKind})",
            ClrValueKind.DateTimeOffset => $"{value.DateTimeOffsetClockTicks} ({value.DateTimeOffsetOffsetMinutes}m)",
            ClrValueKind.TimeSpan => value.TimeSpanTicks!.Value.ToString(),
            ClrValueKind.Enum => value.EnumName ?? value.EnumUnderlyingValue!,
            ClrValueKind.TypeObject => value.RepresentedType?.FullName ?? "System.Type",
            ClrValueKind.ValueType => value.ValueType?.FullName ?? "value type",
            ClrValueKind.ObjectReference => $"ObjectReference ({value.ObjectType?.FullName ?? "object"})",
            _ => value.Kind.ToString()
        };
    }

    private static string FormatHwnd(ulong? hwnd)
    {
        return hwnd is null ? "—" : $"0x{hwnd.Value:X}";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _finderTimer?.Stop();
        _coordinator?.DisposeAsync().GetAwaiter().GetResult();
        _flaUi?.Dispose();
        _target?.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
