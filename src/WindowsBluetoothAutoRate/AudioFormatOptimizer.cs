using System.Runtime.InteropServices;
using static WindowsBluetoothAutoRate.NativeAudio;

namespace WindowsBluetoothAutoRate;

internal sealed class AudioFormatOptimizer
{
    public IReadOnlyList<AudioDeviceInfo> GetActiveDevices(bool probeSupportedFormats = true)
    {
        var devices = new List<AudioDeviceInfo>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;

        try
        {
            enumerator = CreateEnumerator();
            ThrowIfFailed(enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceStateActive, out collection));
            ThrowIfFailed(collection.GetCount(out var count));

            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    ThrowIfFailed(collection.Item(index, out device));
                    var info = ReadDeviceInfo(device, probeSupportedFormats);
                    if (info is not null)
                    {
                        devices.Add(info);
                    }
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }

            return devices;
        }
        finally
        {
            ReleaseComObject(collection);
            ReleaseComObject(enumerator);
        }
    }

    public AudioDeviceInfo? GetActiveDeviceInfo(
        string deviceId,
        bool probeSupportedFormats = true)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;

        try
        {
            enumerator = CreateEnumerator();
            ThrowIfFailed(enumerator.GetDevice(deviceId, out device));
            ThrowIfFailed(device.GetState(out var state));
            return (state & DeviceStateActive) != 0
                ? ReadDeviceInfo(device, probeSupportedFormats)
                : null;
        }
        finally
        {
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
        }
    }

    public OptimizationResult ApplyConfiguredDevice(string deviceId, DeviceProfile profile)
    {
        if (!profile.Enabled)
        {
            return new OptimizationResult(
                profile.DeviceName,
                OptimizationStatus.Unchanged,
                null,
                "该设备未启用自动调整。");
        }

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;

        try
        {
            enumerator = CreateEnumerator();
            ThrowIfFailed(enumerator.GetDevice(deviceId, out device));
            ThrowIfFailed(device.GetState(out var state));
            if ((state & DeviceStateActive) == 0)
            {
                return new OptimizationResult(
                    profile.DeviceName,
                    OptimizationStatus.Failed,
                    null,
                    "设备当前未启用。");
            }

            return ApplyProfile(device, profile);
        }
        catch (Exception exception)
        {
            return new OptimizationResult(
                profile.DeviceName,
                OptimizationStatus.Failed,
                null,
                FormatError(exception));
        }
        finally
        {
            ReleaseComObject(device);
            ReleaseComObject(enumerator);
        }
    }

    public IReadOnlyList<OptimizationResult> ApplySavedProfilesToActiveDevices()
    {
        var results = new List<OptimizationResult>();
        foreach (var device in GetActiveDevices(probeSupportedFormats: false))
        {
            var profile = SettingsStore.GetDeviceProfile(device.Id);
            if (profile is { Enabled: true })
            {
                results.Add(ApplyConfiguredDevice(device.Id, profile));
            }
        }

        return results;
    }

    public IReadOnlyList<OptimizationResult> ScanAll()
    {
        return GetActiveDevices(probeSupportedFormats: true)
            .Select(device => new OptimizationResult(
                device.Name,
                OptimizationStatus.Unchanged,
                device.CurrentFormat,
                $"Windows 默认格式选项：{string.Join("；", device.SupportedFormats.Select(FormatOptionLabel))}；设备 ID：{device.Id}"))
            .ToList();
    }

    private static OptimizationResult ApplyProfile(IMMDevice device, DeviceProfile profile)
    {
        var name = GetFriendlyName(device);
        var current = ReadCurrentFormat(device);
        if (current is null)
        {
            return new OptimizationResult(name, OptimizationStatus.Failed, null, "无法读取当前格式。");
        }

        var availableFormats = ReadWindowsDefaultFormatOptions(device, current);
        var target = FindSavedFormat(availableFormats, profile);
        if (target is null)
        {
            return new OptimizationResult(
                name,
                OptimizationStatus.Failed,
                current,
                $"保存的 {profile.ValidBits} 位，{profile.SampleRate:N0} Hz 已不在该设备的 Windows 默认格式选项中。请重新选择并保存。");
        }

        if (FormatsEqual(current, target))
        {
            return new OptimizationResult(
                name,
                OptimizationStatus.Unchanged,
                current,
                "当前格式已经符合该设备的保存设置。");
        }

        try
        {
            WriteDeviceFormat(device, target);
            Thread.Sleep(400);
            var verified = ReadCurrentFormat(device);
            if (verified is not null && FormatsEqual(verified, target))
            {
                return new OptimizationResult(
                    name,
                    OptimizationStatus.Changed,
                    verified,
                    $"已从 {current} 调整为 Windows 列表中的 {verified}，并读回验证成功。");
            }

            return new OptimizationResult(
                name,
                OptimizationStatus.Failed,
                current,
                $"Windows 未采用 {target}；写后读回为 {verified?.ToString() ?? "未知格式"}。");
        }
        catch (Exception exception)
        {
            return new OptimizationResult(
                name,
                OptimizationStatus.Failed,
                current,
                $"Windows 未采用 {target}：{FormatError(exception)}");
        }
    }

    private static AudioFormat? FindSavedFormat(
        IReadOnlyList<AudioFormat> formats,
        DeviceProfile profile)
    {
        if (profile.HasExactFormat)
        {
            return formats.FirstOrDefault(format =>
                format.Channels == profile.Channels &&
                format.SampleRate == profile.SampleRate &&
                format.ContainerBits == profile.ContainerBits &&
                format.ValidBits == profile.ValidBits &&
                format.IsFloat == profile.IsFloat &&
                format.ChannelMask == profile.ChannelMask);
        }

        // Version 2.0 stored bit depth and sample rate separately. Migrate only
        // when that pair maps to a real Windows option for this endpoint.
        return formats.FirstOrDefault(format =>
            format.SampleRate == profile.SampleRate &&
            format.ValidBits == profile.ValidBits);
    }

    private static bool FormatsEqual(AudioFormat left, AudioFormat right)
    {
        return left.Channels == right.Channels &&
               left.SampleRate == right.SampleRate &&
               left.ContainerBits == right.ContainerBits &&
               left.ValidBits == right.ValidBits &&
               left.IsFloat == right.IsFloat &&
               left.ChannelMask == right.ChannelMask;
    }

    private static AudioDeviceInfo? ReadDeviceInfo(IMMDevice device, bool probeSupportedFormats)
    {
        ThrowIfFailed(device.GetId(out var id));
        var current = ReadCurrentFormat(device);
        if (current is null)
        {
            return null;
        }

        var supported = probeSupportedFormats
            ? ReadWindowsDefaultFormatOptions(device, current)
            : [];

        return new AudioDeviceInfo(
            id,
            GetFriendlyName(device),
            current,
            supported,
            IsConnected: true);
    }

    private static IReadOnlyList<AudioFormat> ReadWindowsDefaultFormatOptions(
        IMMDevice device,
        AudioFormat current)
    {
        IPropertyStore? properties = null;
        var value = default(PropVariant);
        var formats = new List<AudioFormat>();

        try
        {
            ThrowIfFailed(device.OpenPropertyStore(StgmRead, out properties));
            var key = AudioEngineSupportedDeviceFormats;
            if (properties.GetValue(ref key, out value) < 0 ||
                value.VariantType != VtBlob ||
                value.BlobValue.Data == IntPtr.Zero ||
                value.BlobValue.Size < 40)
            {
                return [current];
            }

            var bytes = new byte[value.BlobValue.Size];
            Marshal.Copy(value.BlobValue.Data, bytes, 0, bytes.Length);

            for (var offset = 0; offset <= bytes.Length - 40; offset++)
            {
                if (BitConverter.ToUInt16(bytes, offset) != WaveFormatExtensibleTag ||
                    BitConverter.ToUInt16(bytes, offset + 16) != 22)
                {
                    continue;
                }

                var channels = BitConverter.ToUInt16(bytes, offset + 2);
                var sampleRate = BitConverter.ToUInt32(bytes, offset + 4);
                var averageBytes = BitConverter.ToUInt32(bytes, offset + 8);
                var blockAlign = BitConverter.ToUInt16(bytes, offset + 12);
                var containerBits = BitConverter.ToUInt16(bytes, offset + 14);
                var validBits = BitConverter.ToUInt16(bytes, offset + 18);
                var channelMask = BitConverter.ToUInt32(bytes, offset + 20);
                var subFormat = new Guid(bytes.AsSpan(offset + 24, 16));

                if (channels == 0 ||
                    channels != current.Channels ||
                    sampleRate < 8_000 ||
                    sampleRate > 1_536_000 ||
                    containerBits == 0 ||
                    containerBits % 8 != 0 ||
                    validBits == 0 ||
                    validBits > containerBits ||
                    blockAlign != channels * (containerBits / 8) ||
                    averageBytes != sampleRate * blockAlign ||
                    (channelMask != current.ChannelMask &&
                     channelMask != 0 &&
                     current.ChannelMask != 0) ||
                    (subFormat != PcmSubFormat && subFormat != FloatSubFormat))
                {
                    continue;
                }

                formats.Add(new AudioFormat(
                    channels,
                    checked((int)sampleRate),
                    containerBits,
                    validBits,
                    subFormat == FloatSubFormat,
                    channelMask));
            }
        }
        catch
        {
            // This list is not exposed by a public enumeration API. If a driver
            // omits the endpoint property, offer only the format already in use.
        }
        finally
        {
            PropVariantClear(ref value);
            ReleaseComObject(properties);
        }

        if (!formats.Any(format => FormatsEqual(format, current)))
        {
            formats.Add(current);
        }

        return formats
            .Distinct()
            .OrderBy(format => format.SampleRate)
            .ThenBy(format => format.ValidBits)
            .ThenBy(format => format.IsFloat)
            .ToList();
    }

    private static string FormatOptionLabel(AudioFormat format) => format.ToString();

    private static AudioFormat? ReadCurrentFormat(IMMDevice device)
    {
        IPropertyStore? properties = null;
        var value = default(PropVariant);

        try
        {
            ThrowIfFailed(device.OpenPropertyStore(StgmRead, out properties));
            var key = AudioEngineDeviceFormat;
            ThrowIfFailed(properties.GetValue(ref key, out value));

            if (value.VariantType != VtBlob ||
                value.BlobValue.Data == IntPtr.Zero ||
                value.BlobValue.Size < 18)
            {
                return null;
            }

            var header = Marshal.PtrToStructure<WaveFormatHeader>(value.BlobValue.Data);
            var validBits = header.BitsPerSample;
            var channelMask = DefaultChannelMask(header.Channels);
            var isFloat = header.FormatTag == 3;

            if (header.FormatTag == WaveFormatExtensibleTag && value.BlobValue.Size >= 40)
            {
                var extensible = Marshal.PtrToStructure<WaveFormatExtensible>(value.BlobValue.Data);
                validBits = extensible.ValidBitsPerSample;
                channelMask = extensible.ChannelMask;
                isFloat = extensible.SubFormat == FloatSubFormat;
            }

            return new AudioFormat(
                header.Channels,
                checked((int)header.SamplesPerSecond),
                header.BitsPerSample,
                validBits,
                isFloat,
                channelMask);
        }
        finally
        {
            PropVariantClear(ref value);
            ReleaseComObject(properties);
        }
    }

    private static void WriteDeviceFormat(IMMDevice device, AudioFormat format)
    {
        IPolicyConfig? policyConfig = null;
        var formatPointer = AllocateWaveFormat(format);

        try
        {
            ThrowIfFailed(device.GetId(out var deviceId));
            policyConfig = (IPolicyConfig)Activator.CreateInstance(
                Type.GetTypeFromCLSID(PolicyConfigClientClassId, throwOnError: true)!)!;
            ThrowIfFailed(policyConfig.SetDeviceFormat(deviceId, formatPointer, formatPointer));
        }
        finally
        {
            Marshal.FreeHGlobal(formatPointer);
            ReleaseComObject(policyConfig);
        }
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? properties = null;
        var value = default(PropVariant);

        try
        {
            ThrowIfFailed(device.OpenPropertyStore(StgmRead, out properties));
            var key = DeviceFriendlyName;
            ThrowIfFailed(properties.GetValue(ref key, out value));
            return value.VariantType == VtLpwstr && value.PointerValue != IntPtr.Zero
                ? Marshal.PtrToStringUni(value.PointerValue) ?? "未知播放设备"
                : "未知播放设备";
        }
        catch
        {
            return "未知播放设备";
        }
        finally
        {
            PropVariantClear(ref value);
            ReleaseComObject(properties);
        }
    }

    private static IntPtr AllocateWaveFormat(AudioFormat format)
    {
        var waveFormat = new WaveFormatExtensible
        {
            FormatTag = WaveFormatExtensibleTag,
            Channels = checked((ushort)format.Channels),
            SamplesPerSecond = checked((uint)format.SampleRate),
            AverageBytesPerSecond = checked((uint)(format.SampleRate * format.Channels * format.BytesPerSample)),
            BlockAlign = checked((ushort)(format.Channels * format.BytesPerSample)),
            BitsPerSample = checked((ushort)format.ContainerBits),
            ExtraSize = 22,
            ValidBitsPerSample = checked((ushort)format.ValidBits),
            ChannelMask = format.ChannelMask,
            SubFormat = format.IsFloat ? FloatSubFormat : PcmSubFormat
        };

        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatExtensible>());
        Marshal.StructureToPtr(waveFormat, pointer, fDeleteOld: false);
        return pointer;
    }

    private static uint DefaultChannelMask(int channels)
    {
        return channels switch
        {
            1 => 0x0004,
            2 => 0x0003,
            3 => 0x0007,
            4 => 0x0033,
            5 => 0x0037,
            6 => 0x003F,
            7 => 0x013F,
            8 => 0x063F,
            _ => 0
        };
    }

    private static IMMDeviceEnumerator CreateEnumerator()
    {
        return (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(MmDeviceEnumeratorClassId, throwOnError: true)!)!;
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    private static string FormatError(Exception exception)
    {
        return $"HRESULT 0x{exception.HResult:X8}：{exception.Message}";
    }

    internal static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatHeader
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }
}
