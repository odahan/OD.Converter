param(
    [Parameter(Mandatory = $true)][ValidateSet('Offline', 'Modules')][string]$Phase,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [switch]$OfflineNetworkConfirmed,
    [switch]$DevelopmentHost,
    [ValidatePattern('^(auto|[a-z]{2})$')][string]$Language = 'auto'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($OutputDirectory)
$kit = $PSScriptRoot
$exe = Join-Path $kit 'odconv.exe'
$manifest = Get-Content -LiteralPath (Join-Path $kit 'kit.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.Files) {
    if ([IO.Path]::GetFileName($entry.Name) -ne $entry.Name) { throw 'Unsafe kit manifest path.' }
    $file = Get-Item -LiteralPath (Join-Path $kit $entry.Name)
    if ($file.Length -ne $entry.Bytes -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $entry.Sha256) {
        throw "Kit integrity failed: $($entry.Name)"
    }
}
if ($DevelopmentHost -and $Phase -ne 'Offline') { throw 'DevelopmentHost supports only read-only module checks and offline media processing.' }
if (!$DevelopmentHost -and $Phase -eq 'Offline' -and !$OfflineNetworkConfirmed) {
    throw 'Disconnect the guest network, then pass -OfflineNetworkConfirmed. The script does not disable networking.'
}
if ((Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Force).Count) {
    throw 'Each phase requires a new or empty output directory.'
}
New-Item -ItemType Directory -Path $root -Force | Out-Null
$checks = New-Object 'System.Collections.Generic.List[object]'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$administrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$detected = @(foreach ($name in @('python.exe', 'python3.exe', 'py.exe', 'dotnet.exe', 'ffmpeg.exe')) {
    Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object Name, Source
})
$moduleRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'OD.Converter/modules'
$report = [ordered]@{
    Schema = 1; Phase = $Phase; StartedUtc = [DateTime]::UtcNow.ToString('o')
    ExecutableSha256 = (Get-FileHash -LiteralPath $exe).Hash
    KitManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $kit 'kit.json')).Hash
    Computer = $env:COMPUTERNAME; OS = [Environment]::OSVersion.VersionString
    PowerShell = $PSVersionTable.PSVersion.ToString(); Cpu = $env:PROCESSOR_IDENTIFIER
    LogicalProcessors = [Environment]::ProcessorCount; AdministratorToken = $administrator
    DetectedHostTools = $detected; DevelopmentHost = [bool]$DevelopmentHost
    OfflineNetworkConfirmedByOperator = [bool]$OfflineNetworkConfirmed
    CleanWindowsVerified = $false; HumanReviewCompleted = $false
    Checks = $checks; Passed = $false
}

function Invoke-Odconv {
    param([string[]]$Arguments, [int]$Expected = 0)
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $exe @Arguments 2>&1)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    $output = ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    [ordered]@{ Arguments = $Arguments; ExitCode = $code; Output = $output } |
        ConvertTo-Json -Compress -Depth 4 | Add-Content -LiteralPath (Join-Path $root 'commands.jsonl') -Encoding UTF8
    $checks.Add([ordered]@{ Arguments = $Arguments; ExitCode = $code; Expected = $Expected; Passed = ($code -eq $Expected) })
    if ($code -ne $Expected) { throw "Expected exit $Expected, received ${code}: $output" }
    Write-Host "PASS $($Arguments -join ' ')"
    return $output
}

function Assert-Absent {
    param([string]$Module)
    $status = Invoke-Odconv -Arguments @('modules', 'status', $Module)
    if ($status -notmatch "$Module`: Not installed") { throw "Module must be absent: $Module" }
    Invoke-Odconv -Arguments @('modules', 'verify', $Module) -Expected 2 | Out-Null
    if (Test-Path -LiteralPath (Join-Path $moduleRoot $Module)) { throw "Module directory unexpectedly present: $Module" }
}

function Invoke-Extraction {
    param([string]$Prefix)
    Invoke-Odconv -Arguments @('process', (Join-Path $kit 'source.mp4'), '--action', 'extract', '--audio-track', '1',
        '--bit-depth', '32', '--sample-rate', '48000', '--output-dir', $root, '--prefix', $Prefix) | Out-Null
}

