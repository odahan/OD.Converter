using System.IO;
using System.Text.Json;
using OD.Converter.Models;
using OD.Converter.Services;
using OD.Converter.Services.Modules;

namespace OD.Converter.Gui;

/// <summary>Runs an offline smoke test from the exact self-contained executable used for release.</summary>
internal static class BundleValidation
{
    public static async Task<int> RunAsync(string report, ConversionService conversion, ProcessingService processing,
        FfmpegLocator locator, IReadOnlyList<IModuleRecipe> recipes, Func<Task> validateWindow)
    {
        try
        {
            await validateWindow();
            using var workspace = new AudioWorkspace();
            var source = Path.Combine(workspace.DirectoryPath, "mono.wav");
            var destination = Path.Combine(workspace.DirectoryPath, "mono.mp3");
            WriteSource(source);
            var ffmpeg = locator.Resolve(null);
            var ffprobe = locator.ResolveFfprobe(null);
            var bundledTools = Path.GetDirectoryName(ffmpeg)!;
            var extractionRoot = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR")
                ?? throw new InvalidOperationException("An isolated extraction directory is required for the bundle check.");
            var extractionPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(extractionRoot)) + Path.DirectorySeparatorChar;
            if (!bundledTools.StartsWith(extractionPrefix, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(ffprobe), bundledTools, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(bundledTools, "LICENSE")))
                throw new InvalidOperationException("FFmpeg, FFprobe and their license were not resolved from the application bundle.");

            var encoding = new ConversionOptions { TargetFormat = "mp3" };
            var plan = conversion.Plan(new ConversionRequest([source], encoding, destination));
            var converted = await conversion.ExecuteAsync(plan.Executable, plan.Entries.Single(), default,
                approval: new OverwriteApproval([]));
            if (converted.ExitCode != 0) throw new InvalidOperationException(converted.Error);

            var analysis = await processing.AnalyzeAsync(destination, null, default);
            if (analysis.Channels != 1 || analysis.Source?.AudioTracks.Count != 1)
                throw new InvalidOperationException("FFprobe did not find the expected mono track.");
            var extract = new ProcessingRequest(source, ProcessingAction.Extract, 0, workspace.DirectoryPath, "track",
                [ExportKind.Original], "auto", new ConversionOptions { TargetFormat = "wav" });
            var pcm = await processing.AnalyzeAsync(source, null, default);
            var extracted = await processing.ExecuteAsync(ProcessingService.Plan(extract, pcm), default);
            if (extracted.ExitCode != 0 || extracted.Outputs.Count != 1 || !File.Exists(extracted.Outputs[0].Path))
                throw new InvalidOperationException(extracted.ErrorMessage ?? "Offline extraction failed.");

            var guide = new UserGuideService().Extract();
            if (new FileInfo(guide).Length == 0 || recipes.Count != 2 || recipes.Any(recipe => recipe.Downloads.Count == 0))
                throw new InvalidOperationException("The guide or optional module catalogue is missing.");

            WriteReport(report, new { Status = "Passed", ApplicationInfo.Version, Ffmpeg = ffmpeg, Ffprobe = ffprobe, Guide = guide,
                ComboBoxSelection = "Passed", OptionalModulesRefresh = "Passed",
                Modules = recipes.Select(recipe => recipe.Id.ToString()).ToArray() });
            return 0;
        }
        catch (Exception ex)
        {
            WriteReport(report, new { Status = "Failed", Error = ex.ToString() });
            return 1;
        }
    }

    private static void WriteReport(string path, object result) =>
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

    private static void WriteSource(string path)
    {
        const int sampleRate = 44100;
        const int size = sampleRate * sizeof(short);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + size);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(size);
        writer.Write(new byte[size]);
    }
}
