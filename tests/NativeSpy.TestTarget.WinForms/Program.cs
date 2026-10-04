using System.Windows.Forms;
using NativeSpy.Agent.Host;
using NativeSpy.Agent.Host.WinForms;
using NativeSpy.Protocol.Json;
using NativeSpy.Transport.NamedPipes;

namespace NativeSpy.TestTarget.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new MainForm();
        var processIdentity = ProcessIdentityReader.ReadCurrent();
        var options = AgentHostOptions.CreateDefault(
            processIdentity,
            NamedPipeSecurity.GetCurrentUserSid());
        var host = new AgentHost(
            options,
            new WinFormsHostCompositionFactory(form));

        host.StartAsync().GetAwaiter().GetResult();
        form.Shown += (_, _) => PublishBootstrap(host);
        try
        {
            Application.Run(form);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
            host.DisposeAsync().GetAwaiter().GetResult();
        }
    }

    private static void PublishBootstrap(AgentHost host)
    {
        var json = ProtocolJsonCodec.SerializeBootstrap(host.BootstrapDescriptor);
        Console.Out.WriteLine(System.Text.Encoding.UTF8.GetString(json));
        Console.Out.Flush();
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

    public string PublicText = "public field";
    public int PublicNumber = 42;
    public decimal PublicDecimal = 12.5m;
    public InspectableChild PublicReference = new();
    public InspectableStruct PublicStruct = new(7, "struct value");
    public InspectableEnum PublicEnum = InspectableEnum.Ready;
    public string LongPublicText = new('x', 5_000);

    public int GetterReads { get; private set; }

    public string CountingProperty
    {
        get
        {
            GetterReads++;
            return "getter value";
        }
    }

    public InspectableChild ChildProperty => PublicReference;

    public string ThrowingProperty => throw new InvalidOperationException("target-only detail");
}

public sealed class InspectableChild
{
    public string ChildText = "child field";
    public int ChildNumber = 7;
}

public enum InspectableEnum
{
    Ready = 7,
    Other = 9
}

public readonly struct InspectableStruct
{
    public InspectableStruct(int number, string text)
    {
        Number = number;
        Text = text;
    }

    public readonly int Number;
    public readonly string Text;
}
