using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml;

namespace WindowsBluetoothAutoRate;

public sealed partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        ShowWindowCommand = new DelegateCommand(ShowFromTray);
        ExitCommand = new DelegateCommand(() =>
            ((App)Application.Current).Shutdown());

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 720));
        RootFrame.Navigate(typeof(MainPage));
        Closed += MainWindow_Closed;
        TrayIcon.ForceCreate();
    }

    public ICommand ShowWindowCommand { get; }

    public ICommand ExitCommand { get; }

    public void ShowFromTray()
    {
        this.Show();
        Activate();
    }

    public void HideToTray()
    {
        this.Hide();
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        TrayIcon.Dispose();
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
}
