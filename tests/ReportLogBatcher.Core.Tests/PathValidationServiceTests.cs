using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Core.Tests;

public sealed class PathValidationServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public PathValidationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB_CoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string NewTempPath(string name) => Path.Combine(_tempRoot, name);

    [Fact]
    public void ValidDocxFile_IsValidReportLog()
    {
        var file = NewTempPath("report.docx");
        File.WriteAllText(file, string.Empty);

        var result = PathValidationService.ValidateReportLog(file);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void UppercaseDocxExtension_IsValidReportLog()
    {
        var file = NewTempPath("report.DOCX");
        File.WriteAllText(file, string.Empty);

        var result = PathValidationService.ValidateReportLog(file);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void WrongFileExtension_IsInvalidReportLog()
    {
        var file = NewTempPath("report.txt");
        File.WriteAllText(file, string.Empty);

        var result = PathValidationService.ValidateReportLog(file);

        Assert.False(result.IsValid);
        Assert.Equal("The report log must be a .docx file.", result.ErrorMessage);
    }

    [Fact]
    public void NonexistentReportLog_IsInvalid()
    {
        var file = NewTempPath("missing.docx");
        File.Delete(file);

        var result = PathValidationService.ValidateReportLog(file);

        Assert.False(result.IsValid);
        Assert.Equal("The selected report log does not exist.", result.ErrorMessage);
    }

    [Fact]
    public void DirectorySuppliedAsReportLog_IsInvalid()
    {
        var directory = NewTempPath("not-a-file");
        Directory.CreateDirectory(directory);

        var result = PathValidationService.ValidateReportLog(directory);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidDirectory_IsValidReportsDirectory()
    {
        var directory = NewTempPath("reports");
        Directory.CreateDirectory(directory);

        var result = PathValidationService.ValidateReportsDirectory(directory);

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void NonexistentReportsDirectory_IsInvalid()
    {
        var directory = NewTempPath("missing-dir");

        var result = PathValidationService.ValidateReportsDirectory(directory);

        Assert.False(result.IsValid);
        Assert.Equal("The selected reports directory does not exist.", result.ErrorMessage);
    }

    [Fact]
    public void FileSuppliedAsReportsDirectory_IsInvalid()
    {
        var file = NewTempPath("file.txt");
        File.WriteAllText(file, string.Empty);

        var result = PathValidationService.ValidateReportsDirectory(file);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhitespacePaths_AreInvalidForBoth(string? path)
    {
        Assert.False(PathValidationService.ValidateReportLog(path).IsValid);
        Assert.Null(PathValidationService.ValidateReportLog(path).ErrorMessage);

        Assert.False(PathValidationService.ValidateReportsDirectory(path).IsValid);
        Assert.Null(PathValidationService.ValidateReportsDirectory(path).ErrorMessage);
    }
}