namespace OD.Converter.Services;

/// <summary>
/// Builds the command-line arguments for an FFmpeg conversion.
/// </summary>
public interface IArgumentBuilder
{
    /// <summary>Constructs the argument list from the given conversion options.</summary>
    List<string> BuildArguments(Models.ConversionOptions opts);
}