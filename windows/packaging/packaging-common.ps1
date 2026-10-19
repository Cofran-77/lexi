$ErrorActionPreference = 'Stop'

function Resolve-PackagingDotnet {
  param(
    [string]$Requested,
    [string]$WorkspaceRoot
  )

  if (![string]::IsNullOrWhiteSpace($Requested)) {
    if ($Requested.IndexOfAny([char[]]@('\','/')) -ge 0 -or $Requested.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) {
      if (!(Test-Path -LiteralPath $Requested)) {
        throw "dotnet executable not found at '$Requested'."
      }
      return (Resolve-Path -LiteralPath $Requested).Path
    }
    return $Requested
  }

  $local = Join-Path $WorkspaceRoot '.tools\dotnet\dotnet.exe'
  if (Test-Path -LiteralPath $local) {
    return (Resolve-Path -LiteralPath $local).Path
  }
  $command = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($null -ne $command) {
    return $command.Source
  }
  throw "dotnet was not found. Pass -Dotnet with a local dotnet.exe path."
}

function Resolve-PackagingNsis {
  param(
    [string]$Requested,
    [string]$WorkspaceRoot
  )

  if (![string]::IsNullOrWhiteSpace($Requested)) {
    if (!(Test-Path -LiteralPath $Requested)) {
      throw "NSIS compiler not found at '$Requested'. Specify it with -Nsis."
    }
    return (Resolve-Path -LiteralPath $Requested).Path
  }

  $candidates = @(
    (Join-Path $WorkspaceRoot '.tools\nsis-portable\nsis-3.13\Bin\makensis.exe'),
    (Join-Path $WorkspaceRoot '.tools\nsis-portable\nsis-3.13\makensis.exe'),
    'C:\Program Files (x86)\NSIS\makensis.exe',
    'C:\Program Files\NSIS\makensis.exe'
  )
  foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) {
      return (Resolve-Path -LiteralPath $candidate).Path
    }
  }

  $portableRoot = Join-Path $WorkspaceRoot '.tools\nsis-portable'
  if (Test-Path -LiteralPath $portableRoot) {
    $portable = Get-ChildItem -LiteralPath $portableRoot -Recurse -File -Filter 'makensis.exe' -ErrorAction SilentlyContinue |
      Sort-Object { $_.FullName.Length } |
      Select-Object -First 1
    if ($null -ne $portable) {
      return $portable.FullName
    }
  }

  throw "NSIS compiler was not found. Pass -Nsis with the makensis.exe path."
}

