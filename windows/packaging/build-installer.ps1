# Requires PowerShell 7, .NET 8 SDK and NSIS 3 Unicode.
param(
  [string]$Nsis = '',
  [string]$Dotnet = '',
  [string]$Version = '3.3.1',
  [string]$OutputDir = '',
  [string]$PublishDir = '',
  [string]$SocialConfigPath = '',
  [switch]$SkipPublish,
  [switch]$AllowMissingSocialConfig,
  [switch]$AllowMissingIeltsAssets,
  [switch]$KeepWork
)
$ErrorActionPreference = 'Stop'

$project = Split-Path $PSScriptRoot -Parent
$versionRoot = Split-Path $project -Parent
$workspaceRoot = Split-Path $versionRoot -Parent
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

function Assert-SafePublishDestination([string]$Path) {
  $full = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
  $projectFull = [IO.Path]::GetFullPath($project).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
  $versionFull = [IO.Path]::GetFullPath($versionRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
  $legacyPublish = [IO.Path]::GetFullPath((Join-Path $project 'publish')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
  if ($full -eq $projectFull -or $full -eq $versionFull -or $projectFull.StartsWith($full + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace unsafe publish destination '$full'."
  }
  if ($full.StartsWith($projectFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
      !$full.Equals($legacyPublish, [StringComparison]::OrdinalIgnoreCase)) {
    throw "A publish destination inside the Windows source tree must be '$legacyPublish'. Refusing '$full'."
  }
  return $full
}

$release = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
  Join-Path $versionRoot "Lexi-$Version-Windows-x64"
} else {
  Resolve-FullPath $OutputDir
}
$legacyPublish = Join-Path $project 'publish'
$workBase = Join-Path $versionRoot '.work\windows-packaging'
$generatedDirectories = [Collections.Generic.List[string]]::new()
$publishSnapshotMoved = $false

try {
  if ($SkipPublish) {
    $publish = if ([string]::IsNullOrWhiteSpace($PublishDir)) {
      $legacyPublish
    } else {
      Resolve-FullPath $PublishDir
    }
    $validation = Assert-PublishDirectory -Path $publish -AllowMissingSocialConfig:$AllowMissingSocialConfig -AllowMissingIeltsAssets:$AllowMissingIeltsAssets
  } else {
    $dotnetExe = Resolve-PackagingDotnet -Requested $Dotnet -WorkspaceRoot $workspaceRoot
    $fsrsHelper = Join-Path $project 'native\fsrs-optimizer\target\release\fsrs-optimizer.exe'
    if (!(Test-Path -LiteralPath $fsrsHelper -PathType Leaf)) {
      throw "Missing fsrs-optimizer.exe helper at '$fsrsHelper'. Build the Rust helper before publishing."
    }
    if ((Get-Item -LiteralPath $fsrsHelper).Length -le 0) {
      throw "fsrs-optimizer.exe helper is empty at '$fsrsHelper'."
    }

    $publishSnapshot = if ([string]::IsNullOrWhiteSpace($PublishDir)) {
      $legacyPublish
    } else {
      Resolve-FullPath $PublishDir
    }
    $publishSnapshot = Assert-SafePublishDestination $publishSnapshot

    $publishWorkRoot = Join-Path $workBase 'publish'
    $publish = New-PackageStagingDirectory -Root $publishWorkRoot -Label "publish-$Version"
    $generatedDirectories.Add($publish)

    & $dotnetExe publish (Join-Path $project 'Lexi.csproj') -c Release -r win-x64 --self-contained true -o $publish
    if ($LASTEXITCODE -ne 0) {
      throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $targetSocial = Join-Path $publish 'social-public.json'
    if (![string]::IsNullOrWhiteSpace($SocialConfigPath)) {
      $sourceSocial = Resolve-FullPath $SocialConfigPath
      Assert-SocialPublicConfiguration -Path $sourceSocial
      [IO.File]::Copy($sourceSocial, $targetSocial, $false)
    } elseif (!(Test-Path -LiteralPath $targetSocial -PathType Leaf)) {
      $socialCandidates = @(
        (Join-Path $project 'social-public.json'),
        (Join-Path $publishSnapshot 'social-public.json')
      )
      $existingSocial = $socialCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
      if ($null -ne $existingSocial) {
        Assert-SocialPublicConfiguration -Path $existingSocial
        [IO.File]::Copy($existingSocial, $targetSocial, $false)
      }
    }

    $validation = Assert-PublishDirectory -Path $publish -AllowMissingSocialConfig:$AllowMissingSocialConfig -AllowMissingIeltsAssets:$AllowMissingIeltsAssets
  }

  $staging = New-PackageStagingDirectory -Root (Join-Path $workBase 'staging') -Label "installer-$Version"
  $generatedDirectories.Add($staging)
  $copyResult = Copy-CleanPackageTree -Source $publish -Destination $staging
  Assert-CleanPackageTree -Path $staging | Out-Null
  $stagingValidation = Assert-PublishDirectory -Path $staging -AllowMissingSocialConfig:$AllowMissingSocialConfig -AllowMissingIeltsAssets:$AllowMissingIeltsAssets

  $support = New-PackageStagingDirectory -Root (Join-Path $workBase 'support') -Label "installer-$Version"
  $generatedDirectories.Add($support)
  $uninstallList = Join-Path $support 'uninstall-files.nsh'
  $uninstallLines = Write-PackageUninstallList -StagingPath $staging -Destination $uninstallList
  if ($uninstallLines -le 0) {
    throw 'Generated uninstall list is empty.'
  }

  $nsisExe = Resolve-PackagingNsis -Requested $Nsis -WorkspaceRoot $workspaceRoot
  $buildOutput = New-PackageStagingDirectory -Root (Join-Path $workBase 'output') -Label "installer-$Version"
  $generatedDirectories.Add($buildOutput)

  Push-Location $PSScriptRoot
  try {
    & $nsisExe /INPUTCHARSET UTF8 /V2 "/DVERSION=$Version" "/DOUTDIR=$buildOutput" "/DSTAGING=$staging" "/DUNINSTALL_LIST=$uninstallList" installer.nsi
    if ($LASTEXITCODE -ne 0) {
      throw "NSIS installer build failed with exit code $LASTEXITCODE."
    }
  } finally {
    Pop-Location
  }

  $builtInstaller = Join-Path $buildOutput "Lexi-$Version-x64-setup.exe"
  Assert-InstallerFile -Path $builtInstaller
  New-Item -ItemType Directory -Force -Path $release | Out-Null
  Copy-Item -LiteralPath $builtInstaller -Destination (Join-Path $release "Lexi-$Version-x64-setup.exe") -Force

  if (!$SkipPublish) {
    $publishSnapshotParent = Split-Path $publishSnapshot -Parent
    New-Item -ItemType Directory -Force -Path $publishSnapshotParent | Out-Null
    Remove-PackageGeneratedDirectory -Path $publishSnapshot -Root $publishSnapshotParent
    Move-Item -LiteralPath $staging -Destination $publishSnapshot
    $publishSnapshotMoved = $true
  }

  [pscustomobject]@{
    Installer = Join-Path $release "Lexi-$Version-x64-setup.exe"
    PublishSource = $publish
    CleanStage = $(if ($publishSnapshotMoved) { $publishSnapshot } else { $staging })
    CopiedFiles = $copyResult.CopiedCount
    ExcludedFiles = $copyResult.Excluded.Count
    UninstallEntries = $uninstallLines
    IeltsVerified = $stagingValidation.IeltsVerified
    SocialConfigPath = $stagingValidation.SocialConfigPath
  } | Format-List
} finally {
  if ($KeepWork) {
    Write-Warning "Packaging work files were kept under '$workBase'."
  } else {
    for ($i = $generatedDirectories.Count - 1; $i -ge 0; $i--) {
      $generated = $generatedDirectories[$i]
      if ($publishSnapshotMoved -and $generated -eq $staging) {
        continue
      }
      Remove-PackageGeneratedDirectory -Path $generated -Root $workBase
    }
  }
}
