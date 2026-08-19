param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$version = '1.0.0'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$distRoot = Join-Path $root 'dist'
$publicFolder = Join-Path $distRoot "SleepyChat $version"
$publicZip = Join-Path $distRoot "SleepyChat_${version}_Public.zip"
$sourceZip = Join-Path $distRoot "SleepyChat_${version}_Source.zip"
$sourceStageRoot = Join-Path $distRoot '_source_stage'
$sourceStage = Join-Path $sourceStageRoot "SleepyChat $version Source"

if (-not $SkipBuild) {
    & (Join-Path $root 'BUILD_RELEASE.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}

if (-not (Test-Path (Join-Path $publicFolder 'SleepyChat.exe'))) {
    throw 'Public build folder is missing. Run BUILD_RELEASE.ps1 first or omit -SkipBuild.'
}

foreach ($path in @($publicZip,$sourceZip,$sourceStageRoot)) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

# Public ZIP: one top-level "SleepyChat 1.0.0" folder.
Compress-Archive -Path $publicFolder -DestinationPath $publicZip -CompressionLevel Optimal

# Source ZIP: copy the clean source tree while excluding generated/runtime junk.
New-Item -ItemType Directory -Path $sourceStage -Force | Out-Null
$excludeRoot = @('dist','.git','.vs','.vscode','SleepyChat_Data')
Get-ChildItem $root -Force | Where-Object { $excludeRoot -notcontains $_.Name } | ForEach-Object {
    Copy-Item $_.FullName $sourceStage -Recurse -Force
}

Get-ChildItem $sourceStage -Directory -Recurse -Force | Where-Object {
    $_.Name -in @('bin','obj','.vs','.vscode','SleepyChat_Data')
} | Sort-Object FullName -Descending | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Get-ChildItem $sourceStage -File -Recurse -Force | Where-Object {
    $_.Extension -in @('.pdb','.user','.suo','.log','.tmp')
} | Remove-Item -Force -ErrorAction SilentlyContinue

Compress-Archive -Path $sourceStage -DestinationPath $sourceZip -CompressionLevel Optimal
Remove-Item $sourceStageRoot -Recurse -Force

$publicHash = (Get-FileHash $publicZip -Algorithm SHA256).Hash.ToLowerInvariant()
$sourceHash = (Get-FileHash $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host "Created: $publicZip"
Write-Host "SHA256: $publicHash"
Write-Host "Created: $sourceZip"
Write-Host "SHA256: $sourceHash"
