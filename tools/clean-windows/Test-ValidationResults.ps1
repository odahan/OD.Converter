param(
    [Parameter(Mandatory = $true)][string]$KitDirectory,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [string]$Ffmpeg,
    [string]$Ffprobe
)
$ErrorActionPreference = 'Stop'
if (!$Ffmpeg) { $Ffmpeg = "$PSScriptRoot/../../src/OD.Converter/tools/ffmpeg.exe" }
if (!$Ffprobe) { $Ffprobe = "$PSScriptRoot/../../src/OD.Converter/tools/ffprobe.exe" }
$kit = (Resolve-Path -LiteralPath $KitDirectory).Path
$root = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$decoder = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath($Ffmpeg))).Path
$probe = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath($Ffprobe))).Path
$report = Get-Content -LiteralPath (Join-Path $root 'results.json') -Raw | ConvertFrom-Json
$manifestPath = Join-Path $kit 'kit.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$measurements = New-Object 'System.Collections.Generic.List[object]'
$result = [ordered]@{
    Schema = 1; MeasuredUtc = [DateTime]::UtcNow.ToString('o'); Passed = $false
    GuestReportSha256 = (Get-FileHash -LiteralPath (Join-Path $root 'results.json')).Hash
    DecoderSha256 = (Get-FileHash -LiteralPath $decoder).Hash
    ProbeSha256 = (Get-FileHash -LiteralPath $probe).Hash
    CleanWindowsVerified = $false; HumanReviewCompleted = $false; Measurements = $measurements
}
try {
    if (!$report.Passed -or @($report.Checks).Count -eq 0 -or @($report.Checks | Where-Object { !$_.Passed }).Count) {
        throw 'Guest command checks did not pass.'
    }
    if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne $report.KitManifestSha256 -or
        (Get-FileHash -LiteralPath (Join-Path $kit 'odconv.exe')).Hash -ne $report.ExecutableSha256) {
        throw 'Guest report does not match this kit.'
    }
    foreach ($entry in $manifest.Files) {
        if ([IO.Path]::GetFileName($entry.Name) -ne $entry.Name) { throw 'Unsafe kit manifest path.' }
        $file = Get-Item -LiteralPath (Join-Path $kit $entry.Name)
        if ($file.Length -ne $entry.Bytes -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $entry.Sha256) {
            throw "Kit integrity failed: $($entry.Name)"
        }
    }
    foreach ($entry in $report.Exports) {
        if ([IO.Path]::GetFileName($entry.Name) -ne $entry.Name) { throw 'Unsafe report path.' }
        $file = Get-Item -LiteralPath (Join-Path $root $entry.Name)
        if ($file.Length -ne $entry.Bytes -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $entry.Sha256) {
            throw "Transferred export integrity failed: $($entry.Name)"
        }
    }
    $names = if ($report.Phase -eq 'Offline') { @('offline-original.wav') }
        elseif ($report.Phase -eq 'Modules') { @('after-separation-original.wav', 'after-transcription-original.wav') }
        else { throw 'Unknown validation phase.' }
    foreach ($name in $names) {
        if ($name -notin @($report.Exports.Name)) { throw "Missing reported extraction: $name" }
        $metadata = & $probe -v error -select_streams a -show_entries stream=sample_rate,channels,codec_name -of json (Join-Path $root $name)
        if ($LASTEXITCODE -ne 0) { throw "Cannot inspect $name" }
        $streams = @(($metadata -join [Environment]::NewLine | ConvertFrom-Json).streams)
        if ($streams.Count -ne 1 -or $streams[0].sample_rate -ne '48000' -or
            $streams[0].channels -ne 1 -or $streams[0].codec_name -ne 'pcm_s32le') {
            throw "Incorrect PCM format: $name"
        }
        $raw = Join-Path $root ([Guid]::NewGuid().ToString('N') + '.f32')
        try {
            & $decoder -nostdin -v error -i (Join-Path $root $name) -map 0:a:0 -c:a pcm_f32le -f f32le $raw
            if ($LASTEXITCODE -ne 0) { throw "Cannot decode $name" }
            $reader = New-Object IO.BinaryReader([IO.File]::OpenRead($raw))
            try {
                if ($reader.BaseStream.Length -ne 240000 * 4) { throw "Incorrect PCM frame count: $name" }
                $errors = @(foreach ($time in @(0.75, 2.0, 3.25)) {
                    $target = [long]($time * 48000)
                    $start = $target - 2400
                    $reader.BaseStream.Position = $start * 4
                    $amplitude = 0.0
                    $peak = $start
                    for ($frame = $start; $frame -lt $target + 2400; $frame++) {
                        $value = [Math]::Abs($reader.ReadSingle())
                        if ($value -gt $amplitude) { $amplitude = $value; $peak = $frame }
                    }
                    if ($amplitude -lt 0.01 -or [Math]::Abs($peak - $target) -gt 1) { throw "Marker mismatch in $name at ${time}s" }
                    $peak - $target
                })
                $measurements.Add(@{ Name = $name; Frames = 240000; Rate = 48000; MarkerErrors = $errors; ToleranceSamples = 1 })
            } finally { $reader.Dispose() }
        } finally { if (Test-Path -LiteralPath $raw) { Remove-Item -LiteralPath $raw } }
    }
    $result.Passed = $true
    Write-Host 'Transferred exports match their hashes; extraction frames and markers passed.'
} catch {
    $result['Error'] = $_.ToString()
    throw
} finally {
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'measurements.json') -Encoding UTF8
}
