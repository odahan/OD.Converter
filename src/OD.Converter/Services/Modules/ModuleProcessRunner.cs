using System.Diagnostics;

namespace OD.Converter.Services.Modules;

/// <summary>Runs a private engine by absolute path with isolated environment and bounded execution time.</summary>
public sealed class ModuleProcessRunner(Action<string>? output = null) : IModuleProcessRunner
{
    /// <summary>Executes an engine without a shell, killing its process tree on cancellation or timeout.</summary>
    public async Task RunAsync(ModuleContext context, string relativeExecutable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var executable = ModulePaths.Child(context.PayloadDirectory, relativeExecutable);
        if (!File.Exists(executable)) throw new FileNotFoundException("Private engine executable is missing.", executable);
        ModulePaths.EnsureNoLinks(context.CacheDirectory);
        Directory.CreateDirectory(context.CacheDirectory);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = context.CacheDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment.Clear();
        foreach (var (key, value) in context.CreateEnvironment()) start.Environment[key] = value;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        var stdout = DrainAsync(process.StandardOutput);
        var stderr = DrainAsync(process.StandardError);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            ct.ThrowIfCancellationRequested();
            throw new IOException($"Private engine execution timed out after {timeout}.");
        }
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0)
            throw new IOException($"Private engine exited with code {process.ExitCode}: {await stderr}");
    }

    private async Task<string> DrainAsync(StreamReader reader)
    {
        var retained = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            output?.Invoke(new string(buffer, 0, count));
            var keep = Math.Min(count, 8192 - retained.Length);
            if (keep > 0) retained.Append(buffer, 0, keep);
        }
        return retained.ToString();
    }
}
