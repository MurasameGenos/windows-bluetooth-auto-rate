using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindowsBluetoothAutoRate;

public sealed partial class MainPage : Page
{
    private readonly AudioFormatOptimizer _optimizer = App.Optimizer;
    private bool _loadingStartupSetting;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
    }

    internal ObservableCollection<DeviceViewModel> Devices { get; } = [];

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _loadingStartupSetting = true;
        StartupToggle.IsOn = SettingsStore.IsStartupEnabled();
        SilentStartupToggle.IsOn = SettingsStore.IsSilentStartupEnabled();
        SilentStartupToggle.IsEnabled = StartupToggle.IsOn;
        _loadingStartupSetting = false;
        await LoadDevicesAsync();
    }

    private async Task LoadDevicesAsync()
    {
        SetStatus("正在读取 Windows 默认格式选项…", InfoBarSeverity.Informational);
        try
        {
            var devices = await Task.Run(() =>
            {
                var active = _optimizer.GetActiveDevices(probeSupportedFormats: true);
                foreach (var device in active)
                {
                    SettingsStore.RememberDevice(device);
                }

                var activeIds = active
                    .Select(device => device.Id)
                    .ToHashSet(StringComparer.Ordinal);
                return active
                    .Concat(SettingsStore.GetRememberedDevices()
                        .Where(device => !activeIds.Contains(device.Id)))
                    .OrderByDescending(device => device.IsConnected)
                    .ThenBy(device => device.Name, StringComparer.CurrentCulture)
                    .ToList();
            });

            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(new DeviceViewModel(
                    device,
                    SettingsStore.GetDeviceProfile(device.Id)));
            }

            var connected = devices.Count(device => device.IsConnected);
            SetStatus(
                devices.Count == 0
                    ? "没有设备记录。"
                    : $"共 {devices.Count} 台设备，{connected} 台已连接。",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            AppLog.Write($"读取设备失败：{exception}");
            SetStatus($"读取设备失败：{exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var device in Devices)
        {
            SettingsStore.SaveDeviceProfile(device.ToProfile());
        }

        SetStatus(
            $"设置已保存到 {SettingsStore.SettingsFilePath}",
            InfoBarSeverity.Success);
    }

    private async void ApplyDevice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DeviceViewModel device)
        {
            return;
        }

        var profile = device.ToProfile();
        SettingsStore.SaveDeviceProfile(profile);
        if (!device.IsConnected)
        {
            SetStatus(
                $"{device.Name} 未连接；设置已保存，将在下次连接时应用。",
                InfoBarSeverity.Informational);
            return;
        }

        SetStatus($"正在应用 {device.Name}…", InfoBarSeverity.Informational);
        var result = await Task.Run(() =>
            _optimizer.ApplyConfiguredDevice(
                device.DeviceId,
                profile with { Enabled = true }));
        AppLog.WriteSummary([result]);
        SetStatus(
            result.Message,
            result.Status == OptimizationStatus.Failed
                ? InfoBarSeverity.Error
                : InfoBarSeverity.Success);
        await LoadDevicesAsync();
    }

    private void ClearDevice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DeviceViewModel device)
        {
            return;
        }

        device.ClearConfiguration();
        SettingsStore.SaveDeviceProfile(device.ToProfile());
        SetStatus(
            $"已清除 {device.Name} 的自动调整配置，设备历史仍保留。",
            InfoBarSeverity.Success);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDevicesAsync();
    }

    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "重置全部设置？",
            Content = "这会清除设置文件、设备历史和开机启动设置。",
            PrimaryButtonText = "重置",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        SettingsStore.ResetAll();
        _loadingStartupSetting = true;
        StartupToggle.IsOn = false;
        SilentStartupToggle.IsOn = true;
        SilentStartupToggle.IsEnabled = false;
        _loadingStartupSetting = false;
        await LoadDevicesAsync();
        SetStatus("全部设置已重置。当前连接设备已重新登记。", InfoBarSeverity.Success);
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingStartupSetting)
        {
            return;
        }

        try
        {
            SettingsStore.SetStartupEnabled(StartupToggle.IsOn);
            SilentStartupToggle.IsEnabled = StartupToggle.IsOn;
            SetStatus(
                StartupToggle.IsOn ? "已启用开机启动。" : "已关闭开机启动。",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            _loadingStartupSetting = true;
            StartupToggle.IsOn = !StartupToggle.IsOn;
            SilentStartupToggle.IsEnabled = StartupToggle.IsOn;
            _loadingStartupSetting = false;
            SetStatus($"修改开机启动失败：{exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void SilentStartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loadingStartupSetting)
        {
            return;
        }

        try
        {
            SettingsStore.SetSilentStartupEnabled(SilentStartupToggle.IsOn);
            SetStatus(
                SilentStartupToggle.IsOn
                    ? "已启用静默启动；开机后只驻留系统托盘。"
                    : "已关闭静默启动；开机后会显示主窗口。",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            _loadingStartupSetting = true;
            SilentStartupToggle.IsOn = !SilentStartupToggle.IsOn;
            _loadingStartupSetting = false;
            SetStatus($"修改静默启动失败：{exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = SettingsStore.DataDirectory,
            UseShellExecute = true
        });
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        AppLog.EnsureExists();
        Process.Start(new ProcessStartInfo
        {
            FileName = AppLog.FilePath,
            UseShellExecute = true
        });
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
