using System.Text.Json;
using Microsoft.Win32;

namespace WindowsBluetoothAutoRate;

internal static class SettingsStore
{
    private const string SettingsFileName = "settings.json";
    private const string LogFileName = "app.log";

    // Versions up to 3.0.2 used this GitHub account name as a data-directory
    // component. It is retained only so those settings can be migrated.
    private const string LegacyPublisherDirectoryName = "MurasameGenos";

    public const string StartupRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string StartupValueName = "WindowsBluetoothAutoRate";
    private static readonly string[] StartupApprovedRegistryPaths =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"
    ];

    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string DataDirectory { get; } = GetDataDirectory();

    public static string SettingsFilePath { get; } =
        Path.Combine(DataDirectory, SettingsFileName);

    public static DeviceProfile? GetDeviceProfile(string deviceId)
    {
        lock (SyncRoot)
        {
            var data = Load();
            return data.Devices.TryGetValue(deviceId, out var device)
                ? ToProfile(device)
                : null;
        }
    }

    public static IReadOnlyList<DeviceProfile> GetAllDeviceProfiles()
    {
        lock (SyncRoot)
        {
            return Load().Devices.Values.Select(ToProfile).ToList();
        }
    }

    public static void SaveDeviceProfile(DeviceProfile profile)
    {
        lock (SyncRoot)
        {
            var data = Load();
            if (!data.Devices.TryGetValue(profile.DeviceId, out var device))
            {
                device = new StoredDevice
                {
                    DeviceId = profile.DeviceId,
                    DeviceName = profile.DeviceName
                };
                data.Devices[profile.DeviceId] = device;
            }

            device.DeviceName = profile.DeviceName;
            device.Enabled = profile.Enabled;
            device.TargetFormat = ToStoredFormat(profile);
            Save(data);
        }
    }

    public static void RememberDevice(AudioDeviceInfo info)
    {
        lock (SyncRoot)
        {
            var data = Load();
            if (!data.Devices.TryGetValue(info.Id, out var device))
            {
                device = new StoredDevice
                {
                    DeviceId = info.Id,
                    DeviceName = info.Name,
                    Enabled = false,
                    TargetFormat = StoredFormat.From(info.CurrentFormat)
                };
                data.Devices[info.Id] = device;
            }

            device.DeviceName = info.Name;
            device.LastSeenUtc = DateTime.UtcNow;
            device.LastFormat = StoredFormat.From(info.CurrentFormat);
            device.SupportedFormats = info.SupportedFormats
                .Append(info.CurrentFormat)
                .Distinct()
                .Select(StoredFormat.From)
                .ToList();
            Save(data);
        }
    }

    public static IReadOnlyList<AudioDeviceInfo> GetRememberedDevices()
    {
        lock (SyncRoot)
        {
            var devices = new List<AudioDeviceInfo>();
            foreach (var stored in Load().Devices.Values)
            {
                var last = stored.LastFormat?.ToAudioFormat() ??
                           stored.TargetFormat?.ToAudioFormat();
                if (last is null)
                {
                    continue;
                }

                var formats = stored.SupportedFormats
                    .Select(format => format.ToAudioFormat())
                    .Append(last)
                    .Distinct()
                    .OrderBy(format => format.SampleRate)
                    .ThenBy(format => format.ValidBits)
                    .ThenBy(format => format.IsFloat)
                    .ToList();

                devices.Add(new AudioDeviceInfo(
                    stored.DeviceId,
                    stored.DeviceName,
                    last,
                    formats,
                    IsConnected: false));
            }

            return devices;
        }
    }

    public static bool IsStartupEnabled()
    {
        lock (SyncRoot)
        {
            return Load().StartWithWindows;
        }
    }

    public static bool IsSilentStartupEnabled()
    {
        lock (SyncRoot)
        {
            return Load().StartSilently;
        }
    }

    public static void SetStartupEnabled(bool enabled)
    {
        lock (SyncRoot)
        {
            var data = Load();
            data.StartWithWindows = enabled;
            UpdateStartupRegistration(data);
            Save(data);
        }
    }

    public static void SetSilentStartupEnabled(bool enabled)
    {
        lock (SyncRoot)
        {
            var data = Load();
            data.StartSilently = enabled;
            UpdateStartupRegistration(data);
            Save(data);
        }
    }

    public static void RefreshStartupRegistration()
    {
        lock (SyncRoot)
        {
            var data = Load();
            if (UpdateStartupRegistration(data))
            {
                Save(data);
            }
        }
    }

    public static void ResetAll()
    {
        lock (SyncRoot)
        {
            if (File.Exists(SettingsFilePath))
            {
                File.Delete(SettingsFilePath);
            }

            using var run = Registry.CurrentUser.OpenSubKey(
                StartupRegistryPath,
                writable: true);
            run?.DeleteValue(StartupValueName, throwOnMissingValue: false);
            DeleteStartupApprovalCache();
        }
    }

    private static AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            return ReadSettings(SettingsFilePath);
        }
        catch (Exception exception)
        {
            AppLog.Write($"读取设置文件失败，将使用空设置：{exception.Message}");
            return new AppSettings();
        }
    }

    private static string GetDataDirectory()
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var currentDirectory = Path.Combine(
            localData,
            "WindowsBluetoothAutoRate");
        var legacyDirectory = Path.Combine(
            localData,
            LegacyPublisherDirectoryName,
            "WindowsBluetoothAutoRate");
        TryMigrateLegacyData(legacyDirectory, currentDirectory);
        return currentDirectory;
    }

    private static string GetStartupExecutablePath()
    {
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("无法确定程序路径。");
        var applicationDirectory = Path.GetDirectoryName(executable);
        var releaseDirectory = applicationDirectory is null
            ? null
            : Directory.GetParent(applicationDirectory)?.FullName;
        var launcher = releaseDirectory is null
            ? null
            : Path.Combine(releaseDirectory, "WindowsBluetoothAutoRate.exe");

        return launcher is not null && File.Exists(launcher)
            ? launcher
            : executable;
    }

    private static bool UpdateStartupRegistration(AppSettings settings)
    {
        using var run = Registry.CurrentUser.CreateSubKey(
            StartupRegistryPath,
            writable: true);
        if (!settings.StartWithWindows)
        {
            run.DeleteValue(StartupValueName, throwOnMissingValue: false);
            DeleteStartupApprovalCache();
            if (settings.RegisteredStartupExecutablePath is null)
            {
                return false;
            }

            settings.RegisteredStartupExecutablePath = null;
            return true;
        }

        var executable = GetStartupExecutablePath();
        var arguments = settings.StartSilently ? " --background" : string.Empty;
        var command = $"\"{executable}\"{arguments}";
        var registeredCommand = run.GetValue(StartupValueName) as string;
        var executableMoved = !string.Equals(
            settings.RegisteredStartupExecutablePath,
            executable,
            StringComparison.OrdinalIgnoreCase);
        var commandChanged = !string.Equals(
            registeredCommand,
            command,
            StringComparison.OrdinalIgnoreCase);

        if (executableMoved || commandChanged)
        {
            // Task Manager retains an approval record under the startup entry name.
            // Recreate both records after a move so it does not keep presenting the
            // old executable location. The setting explicitly says startup is enabled,
            // so clearing a stale disabled-state record is intentional here.
            run.DeleteValue(StartupValueName, throwOnMissingValue: false);
            DeleteStartupApprovalCache();
        }

        run.SetValue(
            StartupValueName,
            command,
            RegistryValueKind.String);

        if (!executableMoved)
        {
            return false;
        }

        settings.RegisteredStartupExecutablePath = executable;
        return true;
    }

    private static void DeleteStartupApprovalCache()
    {
        foreach (var path in StartupApprovedRegistryPaths)
        {
            using var approved = Registry.CurrentUser.OpenSubKey(path, writable: true);
            approved?.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }
    }

    private static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        SaveToPath(SettingsFilePath, settings);
    }

    private static void SaveToPath(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static AppSettings ReadSettings(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ??
               new AppSettings();
    }

    private static void TryMigrateLegacyData(
        string legacyDirectory,
        string currentDirectory)
    {
        try
        {
            if (!Directory.Exists(legacyDirectory))
            {
                return;
            }

            Directory.CreateDirectory(currentDirectory);
            MigrateSettingsFile(
                Path.Combine(legacyDirectory, SettingsFileName),
                Path.Combine(currentDirectory, SettingsFileName));
            MigrateLogFile(
                Path.Combine(legacyDirectory, LogFileName),
                Path.Combine(currentDirectory, LogFileName));

            if (!Directory.EnumerateFileSystemEntries(legacyDirectory).Any())
            {
                Directory.Delete(legacyDirectory);
                var legacyParent = Directory.GetParent(legacyDirectory);
                if (legacyParent is not null &&
                    !Directory.EnumerateFileSystemEntries(legacyParent.FullName).Any())
                {
                    legacyParent.Delete();
                }
            }
        }
        catch
        {
            // Keep the old files untouched when migration cannot complete.
        }
    }

    private static void MigrateSettingsFile(
        string legacyPath,
        string currentPath)
    {
        if (!File.Exists(legacyPath))
        {
            return;
        }

        if (!File.Exists(currentPath))
        {
            File.Move(legacyPath, currentPath);
            return;
        }

        AppSettings? legacySettings = null;
        AppSettings? currentSettings = null;
        try
        {
            legacySettings = ReadSettings(legacyPath);
        }
        catch
        {
        }

        try
        {
            currentSettings = ReadSettings(currentPath);
        }
        catch
        {
        }

        if (legacySettings is not null && currentSettings is null)
        {
            File.Move(
                currentPath,
                GetAvailableBackupPath(currentPath, "invalid"));
            SaveToPath(currentPath, legacySettings);
            File.Delete(legacyPath);
            return;
        }

        if (legacySettings is not null && currentSettings is not null)
        {
            foreach (var (deviceId, device) in legacySettings.Devices)
            {
                currentSettings.Devices.TryAdd(deviceId, device);
            }

            SaveToPath(currentPath, currentSettings);
            File.Delete(legacyPath);
            return;
        }

        File.Move(
            legacyPath,
            GetAvailableBackupPath(currentPath, "legacy"));
    }

    private static void MigrateLogFile(string legacyPath, string currentPath)
    {
        if (!File.Exists(legacyPath))
        {
            return;
        }

        if (!File.Exists(currentPath))
        {
            File.Move(legacyPath, currentPath);
            return;
        }

        var legacyLog = File.ReadAllText(legacyPath);
        if (!string.IsNullOrWhiteSpace(legacyLog))
        {
            File.AppendAllText(
                currentPath,
                $"{Environment.NewLine}--- 从旧版本迁移的日志 ---{Environment.NewLine}{legacyLog}");
        }

        File.Delete(legacyPath);
    }

    private static string GetAvailableBackupPath(
        string currentSettingsPath,
        string label)
    {
        var directory = Path.GetDirectoryName(currentSettingsPath)!;
        var fileName = Path.GetFileNameWithoutExtension(currentSettingsPath);
        var extension = Path.GetExtension(currentSettingsPath);
        var backupPath = Path.Combine(
            directory,
            $"{fileName}.{label}{extension}");
        for (var index = 2; File.Exists(backupPath); index++)
        {
            backupPath = Path.Combine(
                directory,
                $"{fileName}.{label}-{index}{extension}");
        }

        return backupPath;
    }

    private static DeviceProfile ToProfile(StoredDevice device)
    {
        var format = device.TargetFormat?.ToAudioFormat() ??
                     device.LastFormat?.ToAudioFormat() ??
                     new AudioFormat(2, 48_000, 32, 24, false, 3);
        return new DeviceProfile(
            device.DeviceId,
            device.DeviceName,
            device.Enabled,
            format.Channels,
            format.SampleRate,
            format.ContainerBits,
            format.ValidBits,
            format.IsFloat,
            format.ChannelMask);
    }

    private static StoredFormat ToStoredFormat(DeviceProfile profile)
    {
        return new StoredFormat
        {
            Channels = profile.Channels,
            SampleRate = profile.SampleRate,
            ContainerBits = profile.ContainerBits,
            ValidBits = profile.ValidBits,
            IsFloat = profile.IsFloat,
            ChannelMask = profile.ChannelMask
        };
    }

    private sealed class AppSettings
    {
        public bool StartWithWindows { get; set; }

        public bool StartSilently { get; set; } = true;

        public string? RegisteredStartupExecutablePath { get; set; }

        public Dictionary<string, StoredDevice> Devices { get; set; } =
            new(StringComparer.Ordinal);
    }

    private sealed class StoredDevice
    {
        public string DeviceId { get; set; } = string.Empty;

        public string DeviceName { get; set; } = "未知播放设备";

        public bool Enabled { get; set; }

        public StoredFormat? TargetFormat { get; set; }

        public StoredFormat? LastFormat { get; set; }

        public List<StoredFormat> SupportedFormats { get; set; } = [];

        public DateTime LastSeenUtc { get; set; }
    }

    private sealed class StoredFormat
    {
        public int Channels { get; set; }

        public int SampleRate { get; set; }

        public int ContainerBits { get; set; }

        public int ValidBits { get; set; }

        public bool IsFloat { get; set; }

        public uint ChannelMask { get; set; }

        public static StoredFormat From(AudioFormat format)
        {
            return new StoredFormat
            {
                Channels = format.Channels,
                SampleRate = format.SampleRate,
                ContainerBits = format.ContainerBits,
                ValidBits = format.ValidBits,
                IsFloat = format.IsFloat,
                ChannelMask = format.ChannelMask
            };
        }

        public AudioFormat ToAudioFormat()
        {
            return new AudioFormat(
                Channels,
                SampleRate,
                ContainerBits,
                ValidBits,
                IsFloat,
                ChannelMask);
        }
    }
}
