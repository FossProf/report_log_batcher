using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.App.ViewModels;

public sealed class BatchEntryRow : ObservableObject
{
    public BatchEntryRow(BatchEntry entry, int sequence)
    {
        FullPath = entry.FullPath;
        FileName = entry.FileName;
        Sequence = sequence;
    }

    public string FullPath { get; }

    public string FileName { get; }

    public int Sequence { get; }

    public string Status => "Pending";
}