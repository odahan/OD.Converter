# Audio/video workflow contracts (lots 1–5)

The syntax below is fixed for the extension. `probe` performs full source
analysis. `process --action extract` selects and decodes a soundtrack, preserves
its presentation timing, measures and normalizes PCM length, then exports it with
the requested encoding settings. No module or network is required. `separate`
executes the installed private Demucs CPU engine. `transcribe` executes native
Whisper CPU; `separate-transcribe` transcribes aligned Demucs vocals. Module inspection,
verification, install/repair and targeted uninstall are implemented. The pinned
CPU recipes provision independent Demucs and Whisper engines and models.
Help commands return **0**.

Since version 1.3.0, `odconv guide` opens the embedded EN/FR user guide in the
default browser after extracting `odconv-user-guide.html` next to the running
EXE. `odconv guide --extract` only extracts/refreshes that file. This requires a
writable executable directory. Extraction/browser failures return 3 and explain
how to open the file manually. The same guide is available in the main menu and
as [a standalone HTML document](user-guide.html).

Existing `convert`, `probe` / `info`, `formats`, options and direct-file interactive
launch remain supported. `convert` keeps its current naming and FFmpeg behavior;
the new stream-selection contract applies to `process`. Interactive track selection
and the common action menu belong to lot 6.

## Workflow command

```powershell
odconv process <file> --action <extract|separate|transcribe|separate-transcribe> [options]
odconv process --help
```

Exactly one audio or video source is accepted per request. Option values are case
sensitive, with the existing format aliases accepted by `--to`.

| Option | Contract / default |
|---|---|
| `--action` | Required; one of the four values above |
| `--audio-track <index>` | **Absolute FFprobe stream index**, as displayed by `probe`; not the audio ordinal. Nonnegative integer. Defaults to the first audio stream in probe order, which the command reports explicitly |
| `--exports <list>` | Comma-separated export names, without spaces; unique and compatible with the action |
| `--output-dir <dir>` | Destination; defaults to the source directory |
| `--prefix <name>` | Filename stem, without directories; defaults to the source stem |
| `--language <code>` | `auto` or a lowercase two-letter source language code such as `en` or `fr`. Only transcription actions accept a non-auto value. No translation |
| `--to, -t <format>` | Audio export format; default `wav`; all current audio formats and aliases supported |
| `--overwrite <value>` | `never` (default) or `always` |
| `--ffmpeg <path>` | Same tool override convention as existing commands |
| `--bitrate, -b`, `--quality, -q`, `--sample-rate`, `--channels`, `--mono`, `--bit-depth`, `--compression` | Same values, precedence and compatibility checks as `convert`; apply to audio exports only |

`--dry-run`, `-o`, batch inputs, `--recursive` and `--pattern` belong to existing
`convert` and are not part of `process`. Process execution will always remain
non-interactive; no implicit module installation is permitted.

| Action | Allowed exports | Default exports | Required modules |
|---|---|---|---|
| `extract` | `original` | `original` | None |
| `separate` | `original,vocals,background` | `vocals,background` | `separation` |
| `transcribe` | `transcript,subtitles` | `transcript,subtitles` | `transcription` |
| `separate-transcribe` | All five | `vocals,background,transcript,subtitles` | Both |

Transcription uses the selected soundtrack for `transcribe`, and the aligned
isolated vocals for `separate-transcribe`. TXT and SRT are global, without speaker
labels. The selected language is passed to the pinned multilingual engine;
translation is disabled. See [transcription policy and limitations](transcription-audio.md).

New exports use `<prefix>-original.<audio extension>`, `-vocals.<audio extension>`,
`-background.<audio extension>`, `-transcript.txt` and `-subtitles.srt`. Format
aliases resolve to the registered extension (for example ALAC uses `.m4a`). A
different prefix or destination is needed to distinguish exports from two source
files with the same stem. Extraction checks the destination before decoding,
refuses to replace the source, and finalizes a complete temporary export by a
same-directory move. A failed/cancelled export preserves any previous destination.
Separation checks every requested destination before preparing audio or running
Demucs. Each export is finalized separately; a later failure or cancellation
reports already finalized files and returns a global failure code. Older files
for unfinished exports are preserved.

Examples:

```powershell
odconv process "clip.mp4" --action extract --audio-track 2 --to wav --bit-depth 24
odconv modules install separation
odconv process "clip.mp4" --action separate --audio-track 2 --exports original,vocals,background --to wav --bit-depth 24
odconv modules install transcription
odconv process "interview.wav" --action transcribe --language fr
odconv process "clip.mp4" --action separate-transcribe --exports original,vocals,background,transcript,subtitles --output-dir "D:\Exports" --prefix clip-en
```

## Optional modules command

```powershell
odconv modules list
odconv modules status <separation|transcription>
odconv modules install <separation|transcription>
odconv modules verify <separation|transcription>
odconv modules repair <separation|transcription>
odconv modules uninstall <separation|transcription>
odconv modules --help
```

