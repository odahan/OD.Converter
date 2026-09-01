using OD.Converter.Models;

namespace OD.Converter.Services;

/// <summary>
/// Validates conversion options before execution.
/// </summary>
public interface IConversionOptionsValidator
{
    /// <summary>Validates the conversion options, throwing if any parameter is invalid.</summary>
    void Validate(ConversionOptions options);
    /// <summary>Ensures the input and output paths are not the same file.</summary>
    void EnsureDifferentPaths(string inputPath, string outputPath);
}