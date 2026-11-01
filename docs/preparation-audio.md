# PCM preparation and presentation timing (lot 2)

`process --action extract` works with the bundled FFmpeg/FFprobe only, on audio
and video sources. It never installs a module or downloads a component. The
historical `convert` command keeps its existing conversion path and naming.

## Preparation contract

`AudioPreparationService` takes a `ProcessingPlan` with an absolute audio stream
index. FFmpeg receives each argument separately, including paths containing spaces
or accents. `-copyts` retains input timestamps; the audio filter subtracts the
presentation origin, then `aresample` inserts or removes samples at timestamp
discontinuities. Soft compensation is disabled: no speed change is used to match
the target duration. `first_pts=0` inserts leading silence or discards samples
before presentation zero. Tail trimming bounds decoding output to the target.

The reference is interleaved float32 PCM at the selected track's sample rate and
channel count, with no intermediate lossy codec. This uses floating-point audio
precision, rather than promising bit identity with every source sample format.
`PcmLength` measures actual bytes divided by channel count and sample size, rejects
empty/incomplete PCM, then pads zeros or trims only the tail to
`round(duration × rate, AwayFromZero)` frames. Tail correction does not validate
internal alignment; marker tests perform that separate check.

Separation input can be written as float32 WAV at the native rate/channel count.
Whisper input is resampled without changing speed to mono 16 kHz, measured and
normalized, then written as signed 16-bit PCM WAV. WAV intermediates support RF64
when necessary. The adapters are available to subsequent lots; no AI engine runs
in lot 2. Future engine adapters must decode their outputs into the common PCM
representation, compensate measured delay and preserve internal positions before
using the tail normalizer.

Exports reuse the existing encoder settings. If a requested sample rate/channel
count differs, another measured PCM is prepared, using the original target
duration at the output sample rate. Lossy exports report a warning about encoder
delay and editing compatibility. Exact decoded length is verified for PCM;
compressed exports do not carry that guarantee.

## Unknown or contradictory metadata

Preparation requires finite origin, positive duration, selected track start,
sample rate and channel count. Unknown or inconsistent timeline metadata causes
an explicit processing failure (exit 3). There is no fallback to the selected
audio length for a video, and no automatic rewrite of contradictory containers.
The current implementation does not reconstruct unknown video endpoints by
scanning every decoded video frame. Containers with misleading durations need
timestamp repair before extraction; this limitation is deliberate and visible.

PCM WAV without timestamp metadata uses its sample-based presentation timeline:
sample zero defines origin zero. This format-specific rule is printed during
preparation. `probe` continues to report the original missing timestamps. Other
formats do not receive this exception.

`EngineTimeline` maps an engine timestamp to presentation-relative seconds as
`engine time + segment start - measured engine delay`. The mapping is applied
once at the adapter boundary, never again when writing SRT. Negative mapped
positions are retained for explicit clipping by that adapter. No engine delay
is guessed in this lot.

## Temporary files and failures

Each operation owns a fresh `%TEMP%/OD.Converter/<guid>` directory; concurrent
operations never reuse another operation's results. Work files are removed on
success, failure and cancellation. A cleanup failure reports the directory to
remove manually. Diagnostic media are not retained automatically.

The encoder writes a unique `.odconv-<guid>.<extension>` file beside the destination,
then moves it into place only after success and a cancellation check. The move
enforces overwrite policy again, covering a destination created after preflight.
On failure or cancellation the partial export is deleted and any previous output
is preserved. FFmpeg cancellation uses the existing runner's process-tree kill.

## Validation

The real FFmpeg tests are in `AudioPreparationTests`. They generate their own
media with three impulses, test both exact lengths and impulse positions, and
delete their fixtures. Cases include WAV/MP3 mono/stereo at 22.05/44.1/48 kHz,
float WAV for separation, 16 kHz mono PCM for Whisper, multipiste MP4/MKV with a
500 ms delayed selected stream, shorter audio than video, timestamp gaps, a
nonzero MPEG-TS origin, and negative input packets with an explicitly supplied
presentation reference. The latter checks preparation from a known reference,
not automatic inference of a negative container's timeline.

MP4 lossless markers and resampling tests use a one-sample position tolerance.
Matroska timestamps have millisecond resolution in these fixtures, so marker
positions allow 48 samples at 48 kHz; lengths remain exact. MPEG-TS/MP2 markers
allow two samples. MP3 markers allow 10 ms because of the lossy impulse response;
this is not an assertion of PCM identity. A one-minute WAV checks resampling
at the beginning, middle and end within one output sample. Long programme media,
AI engine delays, recognition quality and clean-machine publication validation
remain in later lots.
