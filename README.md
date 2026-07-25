# Windows Bluetooth Auto Rate

一个使用 WinUI 3 编写的 Windows 音频设备格式管理工具。它会按 Windows 音频端点 ID 记忆每台播放设备的目标格式，并在设备新接入或重新启用时应用一次。

虽然项目名称包含 Bluetooth，但程序同样支持 USB DAC、板载声卡、HDMI/DisplayPort 音频等 Windows 播放设备。

## 功能

- 使用 WinUI 3 和 Windows App SDK 构建的现代界面。
- 从 Windows 端点读取完整的“默认格式”选项，不会分别拼接位深和采样率。
- 每台设备按唯一端点 ID 单独保存配置，重名设备不会互相覆盖。
- 设备拔出后仍保留在列表中，并显示“未连接”状态。
- 只在设备接入或重新启用时处理一次，没有定时轮询。
- 支持逐设备启用自动调整、立即应用、清除配置。
- 支持开机启动、查看日志和重置全部设置。
- 配置保存在独立 JSON 文件中，不再写入软件专用注册表项。
- 关闭窗口后继续驻留系统托盘。

## 下载与运行

从 [Releases](../../releases/latest) 下载 `windows-bluetooth-auto-rate-win-x64.zip`。

1. 将压缩包完整解压到一个固定目录。
2. 运行 `WindowsBluetoothAutoRate.exe`。
3. 不要只复制 EXE；WinUI 3、Windows App SDK 和 .NET 运行库以多个文件随程序分发。

发布包为 Windows x64 自包含版本，无需单独安装 .NET 或 Windows App SDK 运行时。

## 设置与日志

设置文件：

```text
%LOCALAPPDATA%\MurasameGenos\WindowsBluetoothAutoRate\settings.json
```

日志文件位于同一目录：

```text
%LOCALAPPDATA%\MurasameGenos\WindowsBluetoothAutoRate\app.log
```

开机启动需要在当前用户的 `Run` 注册表项中登记程序路径；关闭开机启动或执行“重置全部设置”会删除该值。

## 工作方式

1. 程序启动时读取当前设备并更新设备历史，但不自动修改当前设备格式。
2. 后台使用 Windows 音频端点通知监听设备接入和重新启用事件。
3. 只有设备已启用自动调整时，才应用该设备保存的完整格式。
4. 写入后重新读取并验证；Windows 未采用时会记录失败。
5. 无法可靠读取格式列表时，仅提供设备当前格式，避免显示不可设置的组合。

## 构建

要求：

- Windows 10 1809 或更高版本
- .NET 9 SDK

```powershell
dotnet build .\WindowsBluetoothAutoRate.slnx -c Release
```

生成多文件自包含发布目录：

```powershell
dotnet publish .\src\WindowsBluetoothAutoRate\WindowsBluetoothAutoRate.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false `
  -o .\artifacts\windows-bluetooth-auto-rate-win-x64
```

## 主要依赖

- Microsoft.WindowsAppSDK 2.3.1
- H.NotifyIcon.WinUI 2.3.2
- .NET 9

## 许可证

[MIT](LICENSE)
