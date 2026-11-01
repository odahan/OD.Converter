using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OD.Converter.Services.Modules;

namespace OD.Converter.Services;

/// <summary>Contains one global subtitle cue, without speaker attribution.</summary>
public sealed record TranscriptCue(long StartMilliseconds, long EndMilliseconds, string Text);

/// <summary>Validates engine subtitles and writes consistent UTF-8 text and SRT exports.</summary>
public static class TranscriptFormat
{
    /// <summary>Rejects incomplete, unordered or out-of-range engine output rather than hiding it.</summary>
    public static IReadOnlyList<TranscriptCue> Parse(string srt, double duration) =>
        ParseCore(srt, duration, 0, out _);

    /// <summary>Clips only a final Whisper VAD endpoint within its padded 512-sample window and centisecond rounding.</summary>
    public static IReadOnlyList<TranscriptCue> ParseWhisperOutput(string srt, double duration, out bool finalEndClipped)
    {
        var frames = PcmLength.TargetSamples(duration, 16000);
        var paddedFrames = checked((frames + 511) / 512 * 512);
        var maximumEnd = checked((long)Math.Round(paddedFrames * 100d / 16000, MidpointRounding.AwayFromZero) * 10);
        return ParseCore(srt, duration, maximumEnd, out finalEndClipped);
    }

    private static IReadOnlyList<TranscriptCue> ParseCore(string srt, double duration, long maximumEngineEnd, out bool finalEndClipped)
    {
        finalEndClipped = false;
        var cues = new List<TranscriptCue>();
        var limit = checked((long)Math.Round(duration * 1000, MidpointRounding.AwayFromZero));
        var blocks = Regex.Split(srt.Trim().Replace("\r\n", "\n"), @"\n[ \t]*\n");
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            var block = blocks[blockIndex];
            if (string.IsNullOrWhiteSpace(block)) continue;
            var lines = block.Split('\n');
            if (lines.Length < 3 || !int.TryParse(lines[0], out var index) || index != cues.Count + 1)
                throw new InvalidDataException("Whisper produced an incomplete or invalid SRT cue index.");
            var match = Regex.Match(lines[1], @"^(\d{2,}):([0-5]\d):([0-5]\d),(\d{3}) --> (\d{2,}):([0-5]\d):([0-5]\d),(\d{3})$");
            if (!match.Success) throw new InvalidDataException("Whisper produced an invalid SRT timestamp.");
            long Time(int offset) => checked(long.Parse(match.Groups[offset].Value, CultureInfo.InvariantCulture) * 3600000
                + int.Parse(match.Groups[offset + 1].Value, CultureInfo.InvariantCulture) * 60000L
                + int.Parse(match.Groups[offset + 2].Value, CultureInfo.InvariantCulture) * 1000L
                + int.Parse(match.Groups[offset + 3].Value, CultureInfo.InvariantCulture));
            var start = Time(1);
            var end = Time(5);
            var text = string.Join(' ', lines.Skip(2).Select(line => line.Trim())).Trim();
            if (blockIndex == blocks.Length - 1 && start < limit && end > limit && end <= maximumEngineEnd)
            {
                end = limit;
                finalEndClipped = true;
            }
            if (end <= start || end > limit || cues.Count > 0 && start < cues[^1].EndMilliseconds || text.Length == 0)
                throw new InvalidDataException("Whisper produced overlapping, empty or out-of-range subtitles; transcription may be incomplete.");
            cues.Add(new TranscriptCue(start, end, text));
        }
        return cues;
    }

    /// <summary>Formats presentation-relative milliseconds, including durations over 24 hours.</summary>
    public static string Timestamp(long milliseconds) => string.Create(CultureInfo.InvariantCulture,
        $"{milliseconds / 3600000:00}:{milliseconds / 60000 % 60:00}:{milliseconds / 1000 % 60:00},{milliseconds % 1000:000}");

    /// <summary>Serializes both requested formats from the same validated cues.</summary>
    public static string Serialize(IReadOnlyList<TranscriptCue> cues, bool subtitles) => subtitles
        ? string.Concat(cues.Select((cue, index) => $"{index + 1}\n{Timestamp(cue.StartMilliseconds)} --> {Timestamp(cue.EndMilliseconds)}\n{cue.Text}\n\n"))
        : cues.Count == 0 ? "" : string.Join('\n', cues.Select(cue => cue.Text)) + "\n";

    /// <summary>Atomically finalizes an export and preserves any prior destination on cancellation or failure.</summary>
    public static async Task ExportAsync(string content, string destination, bool overwrite, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = Path.Combine(Path.GetDirectoryName(destination)!, $".odconv-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(partial, content, new UTF8Encoding(false), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(partial, destination, overwrite);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}

/// <summary>Runs the pinned native CPU engine on complete PCM, without translation or silence compaction.</summary>
public sealed class WhisperTranscriber(IModuleProcessRunner processRunner, IFfmpegRunner runner)
{
    /// <summary>Uses relative ASCII engine filenames to support private directories containing accents.</summary>
    public async Task<IReadOnlyList<TranscriptCue>> TranscribeAsync(PcmAudio audio, ModuleContext module,
        string language, string ffmpeg, CancellationToken ct)
    {
        using var engineWorkspace = new AudioWorkspace(module.CacheDirectory);
        var wave = await new AudioPreparationService(runner).CreateWhisperWaveAsync(audio, ffmpeg, engineWorkspace, ct);
        var raw = Path.Combine(engineWorkspace.DirectoryPath, "whisper.f32");
        var silent = true;
        using (var reader = new BinaryReader(File.OpenRead(raw)))
        {
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                ct.ThrowIfCancellationRequested();
                var sample = reader.ReadSingle();
                if (!float.IsFinite(sample)) throw new InvalidDataException("Whisper input contains non-finite PCM samples.");
                if (Math.Abs(sample) > 0.00001f) silent = false;
            }
        }
        if (silent) return [];
        Console.WriteLine("Transcribing with Whisper (CPU, source language; no translation)...");
        var context = module with { CacheDirectory = engineWorkspace.DirectoryPath };
        var model = ModulePaths.Child(module.PayloadDirectory, "models/ggml-large-v3-turbo-q5_0.bin");
        var vad = ModulePaths.Child(module.PayloadDirectory, "models/ggml-silero-v5.1.2.bin");
        await processRunner.RunAsync(context, "runtime/Release/whisper-cli.exe",
            ["-m", Path.GetRelativePath(context.CacheDirectory, model), "-f", Path.GetFileName(wave),
                "-l", language, "-t", Math.Clamp(Environment.ProcessorCount, 1, 8).ToString(CultureInfo.InvariantCulture),
                "-ng", "-sns", "--vad", "-vm", Path.GetRelativePath(context.CacheDirectory, vad),
                "-osrt", "-of", "transcription"], TimeSpan.FromDays(7), ct);
        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(engineWorkspace.DirectoryPath, "transcription.srt");
        if (!File.Exists(path)) throw new InvalidDataException("Whisper did not produce its SRT output; transcription is incomplete.");
        var cues = TranscriptFormat.ParseWhisperOutput(await File.ReadAllTextAsync(path, ct),
            (double)audio.SampleCount / audio.SampleRate, out var finalEndClipped);
        if (finalEndClipped)
            Console.WriteLine("Warning: The final Whisper VAD timestamp included its padded detection window; its end was clipped to the source duration.");
        return cues;
    }
}
