using System.Collections.Concurrent;
using System.Threading;
using Microsoft.UI.Xaml;

namespace WindowsBluetoothAutoRate;

public partial class App : Application
{
    private const string MutexName =
        @"Local\WindowsBluetoothAutoRate.SingleInstance";

    private readonly ConcurrentDictionary<string, byte> _runningDevices =
        new(StringComparer.Ordinal);
    private Mutex? _mutex;
    private AudioDeviceWatcher? _watcher;

    public App()
    {
        InitializeComponent();
    }

    internal static AudioFormatOptimizer Optimizer { get; } = new();

    internal MainWindow? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mutex = new Mutex(true, MutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            Exit();
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Activate();
        StartDeviceWatcher();

        if (args.Arguments.Contains("--background", StringComparison.OrdinalIgnoreCase))
        {
            MainWindow.HideToTray();
        }
    }

    internal void Shutdown()
    {
        _watcher?.Dispose();
        _watcher = null;
        MainWindow?.ClosePermanently();
        MainWindow = null;
        _mutex?.Dispose();
        _mutex = null;
        Exit();
    }

    private void StartDeviceWatcher()
    {
        try
        {
            var activeDevices = Optimizer.GetActiveDevices(probeSupportedFormats: true);
            foreach (var device in activeDevices)
            {
                SettingsStore.RememberDevice(device);
            }

            _watcher = new AudioDeviceWatcher();
            _watcher.DeviceActivated += OnDeviceActivated;
            AppLog.Write(
                $"设备监听已启动；已记忆 {activeDevices.Count} 台当前设备，不执行周期扫描。");
        }
        catch (Exception exception)
        {
            AppLog.Write($"设备监听启动失败：{exception}");
        }
    }

    private void OnDeviceActivated(string deviceId)
    {
        _ = ApplyInsertedDeviceAsync(deviceId);
    }

    private async Task ApplyInsertedDeviceAsync(string deviceId)
    {
        if (!_runningDevices.TryAdd(deviceId, 0))
        {
            return;
        }

        try
        {
            var profile = SettingsStore.GetDeviceProfile(deviceId);
            var info = await Task.Run(() =>
                Optimizer.GetActiveDeviceInfo(
                    deviceId,
                    probeSupportedFormats: true));
            if (info is not null)
            {
                SettingsStore.RememberDevice(info);
            }

            if (profile is not { Enabled: true })
            {
                AppLog.Write(
                    $"检测到设备但自动调整未启用，不修改格式。设备 ID：{deviceId}");
                return;
            }

            var result = await Task.Run(() =>
                Optimizer.ApplyConfiguredDevice(deviceId, profile));
            AppLog.WriteSummary([result]);
        }
        catch (Exception exception)
        {
            AppLog.Write($"处理新接入设备失败：{exception}");
        }
        finally
        {
            _runningDevices.TryRemove(deviceId, out _);
        }
    }
}
