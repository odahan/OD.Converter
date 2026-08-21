# odconv — Audio Converter CLI (.NET 10 win-x64)

Self-contained Windows x64 executable with embedded FFmpeg 8.1.2. No external dependencies.

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
odconv                   # prompts for source file
```

Menu flow: format selection (mp3/wav/flac/ogg/opus/aac/m4a/aiff/wma/wv/alac) → bitrate / quality / compression → mono → sample rate → output file → confirm → convert → loop "another file?"

### Command line

```powershell
odconv convert input.wav --to mp3 --bitrate 320k -o out.mp3
odconv convert input.flac --to ogg --quality 6
odconv convert input.wav --to opus --bitrate 128k
odconv convert input.wav --to flac --compression 5
odconv convert input.wav --to wav --bit-depth 24 --mono --sample-rate 22050 -o out-mono.wav
odconv convert input.wav --to mp3 --mono --overwrite always --dry-run
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
| `-o, --output` | Output file | `<name>.<ext>` next to source, or `<name>-mono.<ext>` with `--mono` |
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
dotnet build src/OD.Converter -c Release -r win-x64
dotnet publish src/OD.Converter -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o publish
.\publish\odconv.exe --help
```

Prerequisites: .NET SDK 10.0.400, Spectre.Console 0.49.1

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
