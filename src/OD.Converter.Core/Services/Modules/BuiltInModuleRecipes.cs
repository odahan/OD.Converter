using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace OD.Converter.Services.Modules;

/// <summary>Registers the reviewed Windows x64 CPU distributions without accessing the network.</summary>
public static class BuiltInModuleRecipes
{
    /// <summary>Creates both independent recipes from an embedded immutable artifact catalogue.</summary>
    public static IReadOnlyList<IModuleRecipe> Create(IModuleProcessRunner? runner = null)
    {
        runner ??= new ModuleProcessRunner();
        var catalogue = JsonSerializer.Deserialize<ArtifactCatalogue>(ReadResource("artifacts.json"))
            ?? throw new InvalidDataException("The built-in module catalogue is missing.");
        return [new SeparationRecipe(catalogue, runner), new TranscriptionRecipe(catalogue, runner)];
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            $"OD.Converter.Services.Modules.Resources.{name}")
            ?? throw new InvalidDataException($"Missing module resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record Artifact(string Name, string Version, Uri Url, string Sha256,
        long SizeBytes, string Destination, bool IsZip, string? License)
    {
        public ModuleDownload Download => new(Url, Sha256, SizeBytes, Destination, IsZip);
    }

    private sealed record ArtifactCatalogue(Artifact[] Common, Artifact[] Bootstrap, Artifact[] Packages, Artifact[] Engines)
    {
        public Artifact Named(string name) => Common.Concat(Engines).Single(item => item.Name == name);
    }

    private abstract class Recipe(ArtifactCatalogue catalogue, IModuleProcessRunner runner) : IModuleRecipe
    {
        protected readonly ArtifactCatalogue Catalogue = catalogue;
        protected readonly IModuleProcessRunner Runner = runner;
        protected const string NativeDirectory = "runtime/Release";
        private const string VisualCppRoot = "Contents/VC/Redist/MSVC/14.44.35112/x64/";
        private static readonly string[] CrtFiles =
        [
            "msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll", "msvcp140_atomic_wait.dll",
            "msvcp140_codecvt_ids.dll", "concrt140.dll", "vcruntime140.dll", "vcruntime140_1.dll",
            "vcruntime140_threads.dll"
        ];

        public abstract ModuleId Id { get; }
        public abstract string Version { get; }
        public abstract IReadOnlyList<ModuleDownload> Downloads { get; }
        public abstract IReadOnlyList<string> RequiredFiles { get; }
        public abstract Task ProvisionAsync(ModuleContext context, CancellationToken ct);
        public abstract Task CheckAsync(ModuleContext context, CancellationToken ct);

        protected ModuleDownload Download(string name) => Catalogue.Named(name).Download;

        protected static void EnsurePlatform()
        {
            if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture !=
                System.Runtime.InteropServices.Architecture.X64)
                throw new InvalidOperationException("Optional CPU distributions require Windows x64.");
        }

