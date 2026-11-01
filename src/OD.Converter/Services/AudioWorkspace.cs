namespace OD.Converter.Services;

/// <summary>Owns one isolated operation directory, including incomplete intermediates.</summary>
public sealed class AudioWorkspace : IDisposable
{
    /// <summary>Creates a fresh workspace below the specified temporary root.</summary>
    public AudioWorkspace(string? root = null)
    {
        DirectoryPath = Path.Combine(root ?? Path.GetTempPath(), "OD.Converter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>Gets the private directory for this operation.</summary>
    public string DirectoryPath { get; }

    /// <summary>Removes intermediates after success, failure or cancellation.</summary>
    public void Dispose()
    {
        try { Directory.Delete(DirectoryPath, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: unable to remove temporary workspace {DirectoryPath}: {ex.Message}");
        }
    }
}
