namespace OD.Converter.Services;

/// <summary>
/// Provides an abstraction over file system operations for testability and consistency.
/// </summary>
public interface IFileSystem
{
    /// <summary>Determines whether the specified file exists.</summary>
    bool FileExists(string path);
    /// <summary>Determines whether the specified directory exists.</summary>
    bool DirectoryExists(string path);
    /// <summary>Creates all directories and subdirectories as specified.</summary>
    void CreateDirectory(string path);
    /// <summary>Returns the absolute path for the given path.</summary>
    string GetFullPath(string path);
    /// <summary>Returns the directory information for the specified path.</summary>
    string? GetDirectoryName(string path);
    /// <summary>Returns the file name and extension of the specified path.</summary>
    string GetFileName(string path);
    /// <summary>Returns the file name without the extension.</summary>
    string GetFileNameWithoutExtension(string path);
    /// <summary>Returns a FileInfo instance for the specified path.</summary>
    FileInfo GetFileInfo(string path);
}