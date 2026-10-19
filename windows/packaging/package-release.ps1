param(
  [string]$Version = '3.3.1',
  [string]$OutputDir = '',
  [string]$PublishDir = '',
  [string]$InstallerPath = '',
  [switch]$ValidateOnly,
  [switch]$AllowMissingSocialConfig,
  [switch]$AllowMissingIeltsAssets,
  [switch]$CopyToDesktop
)
$ErrorActionPreference = 'Stop'

$project = Split-Path $PSScriptRoot -Parent
$versionRoot = Split-Path $project -Parent
$common = Join-Path $PSScriptRoot 'packaging-common.ps1'
if (!(Test-Path -LiteralPath $common -PathType Leaf)) {
  throw "Missing packaging helper: '$common'."
}
. $common

function Resolve-FullPath([string]$Path) {
  if ([IO.Path]::IsPathRooted($Path)) {
    return [IO.Path]::GetFullPath($Path)
  }
  return [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

$publish = if ([string]::IsNullOrWhiteSpace($PublishDir)) {
  Join-Path $project 'publish'
} else {
  Resolve-FullPath $PublishDir
}

# Publish validation must happen before any installer discovery or artifact reuse.
$publishValidation = Assert-PublishDirectory -Path $publish -AllowMissingSocialConfig:$AllowMissingSocialConfig -AllowMissingIeltsAssets:$AllowMissingIeltsAssets

$release = Join-Path $versionRoot "Lexi-$Version-Windows-x64"
$installer = if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
  Join-Path $release "Lexi-$Version-x64-setup.exe"
} else {
  Resolve-FullPath $InstallerPath
}
if ($ValidateOnly) {
  if (![string]::IsNullOrWhiteSpace($InstallerPath) -or (Test-Path -LiteralPath $installer -PathType Leaf)) {
    Assert-InstallerFile -Path $installer
  }
} else {
  Assert-InstallerFile -Path $installer
}

$workBase = Join-Path $versionRoot '.work\windows-packaging-release'
$generatedDirectories = [Collections.Generic.List[string]]::new()
$releaseMoved = $false

try {
  $publishStage = New-PackageStagingDirectory -Root (Join-Path $workBase 'publish') -Label "release-$Version"
  $generatedDirectories.Add($publishStage)
  $publishCopy = Copy-CleanPackageTree -Source $publish -Destination $publishStage
  Assert-CleanPackageTree -Path $publishStage | Out-Null
  Assert-PublishDirectory -Path $publishStage -AllowMissingSocialConfig:$AllowMissingSocialConfig -AllowMissingIeltsAssets:$AllowMissingIeltsAssets | Out-Null
  $uninstallSupport = New-PackageStagingDirectory -Root (Join-Path $workBase 'support') -Label "release-$Version"
  $generatedDirectories.Add($uninstallSupport)
  $uninstallLines = Write-PackageUninstallList -StagingPath $publishStage -Destination (Join-Path $uninstallSupport 'uninstall-files.nsh')
  if ($uninstallLines -le 0) {
    throw 'Generated uninstall list is empty.'
  }

  if ($ValidateOnly) {
    [pscustomobject]@{
      ValidateOnly = $true
      Publish = $publish
      Installer = $(if (Test-Path -LiteralPath $installer -PathType Leaf) { $installer } else { $null })
      CopiedFiles = $publishCopy.CopiedCount
      ExcludedFiles = $publishCopy.Excluded.Count
      ExcludedPaths = @($publishCopy.Excluded | ForEach-Object { $_.RelativePath + ' [' + $_.Reason + ']' }) -join '; '
      UninstallEntries = $uninstallLines
      SocialConfigPath = $publishValidation.SocialConfigPath
      IeltsVerified = $publishValidation.IeltsVerified
    } | Format-List
    return
  }

  $releaseStage = New-PackageStagingDirectory -Root (Join-Path $workBase 'release') -Label "release-$Version"
  $generatedDirectories.Add($releaseStage)
  Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination (Join-Path $releaseStage '使用说明.md') -Force
  Copy-Item -LiteralPath (Join-Path $project 'RELEASE-NOTES.md') -Destination (Join-Path $releaseStage '版本说明.md') -Force
  if (Test-Path -LiteralPath (Join-Path $project 'ACCEPTANCE.md') -PathType Leaf) {
    Copy-Item -LiteralPath (Join-Path $project 'ACCEPTANCE.md') -Destination (Join-Path $releaseStage '验收说明.md') -Force
  }
  if (Test-Path -LiteralPath (Join-Path $project 'Notices') -PathType Container) {
    Copy-Item -LiteralPath (Join-Path $project 'Notices') -Destination (Join-Path $releaseStage '第三方许可') -Recurse -Force
  }
  Copy-Item -LiteralPath $installer -Destination (Join-Path $releaseStage "Lexi-$Version-x64-setup.exe") -Force
  Assert-CleanPackageTree -Path $releaseStage | Out-Null

  $hashes = Get-ChildItem -LiteralPath $releaseStage -File |
    Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
    Get-FileHash -Algorithm SHA256 |
    ForEach-Object { $_.Hash + '  ' + [IO.Path]::GetFileName($_.Path) }
  [IO.File]::WriteAllLines((Join-Path $releaseStage 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($true))

  $distributionZip = Join-Path $versionRoot "Lexi-$Version-Windows-x64-完整分发包.zip"
  $distributionTemp = Join-Path $releaseStage (Split-Path $distributionZip -Leaf)
  $releaseEntries = @(Get-ChildItem -LiteralPath $releaseStage -Force | Select-Object -ExpandProperty FullName)
  Compress-Archive -LiteralPath $releaseEntries -DestinationPath $distributionTemp -CompressionLevel Optimal

  $sourceZip = Join-Path $versionRoot "Lexi-$Version-Avalonia-源码.zip"
  $sourceTemp = Join-Path $releaseStage (Split-Path $sourceZip -Leaf)
  Add-Type -AssemblyName System.IO.Compression
  $stream = [IO.File]::Create($sourceTemp)
  $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
  try {
    foreach ($file in Get-ChildItem -LiteralPath $project -Recurse -File | Sort-Object FullName) {
      $relative = [IO.Path]::GetRelativePath($project, $file.FullName)
      $normalized = $relative.Replace('\', '/')
      if ($normalized -match '(^|/)(bin|obj|publish|work|\.work|target|\.git|\.avalonia-build-tasks|\.tools|TestResults|\.planning)(/|$)') {
        continue
      }
      if ($null -ne (Get-PackagePathExclusionReason -RelativePath $relative)) {
        continue
      }
      $entry = $zip.CreateEntry('lexi_avalonia/' + $normalized, [IO.Compression.CompressionLevel]::Optimal)
      $entryStream = $entry.Open()
      $inputStream = [IO.File]::OpenRead($file.FullName)
      try {
        $inputStream.CopyTo($entryStream)
      } finally {
        $inputStream.Dispose()
        $entryStream.Dispose()
      }
    }
  } finally {
    $zip.Dispose()
    $stream.Dispose()
  }

  $releaseParent = Split-Path $release -Parent
  New-Item -ItemType Directory -Force -Path $releaseParent | Out-Null
  Remove-PackageGeneratedDirectory -Path $release -Root $releaseParent
  Move-Item -LiteralPath $releaseStage -Destination $release
  $releaseMoved = $true

  $distributionDestination = Join-Path $release (Split-Path $distributionZip -Leaf)
  if (!(Test-Path -LiteralPath $distributionDestination -PathType Leaf)) {
    throw "Distribution archive was not created: '$distributionDestination'."
  }
  $sourceDestination = Join-Path $release (Split-Path $sourceZip -Leaf)
  if (!(Test-Path -LiteralPath $sourceDestination -PathType Leaf)) {
    throw "Source archive was not created: '$sourceDestination'."
  }

  $targetDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $versionRoot
  } else {
    Resolve-FullPath $OutputDir
  }
  New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

  $targetInstaller = Join-Path $targetDir "Lexi-$Version-x64-setup.exe"
  if (![IO.Path]::GetFullPath($installer).Equals([IO.Path]::GetFullPath($targetInstaller), [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $installer -Destination $targetInstaller -Force
  }
  $targetSourceZip = Join-Path $targetDir (Split-Path $sourceDestination -Leaf)
  if (![IO.Path]::GetFullPath($sourceDestination).Equals([IO.Path]::GetFullPath($targetSourceZip), [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $sourceDestination -Destination $targetSourceZip -Force
  }
  $targetDistributionZip = Join-Path $targetDir (Split-Path $distributionDestination -Leaf)
  if (![IO.Path]::GetFullPath($distributionDestination).Equals([IO.Path]::GetFullPath($targetDistributionZip), [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $distributionDestination -Destination $targetDistributionZip -Force
  }

  $workspacePackageFiles = @($targetInstaller, $targetSourceZip, $targetDistributionZip)
  $packageHashes = $workspacePackageFiles |
    Get-FileHash -Algorithm SHA256 |
    ForEach-Object { $_.Hash + '  ' + [IO.Path]::GetFileName($_.Path) }
  [IO.File]::WriteAllLines((Join-Path $targetDir 'SHA256SUMS.txt'), $packageHashes, [Text.UTF8Encoding]::new($true))

  if ($CopyToDesktop) {
    $desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) "Lexi $Version Windows"
    New-Item -ItemType Directory -Force -Path $desktop | Out-Null
    Copy-Item -LiteralPath $targetSourceZip,$targetDistributionZip,$targetInstaller -Destination $desktop -Force
    [IO.File]::WriteAllLines((Join-Path $desktop 'SHA256SUMS.txt'), $packageHashes, [Text.UTF8Encoding]::new($true))
  }

  Get-ChildItem -LiteralPath $targetDir -File |
    Where-Object { $_.Name -match "^(Lexi-$Version|SHA256SUMS\.txt)" } |
    Select-Object FullName, Length
} finally {
  for ($i = $generatedDirectories.Count - 1; $i -ge 0; $i--) {
    $generated = $generatedDirectories[$i]
    if ($releaseMoved -and $generated -eq $releaseStage) {
      continue
    }
    Remove-PackageGeneratedDirectory -Path $generated -Root $workBase
  }
}
