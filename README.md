# odconv — Audio Converter CLI (.NET 10 win-x64)

**Version 1.3.0** · [English / French user guide](docs/user-guide.html)

Self-contained Windows x64 executable with embedded FFmpeg 8.1.2. Conversion and
extraction work without external dependencies. Optional AI processing uses separately
installed private CPU modules.

## License and support

Copyright © 2026 Olivier Dahan. OD.Converter is distributed under the [DonationWare License](LICENSE.md): it is free to use and share, and voluntary support is welcome.

- Support the project: [Buy Me a Coffee](https://buymeacoffee.com/knhsynths)
- Source code: [github.com/odahan/OD.Converter](https://github.com/odahan/OD.Converter)

FFmpeg is a third-party component and remains subject to its own license, included in the distribution.

## Installation

**For immediate use on Windows x64, just download `odconv.exe` from [Releases](https://github.com/odahan/OD.Converter/releases) — no build required, no dependencies.**

1. Go to **Releases** → download `odconv.exe` (~265 MB, self-contained, FFmpeg embedded)
2. Run it directly: double-click or `odconv.exe --help`
3. Optional: add to PATH or associate `.wav/.mp3` → `odconv "%1"`

> Building from source is only needed for development. See [Build](#build) below. `publish/odconv.exe` is the same binary as in Releases.

## Usage

### Interactive menu

```powershell
odconv song.wav          # menu: format, bitrate, mono, output
odconv                   # main menu: audio/video processing and optional modules
```

Menu flow: format selection (mp3/wav/flac/ogg/opus/aac/m4a/aiff/wma/wv/alac) → bitrate / quality / compression → mono → sample rate → output file → confirm → convert → loop "another file?"

Without arguments, the main menu offers `Process audio / video`, `Convert audio (classic)`,
`Optional modules`, `User guide / Guide utilisateur`, and `Buy me a coffee`. A shared ASCII banner,
colored selections, numbered workflow stages and consistent tables guide navigation.
The common workflow analyzes all audio streams, lets you select
an absolute stream index, then offers extraction, separation, transcription, or their
combination. Unavailable actions appear in grey with their module requirement and are
excluded from executable choices. Choose exports, source language, audio encoding,
destination and prefix, then review the summary. Existing destinations are listed
together before overwrite confirmation; every destination is checked before processing.
Processing reports steps, warnings, finalized exports and missing files after a partial
failure. Ctrl+C cancels processing and cleans intermediate files, retaining completed exports.

`Optional modules` shows offline checked states and provides `Verify`, `Install`,
`Repair` and `Uninstall` for each module. Installation/repair shows the private location,
version, CPU profile and download size before confirmation. Installation is always explicit.
Passing a file directly keeps the classic conversion prompts shown above.

The bilingual guide is a single, self-contained HTML document embedded in the EXE.
The help menu (or `odconv guide`) extracts/refreshes `odconv-user-guide.html` next to
the running executable and opens the default browser. Keep the EXE in a writable
folder. If browser launch fails, open the extracted file manually. Use
`odconv guide --extract` to extract it without launching a browser. No network
connection is needed to read the guide.

`Buy me a coffee` presents voluntary support for Olivier Dahan / KNHSynths, using
the same page as HarmoTools: [buymeacoffee.com/knhsynths](https://buymeacoffee.com/knhsynths).
Choose `Open Buy Me a Coffee` to launch your default browser, or `Back` to return.

### Command line

```powershell
odconv convert input.wav --to mp3 --bitrate 320k -o out.mp3
odconv convert input.flac --to ogg --quality 6
odconv convert input.wav --to opus --bitrate 128k
odconv convert input.wav --to flac --compression 5
odconv convert input.wav --to wav --bit-depth 24 --mono --sample-rate 22050 -o out-mono.wav
odconv convert input.wav --to mp3 --mono --overwrite always --dry-run
odconv convert *.wav --to mp3 --output-dir ./mp3          # batch convert all WAVs
odconv convert . --to flac --recursive --pattern "*.wav"  # recursive batch
odconv probe track.mp3
odconv probe track.mp3 --json
odconv formats
odconv --help
odconv --version
```

### `convert` options

| Option | Description | Default |
|--------|-------------|---------|
| `--to, -t` | Target format (required): `wav, flac, mp3, ogg, opus, aac, m4a, aiff, wma, wv, alac` | - |
| `-o, --output` | Output file (only for single input) | `<name>.<ext>` next to source, or `<name>-mono.<ext>` with `--mono` |
| `--output-dir` | Output directory for multiple inputs, preserves structure | same as input |
| `--recursive` | Process subdirectories recursively | `false` |
| `--pattern` | File pattern e.g. `*.wav` (default: all files) | `*` |
| `-b, --bitrate` | Bitrate e.g. `320k, 192k, 128k` (lossy) | mp3 `320k`, ogg `192k`→q6, opus `128k`, aac/m4a/wma `192k` |
| `-q, --quality` | VBR quality: mp3 `0-9` (0 best), ogg `0-10` | mp3 CBR if omitted, ogg `6` |
| `--sample-rate` | `44100, 48000, 22050, 96000` | keep source |
| `--channels` | `1,2,6` | keep source |
| `--mono` | Force mono `-ac 1` (takes precedence) | `false` |
| `--bit-depth` | `16,24,32` (wav/aiff) | `16` |
| `--compression` | `0-8` (flac) | `5` |
| `--overwrite` | `never|always` | `never` (error if exists) |
| `--ffmpeg` | Custom `ffmpeg.exe` path | auto: embedded `tools/ffmpeg.exe` → `C:\ffmpeg\bin` → `PATH` |
| `--dry-run` | Print ffmpeg command without running | `false` |

### Exit codes

The audio/video extension includes full stream analysis in `probe` and offline
soundtrack extraction with `process`. For example:

```powershell
odconv process "clip.mp4" --action extract --audio-track 2 --to wav --bit-depth 24
```

The stream index is the absolute index reported by `probe`. Extraction preserves
presentation offsets and timestamp gaps, pads the soundtrack to the source
duration and measures PCM frame counts. Sources with unknown or inconsistent
timelines are rejected explicitly; PCM WAV uses sample zero as its origin.
The optional module manager exposes `modules list`, `status`, `verify`, `install`,
`repair` and targeted `uninstall`. Explicit installation provisions independent
CPU distributions of Demucs and Whisper, including private runtimes, models,
integrity verification and offline functional checks. Conversion/extraction do
not download or require these modules. `process --action separate` uses the
installed Demucs CPU module to export vocals and background, optionally the
complete soundtrack. `process --action transcribe --language fr` produces global
UTF-8 TXT/SRT with the private Whisper CPU module. `separate-transcribe` transcribes
aligned Demucs vocals using both modules. Language defaults to `auto`; translation
and speaker identification are disabled. Review the text and phrase boundaries.
See [audio/video CLI contracts](docs/cli-audio-video.md),
[PCM preparation policy](docs/preparation-audio.md),
[Demucs separation and validation](docs/separation-audio.md),
[Whisper transcription and validation](docs/transcription-audio.md),
[optional module lifecycle](docs/modules-optionnels.md),
[pinned distributions and licenses](docs/distributions-modules.md),
[release validation](docs/validation-livraison.md), and
[implementation progress](docs/suivi-audio-video.md).

`0 OK`, `1 args/file`, `2 ffmpeg not found`, `3 conversion failed`, `130 cancelled Ctrl+C`

### Error messages (no logs)

```
Error: file not found: "C:\...\in.wav"
Error: unsupported format: "xyz". Formats: aac, aiff, ...
Error: output file already exists: "out.mp3". Use --overwrite always
Error: conversion failed (Invalid data found...). Check source file...
```

## Build

```powershell
dotnet build src/OD.Converter -c Release -r win-x64 -m:1
dotnet publish src/OD.Converter -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -m:1 -o publish
.\publish\odconv.exe --help
```

Prerequisites: .NET SDK 10.0.400, Spectre.Console 0.49.1

### Tests

```powershell
dotnet test OD.Converter.slnx -c Release -m:1
```

The standalone release validator copies only the published EXE into a new directory,
uses a Windows-only PATH and a private bundle extraction directory, then checks video
extraction, two-hour PCM alignment and classic conversion. It writes JSON measurements
and command logs. This local isolation does not replace a clean Windows machine test;
see [the release procedure and remaining checks](docs/validation-livraison.md).

## Embedded FFmpeg

`tools/ffmpeg.exe` + `ffprobe.exe` (essentials_build-www.gyan.dev, gcc 16.1, GPL v3) copied from `C:\ffmpeg`. `tools/LICENSE` included. SingleFile publish extracts to temp folder via `IncludeAllContentForSelfExtract`.

## Structure

```
src/OD.Converter/
  OD.Converter.csproj (AssemblyName odconv, SelfContained, SingleFile)
  tools/ffmpeg.exe, ffprobe.exe, LICENSE
  Program.cs (dispatch + interactive mode)
  Commands/ConvertCommand, ProbeCommand, FormatsCommand
  Services/FormatRegistry, FfmpegConverter, FfprobeService, FfmpegLocator, InteractiveConverter, ErrorHandler
  Models/ConversionOptions
```
