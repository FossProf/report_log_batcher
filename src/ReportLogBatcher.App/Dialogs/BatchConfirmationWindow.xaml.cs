using System.Windows;
using System.Windows.Input;

namespace ReportLogBatcher.App.Dialogs;

/// <summary>
/// The single batch-level confirmation shown once before the first write:
/// destination, template, included count, processing order, and backup notice.
/// </summary>
public partial class BatchConfirmationWindow : Window
{
    public BatchConfirmationWindow(
        string reportLogPath,
        string templatePath,
        int includedCount,
        IReadOnlyList<string> fileOrder)
    {
        InitializeComponent();

        var orderLines = string.Join(Environment.NewLine, fileOrder);
        const int maxLines = 8;
        OrderSummary = orderLines.Length == 0
            ? "(no reports)"
            : $"{string.Join(Environment.NewLine, fileOrder.Take(maxLines))}"
              + (fileOrder.Count > maxLines ? $"{Environment.NewLine}... and {fileOrder.Count - maxLines} more" : string.Empty);

        ReportLogPath = reportLogPath;
        TemplatePath = templatePath;
        IncludedCount = includedCount;

        DataContext = this;
    }

    public string ReportLogPath { get; }

    public string TemplatePath { get; }

    public int IncludedCount { get; }

    public string OrderSummary { get; }

    private void OnProcessBatchClicked(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }
}