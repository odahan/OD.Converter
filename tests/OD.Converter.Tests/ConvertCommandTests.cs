using OD.Converter.Commands;
using OD.Converter.Models;
using OD.Converter.Services;
using Moq;
using Xunit;

namespace OD.Converter.Tests;

public sealed class ConvertCommandTests
{
    private readonly Mock<IFfmpegRunner> _runnerMock;
    private readonly Mock<IFfmpegLocator> _locatorMock;
    private readonly Mock<IFileSystem> _fsMock;
    private readonly Mock<IArgumentBuilder> _builderMock;
    private readonly Mock<IConversionOptionsValidator> _validatorMock;
    private readonly ConvertCommand _command;

    static ConvertCommandTests()
    {
        // Force static initialization of FormatRegistry before any test runs
        _ = FormatRegistry.Get("mp3");
    }

    public ConvertCommandTests()
    {
        _runnerMock = new Mock<IFfmpegRunner>();
        _locatorMock = new Mock<IFfmpegLocator>();
        _fsMock = new Mock<IFileSystem>();
        _builderMock = new Mock<IArgumentBuilder>();
        _validatorMock = new Mock<IConversionOptionsValidator>();
        
        // Setup default file system behavior - default all files exist to simplify
        _fsMock.Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns(true)
            .Callback<string>(s => Console.WriteLine($"FileExists called with: {s}"));
        _fsMock.Setup(f => f.GetFullPath(It.IsAny<string>())).Returns<string>(s => s);
        _fsMock.Setup(f => f.GetDirectoryName(It.IsAny<string>())).Returns<string>(s => Path.GetDirectoryName(s) ?? string.Empty);
        _fsMock.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(false);
        _fsMock.Setup(f => f.CreateDirectory(It.IsAny<string>()));
        _fsMock.Setup(f => f.GetFileName(It.IsAny<string>())).Returns<string>(s => Path.GetFileName(s));
        _fsMock.Setup(f => f.GetFileNameWithoutExtension(It.IsAny<string>())).Returns<string>(s => Path.GetFileNameWithoutExtension(s));
        
        _validatorMock.Setup(v => v.Validate(It.IsAny<ConversionOptions>()));
        _validatorMock.Setup(v => v.EnsureDifferentPaths(It.IsAny<string>(), It.IsAny<string>()));
        
        _command = new ConvertCommand(_runnerMock.Object, _locatorMock.Object, _fsMock.Object, _builderMock.Object, _validatorMock.Object);
    }

