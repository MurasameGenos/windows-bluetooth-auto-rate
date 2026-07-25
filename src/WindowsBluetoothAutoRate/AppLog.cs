using System.Text;

namespace WindowsBluetoothAutoRate;

internal static class AppLog
{
    private static readonly object SyncRoot = new();
    private static readonly string DirectoryPath = SettingsStore.DataDirectory;

    public static string FilePath { get; } = Path.Combine(DirectoryPath, "app.log");

    public static void EnsureExists()
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(DirectoryPath);
            if (!File.Exists(FilePath))
            {
                File.WriteAllText(FilePath, string.Empty, Encoding.UTF8);
            }
        }
    }

    public static void Write(string message)
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(
                FilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
    }

    public static void WriteSummary(IReadOnlyCollection<OptimizationResult> results)
    {
        foreach (var result in results)
        {
            var detail = result.Format is null ? result.Message : $"{result.Format}；{result.Message}";
            Write($"{result.Status}｜{result.DeviceName}｜{detail}");
        }
    }
}
