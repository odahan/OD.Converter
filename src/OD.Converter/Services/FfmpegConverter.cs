using System.ComponentModel;
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

        var codec = (fmt.Key, opts.BitDepth) switch
        {
            ("wav", 24) => "pcm_s24le",
            ("wav", 32) => "pcm_s32le",
            ("aiff", 24) => "pcm_s24be",
            ("aiff", 32) => "pcm_s32be",
            _ => fmt.Codec
        };
        args.Add("-c:a"); args.Add(codec);

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
        else if (fmt.Key == "flac")
        {
            var level = opts.CompressionLevel ?? 5;
            args.Add("-compression_level"); args.Add(level.ToString());
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
        ct.ThrowIfCancellationRequested();
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
        using var stderr = proc.StandardError;
        using var stdout = proc.StandardOutput;
        var stderrTask = stderr.ReadToEndAsync(ct);
        var stdoutTask = stdout.BaseStream.CopyToAsync(Stream.Null, ct);

        try
        {
            await proc.WaitForExitAsync(ct);
            await Task.WhenAll(stderrTask, stdoutTask).WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when ((ex is InvalidOperationException or Win32Exception) && proc.HasExited)
            {
            }
            await proc.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return (proc.ExitCode, await stderrTask);
    }
}
