using ReportLogBatcher.Infrastructure.Batch;

namespace ReportLogBatcher.Infrastructure.Tests;

public sealed class BatchTempArtifactCleanerTests : IDisposable
{
    private readonly string _tempRoot;

    public BatchTempArtifactCleanerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RLB-CleanerTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void AbandonedOldBatchDirectory_IsRemoved()
    {
        var stale = CreateBatchDirectory("RLB-Batch-stale123", content: true);
        SetLastWriteUtc(stale, DateTime.UtcNow.AddDays(-3));

        var removed = new BatchTempArtifactCleaner(_tempRoot)
            .CleanupAbandonedBatchDirectories(TimeSpan.FromHours(24));

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(stale));
    }

    [Fact]
    public void RecentBatchDirectory_IsKept()
    {
        var recent = CreateBatchDirectory("RLB-Batch-recent456");

        var removed = new BatchTempArtifactCleaner(_tempRoot)
            .CleanupAbandonedBatchDirectories(TimeSpan.FromHours(24));

        Assert.Equal(0, removed);
        Assert.True(Directory.Exists(recent));
    }

    [Fact]
    public void OnlyMatchesRlbBatchDirectories()
    {
        var unrelated = Path.Combine(_tempRoot, "something-else");
        Directory.CreateDirectory(unrelated);
        SetLastWriteUtc(unrelated, DateTime.UtcNow.AddDays(-30));

        var removed = new BatchTempArtifactCleaner(_tempRoot)
            .CleanupAbandonedBatchDirectories(TimeSpan.FromHours(24));

        Assert.Equal(0, removed);
        Assert.True(Directory.Exists(unrelated));
    }

    [Fact]
    public void MixedAges_OnlyOldAreRemoved()
    {
        var stale1 = CreateBatchDirectory("RLB-Batch-stale1");
        var stale2 = CreateBatchDirectory("RLB-Batch-stale2");
        var recent = CreateBatchDirectory("RLB-Batch-recent1");
        SetLastWriteUtc(stale1, DateTime.UtcNow.AddDays(-2));
        SetLastWriteUtc(stale2, DateTime.UtcNow.AddDays(-5));

        var removed = new BatchTempArtifactCleaner(_tempRoot)
            .CleanupAbandonedBatchDirectories(TimeSpan.FromHours(24));

        Assert.Equal(2, removed);
        Assert.False(Directory.Exists(stale1));
        Assert.False(Directory.Exists(stale2));
        Assert.True(Directory.Exists(recent));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort test cleanup.
        }
    }

    private string CreateBatchDirectory(string name, bool content = false)
    {
        var path = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(path);
        if (content)
            File.WriteAllText(Path.Combine(path, "entry-001.docx"), "rendered");
        return path;
    }

    private static void SetLastWriteUtc(string path, DateTime utc)
    {
        Directory.SetLastWriteTimeUtc(path, utc);
        if (File.Exists(Path.Combine(path, "entry-001.docx")))
            File.SetLastWriteTimeUtc(Path.Combine(path, "entry-001.docx"), utc);
    }
}