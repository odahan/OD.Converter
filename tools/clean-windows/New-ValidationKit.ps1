param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$Executable,
    [string]$Ffmpeg,
    [string]$SpeechSource
)
$ErrorActionPreference = 'Stop'
if (!$Executable) { $Executable = "$PSScriptRoot/../../publish/odconv.exe" }
if (!$Ffmpeg) { $Ffmpeg = "$PSScriptRoot/../../src/OD.Converter/tools/ffmpeg.exe" }
$root = [IO.Path]::GetFullPath($OutputDirectory)
$exe = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath($Executable))).Path
$generator = (Resolve-Path -LiteralPath ([IO.Path]::GetFullPath($Ffmpeg))).Path
if ((Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Force).Count) {
    throw 'The kit requires a new or empty directory.'
}
New-Item -ItemType Directory -Path $root -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination (Join-Path $root 'odconv.exe')
Copy-Item -LiteralPath "$PSScriptRoot/Invoke-Validation.ps1" -Destination $root
Copy-Item -LiteralPath "$PSScriptRoot/Review.md" -Destination $root
$raw = Join-Path $root 'markers.f32'
$stream = [IO.File]::Create($raw)
$writer = New-Object IO.BinaryWriter($stream)
try {
    $stream.SetLength(3 * 48000 * 4)
    foreach ($time in @(0.25, 1.5, 2.75)) {
        $stream.Position = [long]($time * 48000) * 4
        $writer.Write([single]0.75)
    }
} finally { $writer.Dispose() }
& $generator -nostdin -v error -y -f f32le -ar 48000 -ac 1 -i $raw -c:a pcm_f32le (Join-Path $root 'markers.wav')
if ($LASTEXITCODE -ne 0) { throw 'WAV fixture generation failed.' }
& $generator -nostdin -v error -y -f lavfi -i 'color=s=32x32:r=25:d=5' -itsoffset 0.5 -i (Join-Path $root 'markers.wav') -map 0:v -map 1:a -c:v mpeg4 -c:a alac (Join-Path $root 'source.mp4')
if ($LASTEXITCODE -ne 0) { throw 'MP4 fixture generation failed.' }
Remove-Item -LiteralPath $raw
if ($SpeechSource) {
    $speech = Get-Item -LiteralPath $SpeechSource
    if ($speech.PSIsContainer) { throw 'SpeechSource must be a media file.' }
    Copy-Item -LiteralPath $speech.FullName -Destination (Join-Path $root ('speech' + $speech.Extension))
}
$files = @(Get-ChildItem -LiteralPath $root -File | ForEach-Object {
    [ordered]@{ Name = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
[ordered]@{
    Schema = 1
    CreatedUtc = [DateTime]::UtcNow.ToString('o')
    Files = $files
    Extraction = @{ Frames = 240000; Rate = 48000; Markers = @(0.75, 2.0, 3.25); ToleranceSamples = 1 }
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'kit.json') -Encoding UTF8
Write-Host "Kit prepared: $root"
Write-Host 'Copy the kit to a fresh Windows x64 standard account. Follow Review.md.'
