using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using static WindowsBluetoothAutoRate.NativeAudio;

namespace WindowsBluetoothAutoRate;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class AudioDeviceWatcher : IMMNotificationClient, IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly ConcurrentDictionary<string, byte> _activeDeviceIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new(StringComparer.Ordinal);
    private bool _disposed;

    public AudioDeviceWatcher()
    {
        _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(MmDeviceEnumeratorClassId, throwOnError: true)!)!;
        RememberCurrentlyActiveDevices();
        ThrowIfFailed(_enumerator.RegisterEndpointNotificationCallback(this));
    }

    public event Action<string>? DeviceActivated;

    public int OnDeviceStateChanged(string deviceId, uint newState)
    {
        if ((newState & DeviceStateActive) != 0)
        {
            if (_activeDeviceIds.TryAdd(deviceId, 0))
            {
                ScheduleActivation(deviceId);
            }
        }
        else
        {
            _activeDeviceIds.TryRemove(deviceId, out _);
            CancelPending(deviceId);
        }

        return 0;
    }

    public int OnDeviceAdded(string deviceId)
    {
        ScheduleActivation(deviceId);
        return 0;
    }

    public int OnDeviceRemoved(string deviceId)
    {
        _activeDeviceIds.TryRemove(deviceId, out _);
        CancelPending(deviceId);
        return 0;
    }

    public int OnDefaultDeviceChanged(DataFlow flow, int role, string? defaultDeviceId)
    {
        return 0;
    }

    public int OnPropertyValueChanged(string deviceId, PropertyKey key)
    {
        return 0;
    }

    private void RememberCurrentlyActiveDevices()
    {
        IMMDeviceCollection? collection = null;
        try
        {
            ThrowIfFailed(_enumerator.EnumAudioEndpoints(
                DataFlow.Render,
                DeviceStateActive,
                out collection));
            ThrowIfFailed(collection.GetCount(out var count));

            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    ThrowIfFailed(collection.Item(index, out device));
                    ThrowIfFailed(device.GetId(out var id));
                    _activeDeviceIds.TryAdd(id, 0);
                }
                finally
                {
                    AudioFormatOptimizer.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            AudioFormatOptimizer.ReleaseComObject(collection);
        }
    }

    private void ScheduleActivation(string deviceId)
    {
        if (_disposed)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _pending.AddOrUpdate(
            deviceId,
            cancellation,
            (_, previous) =>
            {
                previous.Cancel();
                previous.Dispose();
                return cancellation;
            });

        _ = NotifyAfterDeviceSettlesAsync(deviceId, cancellation);
    }

    private async Task NotifyAfterDeviceSettlesAsync(
        string deviceId,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(1200, cancellation.Token);
            if (!_disposed && IsActiveRenderDevice(deviceId))
            {
                _activeDeviceIds.TryAdd(deviceId, 0);
                DeviceActivated?.Invoke(deviceId);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_pending.TryGetValue(deviceId, out var current) &&
                ReferenceEquals(current, cancellation))
            {
                _pending.TryRemove(deviceId, out _);
                cancellation.Dispose();
            }
        }
    }

    private static bool IsActiveRenderDevice(string deviceId)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(MmDeviceEnumeratorClassId, throwOnError: true)!)!;
            ThrowIfFailed(enumerator.EnumAudioEndpoints(
                DataFlow.Render,
                DeviceStateActive,
                out collection));
            ThrowIfFailed(collection.GetCount(out var count));

            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    ThrowIfFailed(collection.Item(index, out device));
                    ThrowIfFailed(device.GetId(out var id));
                    if (string.Equals(id, deviceId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                finally
                {
                    AudioFormatOptimizer.ReleaseComObject(device);
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            AppLog.Write($"确认新播放设备失败：{exception.Message}");
            return false;
        }
        finally
        {
            AudioFormatOptimizer.ReleaseComObject(collection);
            AudioFormatOptimizer.ReleaseComObject(enumerator);
        }
    }

    private void CancelPending(string deviceId)
    {
        if (_pending.TryRemove(deviceId, out var cancellation))
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var cancellation in _pending.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        _pending.Clear();
        _enumerator.UnregisterEndpointNotificationCallback(this);
        AudioFormatOptimizer.ReleaseComObject(_enumerator);
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }
}