try {
    if (!$DevelopmentHost) {
        if ($administrator) { throw 'Run in a standard account with a non-administrator token.' }
        if (![Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
        if ($detected.Count) { throw 'Global tools detected; inspect DetectedHostTools before using a fresh account/VM.' }
    }
    Assert-Absent 'separation'
    Assert-Absent 'transcription'
    if ($Phase -eq 'Offline') {
        foreach ($arguments in @(@('--help'), @('formats'), @('modules', 'list'), @('probe', (Join-Path $kit 'source.mp4')))) {
            Invoke-Odconv -Arguments $arguments | Out-Null
        }
        Invoke-Extraction 'offline'
        $audio = Join-Path $root 'classic.mp3'
        Invoke-Odconv -Arguments @('convert', (Join-Path $kit 'markers.wav'), '--to', 'mp3', '-o', $audio) | Out-Null
        Invoke-Odconv -Arguments @('probe', $audio) | Out-Null
        $before = (Get-FileHash -LiteralPath $audio).Hash
        Invoke-Odconv -Arguments @('convert', (Join-Path $kit 'markers.wav'), '--to', 'mp3', '-o', $audio) -Expected 1 | Out-Null
        if ((Get-FileHash -LiteralPath $audio).Hash -ne $before) { throw 'Collision changed the existing export.' }
        foreach ($item in @(@('separate', 'separation'), @('transcribe', 'transcription'))) {
            $output = Invoke-Odconv -Arguments @('process', (Join-Path $kit 'markers.wav'), '--action', $item[0], '--output-dir', $root) -Expected 2
            if ($output -notmatch "modules\s+install\s+$($item[1])") { throw 'Missing module installation guidance.' }
        }
    } else {
        $speech = @(Get-ChildItem -LiteralPath $kit -File | Where-Object { $_.BaseName -eq 'speech' })
        if ($speech.Count -ne 1) { throw 'Prepare the kit with one representative SpeechSource for real inference.' }
        foreach ($module in @('separation', 'transcription')) {
            Invoke-Odconv -Arguments @('modules', 'install', $module) | Out-Null
            Invoke-Odconv -Arguments @('modules', 'verify', $module) | Out-Null
            Assert-Absent $(if ($module -eq 'separation') { 'transcription' } else { 'separation' })
            $action = if ($module -eq 'separation') { 'separate' } else { 'transcribe' }
            $arguments = @('process', $speech[0].FullName, '--action', $action, '--output-dir', $root, '--prefix', $module)
            if ($module -eq 'transcription') { $arguments += @('--language', $Language) }
            Invoke-Odconv -Arguments $arguments | Out-Null
            Invoke-Odconv -Arguments @('modules', 'uninstall', $module) | Out-Null
            Assert-Absent $module
            Invoke-Extraction "after-$module"
        }
        foreach ($module in @('separation', 'transcription')) {
            Invoke-Odconv -Arguments @('modules', 'install', $module) | Out-Null
        }
        Invoke-Odconv -Arguments @('process', $speech[0].FullName, '--action', 'separate-transcribe', '--language', $Language,
            '--output-dir', $root, '--prefix', 'combined') | Out-Null
        foreach ($module in @('separation', 'transcription')) {
            Invoke-Odconv -Arguments @('modules', 'repair', $module) | Out-Null
            foreach ($other in @('separation', 'transcription')) {
                Invoke-Odconv -Arguments @('modules', 'verify', $other) | Out-Null
            }
        }
        $exports = @(Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Extension -in @('.wav', '.txt', '.srt') } |
            ForEach-Object { @{ Path = $_.FullName; Hash = (Get-FileHash -LiteralPath $_.FullName).Hash } })
        Invoke-Odconv -Arguments @('modules', 'uninstall', 'separation') | Out-Null
        Assert-Absent 'separation'
        Invoke-Odconv -Arguments @('modules', 'verify', 'transcription') | Out-Null
        Invoke-Odconv -Arguments @('modules', 'uninstall', 'transcription') | Out-Null
        foreach ($export in $exports) {
            if ((Get-FileHash -LiteralPath $export.Path).Hash -ne $export.Hash) { throw 'Uninstall changed an export.' }
        }
    }
    Assert-Absent 'separation'
    Assert-Absent 'transcription'
    foreach ($entry in $manifest.Files) {
        if ((Get-FileHash -LiteralPath (Join-Path $kit $entry.Name)).Hash -ne $entry.Sha256) { throw 'Kit/source changed during validation.' }
    }
    $report.Passed = $true
} catch {
    $report['Error'] = $_.ToString()
    throw
} finally {
    $report['FinishedUtc'] = [DateTime]::UtcNow.ToString('o')
    $report['Exports'] = @(Get-ChildItem -LiteralPath $root -File | Where-Object { $_.Extension -in @('.wav', '.mp3', '.txt', '.srt') } |
        ForEach-Object { @{ Name = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding UTF8
}
