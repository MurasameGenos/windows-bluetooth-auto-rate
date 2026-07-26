using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace WindowsBluetoothAutoRate;

public sealed partial class MainWindow : Window
{
    private const int HideWindow = 0;
    private const int ShowWindowNormally = 5;

    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 720));
        RootFrame.Navigate(typeof(MainPage));
        Closed += MainWindow_Closed;
    }

    public void ShowFromTray()
    {
        ShowWindow(
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            ShowWindowNormally);
        Activate();
    }

    public void HideToTray()
    {
        ShowWindow(
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            HideWindow);
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Handled = true;
        HideToTray();
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
