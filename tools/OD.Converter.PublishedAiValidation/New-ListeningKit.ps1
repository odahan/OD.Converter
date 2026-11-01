param(
    [Parameter(Mandatory = $true)][string]$FullResultsDirectory,
    [Parameter(Mandatory = $true)][string]$ExcerptResultsDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$Ffmpeg
)
$ErrorActionPreference = 'Stop'
if (!$Ffmpeg) { $Ffmpeg = [IO.Path]::Combine($PSScriptRoot, '../../src/OD.Converter/tools/ffmpeg.exe') }
$culture = [Globalization.CultureInfo]::InvariantCulture
$fullRoot = (Resolve-Path -LiteralPath $FullResultsDirectory).Path
$excerptRoot = (Resolve-Path -LiteralPath $ExcerptResultsDirectory).Path
$ffmpegPath = (Resolve-Path -LiteralPath $Ffmpeg).Path
$full = Get-Content -LiteralPath (Join-Path $fullRoot 'results.json') -Raw | ConvertFrom-Json
$excerpts = Get-Content -LiteralPath (Join-Path $excerptRoot 'results.json') -Raw | ConvertFrom-Json
$review = Get-Content -LiteralPath (Join-Path $fullRoot 'human-review.json') -Raw | ConvertFrom-Json
if (!$full.Passed -or !$excerpts.Passed -or $full.Mode -ne 'FullTranscription') {
    throw 'Successful full-transcription and excerpt recipes are required.'
}
if ($full.SourceSha256 -ne $excerpts.SourceSha256 -or $full.SourceSha256 -ne $review.SourceSha256 -or
    $full.ExecutableSha256 -ne $excerpts.ExecutableSha256) {
    throw 'The recipes and human review must refer to the same source and executable.'
}
function Assert-Hash([string]$Path, [string]$Expected) {
    if (!$Expected -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
        throw "Evidence hash mismatch: $Path"
    }
}
Assert-Hash $full.Source $full.SourceSha256
foreach ($item in @($full.Exports) + @($excerpts.Exports) + @($review.RawExports) + @($review.ReviewedExports)) {
    Assert-Hash $item.Path $item.Sha256
}
$clips = @($excerpts.Measurements | Where-Object Check -eq 'source-excerpt')
if ($clips.Count -ne 3) { throw 'Three measured excerpts are required.' }
foreach ($clip in $clips) {
    if ($clip.Name -notin @('beginning', 'middle', 'ending')) { throw 'Unexpected excerpt name.' }
    Assert-Hash (Join-Path $excerptRoot "$($clip.Name).mp4") $clip.Sha256
}
$source = @($full.Measurements | Where-Object Check -eq 'full-source')
$transcript = @($full.Measurements | Where-Object Check -eq 'transcript-structure')
if ($source.Count -ne 1 -or $transcript.Count -ne 1 -or !$transcript[0].Cues.Count) {
    throw 'A measured source and nonempty complete transcript are required.'
}
$endMs = [long][Math]::Round($source[0].DurationSeconds * 1000, [MidpointRounding]::AwayFromZero)
$lastMs = [long]$transcript[0].Cues[-1].EndMilliseconds
if ($lastMs -lt 0 -or $lastMs -ge $endMs) { throw 'No uncaptioned tail to prepare.' }
$startMs = [Math]::Max(0, $lastMs - 8000)
$process = @($full.Commands | Where-Object { $_.Arguments[0] -eq 'process' })
if ($process.Count -ne 1) { throw 'Cannot identify the selected audio track.' }
$arguments = @($process[0].Arguments)
$trackPosition = [Array]::IndexOf($arguments, '--audio-track')
if ($trackPosition -lt 0) { throw 'An explicit source audio track is required.' }
$track = 0
if (![int]::TryParse($arguments[$trackPosition + 1], [ref]$track) -or $track -lt 0) {
    throw 'Invalid audio track index.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new output directory to preserve existing observations.' }
$output = [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)).FullName
$files = New-Object 'System.Collections.Generic.List[object]'
function Copy-Evidence([string]$Path, [string]$Name) {
    $target = Join-Path $output $Name
    Copy-Item -LiteralPath $Path -Destination $target -ErrorAction Stop
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    Assert-Hash $Path $hash
    $files.Add([ordered]@{ Name = $Name; Sha256 = $hash; Source = $Path })
}
Copy-Evidence (Join-Path $fullRoot 'results.json') 'full-results.json'
Copy-Evidence (Join-Path $excerptRoot 'results.json') 'excerpt-results.json'
Copy-Evidence (Join-Path $fullRoot 'human-review.json') 'prior-human-review.json'
foreach ($name in @('reviewed-transcript.txt', 'reviewed-subtitles.srt', 'full-transcribe-transcript.txt', 'full-transcribe-subtitles.srt')) {
    Copy-Evidence (Join-Path $fullRoot $name) $name
}
foreach ($clip in $clips) {
    Copy-Evidence (Join-Path $excerptRoot "$($clip.Name).mp4") "$($clip.Name).mp4"
    foreach ($suffix in @('original.wav', 'vocals.wav', 'background.wav', 'transcript.txt', 'subtitles.srt')) {
        $name = "$($clip.Name)-separate-transcribe-$suffix"
        Copy-Evidence (Join-Path $excerptRoot $name) $name
    }
    foreach ($suffix in @('transcript.txt', 'subtitles.srt')) {
        $name = "$($clip.Name)-transcribe-$suffix"
        Copy-Evidence (Join-Path $excerptRoot $name) $name
    }
}
$tailName = 'uncaptioned-tail-with-context.wav'
$ffmpegArguments = @('-nostdin', '-v', 'error', '-n', '-i', $full.Source,
    '-ss', ($startMs / 1000.0).ToString('0.000', $culture),
    '-t', (($endMs - $startMs) / 1000.0).ToString('0.000', $culture),
    '-map', "0:$track", '-vn', '-c:a', 'pcm_s16le', '-ar', '48000', '-ac', '2', (Join-Path $output $tailName))
