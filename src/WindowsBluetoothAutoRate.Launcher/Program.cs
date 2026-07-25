using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsBluetoothAutoRate.Launcher;

internal static class Program
{
    private const uint ErrorIcon = 0x00000010;

    [STAThread]
    private static int Main(string[] args)
    {
        var applicationPath = Path.Combine(
            AppContext.BaseDirectory,
            "App",
            "WindowsBluetoothAutoRate.exe");

        try
        {
            if (!File.Exists(applicationPath))
            {
                throw new FileNotFoundException(
                    "程序文件不完整，请重新解压整个压缩包。",
                    applicationPath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = applicationPath,
                WorkingDirectory = Path.GetDirectoryName(applicationPath)!,
                UseShellExecute = false
            };
            foreach (var argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox(
                IntPtr.Zero,
                $"无法启动 Windows Bluetooth Auto Rate。\n\n{exception.Message}",
                "启动失败",
                ErrorIcon);
            return 1;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(
        IntPtr window,
        string text,
        string caption,
        uint type);
}
