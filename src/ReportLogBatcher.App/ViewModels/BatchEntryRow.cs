using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.App.ViewModels;

public sealed class BatchEntryRow : ObservableObject
{
    private string _status = "Pending";
    private bool _isProcessed;

    public BatchEntryRow(BatchEntry entry, int sequence)
    {
        FullPath = entry.FullPath;
        FileName = entry.FileName;
        Sequence = sequence;
    }

    public string FullPath { get; }

    public string FileName { get; }

    public int Sequence { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Whether this row was appended in the current session (double-append guard).</summary>
    public bool IsProcessed
    {
        get => _isProcessed;
        private set
        {
            if (SetProperty(ref _isProcessed, value))
                OnPropertyChanged(nameof(CanProcess));
        }
    }

    public bool CanProcess => !IsProcessed;

    public void MarkProcessed()
    {
        Status = "Complete";
        IsProcessed = true;
    }
}