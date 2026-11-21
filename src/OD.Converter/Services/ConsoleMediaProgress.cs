namespace OD.Converter.Services;

/// <summary>Renders shared diagnostics on the original CLI streams.</summary>
public sealed class ConsoleMediaProgress : IProgress<MediaProgress>
{
    /// <inheritdoc />
    public void Report(MediaProgress value)
    {
        if (value.Kind == MediaProgressKind.CleanupWarning) Console.Error.WriteLine(value.Message);
        else Console.WriteLine(value.Message);
    }
}
