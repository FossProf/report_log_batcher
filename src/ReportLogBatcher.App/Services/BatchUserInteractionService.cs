using System.Windows;
using ReportLogBatcher.App.Dialogs;
using ReportLogBatcher.Infrastructure.Batch;

namespace ReportLogBatcher.App.Services;

/// <summary>
/// The batch workflow's user interactions (confirmation, prompts, end-of-batch
/// summary). Abstracted so the ViewModel stays testable without WPF windows or
/// message boxes; the real implementation drives the WPF dialogs.
/// </summary>
public interface IBatchUserInteraction
{
    /// <summary>Prompts once at batch start when the report log is empty/invalid. True = initialize and continue.</summary>
    bool ConfirmInitializeReportLog(string reportLogPath);

    /// <summary>The single batch-level confirmation shown once before the first write.</summary>
    bool ConfirmBatchStart(
        string reportLogPath,
        string templatePath,
        int includedCount,
        IReadOnlyList<string> fileOrder);

    /// <summary>The single end-of-batch summary.</summary>
    void ShowBatchSummary(BatchRunSummary summary, string reportLogPath);

    void ShowError(string title, string message);
}

/// <summary>
/// Real WPF implementation of <see cref="IBatchUserInteraction"/>.
/// </summary>
public sealed class BatchUserInteractionService : IBatchUserInteraction
{
    public bool ConfirmInitializeReportLog(string reportLogPath)
    {
        var choice = MessageBox.Show(
            "The selected report log is empty or is not a valid Word document:\n\n" +
            reportLogPath +
            "\n\nInitialize a new report log from the current template and continue?",
            "Report Log Requires Initialization",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return choice == MessageBoxResult.Yes;
    }

    public bool ConfirmBatchStart(
        string reportLogPath,
        string templatePath,
        int includedCount,
        IReadOnlyList<string> fileOrder)
    {
        var window = new BatchConfirmationWindow(reportLogPath, templatePath, includedCount, fileOrder)
        {
            Owner = Application.Current?.MainWindow,
        };

        return window.ShowDialog() == true;
    }

    public void ShowBatchSummary(BatchRunSummary summary, string reportLogPath)
    {
        var (title, body) = summary.StopReason switch
        {
            BatchStopReason.Completed => (
                "Batch Complete",
                $"Batch complete.\n\n" +
                $"Included: {summary.IncludedCount}\n" +
                $"Completed: {summary.CompletedCount}\n" +
                $"Manual resolutions: {summary.ManualResolutionCount}\n" +
                $"Failed: {summary.FailedCount}\n\n" +
                $"Report log:\n{reportLogPath}"),
            BatchStopReason.StoppedByUser => (
                "Batch Stopped",
                $"Batch stopped by user.\n\n" +
                $"Included: {summary.IncludedCount}\n" +
                $"Completed: {summary.CompletedCount}\n" +
                $"Needs Input: {summary.NeedsInputCount}\n" +
                $"Failed: {summary.FailedCount}\n" +
                $"Remaining: {summary.RemainingCount}\n\n" +
                "No records were rolled back; completed entries remain appended."),
            _ => (
                "Batch Stopped Due to an Error",
                $"The batch stopped because a report could not be processed.\n\n" +
                $"Completed: {summary.CompletedCount}\n" +
                $"Failed: {summary.FailedCount}\n" +
                $"Remaining: {summary.RemainingCount}\n\n" +
                (summary.ErrorMessage is null ? string.Empty : $"Error: {summary.ErrorMessage}")),
        };

        MessageBox.Show(body, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}