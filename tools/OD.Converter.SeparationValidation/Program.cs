using System.Text.Json;
using OD.Converter.Commands;
using OD.Converter.Models;
using OD.Converter.Services;
using OD.Converter.Services.Modules;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: SeparationValidation <installed-module-root> <new-output-directory> <voice-sample-1> <voice-sample-2>");
    return 1;
}

var root = Path.GetFullPath(args[1]);
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
    throw new ArgumentException("Validation requires a new, empty output directory.");
Directory.CreateDirectory(root);
var tools = Path.GetFullPath("src/OD.Converter/tools");
var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
var ffprobe = Path.Combine(tools, "ffprobe.exe");
var converter = new FfmpegConverter(new ConversionOptionsValidator(new FileSystem()));
using var client = new HttpClient();
var manager = new ModuleManager(new ModulePaths(args[0]), new ModuleDownloader(client), BuiltInModuleRecipes.Create());
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, value) => { value.Cancel = true; cts.Cancel(); };

async Task Run(params string[] arguments)
{
    var result = await converter.RunAsync(ffmpeg, new[] { "-nostdin", "-v", "error", "-y" }.Concat(arguments).ToArray(), cts.Token);
    if (result.ExitCode != 0) throw new IOException(result.StandardError);
}

var music = Path.Combine(root, "synthetic-music.wav");
await Run("-f", "lavfi", "-i",
    "aevalsrc=0.06*sin(2*PI*220*t)+0.04*sin(2*PI*330*t)+0.03*sin(2*PI*440*t):s=44100:d=12",
    "-ac", "2", "-c:a", "pcm_f32le", music);
var markers = Path.Combine(root, "markers.wav");
var markerSamples = new float[12 * 44100 * 2];
foreach (var time in new[] { 0.25, 6, 11.75 })
{
    var start = (int)(time * 44100);
    for (var i = 0; i < 512; i++)
    {
        var value = (float)(0.3 * Math.Sin(2 * Math.PI * 997 * i / 44100) * Math.Sin(Math.PI * i / 512));
        markerSamples[(start + i) * 2] = markerSamples[(start + i) * 2 + 1] = value;
    }
}
var markerRaw = Path.Combine(root, "markers.f32");
var markerBytes = new byte[markerSamples.Length * 4];
Buffer.BlockCopy(markerSamples, 0, markerBytes, 0, markerBytes.Length);
await File.WriteAllBytesAsync(markerRaw, markerBytes, cts.Token);
await Run("-f", "f32le", "-ar", "44100", "-ac", "2", "-i", markerRaw, "-c:a", "pcm_f32le", markers);

var narration = Path.Combine(root, "narration musicale é.wav");
await Run("-i", Path.GetFullPath(args[2]), "-i", music, "-i", markers, "-filter_complex",
    "[0:a]atrim=duration=10,asetpts=PTS-STARTPTS,adelay=500:all=1,apad=whole_dur=12[v];[v][1:a][2:a]amix=inputs=3:normalize=0:duration=longest[a]",
    "-map", "[a]", "-t", "12", "-ar", "44100", "-ac", "2", "-c:a", "pcm_f32le", narration);
var interview = Path.Combine(root, "alternating interview.wav");
await Run("-i", Path.GetFullPath(args[2]), "-i", Path.GetFullPath(args[3]), "-i", markers, "-filter_complex",
    "[0:a]atrim=duration=4.5,asetpts=PTS-STARTPTS,adelay=500:all=1,apad=whole_dur=12[v1];[1:a]atrim=duration=4.5,asetpts=PTS-STARTPTS,adelay=6500:all=1,apad=whole_dur=12[v2];[v1][v2][2:a]amix=inputs=3:normalize=0:duration=longest[a]",
    "-map", "[a]", "-t", "12", "-ar", "44100", "-ac", "2", "-c:a", "pcm_f32le", interview);
var ambience = Path.Combine(root, "synthetic ambience.wav");
await Run("-f", "lavfi", "-i", "anoisesrc=color=brown:amplitude=0.08:sample_rate=44100:duration=12:seed=123",
    "-i", markers, "-filter_complex", "[0:a][1:a]amix=inputs=2:normalize=0[a]", "-map", "[a]",
    "-ar", "44100", "-ac", "2", "-c:a", "pcm_f32le", ambience);