Install, repair and uninstall are explicit requests. The shared manager uses
private per-user storage, independent module locks, pinned HTTPS transfers,
SHA-256 verification, immutable generations and activation after functional
checks. The baseline is CPU; no global runtime discovery or PATH mutation occurs.

`list` and `status` return 0 after successful inspection and display `Not
installed`, `Installing`, `Ready` or `Needs repair`. `verify` returns 0 only for
`Ready`, otherwise 2. Absent-module inspection creates no directories. Uninstall
removes only the selected module and its cache and is idempotent. Operational
errors return 3; cancellation returns 130.

Install/repair first display the private location, pinned version, CPU profile
and total transfer size. Separation provisions Demucs 4.0.1, private CPython and
PyTorch/torchaudio CPU with htdemucs. Transcription provisions native whisper.cpp
1.8.2 and multilingual large-v3-turbo-q5_0 without Python. Both include private
Visual C++ runtime DLLs and verify a short offline inference before activation.
Unsupported existing installations are reported as `Needs repair` rather than
trusted. See [module lifecycle and validation limits](modules-optionnels.md) and
[pinned distributions and licenses](distributions-modules.md).

## Source and temporal contracts

`MediaSource` is shared by audio and video. Each `AudioTrackInfo` includes the
absolute stream index, zero-based audio ordinal, codec, language, title, sample
rate, channel count/layout, bit depth, bitrate, start timestamp and duration.
Unknown characteristics remain zero/null or `unknown`, as appropriate. A video
cover image marked `attached_pic` does not turn an audio file into a video source.
`probe --json` preserves the original FFprobe JSON, including all streams.

`PresentationTimeline` stores nullable signed origin and positive target duration,
with separate reliability values and warnings:

- `Container`: explicitly reported by FFprobe's format metadata.
- `Streams`: derived from complete relevant stream metadata.
- `Unknown`: no usable value; missing values, `N/A`, NaN, infinity and nonpositive
  durations are not converted to a fabricated zero.
- `Inconsistent`: container duration and complete reference endpoints differ by more
  than 100 ms, or a known endpoint exceeds that duration by more than 100 ms;
  the reported container value is retained for diagnosis.

The reported format start is the preferred presentation origin. Without it, the
earliest stream start is used only when **all audio and presentation video stream
starts are known**. Track selection never changes the source origin. A signed
track offset is `track.StartSeconds - timeline.OriginSeconds`; either missing
operand produces an unknown offset. A negative offset means content precedes the
presentation origin, rather than an instruction to move the source origin.

The container duration is the preferred reported target. When absent, duration
can be derived from the latest reference endpoint minus the origin only when all
reference endpoints are known: video streams for a video source, audio streams
for an audio-only source. Shorter audio never shortens a video target. Partial
stream metadata cannot establish a complete target duration.

These reliability states describe **metadata**, not measured sample alignment.
Timestamp discontinuities, container discrepancies and actual PCM length still
require decoding/measurement in lot 2. The 100 ms discrepancy threshold is for
metadata diagnosis only; the final PCM acceptance tolerance remains one sample.
No preparation or engine adapter may silently replace unknown origin/duration
with zero or claim validated alignment from reported metadata alone.

Lot 2 implements preparation and sample measurement for usable timelines. Unknown
or inconsistent origin/duration is rejected with exit code 3. Timestamp-free PCM
WAV is an explicit exception: its origin is defined by sample zero, announced
during preparation; `probe` retains the unknown timestamp metadata. Read the
[preparation policy](preparation-audio.md) for precision, temporary files and
measured validation limits.

`ProcessingRequest` records action, source, selection, exports, language,
destination and shared audio encoding settings. `AudioOptions` is an encoding
template: its input/output path fields are not used; export paths are resolved
per output. `ProcessingPlan` binds it to the analyzed source and selected track.
`ProcessingResult` carries a global exit code, warnings/error and a list of only
finalized files; failures and cancellations may retain earlier finalized outputs.

## Exit codes

The no-argument interactive menu shares argument validation, module prerequisites,
destination checks and workflow execution with `process` and `modules`. CLI commands
remain noninteractive, including overwrite decisions (`--overwrite always`). The menu
displays unavailable treatments in grey outside its executable choices. Readiness is
checked offline; no implicit install or download is triggered. Every requested export,
including TXT/SRT, is checked before invoking an engine. Completed exports remain listed
when a later export fails or processing is cancelled.

| Code | Meaning |
|---|---|
| 0 | Success or help |
| 1 | Arguments/file/selection error, including a source without any audio track |
| 2 | Required tool, module, model or installable distribution unavailable; `modules verify` found a module that is not ready |
| 3 | Source analysis process failure or processing/engine failure |
| 130 | Cancellation |

`probe` keeps its existing file-analysis error code (1) and reports cancellation
as 130. A mute video can still be inspected successfully with `probe`; it is
rejected by processing before invoking an engine. Missing-module errors
name the matching `odconv modules install separation` or
`odconv modules install transcription` command.
