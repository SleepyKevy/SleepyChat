$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'CSharpHost\SleepyChat.csproj'
$distRoot = Join-Path $root 'dist'
$out = Join-Path $distRoot 'SleepyChat 1.0.0'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK is required. Install the .NET 8 SDK, then run this script again.'
}

if (Get-ChildItem $root -Recurse -Filter *.go -File -ErrorAction SilentlyContinue) {
    throw 'Release source contains unexpected Go files. SleepyChat 1.0.0 must publish from the C#/.NET host only.'
}

$forbidden = @(('Edge' + 'Profile'), ('ms' + 'edge.exe'), ('Set' + 'Parent('), ('FirstRun' + 'Bootstrap'), ('BUILD_FIRST' + '_RUN_EXE'))
$sourceFiles = Get-ChildItem $root -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/]dist[\\/]' -and $_.Extension -in '.cs','.csproj','.html','.js','.css','.md','.txt','.ps1','.yml','.yaml','.webmanifest'
}
foreach ($needle in $forbidden) {
    if ($sourceFiles | Where-Object { Select-String -Path $_.FullName -SimpleMatch $needle -Quiet }) {
        throw "Legacy release marker found: $needle"
    }
}

$version = & dotnet --version
Write-Host "Using .NET SDK $version"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

& dotnet restore $project -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-restore -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$exe = Join-Path $out 'SleepyChat.exe'
if (-not (Test-Path $exe)) { throw 'SleepyChat.exe was not produced.' }
if (-not (Test-Path (Join-Path $out 'web\index.html'))) { throw 'Published WebView2 UI is missing.' }
if (-not (Test-Path (Join-Path $out 'web\favicon.ico'))) { throw 'Published favicon is missing.' }
if (-not (Test-Path (Join-Path $out 'assets\app.ico'))) { throw 'Published application icon is missing.' }
if (-not (Test-Path (Join-Path $out 'assets\platform-kick.png'))) { throw 'Kick platform asset is missing.' }
if (-not (Test-Path (Join-Path $out 'assets\platform-twitch.png'))) { throw 'Twitch platform asset is missing.' }

$versionInfo = (Get-Item $exe).VersionInfo
if ($versionInfo.FileVersion -notlike '1.0.0*') { throw "Unexpected EXE file version: $($versionInfo.FileVersion)" }
if ($versionInfo.ProductVersion -notlike '1.0.0*') { throw "Unexpected EXE product version: $($versionInfo.ProductVersion)" }

Write-Host "SleepyChat 1.0.0 published successfully: $out"