function New-PackageStagingDirectory {
  param(
    [Parameter(Mandatory = $true)][string]$Root,
    [string]$Label = 'package'
  )

  $safeLabel = $Label -replace '[^A-Za-z0-9._-]+', '-'
  if ([string]::IsNullOrWhiteSpace($safeLabel)) {
    $safeLabel = 'package'
  }
  New-Item -ItemType Directory -Force -Path $Root | Out-Null
  for ($attempt = 0; $attempt -lt 20; $attempt++) {
    $candidate = Join-Path $Root ($safeLabel + '-' + (Get-Date).ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
    if (!(Test-Path -LiteralPath $candidate)) {
      New-Item -ItemType Directory -Path $candidate | Out-Null
      return (Resolve-Path -LiteralPath $candidate).Path
    }
  }
  throw "Could not create a unique staging directory under '$Root'."
}

function Get-PackagePathExclusionReason {
  param(
    [Parameter(Mandatory = $true)][string]$RelativePath,
    [switch]$IsDirectory
  )

  $path = $RelativePath.Replace('\', '/').TrimStart('/')
  $name = [IO.Path]::GetFileName($path)
  $directoryPath = Split-Path -Path $path -Parent
  if ([string]::IsNullOrWhiteSpace($directoryPath)) {
    $directoryPath = ''
  }
  $containerPath = if ($IsDirectory) { $path } else { $directoryPath }

  if ($path -ieq 'social-public.json') {
    return $null
  }
  if ($path -ieq 'Assets/dictionary.sqlite3') {
    return $null
  }
  if ($name -ieq 'Lexi.runtimeconfig.json' -or $name -ieq 'Lexi.deps.json') {
    return $null
  }
  if ($name -match '(?i)\.pdb$') {
    return 'debug symbols are not distributed'
  }
  if ($name -match '(?i)^social-public(?:\.example)?\.json$') {
    return 'only the root social-public.json release config is allowed'
  }

  if ($containerPath -match '(?i)(^|/)(data|user-data|personal|profiles?|backups?|exports?|logs?|temp|tmp|cache|\.cache|\.lexi|\.local|\.git|\.vs|\.planning|TestResults)(?:[-_.][^/]*)?(/|$)') {
    return 'runtime, personal, VCS, or cache directory'
  }
  if ($containerPath -match '(?i)(^|/)(config|configuration|credentials?|secrets?|tokens?|cookies?)(?:[-_.][^/]*)?(/|$)') {
    return 'local configuration or credential directory'
  }

  if ($path -match '(?i)\.(sqlite3?|db)(?:-(?:wal|shm|journal))?$' -or
      $path -match '(?i)\.(?:wal|shm|journal)$') {
    return 'SQLite database, WAL, SHM, or journal'
  }
  if ($name -match '(?i)\.(?:dpapi|protected)$') {
    return 'DPAPI or protected credential file'
  }
  if ($name -match '(?i)\.log(?:\.\d+)?$' -or
      $name -match '(?i)-(?:result|results)\.(?:txt|json|log)$' -or
      $name -match '(?i)^(?:daily-study-plans|ielts-learning|daily-plans|learning-progress|study-plan|study-progress|progress|app-state|learning-state|session-state|review-state|bookmarks|notes|statistics|history)(?:\..*)?\.json$') {
    return 'log or local verification report'
  }
  if ($name -match '(?i)^\.env(?:\.|$)' -or
      $name -match '(?i)^appsettings.*\.json$' -or
      $name -match '(?i)^(?:settings|config|configuration)(?:\..*)?\.json$' -or
      $name -match '(?i)^(?:profile|user|account|credentials?|secrets?|tokens?|cookies?)(?:\..*)?\.json$' -or
      $name -match '(?i)\.(?:local|user)\.json$' -or
      $name -match '(?i)\.config$') {
    return 'local, personal, credential, or environment configuration'
  }
  if ($name -match '(?i)(?:^|\.)(?:tmp|bak|suo|user)$' -or $name -ieq 'Thumbs.db') {
    return 'temporary or editor user file'
  }
  if ($name -match '(?i)\.(?:jsonl|lock)$') {
    return 'runtime journal, lock, or append-only local data'
  }
  return $null
}

function Test-PackagePathExcluded {
  param([Parameter(Mandatory = $true)][string]$RelativePath)
  return $null -ne (Get-PackagePathExclusionReason -RelativePath $RelativePath)
}

function Copy-CleanPackageTree {
  param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination
  )

  if (!(Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Package source directory is missing: '$Source'."
  }
  $sourceFull = (Resolve-Path -LiteralPath $Source).Path
  if (!(Test-Path -LiteralPath $Destination -PathType Container)) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  }
  $destinationFull = (Resolve-Path -LiteralPath $Destination).Path
  if (@(Get-ChildItem -LiteralPath $destinationFull -Force).Count -gt 0) {
    throw "Clean staging destination must be empty: '$destinationFull'."
  }
  $sourcePrefix = $sourceFull.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
  $destinationPrefix = $destinationFull.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
  if ($destinationFull.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase) -or
      $sourceFull.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package source and staging destination must not contain one another: '$sourceFull' -> '$destinationFull'."
  }

  $copied = 0
  $excluded = [Collections.Generic.List[object]]::new()
  foreach ($file in Get-ChildItem -LiteralPath $sourceFull -Recurse -File | Sort-Object FullName) {
    $relative = [IO.Path]::GetRelativePath($sourceFull, $file.FullName)
    $reason = Get-PackagePathExclusionReason -RelativePath $relative
    if ($null -ne $reason) {
      $excluded.Add([pscustomobject]@{ RelativePath = $relative; Reason = $reason })
      continue
    }
    $target = Join-Path $destinationFull $relative
    $targetDirectory = Split-Path $target -Parent
    if (!(Test-Path -LiteralPath $targetDirectory -PathType Container)) {
      New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    }
    [IO.File]::Copy($file.FullName, $target, $false)
    $copied++
  }
  foreach ($directory in Get-ChildItem -LiteralPath $sourceFull -Recurse -Directory | Sort-Object { $_.FullName.Length }) {
    $relative = [IO.Path]::GetRelativePath($sourceFull, $directory.FullName)
    if ($null -ne (Get-PackagePathExclusionReason -RelativePath $relative -IsDirectory)) {
      continue
    }
    $target = Join-Path $destinationFull $relative
    if (!(Test-Path -LiteralPath $target -PathType Container)) {
      New-Item -ItemType Directory -Force -Path $target | Out-Null
    }
  }

  return [pscustomobject]@{
    Source = $sourceFull
    Destination = $destinationFull
    CopiedCount = $copied
    Excluded = $excluded
  }
}

