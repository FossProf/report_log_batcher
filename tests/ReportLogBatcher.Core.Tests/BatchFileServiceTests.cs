using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Core.Tests;

public sealed class BatchFileServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public BatchFileServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB_FileServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string NewTempFile(string name, string content = "content")
    {
        var path = Path.Combine(_tempRoot, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void FileNameWithoutExtension_GetsDocxAppended()
    {
        var result = new BatchFileService().ValidateFileName("New Report Name");

        Assert.True(result.IsValid);
        Assert.Equal("New Report Name.docx", result.FinalFileName);
    }

    [Fact]
    public void FileNameWithDocx_IsAccepted()
    {
        var result = new BatchFileService().ValidateFileName("New Report Name.docx");

        Assert.True(result.IsValid);
        Assert.Equal("New Report Name.docx", result.FinalFileName);
    }

    [Fact]
    public void UppercaseDocxExtension_IsAccepted()
    {
        var result = new BatchFileService().ValidateFileName("REPORT.DOCX");

        Assert.True(result.IsValid);
        Assert.Equal("REPORT.DOCX", result.FinalFileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceFileName_IsRejected(string name)
    {
        var result = new BatchFileService().ValidateFileName(name);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("bad:name")]
    [InlineData("bad<name>")]
    [InlineData("bad|name")]
    public void InvalidWindowsCharacters_AreRejected(string name)
    {
        var result = new BatchFileService().ValidateFileName(name);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("bad\\name.docx")]
    [InlineData("bad/name.docx")]
    public void DirectorySeparators_AreRejected(string name)
    {
        var result = new BatchFileService().ValidateFileName(name);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void TrailingPeriod_IsRejected()
    {
        var result = new BatchFileService().ValidateFileName("report.");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void TrailingSpace_IsRejected()
    {
        var result = new BatchFileService().ValidateFileName("report ");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void SinglePeriod_IsRejected()
    {
        var result = new BatchFileService().ValidateFileName(".");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void DoublePeriod_IsRejected()
    {
        var result = new BatchFileService().ValidateFileName("..");

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("CON.docx")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("com5.docx")]
    [InlineData("LPT9.docx")]
    public void ReservedDeviceNames_AreRejected(string name)
    {
        var result = new BatchFileService().ValidateFileName(name);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void RenameSucceeds_WhenDestinationIsFree()
    {
        var source = NewTempFile("a.docx");
        var service = new BatchFileService();

        var result = service.RenameFile(source, "b");

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(_tempRoot, "b.docx"), result.DestinationPath);
        Assert.Equal("b.docx", result.DestinationFileName);
    }

    [Fact]
    public void OriginalFile_DoesNotExistAfterRename()
    {
        var source = NewTempFile("a.docx");
        var service = new BatchFileService();

        service.RenameFile(source, "b");

        Assert.False(File.Exists(source));
    }

    [Fact]
    public void RenameFile_ExistsAtDestination()
    {
        var source = NewTempFile("a.docx");
        var service = new BatchFileService();

        var result = service.RenameFile(source, "b");

        Assert.True(File.Exists(result.DestinationPath));
    }

    [Fact]
    public void Collision_IsRejected()
    {
        var source = NewTempFile("a.docx", "source");
        NewTempFile("b.docx", "other");
        var service = new BatchFileService();

        var result = service.RenameFile(source, "b");

        Assert.False(result.IsSuccess);
        Assert.Equal(RenameFailureReason.Collision, result.FailureReason);
    }

    [Fact]
    public void Collision_DoesNotAlterEitherFile()
    {
        var source = NewTempFile("a.docx", "source");
        NewTempFile("b.docx", "other");
        var service = new BatchFileService();

        service.RenameFile(source, "b");

        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("other", File.ReadAllText(Path.Combine(_tempRoot, "b.docx")));
        Assert.Equal(2, Directory.GetFiles(_tempRoot).Length);
    }

    [Fact]
    public void DirectoryAtDestination_IsRejectedAsCollision()
    {
        var source = NewTempFile("a.docx", "source");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "b.docx"));
        var service = new BatchFileService();

        var result = service.RenameFile(source, "b");

        Assert.False(result.IsSuccess);
        Assert.Equal(RenameFailureReason.Collision, result.FailureReason);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void RenameCannotEscapeParentDirectory()
    {
        var source = NewTempFile("a.docx");
        var service = new BatchFileService();

        Assert.False(service.RenameFile(source, "..\\evil.docx").IsSuccess);
        Assert.False(service.RenameFile(source, "..").IsSuccess);
        Assert.False(service.RenameFile(source, "sub\\evil.docx").IsSuccess);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void CaseOnlyRename_Works()
    {
        var source = NewTempFile("spin 10.docx");
        var service = new BatchFileService();

        var result = service.RenameFile(source, "SPIN 10.docx");

        Assert.True(result.IsSuccess);
        var onDisk = Directory.GetFiles(_tempRoot).Select(Path.GetFileName).ToArray();
        Assert.Single(onDisk);
        Assert.Equal("SPIN 10.docx", onDisk[0]);
    }

    [Fact]
    public void RenameToSameName_IsSuccessfulNoOp()
    {
        var source = NewTempFile("a.docx", "content");
        var service = new BatchFileService();

        var result = service.RenameFile(source, "a");

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(source));
        Assert.Equal("content", File.ReadAllText(source));
        Assert.Single(Directory.GetFiles(_tempRoot));
    }

    [Fact]
    public void FailedRename_LeavesSourceIntact()
    {
        var source = NewTempFile("a.docx", "source-content");
        NewTempFile("b.docx", "other-content");
        var service = new BatchFileService();

        var result = service.RenameFile(source, "b");

        Assert.False(result.IsSuccess);
        Assert.True(File.Exists(source));
        Assert.Equal("source-content", File.ReadAllText(source));
        Assert.Equal("other-content", File.ReadAllText(Path.Combine(_tempRoot, "b.docx")));
    }

    [Fact]
    public void InvalidFileName_LeavesSourceIntact()
    {
        var source = NewTempFile("a.docx", "content");
        var service = new BatchFileService();

        var result = service.RenameFile(source, "CON");

        Assert.False(result.IsSuccess);
        Assert.Equal(RenameFailureReason.InvalidFileName, result.FailureReason);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MissingSourceFile_FailsPredictably()
    {
        var missing = Path.Combine(_tempRoot, "nope.docx");
        var service = new BatchFileService();

        var result = service.RenameFile(missing, "b");

        Assert.False(result.IsSuccess);
        Assert.Equal(RenameFailureReason.SourceMissing, result.FailureReason);
    }
}