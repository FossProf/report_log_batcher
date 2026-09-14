using System.Windows;
using System.Windows.Input;
using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.App.Dialogs;

/// <summary>
/// Read-only presentation of a completed <see cref="SpinParseResult"/>.
/// All text is set directly from the parse result; nothing here is editable and
/// nothing here can change the staged batch.
/// </summary>
public partial class ParsePreviewWindow : Window
{
    public ParsePreviewWindow(SpinParseResult result)
    {
        InitializeComponent();

        var record = result.Record;
        SourceFileText.Text = result.SourcePath;
        StatusText.Text = BuildStatusText(result.Status);

        ReportNumberText.Text = record.ReportNumber ?? "[UNRESOLVED]";
        InspectionDateText.Text = record.InspectionDate?.ToString("MM/dd/yy") ?? "[UNRESOLVED]";
        InspectorFirstNameText.Text = record.InspectorFirstName ?? "[UNRESOLVED]";
        DescriptionOfWorkText.Text = record.DescriptionOfWork ?? "[UNRESOLVED]";
        DrawingReferencesText.Text = record.DrawingReferences ?? "[UNRESOLVED]";
        GeneralObservationsText.Text = record.GeneralObservations ?? "[UNRESOLVED]";
        DiscrepanciesText.Text = record.Discrepancies ?? "[UNRESOLVED]";
        PreviousDiscrepancyCorrectionsText.Text = record.PreviousDiscrepancyCorrections ?? "[UNRESOLVED]";

        if (result.Issues.Count == 0)
        {
            DiagnosticsText.Text = "No diagnostics — every required field was extracted deterministically.";
        }
        else
        {
            DiagnosticsText.Text = string.Join(
                Environment.NewLine + Environment.NewLine,
                result.Issues.Select(issue =>
                    $"[{issue.Kind}] {(issue.Field?.ToString() ?? "Document")}: {issue.Message}"));
        }
    }

    private static string BuildStatusText(SpinParseStatus status) => status switch
    {
        SpinParseStatus.Parsed => "Parsed — all eight required fields extracted.",
        SpinParseStatus.ParsedWithUnresolvedFields => "Parsed with unresolved fields (see diagnostics).",
        SpinParseStatus.Failed => "Failed — the document could not be opened or read.",
        _ => status.ToString(),
    };

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}