function Assert-CleanPackageTree {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (!(Test-Path -LiteralPath $Path -PathType Container)) {
    throw "Package staging directory is missing: '$Path'."
  }
  $root = (Resolve-Path -LiteralPath $Path).Path
  $bad = [Collections.Generic.List[object]]::new()
  foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
    $reason = Get-PackagePathExclusionReason -RelativePath $relative
    if ($null -ne $reason) {
      $bad.Add([pscustomobject]@{ RelativePath = $relative; Reason = $reason })
    }
  }
  if ($bad.Count -gt 0) {
    $detail = ($bad | ForEach-Object { "$($_.RelativePath) [$($_.Reason)]" }) -join '; '
    throw "Staging contains files that must not be released: $detail"
  }
  return $bad.Count
}

function Write-PackageUninstallList {
  param(
    [Parameter(Mandatory = $true)][string]$StagingPath,
    [Parameter(Mandatory = $true)][string]$Destination
  )

  Assert-CleanPackageTree -Path $StagingPath | Out-Null
  $root = (Resolve-Path -LiteralPath $StagingPath).Path
  $lines = [Collections.Generic.List[string]]::new()

  foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('$', '$$')
    $lines.Add('Delete "$INSTDIR\' + $relative + '"')
  }
  foreach ($directory in Get-ChildItem -LiteralPath $root -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending) {
    $relative = [IO.Path]::GetRelativePath($root, $directory.FullName).Replace('$', '$$')
    $lines.Add('RMDir "$INSTDIR\' + $relative + '"')
  }

  $destinationDirectory = Split-Path $Destination -Parent
  if (!(Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
  }
  [IO.File]::WriteAllLines($Destination, $lines, [Text.UTF8Encoding]::new($true))
  return $lines.Count
}

function Remove-PackageGeneratedDirectory {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Root
  )

  if (!(Test-Path -LiteralPath $Path -PathType Container)) {
    return
  }
  $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
  $pathFull = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
  $prefix = $rootFull + [IO.Path]::DirectorySeparatorChar
  if (!$pathFull.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove a directory outside '$rootFull': '$pathFull'."
  }
  [IO.Directory]::Delete($pathFull, $true)
}

function Assert-SocialPublicConfiguration {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "social-public.json is missing from '$Path'."
  }
  $config = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
  $configUrl = [string]$config.project_url
  $configKey = [string]$config.public_key
  $configUri = $null
  if (![Uri]::TryCreate($configUrl, [UriKind]::Absolute, [ref]$configUri) -or
      $configUri.Scheme -ne 'https' -or
      !$configUri.Host.EndsWith('.supabase.co', [StringComparison]::OrdinalIgnoreCase)) {
    throw "social-public.json must hold the shared HTTPS *.supabase.co project URL."
  }
  if (!$configKey.StartsWith('sb_publishable_', [StringComparison]::Ordinal) -or $configKey.Length -le 15) {
    throw 'social-public.json must hold the shared publishable client key (sb_publishable_*). Server credentials are never packaged.'
  }
}

function Assert-InstallerFile {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Installer is missing: '$Path'."
  }
  $item = Get-Item -LiteralPath $Path
  if ($item.Length -lt 2) {
    throw "Installer is not a valid Windows executable: '$Path'."
  }
  $stream = [IO.File]::OpenRead($item.FullName)
  try {
    $signature = [byte[]]::new(2)
    [void]$stream.Read($signature, 0, 2)
  } finally {
    $stream.Dispose()
  }
  if ($signature[0] -ne 0x4D -or $signature[1] -ne 0x5A) {
    throw "Installer does not have a Windows PE/MZ signature: '$Path'."
  }
}

