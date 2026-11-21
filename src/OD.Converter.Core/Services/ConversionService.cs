using System.Globalization;
using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>Describes the batch syntax independently of command-line parsing.</summary>
public sealed record ConversionRequest(IReadOnlyList<string> Inputs, ConversionOptions Encoding,
    string? OutputFile = null, string? OutputDirectory = null, string? Pattern = null, bool Recursive = false);

/// <summary>Describes a resolved conversion and any validation error specific to the entry.</summary>
public sealed record ConversionEntry(ConversionOptions Options, IReadOnlyList<string>? Arguments, string? Error,
    int ErrorCode = 0);

/// <summary>Contains the executable and all resolved conversions in input order.</summary>
public sealed record ConversionPlan(string Executable, IReadOnlyList<ConversionEntry> Entries);

/// <summary>Reports a conversion attempt, including the diagnostic from FFmpeg when available.</summary>
public sealed record ConversionItemResult(ConversionEntry Entry, int ExitCode, string? Error = null,
    long? OutputBytes = null, string? TechnicalDetail = null);

/// <summary>Identifies a failure while resolving a configured native tool.</summary>
public sealed class MediaToolException(Exception cause) : Exception(cause.Message, cause);

/// <summary>Plans classic conversions and runs the same argument builder and FFmpeg runner for both interfaces.</summary>
public sealed class ConversionService(IFfmpegRunner runner, IFfmpegLocator locator, IFileSystem files,
    IArgumentBuilder builder, IConversionOptionsValidator validator)
{
    /// <summary>Resolves source expansion, destinations and commands without creating files or directories.</summary>
    public ConversionPlan Plan(ConversionRequest request, CancellationToken ct = default, bool prepareCommands = true)
    {
        if (request.Inputs.Count == 0)
            throw new ArgumentException("Source file or directory is required. Usage: odconv convert <file>... --to <format> [options]");
        if (string.IsNullOrWhiteSpace(request.Encoding.TargetFormat))
            throw new ArgumentException("Target format is required. Use --to <format>, for example --to mp3.");
        if (!string.IsNullOrEmpty(request.OutputFile) &&
            (request.Inputs.Count > 1 || request.Inputs.Any(files.DirectoryExists)))
            throw new ArgumentException("--output cannot be used with multiple input files or directories. Use --output-dir instead.");

        var inputs = new List<string>();
        foreach (var input in request.Inputs.Select(path => path.Trim().Trim('"')))
        {
            ct.ThrowIfCancellationRequested();
            if (files.DirectoryExists(input))
            {
                var search = request.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var path in Directory.EnumerateFiles(input, request.Pattern ?? "*", search))
                {
                    ct.ThrowIfCancellationRequested();
                    inputs.Add(path);
                }
            }
            else
            {
                if (!files.FileExists(input)) throw new ArgumentException($"Source file not found: \"{input}\".");
                inputs.Add(input);
            }
        }

        if (inputs.Count == 0) throw new ArgumentException("No input files found.");
        var formatKey = request.Encoding.TargetFormat.Trim().TrimStart('.').ToLowerInvariant();
        var format = FormatRegistry.Get(formatKey)
            ?? throw new ArgumentException($"Unsupported format: \"{formatKey}\". Formats: {FormatRegistry.SupportedList}");
        string executable;
        try { executable = locator.Resolve(request.Encoding.FfmpegPath); }
        catch (Exception ex) { throw new MediaToolException(ex); }

        var entries = new List<ConversionEntry>(inputs.Count);
        var singleOutput = inputs.Count == 1 ? request.OutputFile?.Trim().Trim('"') : null;
        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();
            var output = string.IsNullOrEmpty(singleOutput) ? OutputPath(request, input, format) : singleOutput;
            if (string.IsNullOrWhiteSpace(Path.GetExtension(output))) output += format.Extension;
            var options = request.Encoding with { InputPath = input, OutputPath = output, TargetFormat = formatKey };
            if (!prepareCommands)
            {
                entries.Add(new ConversionEntry(options, null, null));
                continue;
            }
            try
            {
                validator.Validate(options);
                validator.EnsureDifferentPaths(input, output);
                entries.Add(new ConversionEntry(options, builder.BuildArguments(options), null));
            }
            catch (ArgumentException ex) { entries.Add(new ConversionEntry(options, null, ex.Message, 1)); }
            catch (Exception ex) { entries.Add(new ConversionEntry(options, null, $"Unexpected error: {ex.Message}", 3)); }
        }
        return new ConversionPlan(executable, entries);
    }

    /// <summary>Converts one planned file. With scoped approval, only complete output replaces a reviewed destination.</summary>
    public async Task<ConversionItemResult> ExecuteAsync(string executable, ConversionEntry entry, CancellationToken ct,
        Action? started = null, OverwriteApproval? approval = null)
    {
        ct.ThrowIfCancellationRequested();
        if (entry.Error is not null) return new ConversionItemResult(entry, entry.ErrorCode, entry.Error);
        if (entry.Options.DryRun) return new ConversionItemResult(entry, 0);

        var output = entry.Options.OutputPath;
        string? temporary = null;
        try
        {
            if (entry.Arguments is null)
            {
                validator.Validate(entry.Options);
                validator.EnsureDifferentPaths(entry.Options.InputPath, output);
            }
            if (approval is null && files.FileExists(output) && entry.Options.Overwrite == "never")
                return new ConversionItemResult(entry, 1,
                    $"Output file already exists: \"{output}\". Use --overwrite always or choose another name.");
            approval?.CanReplace(output);

            var directory = files.GetDirectoryName(files.GetFullPath(output));
            if (!string.IsNullOrEmpty(directory) && !files.DirectoryExists(directory))
                files.CreateDirectory(directory);

            var arguments = entry.Arguments ?? builder.BuildArguments(entry.Options);
            if (approval is not null)
            {
                temporary = Path.Combine(directory!, $".odconv-{Guid.NewGuid():N}{Path.GetExtension(output)}");
                arguments = builder.BuildArguments(entry.Options with { OutputPath = temporary, Overwrite = "never" });
            }
            started?.Invoke();
            var (exitCode, error) = await runner.RunAsync(executable, arguments, ct);
            if (exitCode != 0)
                return new ConversionItemResult(entry, 3,
                    $"Conversion failed ({FfmpegDetail(error, exitCode)}). Check the source file or try another format.",
                    TechnicalDetail: error);
            if (!files.FileExists(temporary ?? output))
                return new ConversionItemResult(entry, 3, $"Conversion completed but output file was not found: \"{output}\".");
            if (temporary is not null)
            {
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, output, approval!.CanReplace(output));
            }
            return new ConversionItemResult(entry, 0, OutputBytes: files.GetFileInfo(output).Length);
        }
        catch (ArgumentException ex) { return new ConversionItemResult(entry, 1, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConversionItemResult(entry, 3, $"Unexpected error: {ex.Message}");
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Returns the last meaningful FFmpeg error line, limited to 300 text elements.</summary>
    public static string FfmpegDetail(string standardError, int exitCode)
    {
        var error = standardError.AsSpan().TrimEnd();
        if (error.IsEmpty) return $"FFmpeg exited with code {exitCode}";

        var detail = error[(error.LastIndexOf('\n') + 1)..].TrimStart().ToString();
        const int maxTextElements = 300;
        if (detail.Length <= maxTextElements) return detail;

        var text = new StringInfo(detail);
        return text.LengthInTextElements > maxTextElements ? text.SubstringByTextElements(0, maxTextElements) : detail;
    }

    private static string OutputPath(ConversionRequest request, string input, FormatInfo format)
    {
        if (!string.IsNullOrEmpty(request.OutputDirectory))
        {
            var relative = Path.GetRelativePath(Environment.CurrentDirectory, input);
            if (Path.IsPathRooted(relative) || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                relative = Path.GetFileName(input);
            return Path.Combine(request.OutputDirectory, Path.ChangeExtension(relative, format.Extension));
        }
        var directory = Path.GetDirectoryName(input) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(input);
        return Path.Combine(directory, $"{stem}{(request.Encoding.Mono ? "-mono" : "")}{format.Extension}");
    }
}
