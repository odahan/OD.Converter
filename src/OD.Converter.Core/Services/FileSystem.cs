namespace OD.Converter.Services;

/// <summary>
/// Concrete implementation of IFileSystem using System.IO.
/// </summary>
public class FileSystem : IFileSystem
{
    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);
    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);
    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    /// <inheritdoc />
    public string GetFullPath(string path) => Path.GetFullPath(path);
    /// <inheritdoc />
    public string? GetDirectoryName(string path) => Path.GetDirectoryName(path);
    /// <inheritdoc />
    public string GetFileName(string path) => Path.GetFileName(path);
    /// <inheritdoc />
    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);
    /// <inheritdoc />
    public FileInfo GetFileInfo(string path) => new FileInfo(path);
}