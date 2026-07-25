[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repository = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$artifactRoot = Join-Path $repository 'artifacts'
$publishRoot = Join-Path $artifactRoot 'windows-bluetooth-auto-rate-win-x64'
$applicationRoot = Join-Path $publishRoot 'App'

if ([System.IO.Directory]::Exists($publishRoot)) {
    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
    $resolvedPublishRoot = [System.IO.Path]::GetFullPath($publishRoot)
    if (-not $resolvedPublishRoot.StartsWith(
        $resolvedArtifactRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside artifacts: $resolvedPublishRoot"
    }

    [System.IO.Directory]::Delete($resolvedPublishRoot, $true)
}

[System.IO.Directory]::CreateDirectory($applicationRoot) | Out-Null

& dotnet publish `
    (Join-Path $repository 'src\WindowsBluetoothAutoRate\WindowsBluetoothAutoRate.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -o $applicationRoot
if ($LASTEXITCODE -ne 0) {
    throw 'Publishing the WinUI application failed.'
}

& dotnet publish `
    (Join-Path $repository 'src\WindowsBluetoothAutoRate.Launcher\WindowsBluetoothAutoRate.Launcher.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw 'Publishing the launcher failed.'
}

Copy-Item -LiteralPath `
    (Join-Path $repository 'README.md'), `
    (Join-Path $repository 'LICENSE') `
    -Destination $publishRoot

$requiredFiles = @(
    (Join-Path $publishRoot 'WindowsBluetoothAutoRate.exe'),
    (Join-Path $applicationRoot 'WindowsBluetoothAutoRate.exe'),
    (Join-Path $applicationRoot 'App.xbf'),
    (Join-Path $applicationRoot 'MainPage.xbf'),
    (Join-Path $applicationRoot 'MainWindow.xbf'),
    (Join-Path $applicationRoot 'WindowsBluetoothAutoRate.pri')
)
foreach ($requiredFile in $requiredFiles) {
    if (-not [System.IO.File]::Exists($requiredFile)) {
        throw "The release is missing: $requiredFile"
    }
}

$allowedDirectories = @(
    'en-us',
    'ja-JP',
    'Microsoft.UI.Xaml',
    'zh-CN',
    'zh-TW'
)
$unexpectedDirectories = Get-ChildItem -LiteralPath $applicationRoot -Directory |
    Where-Object { $_.Name -notin $allowedDirectories }
if ($unexpectedDirectories) {
    throw "The release contains unexpected directories: $($unexpectedDirectories.Name -join ', ')"
}

foreach ($language in @('en-us', 'ja-JP', 'zh-CN', 'zh-TW')) {
    if (-not [System.IO.Directory]::Exists(
        (Join-Path $applicationRoot $language))) {
        throw "The release is missing language resources: $language"
    }
}

Write-Host "Release created at: $publishRoot"