& $ffmpegPath @ffmpegArguments
if ($LASTEXITCODE -ne 0) { throw "FFmpeg failed with code $LASTEXITCODE; incomplete kit retained for inspection." }
$files.Add([ordered]@{ Name = $tailName; Sha256 = (Get-FileHash -LiteralPath (Join-Path $output $tailName)).Hash; Source = $full.Source })
Assert-Hash $full.Source $full.SourceSha256
$checks = @(
    [ordered]@{ Id = 'uncaptioned-tail'; Status = 'Pending'; Observation = ''; Decision = '' },
    [ordered]@{ Id = 'global-transcription-and-timing'; Status = 'Pending'; Observation = ''; Decision = '' },
    [ordered]@{ Id = 'separation-beginning'; Status = 'Pending'; Observation = ''; Decision = '' },
    [ordered]@{ Id = 'separation-middle'; Status = 'Pending'; Observation = ''; Decision = '' },
    [ordered]@{ Id = 'separation-ending'; Status = 'Pending'; Observation = ''; Decision = '' }
)
[ordered]@{
    Schema = 1; SourceSha256 = $full.SourceSha256; ExecutableSha256 = $full.ExecutableSha256
    HumanReviewCompleted = $false; IsCleanWindows = $false; Reviewer = ''; Date = ''; Checks = $checks
    Tail = [ordered]@{ SourceStartMilliseconds = $startMs; UncaptionedStartMilliseconds = $lastMs
        SourceEndMilliseconds = $endMs; UncaptionedStartInClipMilliseconds = $lastMs - $startMs
        AudioTrackIndex = $track }
    Generation = [ordered]@{ Program = $ffmpegPath; Arguments = $ffmpegArguments; ExitCode = 0 }
    Files = $files
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'listening-review.json') -Encoding UTF8
$lines = New-Object 'System.Collections.Generic.List[string]'
$lines.Add('# Remaining listening review')
$lines.Add('')
$lines.Add('Status: **pending human decisions**. Record reviewer, date, observations and scope in listening-review.json. Existing targeted verdicts are preserved in prior-human-review.json. This kit does not validate a clean Windows environment.')
$lines.Add('')
$lines.Add("Source SHA-256: $($full.SourceSha256). Executable SHA-256: $($full.ExecutableSha256).")
$lines.Add('')
$lines.Add('## Uncaptioned tail')
$lines.Add('')
$lines.Add("[Listen with context]($tailName). Source interval: $startMs-$endMs ms. The uncaptioned interval begins at **$($lastMs - $startMs) ms in this clip**, source $lastMs ms, and lasts $($endMs - $lastMs) ms. Record silence, music, or actual omitted speech after listening. Do not infer a verdict from subtitle absence.")
$lines.Add('')
$lines.Add('## Complete transcription')
$lines.Add('')
$sourceLink = $full.Source.Replace('\', '/')
$lines.Add("Compare the [complete source](<$sourceLink>) with [reviewed TXT](reviewed-transcript.txt) and [reviewed SRT](reviewed-subtitles.srt). Check words, omissions and phrase boundaries throughout. The complete video remains at its original local path; copy it separately when transferring the kit. Raw engine exports are also preserved here. Six targeted verdicts do not constitute global acceptance.")
foreach ($clip in $clips) {
    $name = $clip.Name
    $lines.Add('')
    $lines.Add("## Separation: $name (source offset $($clip.SourceOffsetSeconds) s)")
    $lines.Add('')
    $lines.Add("[Video]($name.mp4) | [Original]($name-separate-transcribe-original.wav) | [Vocals]($name-separate-transcribe-vocals.wav) | [Background]($name-separate-transcribe-background.wav)")
    $lines.Add('')
    $lines.Add("[Direct TXT]($name-transcribe-transcript.txt) | [Direct SRT]($name-transcribe-subtitles.srt) | [Separated TXT]($name-separate-transcribe-transcript.txt) | [Separated SRT]($name-separate-transcribe-subtitles.srt)")
    $lines.Add('')
    $lines.Add('Listen at a consistent playback volume without independent normalization. Note missing or distorted speech, residual speech in the background, musical leakage and audible artifacts. Subtitle times are local to this excerpt. Record observations and accept / reject / inconclusive for this source only.')
}
$lines.Add('')
$lines.Add('These excerpts cover one source. Music, interview, ambience, multiple speakers and language changes still require representative evidence where absent here. Keep lots 4, 5 and 7 open until their respective criteria are met.')
$lines | Set-Content -LiteralPath (Join-Path $output 'Review.md') -Encoding UTF8
Write-Host "Listening kit prepared: $output"
