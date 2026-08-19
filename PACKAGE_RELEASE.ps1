param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$version = '1.0.0'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$distRoot = Join-Path $root 'dist'
$publicFolderName = "SleepyChat $version"
$sourceFolderName = "SleepyChat $version Source"
$publicFolder = Join-Path $distRoot $publicFolderName
$publicZip = Join-Path $distRoot "SleepyChat_${version}_Public.zip"
$sourceZip = Join-Path $distRoot "SleepyChat_${version}_Source.zip"
$sourceStageRoot = Join-Path $distRoot '_source_stage'
$sourceStage = Join-Path $sourceStageRoot $sourceFolderName

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

# Public ZIP: exactly one clean top-level "SleepyChat 1.0.0" folder.
# Developer-only output must never be included in the public package.
Get-ChildItem $publicFolder -Recurse -File -Force | Where-Object {
    $_.Extension -in @('.xml','.pdb','.user','.suo','.log','.tmp')
} | Remove-Item -Force -ErrorAction Stop

Compress-Archive -Path $publicFolder -DestinationPath $publicZip -CompressionLevel Optimal

# Source ZIP: exactly one clean top-level "SleepyChat 1.0.0 Source" folder.
# Copy the canonical source tree while excluding generated/runtime junk.
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

# Validate that both GitHub release downloads extract into one clean folder,
# never loose files at the ZIP root.
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Assert-SingleTopLevelFolder([string]$zipPath, [string]$expectedRoot, [switch]$PublicPackage) {
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $zipPath))
    try {
        if ($zip.Entries.Count -eq 0) { throw "Empty release ZIP: $zipPath" }

        $badRootEntries = @()
        $badPublicFiles = @()
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('\\','/')
            if (-not $name.StartsWith("$expectedRoot/", [StringComparison]::OrdinalIgnoreCase)) {
                $badRootEntries += $name
            }
            if ($PublicPackage -and $name -match '(?i)\.(xml|pdb|user|suo|log|tmp)$') {
                $badPublicFiles += $name
            }
        }

        if ($badRootEntries.Count -gt 0) {
            throw "Release ZIP contains loose or unexpected root entries: $($badRootEntries -join ', ')"
        }
        if ($badPublicFiles.Count -gt 0) {
            throw "Public release ZIP contains developer-only files: $($badPublicFiles -join ', ')"
        }
    } finally {
        $zip.Dispose()
    }
}

Assert-SingleTopLevelFolder $publicZip $publicFolderName -PublicPackage
Assert-SingleTopLevelFolder $sourceZip $sourceFolderName

$publicHash = (Get-FileHash $publicZip -Algorithm SHA256).Hash.ToLowerInvariant()
$sourceHash = (Get-FileHash $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host "Created clean-folder release: $publicZip"
Write-Host "SHA256: $publicHash"
Write-Host "Created clean-folder release: $sourceZip"
Write-Host "SHA256: $sourceHash"
