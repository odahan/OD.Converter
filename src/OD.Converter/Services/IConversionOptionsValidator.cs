using OD.Converter.Models;

namespace OD.Converter.Services;

public interface IConversionOptionsValidator
{
    void Validate(ConversionOptions options);
    void EnsureDifferentPaths(string inputPath, string outputPath);
}