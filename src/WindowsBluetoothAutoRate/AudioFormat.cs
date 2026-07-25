namespace WindowsBluetoothAutoRate;

internal sealed record AudioFormat(
    int Channels,
    int SampleRate,
    int ContainerBits,
    int ValidBits,
    bool IsFloat,
    uint ChannelMask)
{
    public int BytesPerSample => ContainerBits / 8;

    public override string ToString()
    {
        var sampleKind = IsFloat ? "，浮点" : string.Empty;
        return $"{Channels} 声道，{ValidBits} 位，{SampleRate:N0} Hz{sampleKind}";
    }
}

internal sealed record AudioDeviceInfo(
    string Id,
    string Name,
    AudioFormat CurrentFormat,
    IReadOnlyList<AudioFormat> SupportedFormats,
    bool IsConnected);

internal sealed record DeviceProfile(
    string DeviceId,
    string DeviceName,
    bool Enabled,
    int Channels,
    int SampleRate,
    int ContainerBits,
    int ValidBits,
    bool IsFloat,
    uint ChannelMask)
{
    public bool HasExactFormat => Channels > 0 && ContainerBits > 0;
}

internal enum OptimizationStatus
{
    Unchanged,
    Changed,
    Failed
}

internal sealed record OptimizationResult(
    string DeviceName,
    OptimizationStatus Status,
    AudioFormat? Format,
    string Message);