var video = Path.Combine(root, "delayed second track é.mkv");
await Run("-f", "lavfi", "-i", "color=c=black:s=160x90:r=10:d=14", "-f", "lavfi", "-i",
    "anullsrc=r=48000:cl=stereo:d=14", "-itsoffset", "0.5", "-i", narration, "-map", "0:v", "-map", "1:a", "-map", "2:a",
    "-c:v", "ffv1", "-c:a", "pcm_f32le", "-ar", "48000", video);

var report = new List<object>();
foreach (var input in new[] { narration, interview, ambience, video })
{
    var isVideo = input == video;
    var request = ProcessCommand.ParseArguments(new[] { input, "--action", "separate", "--exports", "original,vocals,background",
        "--bit-depth", "32", "--output-dir", Path.Combine(root, "exports") }.Concat(isVideo ? new[] { "--audio-track", "2" } : []).ToArray());
    var source = (await new FfprobeService().ProbeAsync(input, ffprobe, cts.Token)).Source!;
    var plan = new ProcessingPlan(request, source, source.SelectAudioTrack(request.AudioStreamIndex));
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var result = await new AudioSeparationService(manager, new ModuleProcessRunner(Console.Write), converter, converter)
        .SeparateAsync(plan, ffmpeg, cts.Token);
    if (result.ExitCode != 0) throw new IOException(result.ErrorMessage);
    var decoded = new Dictionary<ExportKind, float[]>();
    foreach (var output in result.Outputs)
    {
        var raw = output.Path + ".f32";
        await Run("-i", output.Path, "-c:a", "pcm_f32le", "-f", "f32le", raw);
        var expected = PcmLength.TargetSamples(source.Timeline.TargetDurationSeconds!.Value, plan.Track.SampleRate);
        if (PcmLength.Measure(raw, plan.Track.Channels) != expected) throw new IOException("Incorrect export length.");
        var bytes = await File.ReadAllBytesAsync(raw, cts.Token);
        var samples = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        if (samples.Any(value => !float.IsFinite(value))) throw new IOException("Non-finite output.");
        decoded.Add(output.Kind, samples);
    }
    var original = decoded[ExportKind.Original];
    var vocals = decoded[ExportKind.Vocals];
    var background = decoded[ExportKind.Background];
    var sum = vocals.Zip(background, (voice, bed) => voice + bed).ToArray();
    var markersAt = new[] { 0.25, 6, 11.75 }.Select(time => time + (isVideo ? 0.5 : 0)).ToArray();
    var lags = markersAt.Select(time => BestLag(original, sum, plan.Track.SampleRate, plan.Track.Channels, time)).ToArray();
    report.Add(new
    {
        Input = input, Track = plan.Track.StreamIndex, Duration = source.Timeline.TargetDurationSeconds,
        plan.Track.SampleRate, plan.Track.Channels, Frames = original.Length / plan.Track.Channels,
        Seconds = watch.Elapsed.TotalSeconds, MarkerTimesSeconds = markersAt, ReconstructedMarkerLagsSamples = lags,
        VocalRms = Rms(vocals), BackgroundRms = Rms(background), ReconstructionErrorRms = Rms(sum.Zip(original, (a, b) => a - b).ToArray()),
        Outputs = result.Outputs
    });
    await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(report,
        new JsonSerializerOptions { WriteIndented = true }), cts.Token);
    if (lags.Any(lag => Math.Abs(lag) > 1))
        throw new IOException("Reconstructed marker alignment exceeds one sample; inspect results.json before accepting the recipe.");
}
return 0;

static double Rms(float[] values) => Math.Sqrt(values.Average(value => (double)value * value));

static int BestLag(float[] reference, float[] actual, int rate, int channels, double time)
{
    var start = (int)Math.Round(time * rate) - 64;
    var length = 640;
    var best = double.NegativeInfinity;
    var bestLag = 0;
    for (var lag = -32; lag <= 32; lag++)
    {
        double cross = 0, energy = 0;
        for (var i = start; i < start + length; i++)
        {
            var a = reference[i * channels];
            var b = actual[(i + lag) * channels];
            cross += a * b;
            energy += b * b;
        }
        var score = cross / Math.Sqrt(energy + 1e-20);
        if (score > best) { best = score; bestLag = lag; }
    }
    return bestLag;
}
