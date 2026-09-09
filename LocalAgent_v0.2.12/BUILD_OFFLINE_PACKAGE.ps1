$ErrorActionPreference = 'Stop'
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskManifest = Get-Content -LiteralPath (Join-Path $taskRepo 'RELEASE_MANIFEST.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$taskVersion = $taskManifest.webVersion
$taskInstaller = Join-Path $taskRepo $taskManifest.artifacts.agentInstaller
$taskZip = Join-Path $taskRepo $taskManifest.artifacts.offlinePackage
$taskFiles = @(
    $taskInstaller,
    (Join-Path $taskRepo "downloads\OFFLINE_README_v${taskVersion}_KR.txt"),
    (Join-Path $taskRepo "RELEASE_NOTES_v${taskVersion}_KR.md"),
    (Join-Path $taskRepo 'VERSION.txt')
)
# Explicit allowlist. No images, Workspace, personal DB, logs or Cognex libraries.
foreach ($taskFile in $taskFiles) { if (-not (Test-Path -LiteralPath $taskFile -PathType Leaf)) { throw "Missing release input: $taskFile" } }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Write a unique staging ZIP and replace only this versioned build artifact after success.
$taskStage = Join-Path ([IO.Path]::GetTempPath()) ('VisionQC-offline-' + [guid]::NewGuid().ToString('N') + '.zip')
$taskArchive = [IO.Compression.ZipFile]::Open($taskStage,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskFile in $taskFiles) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive,$taskFile,[IO.Path]::GetFileName($taskFile),[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
} finally { $taskArchive.Dispose() }
Copy-Item -LiteralPath $taskStage -Destination $taskZip -Force
Get-FileHash -LiteralPath $taskInstaller,$taskZip -Algorithm SHA256
