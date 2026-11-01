using OD.Converter.Services;
using Xunit;

namespace OD.Converter.Tests;

public sealed class UserGuideTests
{
    [Fact]
    public void Open_ExtractsEmbeddedBilingualContentBeforeLaunchingBrowser()
    {
        using var workspace = new AudioWorkspace();
        var directory = Path.Combine(workspace.DirectoryPath, "guide été & [one]");
        Directory.CreateDirectory(directory);
        string? opened = null;
        var guide = new UserGuideService(directory, path =>
        {
            Assert.True(File.Exists(path));
            opened = path;
        });

        var result = guide.Open();

        Assert.Equal(Path.Combine(directory, UserGuideService.FileName), result);
        Assert.Equal(result, opened);
        var html = File.ReadAllText(result);
        Assert.Contains("<article id=\"en\" lang=\"en\">", html);
        Assert.Contains("<article id=\"fr\" lang=\"fr\">", html);
        Assert.Contains("Premiers pas", html);
        Assert.Contains($"OD.Converter {ApplicationInfo.Version}", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_RefreshesAnOldCopyAndLeavesOtherFilesAlone()
    {
        using var workspace = new AudioWorkspace();
        var path = Path.Combine(workspace.DirectoryPath, UserGuideService.FileName);
        var other = Path.Combine(workspace.DirectoryPath, "notes.html");
        File.WriteAllText(path, "old version");
        File.WriteAllText(other, "keep");

        new UserGuideService(workspace.DirectoryPath).Extract();

        Assert.Contains("<!doctype html>", File.ReadAllText(path));
        Assert.Equal("keep", File.ReadAllText(other));
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public void Extract_LeavesAnIdenticalCopyUntouched()
    {
        using var workspace = new AudioWorkspace();
        var guide = new UserGuideService(workspace.DirectoryPath);
        var path = guide.Extract();
        var oldTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, oldTime);

        Assert.Equal(path, guide.Extract());
        Assert.Equal(oldTime, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Open_WhenExtractionFailsDoesNotLaunchBrowserOrReplaceExistingData()
    {
        using var workspace = new AudioWorkspace();
        var blocked = Path.Combine(workspace.DirectoryPath, UserGuideService.FileName);
        Directory.CreateDirectory(blocked);
        var called = false;
        var guide = new UserGuideService(workspace.DirectoryPath, _ => called = true);

        var error = Record.Exception(() => guide.Open());
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());

        Assert.False(called);
        Assert.True(Directory.Exists(blocked));
        Assert.Empty(Directory.GetFiles(workspace.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public void Open_WhenBrowserFailsLeavesTheGuideAvailableForManualOpening()
    {
        using var workspace = new AudioWorkspace();
        var guide = new UserGuideService(workspace.DirectoryPath, _ => throw new System.ComponentModel.Win32Exception("No browser"));

        Assert.Throws<System.ComponentModel.Win32Exception>(() => guide.Open());

        Assert.True(File.Exists(Path.Combine(workspace.DirectoryPath, UserGuideService.FileName)));
    }
}
