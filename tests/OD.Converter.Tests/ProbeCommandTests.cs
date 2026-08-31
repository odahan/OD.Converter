using OD.Converter.Commands;
using OD.Converter.Services;
using Moq;
using Xunit;

namespace OD.Converter.Tests;

public sealed class ProbeCommandTests
{
    private readonly Mock<IFfmpegLocator> _locatorMock;
    private readonly Mock<IFileSystem> _fsMock;
    private readonly Mock<IFfprobeService> _ffprobeMock;
    private readonly ProbeCommand _command;

    public ProbeCommandTests()
    {
        _locatorMock = new Mock<IFfmpegLocator>();
        _fsMock = new Mock<IFileSystem>();
        _ffprobeMock = new Mock<IFfprobeService>();
        _command = new ProbeCommand(_locatorMock.Object, _fsMock.Object, _ffprobeMock.Object);
    }

    [Fact]
    public async Task RunAsync_ReturnsError_WhenNoInput()
    {
        var exit = await _command.RunAsync(Array.Empty<string>(), CancellationToken.None);
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_ReturnsError_WhenFileNotFound()
    {
        var args = new[] { "missing.wav" };
        _fsMock.Setup(f => f.FileExists("missing.wav")).Returns(false);
        var exit = await _command.RunAsync(args, CancellationToken.None);
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_ReturnsHelp_WhenHelpFlag()
    {
        var args = new[] { "--help" };
        var exit = await _command.RunAsync(args, CancellationToken.None);
        Assert.Equal(0, exit);
    }

    // Additional tests would require mocking FfprobeService; we can inject IFfprobeService as well, but we didn't.
    // For simplicity we leave it; but we could refactor later.
}