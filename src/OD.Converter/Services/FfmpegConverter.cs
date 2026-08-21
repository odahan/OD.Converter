using System.Diagnostics;
using OD.Converter.Models;

namespace OD.Converter.Services;

public static class FfmpegConverter
{
    public static List<string> BuildArguments(ConversionOptions opts)
    {
        var fmt = FormatRegistry.Get(opts.TargetFormat)
            ?? throw new ArgumentException($"Format non supporté : {opts.TargetFormat}");

        var args = new List<string>();

        // overwrite handling: we handle file existence ourselves, but tell ffmpeg to overwrite if needed
        if (opts.Overwrite == "always")
            args.Add("-y");
        else
            args.Add("-n");

        args.Add("-hide_banner");
        args.Add("-loglevel"); args.Add("error");
        args.Add("-i"); args.Add(opts.InputPath);

        // no video
        args.Add("-vn");

        // codec
        args.Add("-c:a"); args.Add(fmt.Codec);

        // bitrate / quality per format
        if (!fmt.IsLossless)
        {
            switch (fmt.Key)
            {
                case "mp3":
                    {
                        var br = opts.Bitrate ?? fmt.DefaultBitrate;
                        if (opts.Quality.HasValue)
                        {
                            // VBR: -q:a 0..9 (0 best)
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
                        // libvorbis: -q:a 0..10
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
                // bit depth handling
                if (opts.BitDepth.HasValue)
                {
                    var codec = opts.BitDepth.Value switch
                    {
                        16 => "pcm_s16le",
                        24 => "pcm_s24le",
                        32 => "pcm_s32le",
                        _ => "pcm_s16le"
                    };
                    // replace codec
                    var idx = args.IndexOf(fmt.Codec);
                    if (idx >= 0) args[idx] = codec;
                }
            }
        }

        // sample rate
        if (opts.SampleRate.HasValue)
        {
            args.Add("-ar"); args.Add(opts.SampleRate.Value.ToString());
        }

        // channels / mono
        var effCh = opts.EffectiveChannels;
        if (effCh > 0)
        {
            args.Add("-ac"); args.Add(effCh.ToString());
        }

        args.Add(opts.OutputPath);
        return args;
    }

    public static string BuildCommandLine(string ffmpegPath, List<string> args)
    {
        string Quote(string s) => s.Contains(' ') || s.Contains('"') ? $"\"{s.Replace("\"", "\\\"")}\"" : s;
        return $"{Quote(ffmpegPath)} {string.Join(" ", args.Select(Quote))}";
    }

    public static async Task<int> RunAsync(string ffmpegPath, List<string> args, CancellationToken ct)
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
        var tcs = new TaskCompletionSource<int>();
        var stderrLines = new List<string>();

        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) => tcs.TrySetResult(proc.ExitCode);

        // handle cancellation
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            tcs.TrySetCanceled(ct);
        });

        proc.Start();

        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);

        try
        {
            var exitCode = await tcs.Task;
            var stderr = await stderrTask;
            _ = await stdoutTask;
            if (!string.IsNullOrWhiteSpace(stderr) && exitCode != 0)
            {
                // store for caller
                proc.StartInfo.Environment["_OD_STDERR"] = stderr;
            }
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            throw;
        }
    }

    public static async Task<(int exitCode, string stderr)> RunWithOutputAsync(string ffmpegPath, List<string> args, CancellationToken ct)
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

        // cancellation
        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } });

        await proc.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        _ = await stdoutTask;
        return (proc.ExitCode, stderr);
    }
}
