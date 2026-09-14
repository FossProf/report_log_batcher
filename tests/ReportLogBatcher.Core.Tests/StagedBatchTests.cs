using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Core.Tests;

public sealed class StagedBatchTests : IDisposable
{
    private readonly string _tempRoot;

    public StagedBatchTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB_StagedBatchTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static BatchEntry Entry(string name, int index) =>
        new(Path.Combine("C:\\reports", name), name, index);

    private List<string> Names(StagedBatch batch) =>
        batch.Entries.Select(e => e.FileName).ToList();

    private StagedBatch BatchOf(params string[] names) =>
        new StagedBatch().With(names.ToList());

    private List<string> CreateReportFiles(params string[] names)
    {
        var paths = new List<string>();
        foreach (var name in names)
        {
            var path = Path.Combine(_tempRoot, name);
            File.WriteAllText(path, string.Empty);
            paths.Add(path);
        }

        return paths;
    }

    [Fact]
    public void MoveUp_SwapsWithPrecedingEntry()
    {
        var batch = BatchOf("A.docx", "B.docx", "C.docx");

        var moved = batch.MoveUp(1);

        Assert.True(moved);
        Assert.Equal(["B.docx", "A.docx", "C.docx"], Names(batch));
    }

    [Fact]
    public void MoveDown_SwapsWithFollowingEntry()
    {
        var batch = BatchOf("A.docx", "B.docx", "C.docx");

        var moved = batch.MoveDown(1);

        Assert.True(moved);
        Assert.Equal(["A.docx", "C.docx", "B.docx"], Names(batch));
    }

    [Fact]
    public void FirstEntry_CannotMoveUp()
    {
        var batch = BatchOf("A.docx", "B.docx");

        var moved = batch.MoveUp(0);

        Assert.False(moved);
        Assert.Equal(["A.docx", "B.docx"], Names(batch));
    }

    [Fact]
    public void LastEntry_CannotMoveDown()
    {
        var batch = BatchOf("A.docx", "B.docx");

        var moved = batch.MoveDown(1);

        Assert.False(moved);
        Assert.Equal(["A.docx", "B.docx"], Names(batch));
    }

    [Fact]
    public void RemoveAt_ReturnsNextRowAsNewSelection()
    {
        var batch = BatchOf("A.docx", "B.docx", "C.docx");

        var newSelection = batch.RemoveAt(1);

        Assert.Equal(1, newSelection);
        Assert.Equal(["A.docx", "C.docx"], Names(batch));
    }

    [Fact]
    public void RemoveAt_LastRemoved_ReturnsPreviousRow()
    {
        var batch = BatchOf("A.docx", "B.docx", "C.docx");

        var newSelection = batch.RemoveAt(2);

        Assert.Equal(1, newSelection);
        Assert.Equal(["A.docx", "B.docx"], Names(batch));
    }

    [Fact]
    public void RemoveAt_LastRemainingRow_ReturnsMinusOne()
    {
        var batch = BatchOf("A.docx");

        var newSelection = batch.RemoveAt(0);

        Assert.Equal(-1, newSelection);
        Assert.True(batch.IsEmpty);
    }

    [Fact]
    public void Removal_DoesNotTouchSourceFile()
    {
        CreateReportFiles("SPIN 1.docx", "SPIN 2.docx", "SPIN 3.docx");
        var batch = new StagedBatch();
        batch.Replace(new BatchDiscoveryService().Discover(_tempRoot));
        var removed = batch.Entries[1].FileName;
        var remainingCount = batch.Count - 1;

        batch.RemoveAt(1);

        Assert.Equal(remainingCount, batch.Count);
        Assert.True(File.Exists(Path.Combine(_tempRoot, removed)));
        Assert.Equal(3, Directory.GetFiles(_tempRoot).Length);
    }

    [Fact]
    public void RemovedEntries_NoLongerLeaveSequenceGaps()
    {
        var batch = BatchOf("A.docx", "B.docx", "C.docx", "D.docx");

        batch.RemoveAt(1);
        batch.RemoveAt(2);

        Assert.Equal(["A.docx", "C.docx"], Names(batch));
        Assert.Equal(2, batch.Count);
        for (var index = 0; index < batch.Count; index++)
            Assert.Equal(batch[index].FileName, batch.Entries[index].FileName);
    }

    [Fact]
    public void UpdateEntry_PreservesStagedPosition()
    {
        var batch = BatchOf("A.docx", "B.docx", "C.docx");
        var original = batch[1];
        var renamed = new BatchEntry("C:\\reports\\B RENAMED.docx", "B RENAMED.docx", original.OriginalIndex);

        batch.UpdateEntry(1, renamed);

        Assert.Same(renamed, batch[1]);
        Assert.Equal(["A.docx", "B RENAMED.docx", "C.docx"], Names(batch));
    }

    [Fact]
    public void Replace_RestoresNaturalDiscoveryOrder_AfterManualMoves()
    {
        CreateReportFiles("SPIN 10.docx", "SPIN 2.docx", "SPIN 1.docx");
        var discovery = new BatchDiscoveryService();

        var batch = new StagedBatch();
        batch.Replace(discovery.Discover(_tempRoot));
        batch.MoveUp(2);

        batch.Replace(discovery.Discover(_tempRoot));

        Assert.Equal(["SPIN 1.docx", "SPIN 2.docx", "SPIN 10.docx"], Names(batch));
    }

    [Fact]
    public void Reload_ReturnsPreviouslyRemovedFile()
    {
        CreateReportFiles("SPIN 1.docx", "SPIN 2.docx", "SPIN 3.docx");
        var discovery = new BatchDiscoveryService();

        var batch = new StagedBatch();
        batch.Replace(discovery.Discover(_tempRoot));
        batch.RemoveAt(1);

        batch.Replace(discovery.Discover(_tempRoot));

        Assert.Equal(["SPIN 1.docx", "SPIN 2.docx", "SPIN 3.docx"], Names(batch));
    }

    [Fact]
    public void Reload_ReflectsSuccessfulSourceRename()
    {
        CreateReportFiles("SPIN 10.docx", "SPIN 2.docx");
        var discovery = new BatchDiscoveryService();
        var files = new BatchFileService();

        var batch = new StagedBatch();
        batch.Replace(discovery.Discover(_tempRoot));
        var entry = batch[0];
        var renameResult = files.RenameFile(entry.FullPath, "SPIN 5");

        Assert.True(renameResult.IsSuccess);
        Assert.Equal("SPIN 2.docx", entry.FileName);

        batch.Replace(discovery.Discover(_tempRoot));

        Assert.Equal(["SPIN 5.docx", "SPIN 10.docx"], Names(batch));
    }
}

file static class StagedBatchExtensions
{
    public static StagedBatch With(this StagedBatch batch, List<string> names)
    {
        var entries = names
            .Select((name, index) => new BatchEntry(Path.Combine("C:\\reports", name), name, index))
            .ToList();
        batch.Replace(entries);
        return batch;
    }
}