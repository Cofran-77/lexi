param(
  [string]$Version = '3.3.1'
)
$ErrorActionPreference = 'Stop'

$project = Split-Path $PSScriptRoot -Parent
$versionRoot = Split-Path $project -Parent
$common = Join-Path $PSScriptRoot 'packaging-common.ps1'
if (!(Test-Path -LiteralPath $common)) {
  throw "Missing packaging helper: $common"
}
. $common

$testRoot = Join-Path (Join-Path $versionRoot '.work') ('packaging-selftest-' + [Guid]::NewGuid().ToString('N'))
$report = [Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$message) {
  if (!$condition) { throw "FAIL $message" }
  $report.Add("PASS $message")
}

try {
  $parseFailures = [Collections.Generic.List[string]]::new()
  foreach ($scriptFile in Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.ps1') {
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName, [ref]$null, [ref]$parseErrors)
    foreach ($parseError in @($parseErrors)) {
      if ($null -ne $parseError) {
        $parseFailures.Add($scriptFile.Name + ': ' + $parseError.Message)
      }
    }
  }
  Check ($parseFailures.Count -eq 0) ('packaging scripts parse successfully' + $(if ($parseFailures.Count) { ': ' + ($parseFailures -join '; ') } else { '' }))

  $source = Join-Path $testRoot 'publish'
  $stageRoot = Join-Path $testRoot 'stages'
  New-Item -ItemType Directory -Force -Path $source,$stageRoot | Out-Null

  $requiredFiles = @(
    'Lexi.exe',
    'Lexi.dll',
    'Lexi.Application.dll',
    'Lexi.Core.dll',
    'Lexi.Domain.dll',
    'Lexi.Infrastructure.dll',
    'Lexi.deps.json',
    'Lexi.runtimeconfig.json',
    'fsrs-optimizer.exe',
    'Assets\dictionary.sqlite3',
    'Assets\Exams\catalog.json',
    'Assets\Exams\manifest.json',
    'Assets\IELTS\179_audios\export.mp3',
    'Assets\IELTS\vocabulary\audio\data.mp3',
    'Assets\IELTS\vocabulary\audio\profile.mp3',
    'social-public.json',
    'appsettings.local.json',
    'vocab.sqlite3',
    'vocab.sqlite3-wal',
    'vocab.sqlite3-shm',
    'Assets\social.sqlite3-journal',
    'api-key.dpapi',
    'runtime.log',
    'events.jsonl',
    'install.lock',
    'learning-progress.json',
    'profiles\profile.json',
    'personal\notes.txt',
    'config\settings.json',
    'logs\packaging.log',
    'social-public.example.json'
  )
  foreach ($relative in $requiredFiles) {
    $path = Join-Path $source $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
    Set-Content -LiteralPath $path -Value $relative -Encoding utf8
  }
  Set-Content -LiteralPath (Join-Path $source 'social-public.json') -Value '{"project_url":"https://example.supabase.co","public_key":"sb_publishable_test_only_1234567890"}' -Encoding utf8

  Check (-not (Test-PackagePathExcluded 'social-public.json')) 'root social-public.json is allowed'
  Check (-not (Test-PackagePathExcluded 'Assets/dictionary.sqlite3')) 'bundled read-only dictionary is allowed'
  Check (-not (Test-PackagePathExcluded 'Assets/IELTS/179_audios/export.mp3')) 'audio named export.mp3 is allowed'
  Check (-not (Test-PackagePathExcluded 'Assets/IELTS/vocabulary/audio/data.mp3')) 'audio named data.mp3 is allowed'
  Check (-not (Test-PackagePathExcluded 'Assets/IELTS/vocabulary/audio/profile.mp3')) 'audio named profile.mp3 is allowed'
  Check (Test-PackagePathExcluded 'social-public.example.json') 'example social config is not released'
  Check (Test-PackagePathExcluded 'nested/social-public.json') 'nested social config is not released'
  Check (Test-PackagePathExcluded 'Assets/vocab.sqlite3') 'SQLite database is excluded'
  Check (Test-PackagePathExcluded 'Assets/social.sqlite3-wal') 'SQLite WAL is excluded'
  Check (Test-PackagePathExcluded 'Assets/social.sqlite3-shm') 'SQLite SHM is excluded'
  Check (Test-PackagePathExcluded 'Assets/social.sqlite3-journal') 'SQLite journal is excluded'
  Check (Test-PackagePathExcluded 'api-key.dpapi') 'DPAPI secret is excluded'
  Check (Test-PackagePathExcluded 'runtime.log') 'log file is excluded'
  Check (Test-PackagePathExcluded 'events.jsonl') 'append-only local data is excluded'
  Check (Test-PackagePathExcluded 'install.lock') 'runtime lock is excluded'
  Check (Test-PackagePathExcluded 'learning-progress.json') 'learning progress is excluded'
  Check (Test-PackagePathExcluded 'appsettings.local.json') 'local config is excluded'
  Check (Test-PackagePathExcluded 'config/settings.json') 'local config directory is excluded'
  Check (Test-PackagePathExcluded 'profiles/profile.json') 'profile data is excluded'
  Check (Test-PackagePathExcluded 'logs/packaging.log') 'log directory is excluded'

  $stage1 = New-PackageStagingDirectory -Root $stageRoot -Label 'first'
  $stage2 = New-PackageStagingDirectory -Root $stageRoot -Label 'first'
  Check ($stage1 -ne $stage2) 'each staging directory is unique'
  Check ((Test-Path -LiteralPath $stage1) -and (Test-Path -LiteralPath $stage2)) 'staging directories are created'

  $publishValidation = Assert-PublishDirectory -Path $source -AllowMissingIeltsAssets
  Check (!$publishValidation.IeltsVerified) 'publish fixture allows an explicit development-only IELTS omission'

  $copy = Copy-CleanPackageTree -Source $source -Destination $stage1
  Check ($copy.CopiedCount -ge 8) 'clean copy keeps required release files'
  Check (Test-Path -LiteralPath (Join-Path $stage1 'Lexi.exe')) 'clean copy keeps Lexi.exe'
  Check (Test-Path -LiteralPath (Join-Path $stage1 'Assets\dictionary.sqlite3')) 'clean copy keeps bundled dictionary'
  Check (Test-Path -LiteralPath (Join-Path $stage1 'Assets\IELTS\179_audios\export.mp3')) 'clean copy keeps export-named audio'
  Check (Test-Path -LiteralPath (Join-Path $stage1 'Assets\IELTS\vocabulary\audio\data.mp3')) 'clean copy keeps data-named audio'
  Check (Test-Path -LiteralPath (Join-Path $stage1 'Assets\IELTS\vocabulary\audio\profile.mp3')) 'clean copy keeps profile-named audio'
  foreach ($relative in @('appsettings.local.json','vocab.sqlite3','vocab.sqlite3-wal','vocab.sqlite3-shm','Assets\social.sqlite3-journal','api-key.dpapi','runtime.log','events.jsonl','install.lock','learning-progress.json','profiles\profile.json','personal\notes.txt','config\settings.json','logs\packaging.log','social-public.example.json')) {
    Check (!(Test-Path -LiteralPath (Join-Path $stage1 $relative))) "clean copy excludes $relative"
  }
  Assert-CleanPackageTree -Path $stage1 | Out-Null
  $report.Add('PASS clean staging passes its final content assertion')

  $support = New-PackageStagingDirectory -Root (Join-Path $testRoot 'support') -Label 'first'
  $uninstallList = Join-Path $support 'uninstall-files.nsh'
  $uninstallCount = Write-PackageUninstallList -StagingPath $stage1 -Destination $uninstallList
  $uninstallText = Get-Content -LiteralPath $uninstallList -Raw
  Check ($uninstallCount -gt $copy.CopiedCount) 'uninstall list covers clean files and directories'
  Check ($uninstallText -match '(?i)social-public\.json') 'uninstall list includes the allowed social config'
  Check ($uninstallText -notmatch '(?i)vocab\.sqlite3|appsettings|learning-progress') 'uninstall list excludes local data and config'

  $missing = Join-Path $testRoot 'missing-publish'
  $fakeSetup = Join-Path $testRoot 'Lexi-setup.exe'
  Set-Content -LiteralPath $fakeSetup -Value 'not-an-installer' -Encoding ascii
  $failedClearly = $false
  try {
    & (Join-Path $PSScriptRoot 'package-release.ps1') -Version $Version -PublishDir $missing -InstallerPath $fakeSetup -OutputDir (Join-Path $testRoot 'out') -ValidateOnly
  } catch {
    $failedClearly = $_.Exception.Message -match '(?i)publish directory is missing'
  }
  Check $failedClearly 'package-release fails before using an old setup when publish is missing'

  $missingHelper = Join-Path $source 'fsrs-optimizer.exe'
  $helperBackup = "$missingHelper.missing"
  Move-Item -LiteralPath $missingHelper -Destination $helperBackup
  $helperFailed = $false
  try {
    Assert-PublishDirectory -Path $source -AllowMissingIeltsAssets | Out-Null
  } catch {
    $helperFailed = $_.Exception.Message -match '(?i)missing required files.*fsrs-optimizer\.exe'
  } finally {
    Move-Item -LiteralPath $helperBackup -Destination $missingHelper
  }
  Check $helperFailed 'missing fsrs-optimizer.exe fails with a clear publish error'

  $missingDictionary = Join-Path $source 'Assets\dictionary.sqlite3'
  $dictionaryBackup = "$missingDictionary.missing"
  Move-Item -LiteralPath $missingDictionary -Destination $dictionaryBackup
  $dictionaryFailed = $false
  try {
    Assert-PublishDirectory -Path $source -AllowMissingIeltsAssets | Out-Null
  } catch {
    $dictionaryFailed = $_.Exception.Message -match '(?i)missing required files.*dictionary\.sqlite3'
  } finally {
    Move-Item -LiteralPath $dictionaryBackup -Destination $missingDictionary
  }
  Check $dictionaryFailed 'missing dictionary resource fails with a clear publish error'

  $missingSocial = Join-Path $source 'social-public.json'
  $socialBackup = "$missingSocial.missing"
  Move-Item -LiteralPath $missingSocial -Destination $socialBackup
  $socialFailed = $false
  try {
    Assert-PublishDirectory -Path $source -AllowMissingIeltsAssets | Out-Null
  } catch {
    $socialFailed = $_.Exception.Message -match '(?i)social-public\.json is missing'
  } finally {
    Move-Item -LiteralPath $socialBackup -Destination $missingSocial
  }
  Check $socialFailed 'missing social-public.json fails with a clear publish error'

  $nsi = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer.nsi') -Raw
  Check ($nsi -match '(?im)^\s*!ifndef\s+STAGING\s*$') 'installer requires a staging define'
  Check ($nsi -match '(?im)^\s*!ifndef\s+UNINSTALL_LIST\s*$') 'installer requires a generated uninstall list'
  Check ($nsi -notmatch '(?i)\.\.\\publish') 'installer does not collect the old publish directory'
  Check ($nsi -notmatch '(?i)lexi-uninstall-files\.nsh') 'installer does not use the retired uninstall list'

  $build = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'build-installer.ps1') -Raw
  Check ($build -match 'Copy-CleanPackageTree') 'build-installer materializes a clean staging tree'
  Check ($build -match 'New-PackageStagingDirectory') 'build-installer creates a unique staging directory'
  Check ($build -match 'Assert-PublishDirectory') 'build-installer validates publish before installer compilation'

  $releaseScript = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-release.ps1') -Raw
  Check ($releaseScript -match 'Split-Path \$distributionZip -Leaf') 'release archive destination uses the archive file name'
  Check ($releaseScript -match 'Split-Path \$sourceZip -Leaf') 'source archive destination uses the archive file name'
  Check ($releaseScript -notmatch 'Join-Path \$release \$distributionZip') 'release script does not concatenate an absolute archive path'
  Check ($releaseScript -notmatch '(?i)social-public\.example\.json') 'source archive does not exempt example social config from release exclusions'

  $report
}
finally {
  if (Test-Path -LiteralPath $testRoot) {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
  }
}
