# odconv — Convertisseur audio CLI (.NET 10 win-x64)

Exe autonome Windows x64, FFmpeg 8.1.2 embarqué. Pas de dépendance externe.

## Installation

1. Récupérer `publish/odconv.exe` (~265 Mo self-contained)
2. Optionnel : ajouter au PATH ou associer `.wav/.mp3` → `odconv "%1"`

## Usage

### Menu interactif

```powershell
odconv song.wav          # menu : format, bitrate, mono, sortie
odconv                   # demande le fichier source
```

Menu : choix format (mp3/wav/flac/ogg/opus/aac/m4a/aiff/wma/wv/alac) → bitrate/qualité/compression → mono → fréquence → fichier sortie → confirmation → conversion → boucle "autre fichier ?"

### Ligne de commande

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

### Options `convert`

| Option | Description | Défaut |
|--------|-------------|--------|
| `--to, -t` | Format cible (requis) : `wav, flac, mp3, ogg, opus, aac, m4a, aiff, wma, wv, alac` | - |
| `-o, --output` | Fichier sortie | `<nom>.<ext>` à côté source, ou `<nom>-mono.<ext>` si `--mono` |
| `-b, --bitrate` | Bitrate ex `320k, 192k, 128k` (lossy) | mp3 `320k`, ogg `192k`→q6, opus `128k`, aac/m4a/wma `192k` |
| `-q, --quality` | Qualité VBR : mp3 `0-9` (0 max), ogg `0-10` | mp3 CBR si absent, ogg `6` |
| `--sample-rate` | `44100, 48000, 22050, 96000` | conserve source |
| `--channels` | `1,2,6` | conserve source |
| `--mono` | Force mono `-ac 1` (prioritaire) | `false` |
| `--bit-depth` | `16,24,32` (wav/aiff) | `16` |
| `--compression` | `0-8` (flac) | `5` |
| `--overwrite` | `never|always` | `never` (erreur si existe) |
| `--ffmpeg` | Chemin `ffmpeg.exe` custom | auto : `tools/ffmpeg.exe` embarqué → `C:\ffmpeg\bin` → `PATH` |
| `--dry-run` | Affiche commande ffmpeg | `false` |

### Codes retour

`0 OK`, `1 args/fichier`, `2 ffmpeg introuvable`, `3 échec conversion`, `130 annulé Ctrl+C`

### Messages d'erreur (sans log)

```
Erreur : fichier introuvable : "C:\...\in.wav"
Erreur : format non supporté : "xyz". Formats : aac, aiff, ...
Erreur : le fichier de sortie existe déjà : "out.mp3". Utilisez --overwrite always
Erreur : conversion échouée (Invalid data found...). Vérifiez le fichier source...
```

## Build

```powershell
dotnet build src/OD.Converter -c Release -r win-x64
dotnet publish src/OD.Converter -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o publish
.\publish\odconv.exe --help
```

Prérequis : .NET SDK 10.0.400, Spectre.Console 0.49.1

## FFmpeg embarqué

`tools/ffmpeg.exe` + `ffprobe.exe` (essentials_build-www.gyan.dev, gcc 16.1, GPL v3) copiés depuis `C:\ffmpeg`. `tools/LICENSE` inclus. Publish SingleFile extrait vers dossier temp via `IncludeAllContentForSelfExtract`.

## Structure

```
src/OD.Converter/
  OD.Converter.csproj (AssemblyName odconv, SelfContained, SingleFile)
  tools/ffmpeg.exe, ffprobe.exe, LICENSE
  Program.cs (dispatch + mode interactif)
  Commands/ConvertCommand, ProbeCommand, FormatsCommand
  Services/FormatRegistry, FfmpegConverter, FfprobeService, FfmpegLocator, InteractiveConverter, ErrorHandler
  Models/ConversionOptions
```
