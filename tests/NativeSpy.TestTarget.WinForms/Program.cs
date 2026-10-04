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
}
