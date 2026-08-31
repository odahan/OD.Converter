using OD.Converter.Commands;
using Xunit;

namespace OD.Converter.Tests;

public sealed class FormatsCommandTests
{
    private readonly FormatsCommand _command = new();

    [Fact]
    public async Task RunAsync_ReturnsZero_WhenCalled()
    {
        var exit = await _command.RunAsync(Array.Empty<string>(), CancellationToken.None);
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task RunAsync_ReturnsHelp_WhenHelpFlag()
    {
        var exit = await _command.RunAsync(new[] { "--help" }, CancellationToken.None);
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task RunAsync_ReturnsError_WhenUnknownArgument()
    {
        var exit = await _command.RunAsync(new[] { "unknown" }, CancellationToken.None);
        Assert.Equal(1, exit);
    }
}