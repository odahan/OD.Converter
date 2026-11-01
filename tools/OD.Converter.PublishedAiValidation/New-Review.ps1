param([Parameter(Mandatory = $true)][string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ResultsDirectory).Path.Replace('\', '/')
$report = Get-Content -LiteralPath (Join-Path $root 'results.json') -Raw | ConvertFrom-Json
if (!$report.Passed) { throw 'The automated recipe must pass before preparing its human review.' }
$destination = Join-Path $root 'Review.md'
if (Test-Path -LiteralPath $destination) { throw 'A review already exists; preserve the reviewer observations.' }
$lines = New-Object 'System.Collections.Generic.List[string]'
$lines.Add('# Episode review: published AI workflows')
$lines.Add('')
$lines.Add('Status: **human listening and source comparison pending**. Automated success does not validate recognition accuracy or separation quality.')
$lines.Add('')
$lines.Add("Executable SHA-256: $($report.ExecutableSha256)")
$lines.Add('')
$lines.Add("Source SHA-256: $($report.SourceSha256)")
$lines.Add('')
if ($report.Mode -eq 'FullTranscription') {
    $lines.Add('Subtitle timestamps use the timeline of the complete source video. Compare the transcript and phrase boundaries with the source; a structurally valid SRT does not prove that all speech was recognized.')
} else {
    $lines.Add('Subtitle timestamps are local to each 30-second excerpt. The source offsets below identify its position in the original video. Compare both transcripts with the video and listen to vocals/background independently.')
}
foreach ($clip in @($report.Measurements | Where-Object { $_.Check -in @('source-excerpt', 'full-source') })) {
    $lines.Add('')
    $lines.Add("## $($clip.Name): source offset $($clip.SourceOffsetSeconds) seconds")
    $lines.Add('')
    if ($clip.Check -eq 'full-source') {
        $lines.Add("[Complete source video](<$($report.Source.Replace('\', '/'))>)")
    } else {
        $lines.Add("[Video excerpt](<$root/$($clip.Name).mp4>)")
        $lines.Add('')
        $prefix = "$root/$($clip.Name)-separate-transcribe"
        $lines.Add("[Original audio](<$prefix-original.wav>) | [Vocals](<$prefix-vocals.wav>) | [Background](<$prefix-background.wav>)")
    }
    $actions = @($report.Measurements | Where-Object { $_.Check -eq 'transcript-structure' -and $_.Clip -eq $clip.Name } | ForEach-Object { $_.Action })
    foreach ($action in $actions) {
        $stem = "$($clip.Name)-$action"
        $lines.Add('')
        $lines.Add("### $action")
        $lines.Add('')
        $lines.Add("[TXT](<$root/$stem-transcript.txt>) | [SRT](<$root/$stem-subtitles.srt>)")
        $lines.Add('')
        if ($clip.Check -eq 'full-source') {
            $transcript = $report.Measurements | Where-Object { $_.Check -eq 'transcript-structure' -and $_.Clip -eq $clip.Name -and $_.Action -eq $action }
            $cues = @($transcript.Cues)
            $lines.Add("Subtitle cues: $($cues.Count). First start: $($cues[0].StartMilliseconds) ms. Last end: $($cues[-1].EndMilliseconds) ms.")
            $lines.Add('')
            $lines.Add('Uncaptioned intervals of at least two seconds are listed below for listening. They may contain silence, pauses or unrecognized speech; these intervals are not proof of missing words.')
            $gaps = New-Object 'System.Collections.Generic.List[object]'
            $cursor = 0L
            foreach ($cue in $cues) {
                if ($cue.StartMilliseconds - $cursor -ge 2000) {
                    $gaps.Add(@{ Start = $cursor; End = $cue.StartMilliseconds })
                }
                $cursor = $cue.EndMilliseconds
            }
            $end = [long][Math]::Round($clip.DurationSeconds * 1000, [MidpointRounding]::AwayFromZero)
            if ($end - $cursor -ge 2000) { $gaps.Add(@{ Start = $cursor; End = $end }) }
            $lines.Add('')
            if ($gaps.Count) {
                $lines.Add('| Start (ms) | End (ms) | Duration (ms) |')
                $lines.Add('|---|---|---|')
                foreach ($gap in $gaps) { $lines.Add("| $($gap.Start) | $($gap.End) | $($gap.End - $gap.Start) |") }
            } else {
                $lines.Add('No uncaptioned interval reaches two seconds.')
            }
            $lines.Add('')
        }
        foreach ($line in @(Get-Content -LiteralPath (Join-Path $root "$stem-transcript.txt") -Encoding UTF8)) {
            $lines.Add("> $line")
        }
    }
    $lines.Add('')
    $lines.Add('Review observations: omissions / hallucinations / phrase timing / weak speech / residual speech in background / audible artifacts: **to complete**.')
}
$lines.Add('')
$lines.Add('Reviewer / date / decision: **to complete**. Keep Windows clean-environment validation separate; this recipe ran on the development host using copied generations.')
$lines | Set-Content -LiteralPath $destination -Encoding UTF8
Write-Host "Review prepared: $destination"
