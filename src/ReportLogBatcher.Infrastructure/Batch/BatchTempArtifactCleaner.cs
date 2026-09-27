using System.IO;

namespace ReportLogBatcher.Infrastructure.Batch;

/// <summary>
/// Best-effort recovery cleanup for abandoned batch render directories. The
/// batch processor always deletes its own <c>RLB-Batch-{guid}</c> directory in a
/// finally block; if the application is killed mid-batch, that cleanup never
/// runs. This service removes such directories on the next startup, but only
/// ones old enough that no in-flight batch could still be using them.
/// </summary>
public sealed class BatchTempArtifactCleaner
{
    private readonly string _baseDirectory;

    public BatchTempArtifactCleaner(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? Path.GetTempPath();
    }

    /// <summary>
    /// Deletes <c>RLB-Batch-*</c> directories whose last write time is older than
    /// <paramref name="olderThan"/>. Directories that yield or are too recent are
    /// left untouched. Returns how many directories were removed. Failures are
    /// swallowed so startup never fails because of a cleanup problem.
    /// </summary>
    public int CleanupAbandonedBatchDirectories(TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var removed = 0;

        foreach (var directory in Directory.GetDirectories(_baseDirectory, "RLB-Batch-*"))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                {
                    Directory.Delete(directory, recursive: true);
                    removed++;
                }
            }
            catch (IOException)
            {
                // Best-effort: an open file should not block startup.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }
}