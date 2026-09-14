namespace ReportLogBatcher.Core.Models;

public sealed class BatchEntry
{
    public string FullPath { get; }

    public string FileName { get; }

    public int OriginalIndex { get; }

    public BatchEntry(string fullPath, string fileName, int originalIndex)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(fileName);

        FullPath = fullPath;
        FileName = fileName;
        OriginalIndex = originalIndex;
    }
}