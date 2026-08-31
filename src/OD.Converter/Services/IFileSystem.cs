namespace OD.Converter.Services;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    string GetFullPath(string path);
    string? GetDirectoryName(string path);
    string GetFileName(string path);
    string GetFileNameWithoutExtension(string path);
    FileInfo GetFileInfo(string path);
}