        protected static void DeployVisualCpp(ModuleContext context, string destination)
        {
            var target = ModulePaths.Child(context.PayloadDirectory, destination);
            Directory.CreateDirectory(target);
            var archivePath = ModulePaths.Child(context.PayloadDirectory, "packages/vc-runtime.zip");
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var file in CrtFiles)
                    CopyEntry(archive, $"{VisualCppRoot}Microsoft.VC143.CRT/{file}", ModulePaths.Child(target, file));
                CopyEntry(archive, $"{VisualCppRoot}Microsoft.VC143.OpenMP/vcomp140.dll", ModulePaths.Child(target, "vcomp140.dll"));
            }
            File.Delete(archivePath);
        }

        private static void CopyEntry(ZipArchive archive, string entryName, string destination)
        {
            var entry = archive.GetEntry(entryName) ?? throw new InvalidDataException($"Missing Visual C++ file: {entryName}");
            entry.ExtractToFile(destination, overwrite: true);
        }

        protected async Task WriteNoticesAsync(ModuleContext context, IEnumerable<Artifact> artifacts, CancellationToken ct)
        {
            var components = artifacts.ToArray();
            var lines = new List<string>
            {
                "OD.Converter optional CPU module — third-party components",
                "Each component retains its upstream license; no ownership transfer is implied.",
                "Microsoft Visual C++ runtime: see visual-c-runtime.docx. Only release CRT/OpenMP DLLs are deployed.",
            };
            if (components.Any(item => item.Name == "CPython"))
            {
                lines.Add("Python: see runtime/LICENSE.txt. Python dependency notices are retained in Lib/site-packages.");
                lines.Add("LGPL libraries remain separate replaceable dynamic libraries; corresponding source links are in the module documentation.");
            }
            lines.Add("");
            lines.AddRange(components.Select(item => $"{item.Name} {item.Version} | {item.License ?? "See package notices"} | {item.Url}"));
            Directory.CreateDirectory(ModulePaths.Child(context.PayloadDirectory, "licenses"));
            await File.WriteAllLinesAsync(ModulePaths.Child(context.PayloadDirectory, "licenses/THIRD-PARTY-NOTICES.txt"), lines, ct);
        }

        protected static ModuleContext CreateCheckContext(ModuleContext context)
        {
            var cache = ModulePaths.Child(context.CacheDirectory, "check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cache);
            return new ModuleContext(context.PayloadDirectory, cache);
        }

        protected static void CleanupCheck(ModuleContext parent, ModuleContext check) =>
            ModulePaths.DeleteTree(parent.CacheDirectory, Path.GetFileName(check.CacheDirectory));
    }

    private sealed class SeparationRecipe(ArtifactCatalogue catalogue, IModuleProcessRunner runner) : Recipe(catalogue, runner)
    {
        public override ModuleId Id => ModuleId.Separation;
        public override string Version => "demucs-4.0.1-torch-2.6.0-cpu-python-3.11.9-htdemucs-r1";
        public override IReadOnlyList<ModuleDownload> Downloads => new[] { Download("CPython"), Download("VisualCpp"), Download("VisualCppLicense") }
            .Concat(Catalogue.Bootstrap.Select(item => item.Download))
            .Concat(Catalogue.Packages.Select(item => item.Download))
            .Concat([Download("DemucsModel"), Download("DemucsLicense")]).ToArray();
        public override IReadOnlyList<string> RequiredFiles =>
        [
            "runtime/python.exe", "runtime/python311.dll", "runtime/python311._pth", "runtime/LICENSE.txt",
            "runtime/msvcp140.dll", "runtime/vcomp140.dll", "runtime/vcruntime140.dll", "runtime/vcruntime140_1.dll",
            "runtime/Lib/site-packages/demucs/__init__.py", "runtime/Lib/site-packages/torch/__init__.py",
            "runtime/Lib/site-packages/torchaudio/__init__.py", "runtime/Lib/site-packages/soundfile.py",
            "runtime/odconverter_check.py", "models/955717e8-8726e21a.th", "models/htdemucs.yaml",
            "licenses/demucs-LICENSE", "licenses/visual-c-runtime.docx", "licenses/THIRD-PARTY-NOTICES.txt",
            "licenses/python-packages.json"
        ];

        public override async Task ProvisionAsync(ModuleContext context, CancellationToken ct)
        {
            EnsurePlatform();
            DeployVisualCpp(context, "runtime");
            await File.WriteAllTextAsync(ModulePaths.Child(context.PayloadDirectory, "runtime/python311._pth"),
                "python311.zip\n.\nLib/site-packages\nimport site\n", ct);
            var arguments = new List<string>
            {
                "-I", "-B", "-m", "pip", "install", "--no-index", "--no-deps", "--no-build-isolation", "--no-compile",
                "--ignore-installed", "--no-warn-script-location", "--disable-pip-version-check", "--no-cache-dir",
                "--prefix", ModulePaths.Child(context.PayloadDirectory, "runtime")
            };
            arguments.AddRange(Catalogue.Packages.Select(item => ModulePaths.Child(context.PayloadDirectory, item.Destination)));
            await Runner.RunAsync(context, "runtime/python.exe", arguments, TimeSpan.FromMinutes(10), ct);
            await Runner.RunAsync(context, "runtime/python.exe", ["-I", "-B", "-m", "pip", "check"], TimeSpan.FromMinutes(1), ct);
            await File.WriteAllTextAsync(ModulePaths.Child(context.PayloadDirectory, "runtime/odconverter_check.py"), ReadResource("separation_check.py"), ct);
            await File.WriteAllTextAsync(ModulePaths.Child(context.PayloadDirectory, "models/htdemucs.yaml"), "models: ['955717e8']\n", ct);
            await WriteNoticesAsync(context, Catalogue.Common.Concat(Catalogue.Bootstrap).Concat(Catalogue.Packages)
                .Concat(Catalogue.Engines.Where(item => item.Name.StartsWith("Demucs"))), ct);
            await File.WriteAllTextAsync(ModulePaths.Child(context.PayloadDirectory, "licenses/python-packages.json"),
                JsonSerializer.Serialize(Catalogue.Bootstrap.Concat(Catalogue.Packages), new JsonSerializerOptions { WriteIndented = true }), ct);
            ModulePaths.DeleteTree(context.PayloadDirectory, "packages");
        }

        public override async Task CheckAsync(ModuleContext context, CancellationToken ct)
        {
            EnsurePlatform();
            var check = CreateCheckContext(context);
            try
            {
                await Runner.RunAsync(check, "runtime/python.exe", ["-I", "-B", ModulePaths.Child(context.PayloadDirectory, "runtime/odconverter_check.py"),
                    ModulePaths.Child(context.PayloadDirectory, "models/955717e8-8726e21a.th")], TimeSpan.FromMinutes(3), ct);
            }
            finally { CleanupCheck(context, check); }
        }
    }

    private sealed class TranscriptionRecipe(ArtifactCatalogue catalogue, IModuleProcessRunner runner) : Recipe(catalogue, runner)
    {
        public override ModuleId Id => ModuleId.Transcription;
        public override string Version => "whisper.cpp-1.8.2-cpu-large-v3-turbo-q5_0-silero-v5.1.2-r2";
        public override IReadOnlyList<ModuleDownload> Downloads =>
        [Download("Whisper"), Download("VisualCpp"), Download("VisualCppLicense"), Download("WhisperModel"),
            Download("WhisperLicense"), Download("WhisperModelLicense"), Download("WhisperVadModel"), Download("WhisperVadLicense")];
        public override IReadOnlyList<string> RequiredFiles =>
        [
            "runtime/Release/whisper-cli.exe", "runtime/Release/whisper.dll", "runtime/Release/ggml.dll",
            "runtime/Release/ggml-base.dll", "runtime/Release/ggml-cpu.dll", "runtime/Release/msvcp140.dll",
            "runtime/Release/vcomp140.dll", "runtime/Release/vcruntime140.dll", "runtime/Release/vcruntime140_1.dll",
            "models/ggml-large-v3-turbo-q5_0.bin", "licenses/whisper.cpp-LICENSE", "licenses/whisper-model-LICENSE",
            "models/ggml-silero-v5.1.2.bin", "licenses/silero-vad-LICENSE",
            "licenses/visual-c-runtime.docx", "licenses/THIRD-PARTY-NOTICES.txt"
        ];

        public override async Task ProvisionAsync(ModuleContext context, CancellationToken ct)
        {
            EnsurePlatform();
            var native = ModulePaths.Child(context.PayloadDirectory, NativeDirectory);
            var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "whisper-cli.exe", "whisper.dll", "ggml.dll", "ggml-base.dll", "ggml-cpu.dll"
            };
            foreach (var file in Directory.EnumerateFiles(native))
            {
                if (!retained.Contains(Path.GetFileName(file))) File.Delete(file);
            }
            DeployVisualCpp(context, NativeDirectory);
            await WriteNoticesAsync(context, Catalogue.Common.Where(item => item.Name != "CPython")
                .Concat(Catalogue.Engines.Where(item => item.Name.StartsWith("Whisper"))), ct);
            ModulePaths.DeleteTree(context.PayloadDirectory, "packages");
        }

        public override async Task CheckAsync(ModuleContext context, CancellationToken ct)
        {
            EnsurePlatform();
            var check = CreateCheckContext(context);
            try
            {
                var input = ModulePaths.Child(check.CacheDirectory, "silence.wav");
                WriteSilence(input);
                var prefix = ModulePaths.Child(check.CacheDirectory, "check-output");
                var model = ModulePaths.Child(context.PayloadDirectory, "models/ggml-large-v3-turbo-q5_0.bin");
                var relativeModel = Path.GetRelativePath(check.CacheDirectory, model);
                var vad = Path.GetRelativePath(check.CacheDirectory,
                    ModulePaths.Child(context.PayloadDirectory, "models/ggml-silero-v5.1.2.bin"));
                await Runner.RunAsync(check, "runtime/Release/whisper-cli.exe",
                    ["-m", relativeModel, "-f", Path.GetFileName(input), "-l", "en", "-t", "2", "-ng", "-np",
                     "--vad", "-vm", vad, "-otxt", "-of", Path.GetFileName(prefix)], TimeSpan.FromMinutes(3), ct);
                if (!File.Exists(prefix + ".txt")) throw new InvalidDataException("Whisper did not produce its functional-check output.");
            }
            finally { CleanupCheck(context, check); }
        }

        private static void WriteSilence(string path)
        {
            const int bytes = 16000 * 2;
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write("RIFF"u8);
            writer.Write(36 + bytes);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(16000);
            writer.Write(32000);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(bytes);
            writer.Write(new byte[bytes]);
        }
    }
}
