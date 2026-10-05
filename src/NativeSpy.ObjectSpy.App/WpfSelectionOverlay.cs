using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using NativeSpy.ObjectSpy;

namespace NativeSpy.ObjectSpy.App;

internal sealed class WpfSelectionOverlay : IObjectSpyOverlay
{
    private const int GwlExstyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly Window _window;
    private readonly Rectangle _rectangle;
    private long _generation;

    public WpfSelectionOverlay()
    {
        _rectangle = new Rectangle
        {
            Stroke = Brushes.DeepSkyBlue,
            StrokeThickness = 3,
            Fill = new SolidColorBrush(Color.FromArgb(30, 30, 144, 255)),
            IsHitTestVisible = false
        };
        var canvas = new Grid();
        canvas.Children.Add(_rectangle);
        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Content = canvas,
            IsHitTestVisible = false
        };
        _window.SourceInitialized += (_, _) => ConfigureWindowStyle();
    }

    public void ShowPreview(ObjectSpyOverlayGeometry geometry, long generation)
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.BeginInvoke(() => ShowPreview(geometry, generation));
            return;
        }

        if (generation < _generation)
        {
            return;
        }

        _generation = generation;
        var bounds = geometry.Bounds;
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        var handle = new WindowInteropHelper(_window).Handle;
        SetWindowPos(
            handle,
            HwndTopmost,
            bounds.Left,
            bounds.Top,
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height),
            SwpNoActivate | SwpNoOwnerZOrder | SwpShowWindow);
        _window.UpdateLayout();
    }

    public void Clear(long generation)
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.BeginInvoke(() => Clear(generation));
            return;
        }

        if (generation < _generation)
        {
            return;
        }

        _generation = generation;
        if (_window.IsVisible)
        {
            _window.Hide();
        }
    }

    private void ConfigureWindowStyle()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        var style = GetWindowLongPtr(handle, GwlExstyle).ToInt64();
        style |= WsExNoActivate | WsExTransparent | WsExToolWindow;
        SetWindowLongPtr(handle, GwlExstyle, new IntPtr(style));
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == 0x0084) // WM_NCHITTEST
        {
            handled = true;
            return new IntPtr(-1); // HTTRANSPARENT
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
