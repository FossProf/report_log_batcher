using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Core.Tests;

public sealed class BatchDiscoveryServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public BatchDiscoveryServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB_DiscoveryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private string NewTempPath(string name) => Path.Combine(_tempRoot, name);

    private void CreateFile(string name)
    {
        var path = NewTempPath(name);
        var directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory!);
        File.WriteAllText(path, string.Empty);
    }

    private static List<string> FileNames(IEnumerable<BatchEntry> entries) =>
        entries.Select(e => e.FileName).ToList();

    [Fact]
    public void EmptyDirectory_ReturnsZeroEntries()
    {
        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        Assert.Empty(entries);
    }

    [Fact]
    public void SingleDocxFile_IsDiscovered()
    {
        CreateFile("SPIN 1.docx");
        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        var entry = Assert.Single(entries);
        Assert.Equal("SPIN 1.docx", entry.FileName);
        Assert.Equal(NewTempPath("SPIN 1.docx"), entry.FullPath);
    }

    [Fact]
    public void MultipleDocxFiles_AreDiscovered()
    {
        CreateFile("a.docx");
        CreateFile("b.docx");
        CreateFile("c.docx");
        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void UppercaseDocxExtension_IsAccepted()
    {
        CreateFile("SPIN 1.DOCX");
        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        Assert.Single(entries);
    }

    [Fact]
    public void NonDocxFiles_AreIgnored()
    {
        CreateFile("readme.txt");
        CreateFile("notes.md");
        CreateFile("SPIN 1.docx");
        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        Assert.Single(entries);
        Assert.Equal("SPIN 1.docx", entries[0].FileName);
    }

    [Fact]
    public void WordTemporaryFiles_AreIgnored()
    {
        CreateFile("~$SPIN 1.docx");
        CreateFile("~$SPIN 2.docx");
        CreateFile("SPIN 1.docx");
        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        Assert.Equal(["SPIN 1.docx"], FileNames(entries));
    }

    [Fact]
    public void SubdirectoryDocxFiles_AreIgnored()
    {
        CreateFile(Path.Combine("sub", "nested.docx"));
        CreateFile("SPIN 1.docx");
        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        Assert.Equal(["SPIN 1.docx"], FileNames(entries));
    }

    [Fact]
    public void NaturalNumericSorting_OrdersCorrectly()
    {
        var names = new[]
        {
            "SPIN 100.docx",
            "SPIN 2.docx",
            "SPIN 10.docx",
            "SPIN 1.docx",
            "SPIN 9.docx",
            "SPIN 11.docx",
        };

        foreach (var name in names)
            CreateFile(name);

        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        Assert.Equal(
            new[] { "SPIN 1.docx", "SPIN 2.docx", "SPIN 9.docx", "SPIN 10.docx", "SPIN 11.docx", "SPIN 100.docx" },
            FileNames(entries));
    }

    [Fact]
    public void NaturalSorting_IsCaseInsensitive()
    {
        CreateFile("REPORT 10.docx");
        CreateFile("report 2.docx");
        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        Assert.Equal(new[] { "report 2.docx", "REPORT 10.docx" }, FileNames(entries));
    }

    [Fact]
    public void DuplicateFilenameNotCreatedTwice()
    {
        CreateFile("SPIN 1.docx");
        CreateFile("SPIN 1.docx");
        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        var entry = Assert.Single(entries);
        Assert.Equal("SPIN 1.docx", entry.FileName);
    }

    [Fact]
    public void ReturnedEntries_ExposeFilenameAndFullPath()
    {
        CreateFile("SPIN 42.docx");
        var service = new BatchDiscoveryService();

        var entries = service.Discover(_tempRoot);

        var entry = Assert.Single(entries);
        Assert.Equal("SPIN 42.docx", entry.FileName);
        Assert.Equal(NewTempPath("SPIN 42.docx"), entry.FullPath);
        Assert.Equal(0, entry.OriginalIndex);
    }

    [Fact]
    public void NonexistentDirectory_FailsPredictably()
    {
        var missing = NewTempPath("does-not-exist");
        var service = new BatchDiscoveryService();

        Assert.Throws<DirectoryNotFoundException>(() => service.Discover(missing));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhitespaceDirectory_FailsPredictably(string? path)
    {
        var service = new BatchDiscoveryService();

        Assert.Throws<ArgumentException>(() => service.Discover(path!));
    }
}