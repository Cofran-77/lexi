# Requires PowerShell 7, .NET 8 SDK and NSIS 3 Unicode.
param([string]$Nsis = 'C:\Program Files (x86)\NSIS\makensis.exe', [string]$Dotnet = 'dotnet', [switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $project 'publish'
if (!$SkipPublish) {
  & $Dotnet publish (Join-Path $project 'Lexi.csproj') -c Release -r win-x64 --self-contained true -o $publish
  if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
if (!(Test-Path (Join-Path $publish 'Lexi.exe'))) { throw 'Publish the application first.' }
if (!(Test-Path $Nsis)) { throw 'Specify the NSIS compiler with -Nsis.' }
$release = Join-Path (Split-Path $project -Parent) 'Lexi-1.1.4-Windows-x64'
New-Item -ItemType Directory -Force $release | Out-Null
$lines = [Collections.Generic.List[string]]::new()
Get-ChildItem $publish -Recurse -File | Where-Object Extension -ne '.pdb' | ForEach-Object {
  $lines.Add('Delete "$INSTDIR\' + [IO.Path]::GetRelativePath($publish, $_.FullName) + '"')
}
Get-ChildItem $publish -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
  $lines.Add('RMDir "$INSTDIR\' + [IO.Path]::GetRelativePath($publish, $_.FullName) + '"')
}
[IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'lexi-uninstall-files.nsh'), $lines, [Text.UTF8Encoding]::new($true))
Push-Location $PSScriptRoot
try {
  & $Nsis /INPUTCHARSET UTF8 /V2 installer.nsi
  if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
} finally { Pop-Location }
