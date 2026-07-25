using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowsBluetoothAutoRate;

internal sealed class DeviceViewModel : INotifyPropertyChanged
{
    private bool _enabled;
    private FormatOptionViewModel? _selectedFormat;

    public DeviceViewModel(AudioDeviceInfo device, DeviceProfile? profile)
    {
        Device = device;
        _enabled = profile?.Enabled ?? false;

        var formats = device.SupportedFormats
            .Append(device.CurrentFormat)
            .Distinct()
            .OrderBy(format => format.SampleRate)
            .ThenBy(format => format.ValidBits)
            .ThenBy(format => format.IsFloat)
            .Select(format => new FormatOptionViewModel(format))
            .ToList();
        FormatOptions = new ObservableCollection<FormatOptionViewModel>(formats);

        _selectedFormat =
            FindSavedFormat(formats, profile) ??
            formats.First(option => option.Format.Equals(device.CurrentFormat));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AudioDeviceInfo Device { get; }

    public string DeviceId => Device.Id;

    public string Name => Device.Name;

    public bool IsConnected => Device.IsConnected;

    public string ConnectionText => IsConnected ? "已连接" : "未连接";

    public string CurrentFormatText => IsConnected
        ? Device.CurrentFormat.ToString()
        : $"上次：{Device.CurrentFormat}";

    public ObservableCollection<FormatOptionViewModel> FormatOptions { get; }

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public FormatOptionViewModel? SelectedFormat
    {
        get => _selectedFormat;
        set => SetField(ref _selectedFormat, value);
    }

    public DeviceProfile ToProfile()
    {
        var format = SelectedFormat?.Format ?? Device.CurrentFormat;
        return new DeviceProfile(
            Device.Id,
            Device.Name,
            Enabled,
            format.Channels,
            format.SampleRate,
            format.ContainerBits,
            format.ValidBits,
            format.IsFloat,
            format.ChannelMask);
    }

    public void ClearConfiguration()
    {
        Enabled = false;
        SelectedFormat = FormatOptions.FirstOrDefault(option =>
            option.Format.Equals(Device.CurrentFormat));
    }

    private static FormatOptionViewModel? FindSavedFormat(
        IReadOnlyList<FormatOptionViewModel> formats,
        DeviceProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        if (profile.HasExactFormat)
        {
            return formats.FirstOrDefault(option =>
                option.Format.Channels == profile.Channels &&
                option.Format.SampleRate == profile.SampleRate &&
                option.Format.ContainerBits == profile.ContainerBits &&
                option.Format.ValidBits == profile.ValidBits &&
                option.Format.IsFloat == profile.IsFloat &&
                option.Format.ChannelMask == profile.ChannelMask);
        }

        return formats.FirstOrDefault(option =>
            option.Format.SampleRate == profile.SampleRate &&
            option.Format.ValidBits == profile.ValidBits);
    }

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class FormatOptionViewModel(AudioFormat format)
{
    public AudioFormat Format { get; } = format;

    public string DisplayText => Format.ToString();
}