    [Fact]
    public async Task RunAsync_ReturnsError_WhenNoSourceFile()
    {
        var args = new[] { "--to", "mp3" };
        var exit = await _command.RunAsync(args, CancellationToken.None);
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_ReturnsError_WhenSourceFileDoesNotExist()
    {
        var args = new[] { "nonexistent.wav", "--to", "mp3" };
        _fsMock.Setup(f => f.DirectoryExists("nonexistent.wav")).Returns(false);
        _fsMock.Setup(f => f.FileExists("nonexistent.wav")).Returns(false);
        var exit = await _command.RunAsync(args, CancellationToken.None);
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_ReturnsError_WhenTargetFormatMissing()
    {
        var args = new[] { "input.wav" };
        _fsMock.Setup(f => f.DirectoryExists("input.wav")).Returns(false);
        _fsMock.Setup(f => f.FileExists("input.wav")).Returns(true);
        var exit = await _command.RunAsync(args, CancellationToken.None);
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task RunAsync_DryRun_PrintsCommandAndReturnsZero()
    {
        var inputPath = Path.Combine(Path.GetTempPath(), "odconv_test_dryrun.wav");
        var outputPath = Path.Combine(Path.GetTempPath(), "odconv_test_dryrun.mp3");
        var args = new[] { inputPath, "--to", "mp3", "-o", outputPath, "--overwrite", "always", "--dry-run" };
        
        // Create input file (not strictly needed but to be safe)
        File.WriteAllBytes(inputPath, new byte[1024]);
        try
        {
            _fsMock.Setup(f => f.DirectoryExists(inputPath)).Returns(false);
            _fsMock.Setup(f => f.DirectoryExists(inputPath)).Returns(false);
            _fsMock.Setup(f => f.FileExists(inputPath)).Returns(true);
            _fsMock.Setup(f => f.FileExists(outputPath)).Returns(true);
            
            _locatorMock.Setup(l => l.Resolve(null, It.IsAny<string>())).Returns("ffmpeg.exe");
            _builderMock.Setup(b => b.BuildArguments(It.IsAny<ConversionOptions>())).Returns(new List<string> { "-i", inputPath });

            var exit = await _command.RunAsync(args, CancellationToken.None);

            Assert.Equal(0, exit);
            _runnerMock.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
        }
    }

    [Fact]
    public async Task RunAsync_Success_RunsConversionAndReturnsZero()
    {
        // Force initialization of FormatRegistry by accessing All
        var formats = FormatRegistry.All.ToList();
        Assert.NotEmpty(formats);
        // Explicitly verify mp3 is supported
        var mp3Format = FormatRegistry.Get("mp3");
        Assert.NotNull(mp3Format);
        
        var inputPath = Path.Combine(Path.GetTempPath(), "odconv_test_input.wav");
        var outputPath = Path.Combine(Path.GetTempPath(), "odconv_test_output.mp3");
        var args = new[] { inputPath, "--to", "mp3", "-o", outputPath, "--overwrite", "always" };
        Assert.Equal(inputPath, args[0]); // Verify first argument
        
        // Create real files for FileInfo
        File.WriteAllBytes(inputPath, new byte[1024]);
        var tempFileForOutput = Path.GetTempFileName();
        File.WriteAllBytes(tempFileForOutput, new byte[1024]);
        try
        {
            // Set up file existence
            _fsMock.Setup(f => f.DirectoryExists(inputPath)).Returns(false);
            _fsMock.Setup(f => f.FileExists(inputPath)).Returns(true);
            _fsMock.Setup(f => f.FileExists(outputPath)).Returns(true);
            
            _locatorMock.Setup(l => l.Resolve(null, It.IsAny<string>())).Returns("ffmpeg.exe");
            _builderMock.Setup(b => b.BuildArguments(It.IsAny<ConversionOptions>())).Returns(new List<string> { "-i", inputPath });
            _runnerMock.Setup(r => r.RunAsync("ffmpeg.exe", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((0, ""));
            _fsMock.Setup(f => f.GetFileInfo(outputPath)).Returns(new FileInfo(tempFileForOutput));

            var exit = await _command.RunAsync(args, CancellationToken.None);

            // Verify all mocks were called before asserting exit code
            _fsMock.Verify(f => f.DirectoryExists(inputPath), Times.AtLeastOnce);
            _fsMock.Verify(f => f.FileExists(inputPath), Times.Once);
            _fsMock.Verify(f => f.FileExists(outputPath), Times.AtLeastOnce);
            _validatorMock.Verify(v => v.Validate(It.IsAny<ConversionOptions>()), Times.Once);
            _validatorMock.Verify(v => v.EnsureDifferentPaths(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _locatorMock.Verify(l => l.Resolve(null, It.IsAny<string>()), Times.Once);
            _builderMock.Verify(b => b.BuildArguments(It.IsAny<ConversionOptions>()), Times.Once);
            _runnerMock.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);

            Assert.Equal(0, exit);
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(tempFileForOutput)) File.Delete(tempFileForOutput);
        }
    }

    [Fact]
    public async Task RunAsync_ConversionFails_ReturnsError()
    {
        // Force initialization of FormatRegistry by accessing All
        var formats = FormatRegistry.All.ToList();
        Assert.NotEmpty(formats);
        // Explicitly verify mp3 is supported
        var mp3Format = FormatRegistry.Get("mp3");
        Assert.NotNull(mp3Format);
        
        var inputPath = Path.Combine(Path.GetTempPath(), "odconv_test_input2.wav");
        var outputPath = Path.Combine(Path.GetTempPath(), "odconv_test_output2.mp3");
        var args = new[] { inputPath, "--to", "mp3", "-o", outputPath, "--overwrite", "always" };
        
        File.WriteAllBytes(inputPath, new byte[1024]);
        try
        {
            // Set up file existence
            _fsMock.Setup(f => f.DirectoryExists(inputPath)).Returns(false);
            _fsMock.Setup(f => f.FileExists(inputPath)).Returns(true);
            _fsMock.Setup(f => f.FileExists(outputPath)).Returns(true);
            
            _locatorMock.Setup(l => l.Resolve(null, It.IsAny<string>())).Returns("ffmpeg.exe");
            _builderMock.Setup(b => b.BuildArguments(It.IsAny<ConversionOptions>())).Returns(new List<string> { "-i", inputPath });
            _runnerMock.Setup(r => r.RunAsync("ffmpeg.exe", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((1, "error"));

            var exit = await _command.RunAsync(args, CancellationToken.None);

            // Verify mocks were called before asserting exit code
            _fsMock.Verify(f => f.DirectoryExists(inputPath), Times.AtLeastOnce);
            _fsMock.Verify(f => f.FileExists(inputPath), Times.Once);
            _fsMock.Verify(f => f.FileExists(outputPath), Times.AtLeastOnce);
            _validatorMock.Verify(v => v.Validate(It.IsAny<ConversionOptions>()), Times.Once);
            _validatorMock.Verify(v => v.EnsureDifferentPaths(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _locatorMock.Verify(l => l.Resolve(null, It.IsAny<string>()), Times.Once);
            _builderMock.Verify(b => b.BuildArguments(It.IsAny<ConversionOptions>()), Times.Once);
            _runnerMock.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);

            Assert.Equal(3, exit);
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
        }
    }
}