$ErrorActionPreference = 'Stop'

$version = '1.0.0'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'CSharpHost\SleepyChat.csproj'
$distRoot = Join-Path $root 'dist'
$out = Join-Path $distRoot "SleepyChat $version"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK is required. Install the .NET 8 SDK, then run this script again.'
}

$requiredSource = @(
    'CSharpHost\App\Program.cs',
    'CSharpHost\App\AppUtil.cs',
    'CSharpHost\Backend\BackendHost.cs',
    'CSharpHost\Backend\BackendHost.Media.cs',
    'CSharpHost\Kick\HostedKickModels.cs',
    'CSharpHost\Kick\HostedKickService.cs',
    'CSharpHost\Kick\HostedKickService.Delivery.cs',
    'CSharpHost\Kick\HostedKickService.Storage.cs',
    'CSharpHost\Kick\HostedKickService.Helpers.cs',
    'CSharpHost\Updates\UpdateService.cs',
    'CSharpHost\Window\MainForm.cs',
    'CSharpHost\Window\MainForm.Resize.cs',
    'CSharpHost\Window\MainForm.WebView.cs',
    'CSharpHost\Window\MainForm.Window.cs',
    'CSharpHost\Window\MainForm.CaptionButton.cs',
    'CSharpHost\web\index.html',
    'CSharpHost\web\css\app.css',
    'CSharpHost\web\css\ui-polish.css',
    'CSharpHost\web\js\app.js',
    'CSharpHost\web\manifest.webmanifest',
    'CSharpHost\assets\app.ico',
    'CSharpHost\assets\platform-kick.png',
    'CSharpHost\assets\platform-twitch.png',
    'CSharpHost\assets\theme-blue-raccoon.png',
    'CSharpHost\assets\theme-red-raccoon.png',
    'CSharpHost\assets\theme-purple-raccoon.png',
    'CSharpHost\assets\theme-green-raccoon.png',
    'CSharpHost\assets\theme-pink-raccoon.png',
    'readme.txt',
    'release_notes.txt'
)
foreach ($relative in $requiredSource) {
    if (-not (Test-Path (Join-Path $root $relative))) {
        throw "Required source file is missing: $relative"
    }
}

if (Test-Path (Join-Path $root 'CloudflareWorker')) {
    throw 'CloudflareWorker must not be bundled in SleepyChat desktop source. The hosted backend is shared infrastructure.'
}
if (Get-ChildItem $root -Recurse -Filter *.go -File -ErrorAction SilentlyContinue) {
    throw 'Unexpected Go source found. SleepyChat 1.0.0 publishes from C#/.NET only.'
}

$allText = Get-ChildItem (Join-Path $root 'CSharpHost') -Recurse -File | Where-Object {
    $_.Extension -in '.cs','.csproj','.html','.js','.css','.webmanifest'
}
$forbidden = @(
    'sleepychat-api.sleepyservices.workers.dev',
    '/kick/events/ensure-chat',
    'protected override CreateParams',
    'WS_THICKFRAME',
    'WM_NCCALCSIZE'
)
foreach ($needle in $forbidden) {
    if ($allText | Where-Object { Select-String -Path $_.FullName -SimpleMatch $needle -Quiet }) {
        throw "Stale/forbidden source marker found: $needle"
    }
}

