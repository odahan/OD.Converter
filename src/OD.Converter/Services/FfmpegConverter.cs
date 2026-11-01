using System.Diagnostics;
using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>
/// Builds and executes FFmpeg conversion commands.
/// </summary>
public class FfmpegConverter : IFfmpegRunner, IArgumentBuilder
{
    private readonly IConversionOptionsValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="FfmpegConverter"/> class.
    /// </summary>
    /// <param name="validator">The validator used to ensure conversion options are correct before building arguments.</param>
    public FfmpegConverter(IConversionOptionsValidator validator)
    {
        _validator = validator;
    }

    /// <inheritdoc />
    public List<string> BuildArguments(ConversionOptions opts)
    {
        _validator.Validate(opts);
        var fmt = FormatRegistry.Get(opts.TargetFormat)
            ?? throw new ArgumentException($"Unsupported format: {opts.TargetFormat}.");

        var args = new List<string>();

        // Overwrite behavior: -y always, -n never
        if (opts.Overwrite == "always")
            args.Add("-y");
        else
            args.Add("-n");

        args.Add("-hide_banner");
        args.Add("-loglevel"); args.Add("error");
        args.Add("-i"); args.Add(opts.InputPath);

        args.Add("-vn"); // disable video

        args.Add("-c:a"); args.Add(fmt.Codec);

        // Apply format-specific parameters
        if (!fmt.IsLossless)
        {
            switch (fmt.Key)
            {
                case "mp3":
                    {
                        var br = opts.Bitrate ?? fmt.DefaultBitrate;
                        if (opts.Quality.HasValue)
                        {
                            args.Add("-q:a"); args.Add(opts.Quality.Value.ToString());
                        }
                        else
                        {
                            args.Add("-b:a"); args.Add(br);
                        }
                        break;
                    }
                case "ogg":
                    {
                        var q = opts.Quality ?? 6;
                        if (opts.Bitrate is not null)
                        {
                            args.Add("-b:a"); args.Add(opts.Bitrate);
                        }
                        else
                        {
                            args.Add("-q:a"); args.Add(q.ToString());
                        }
                        break;
                    }
                case "opus":
                case "aac":
                case "m4a":
                case "wma":
                    {
                        var br = opts.Bitrate ?? fmt.DefaultBitrate;
                        args.Add("-b:a"); args.Add(br);
                        break;
                    }
            }
        }
        else
        {
            if (fmt.Key == "flac")
            {
                var level = opts.CompressionLevel ?? 5;
                args.Add("-compression_level"); args.Add(level.ToString());
            }
            if (fmt.Key == "wav" || fmt.Key == "aiff")
            {
                if (opts.BitDepth.HasValue)
                {
                    var codec = opts.BitDepth.Value switch
                    {
                        16 => "pcm_s16le",
                        24 => "pcm_s24le",
                        32 => "pcm_s32le",
                        _ => throw new ArgumentOutOfRangeException(nameof(opts.BitDepth))
                    };
                    // Replace the codec previously set with the specific PCM variant
                    var idx = args.IndexOf(fmt.Codec);
                    if (idx >= 0) args[idx] = codec;
                }
            }
        }

        // Sample rate
        if (opts.SampleRate.HasValue)
        {
            args.Add("-ar"); args.Add(opts.SampleRate.Value.ToString());
        }

        // Channel count (mono overrides any explicit channels)
        var effCh = opts.EffectiveChannels;
        if (effCh > 0)
        {
            args.Add("-ac"); args.Add(effCh.ToString());
        }

        args.Add(opts.OutputPath);
        return args;
    }

    /// <summary>
    /// Builds a command-line string for display or dry-run purposes.
    /// </summary>
    public static string BuildCommandLine(string ffmpegPath, IReadOnlyCollection<string> args)
    {
        string Quote(string s) => s.Contains(' ') || s.Contains('"') ? $"\"{s.Replace("\"", "\\\"")}\"" : s;
        return $"{Quote(ffmpegPath)} {string.Join(" ", args.Select(Quote))}";
    }

    /// <inheritdoc />
    public async Task<(int ExitCode, string StandardError)> RunAsync(string ffmpegPath, IReadOnlyCollection<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);

        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } });

        await proc.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        _ = await stdoutTask;
        return (proc.ExitCode, stderr);
    }
}
