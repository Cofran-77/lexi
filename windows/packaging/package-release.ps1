param([string]$Version = '1.1.1')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$outputs = Split-Path $project -Parent
$release = Join-Path $outputs "Lexi-$Version-Windows-x64"
$installer = Join-Path $release "Lexi-$Version-x64-setup.exe"
if (!(Test-Path -LiteralPath $installer)) { throw 'Build the installer first.' }
Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination (Join-Path $release '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $project 'RELEASE-NOTES.md') -Destination (Join-Path $release '版本说明.md') -Force
Copy-Item -LiteralPath (Join-Path $project 'PHASE-ONE-STATUS.md') -Destination (Join-Path $release '阶段一完成情况.md') -Force
Copy-Item -LiteralPath (Join-Path $project 'Notices') -Destination (Join-Path $release '第三方许可') -Recurse -Force
$sourceZip = Join-Path $outputs "Lexi-$Version-Avalonia-源码.zip"
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Create($sourceZip)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
  Get-ChildItem -LiteralPath $project -Recurse -File | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($project, $_.FullName)
    if ($relative -match '(^|[\\/])(bin|obj|publish|work|\.git)([\\/])') { return }
    if ($relative -match '(vocab\.sqlite3|\.dpapi|\.env$|appsettings\.local\.json)') { throw "Unexpected private file: $relative" }
    $entry = $zip.CreateEntry('lexi_avalonia/' + $relative.Replace('\','/'), [IO.Compression.CompressionLevel]::Optimal)
    $entryStream = $entry.Open()
    $inputStream = [IO.File]::OpenRead($_.FullName)
    try { $inputStream.CopyTo($entryStream) } finally { $inputStream.Dispose(); $entryStream.Dispose() }
  }
} finally { $zip.Dispose(); $stream.Dispose() }
$hashes = Get-ChildItem -LiteralPath $release -File | Where-Object Name -ne 'SHA256SUMS.txt' | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash + '  ' + [IO.Path]::GetFileName($_.Path) }
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($true))
$distributionZip = Join-Path $outputs "Lexi-$Version-Windows-x64-完整分发包.zip"
Compress-Archive -LiteralPath $release -DestinationPath $distributionZip -Force
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) "Lexi $Version Windows"
New-Item -ItemType Directory -Force $desktop | Out-Null
Copy-Item -LiteralPath $sourceZip,$distributionZip,$installer -Destination $desktop -Force
$desktopHashes = Get-ChildItem -LiteralPath $desktop -File | Where-Object Name -ne 'SHA256SUMS.txt' | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash + '  ' + [IO.Path]::GetFileName($_.Path) }
[IO.File]::WriteAllLines((Join-Path $desktop 'SHA256SUMS.txt'), $desktopHashes, [Text.UTF8Encoding]::new($true))
Get-ChildItem -LiteralPath $desktop -File | Select-Object FullName,Length