function Assert-PublishDirectory {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [switch]$AllowMissingSocialConfig,
    [switch]$AllowMissingIeltsAssets
  )

  if (!(Test-Path -LiteralPath $Path -PathType Container)) {
    throw "Publish directory is missing: '$Path'. Run build-installer without -SkipPublish or create a complete publish directory first."
  }
  $root = (Resolve-Path -LiteralPath $Path).Path
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
    'Assets\Exams\manifest.json'
  )
  $missing = [Collections.Generic.List[string]]::new()
  $empty = [Collections.Generic.List[string]]::new()
  foreach ($relative in $requiredFiles) {
    $candidate = Join-Path $root $relative
    if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) {
      $missing.Add($relative)
      continue
    }
    if ((Get-Item -LiteralPath $candidate).Length -le 0) {
      $empty.Add($relative)
    }
  }
  if ($missing.Count -gt 0) {
    throw "Publish directory is incomplete; missing required files: $($missing -join ', ')."
  }
  if ($empty.Count -gt 0) {
    throw "Publish directory is incomplete; required files are empty: $($empty -join ', ')."
  }

  $socialPath = Join-Path $root 'social-public.json'
  if (Test-Path -LiteralPath $socialPath -PathType Leaf) {
    Assert-SocialPublicConfiguration -Path $socialPath
  } elseif (!$AllowMissingSocialConfig) {
    throw "social-public.json is missing from publish directory '$root'. Inject the public project URL and publishable key before packaging."
  }

  $ieltsRequirements = @(
    'Assets\IELTS\catalog.json',
    'Assets\IELTS\manifest.json',
    'Assets\IELTS\179_audios',
    'Assets\IELTS\vocabulary\audio'
  )
  $missingIelts = [Collections.Generic.List[string]]::new()
  foreach ($relative in $ieltsRequirements) {
    $expectedType = if ($relative -match '\.json$') { 'Leaf' } else { 'Container' }
    if (!(Test-Path -LiteralPath (Join-Path $root $relative) -PathType $expectedType)) {
      $missingIelts.Add($relative)
    }
  }
  if ($missingIelts.Count -gt 0) {
    if (!$AllowMissingIeltsAssets) {
      throw "Publish output verification failed: IELTS assets are incomplete ($($missingIelts -join ', '))."
    }
    Write-Warning ('IELTS assets are incomplete; this development build omits the IELTS practice material: ' + ($missingIelts -join ', '))
    return [pscustomobject]@{ Root = $root; SocialConfigPath = $(if (Test-Path -LiteralPath $socialPath) { $socialPath } else { $null }); IeltsVerified = $false }
  }

  $catalog = Get-Content -LiteralPath (Join-Path $root 'Assets\IELTS\catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  $manifest = Get-Content -LiteralPath (Join-Path $root 'Assets\IELTS\manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  $vocabularySections = @($catalog.sections | Where-Object { $_.kind -eq 'vocabulary' })
  $counts = [ordered]@{
    chapters = $vocabularySections.Count
    vocabularyEntries = @($vocabularySections | ForEach-Object { $_.entries }).Count
    listeningEntries = @($catalog.sections | Where-Object { $_.kind -eq 'listening' } | ForEach-Object { $_.entries }).Count
    readingEntries = @($catalog.sections | Where-Object { $_.kind -eq 'reading' } | ForEach-Object { $_.entries }).Count
    spellingEntries = @($catalog.sections | Where-Object { $_.kind -eq 'spelling' } | ForEach-Object { $_.entries }).Count
    writingSentences = @($catalog.sentences).Count
  }
  $expected = [ordered]@{
    chapters = 22
    vocabularyEntries = 3674
    listeningEntries = 179
    readingEntries = 376
    spellingEntries = 72
    writingSentences = 100
  }
  foreach ($key in $expected.Keys) {
    if ([int]$manifest.$key -ne $expected[$key] -or $counts[$key] -ne $expected[$key]) {
      if (!$AllowMissingIeltsAssets) {
        throw "IELTS asset verification failed: $key must be $($expected[$key]); catalog=$($counts[$key]), manifest=$([int]$manifest.$key)."
      }
      Write-Warning "IELTS asset verification is incomplete; this development build will omit the affected IELTS material: $key must be $($expected[$key]); catalog=$($counts[$key]), manifest=$([int]$manifest.$key)."
      return [pscustomobject]@{ Root = $root; SocialConfigPath = $socialPath; IeltsVerified = $false }
    }
  }
  $listeningAudio = @(Get-ChildItem -LiteralPath (Join-Path $root 'Assets\IELTS\179_audios') -File -Filter '*.mp3').Count
  if ($listeningAudio -lt $counts.listeningEntries) {
    if (!$AllowMissingIeltsAssets) {
      throw "IELTS asset verification failed: 179_audios holds $listeningAudio mp3 files for $($counts.listeningEntries) listening entries."
    }
    Write-Warning "IELTS asset verification is incomplete; this development build will omit the affected listening material: 179_audios holds $listeningAudio mp3 files for $($counts.listeningEntries) listening entries."
    return [pscustomobject]@{ Root = $root; SocialConfigPath = $socialPath; IeltsVerified = $false }
  }
  $vocabularyAudio = @(Get-ChildItem -LiteralPath (Join-Path $root 'Assets\IELTS\vocabulary\audio') -Recurse -File -Filter '*.mp3').Count
  if ($vocabularyAudio -lt 1) {
    if (!$AllowMissingIeltsAssets) {
      throw 'IELTS asset verification failed: vocabulary audio is missing.'
    }
    Write-Warning 'IELTS asset verification is incomplete; this development build will omit vocabulary audio.'
    return [pscustomobject]@{ Root = $root; SocialConfigPath = $socialPath; IeltsVerified = $false }
  }

  return [pscustomobject]@{ Root = $root; SocialConfigPath = $(if (Test-Path -LiteralPath $socialPath) { $socialPath } else { $null }); IeltsVerified = $true }
}