$kickText = (Get-ChildItem (Join-Path $root 'CSharpHost\Kick') -Filter *.cs | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
foreach ($needle in @(
    'https://sleepysource-api.sleepyservices.workers.dev',
    '/oauth/kick/start',
    '/oauth/kick/status',
    '/kick/events/sync',
    '/kick/chat/send',
    'chat:write',
    'ProtectCredential'
)) {
    if (-not $kickText.Contains($needle)) { throw "Hosted Kick contract missing: $needle" }
}

$windowText = (Get-ChildItem (Join-Path $root 'CSharpHost\Window') -Filter *.cs | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
foreach ($needle in @(
    'FormBorderStyle = FormBorderStyle.None',
    'Size = new Size(1280, 820)',
    'MinimumSize = new Size(980, 680)',
    'HitTestResizeDirection',
    'DWMWCP_DONOTROUND',
    'IsGeneralAutofillEnabled = false',
    'IsPasswordAutosaveEnabled = false'
)) {
    if (-not $windowText.Contains($needle)) { throw "Window contract missing: $needle" }
}

$html = Get-Content (Join-Path $root 'CSharpHost\web\index.html') -Raw
$js = Get-Content (Join-Path $root 'CSharpHost\web\js\app.js') -Raw
foreach ($needle in @('/assets/theme-blue-raccoon.png','Midnight Pulse','Crimson Rush','Violet Void','Toxic Glow','Bubblegum Byte')) {
    if (-not ($html.Contains($needle) -or $js.Contains($needle))) {
        throw "Theme/UI contract missing: $needle"
    }
}
foreach ($needle in @(
    "blue:'/assets/theme-blue-raccoon.png'",
    "red:'/assets/theme-red-raccoon.png'",
    "purple:'/assets/theme-purple-raccoon.png'",
    "green:'/assets/theme-green-raccoon.png'",
    "pink:'/assets/theme-pink-raccoon.png'"
)) {
    if (-not $js.Contains($needle)) { throw "Theme logo mapping missing: $needle" }
}

if (Get-Command node -ErrorAction SilentlyContinue) {
    & node --check (Join-Path $root 'CSharpHost\web\js\app.js')
    if ($LASTEXITCODE -ne 0) { throw 'Web JavaScript syntax validation failed.' }
}

$manifest = Get-Content (Join-Path $root 'CSharpHost\web\manifest.webmanifest') -Raw | ConvertFrom-Json
if (-not $manifest) { throw 'Web manifest validation failed.' }

$sdkVersion = & dotnet --version
Write-Host "Using .NET SDK $sdkVersion"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

& dotnet restore $project -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --no-restore -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# Copy runtime web and branding assets explicitly so the published folder is
# deterministic even if MSBuild behavior changes for individual content files.
foreach ($folder in @('web','assets')) {
    $sourceFolder = Join-Path $root "CSharpHost\$folder"
    Copy-Item $sourceFolder $out -Recurse -Force
}

Copy-Item (Join-Path $root 'readme.txt') (Join-Path $out 'README.txt') -Force
Copy-Item (Join-Path $root 'release_notes.txt') (Join-Path $out 'RELEASE_NOTES.txt') -Force

$requiredOutput = @(
    'SleepyChat.exe',
    'README.txt',
    'RELEASE_NOTES.txt',
    'web\index.html',
    'web\css\app.css',
    'web\css\ui-polish.css',
    'web\js\app.js',
    'web\favicon.ico',
    'assets\app.ico',
    'assets\platform-kick.png',
    'assets\platform-twitch.png',
    'assets\theme-blue-raccoon.png',
    'assets\theme-red-raccoon.png',
    'assets\theme-purple-raccoon.png',
    'assets\theme-green-raccoon.png',
    'assets\theme-pink-raccoon.png'
)
foreach ($relative in $requiredOutput) {
    if (-not (Test-Path (Join-Path $out $relative))) { throw "Published file missing: $relative" }
}

if (Get-ChildItem $out -Recurse -Filter *.pdb -File -ErrorAction SilentlyContinue) {
    throw 'Release output unexpectedly contains PDB files.'
}

$exe = Join-Path $out 'SleepyChat.exe'
$versionInfo = (Get-Item $exe).VersionInfo
if ($versionInfo.FileVersion -notlike '1.0.0*') { throw "Unexpected EXE file version: $($versionInfo.FileVersion)" }
if ($versionInfo.ProductVersion -notlike '1.0.0*') { throw "Unexpected EXE product version: $($versionInfo.ProductVersion)" }

$exeHash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$buildInfo = @(
    "SleepyChat $version",
    'Release build',
    "Built UTC: $([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))",
    "SDK: $sdkVersion",
    'Target: win-x64',
    'Configuration: Release',
    'Self-contained: true',
    'Single-file: true',
    "SleepyChat.exe SHA256: $exeHash"
) -join "`r`n"
Set-Content -Path (Join-Path $out 'BUILD_INFO.txt') -Value $buildInfo -Encoding UTF8

Write-Host "SleepyChat $version published successfully: $out"
Write-Host "SleepyChat.exe SHA256: $exeHash"
