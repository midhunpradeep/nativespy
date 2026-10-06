using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace NativeSpy.TestTarget.Wpf;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        var window = new Window
        {
            Title = "NativeSpy WPF Test Target",
            Width = 520,
            Height = 320,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = CreateContent()
        };

        app.Run(window);
    }

    private static UIElement CreateContent()
    {
        var button = new Button
        {
            Width = 220,
            Height = 70,
            Content = "NativeSpy WPF child",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.LightSteelBlue
        };
        AutomationProperties.SetAutomationId(button, "NativeSpyWpfChildButton");
        AutomationProperties.SetName(button, "NativeSpy WPF child button");

        var grid = new Grid();
        grid.Children.Add(button);
        return grid;
    }
}
