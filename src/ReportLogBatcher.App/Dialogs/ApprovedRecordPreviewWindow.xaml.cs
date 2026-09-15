using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.App.Dialogs;

/// <summary>
/// Read-only presentation of a <see cref="ReportResolutionResult.ValidatedRecord"/>
/// the user just approved. Header and the five body sections follow the report-log
/// contract headings, and the resolution metadata (manual changes, N/A fallbacks,
/// parser diagnostics) is shown for an audit-ready review. No Word output occurs.
/// </summary>
public partial class ApprovedRecordPreviewWindow : Window
{
    public ApprovedRecordPreviewWindow(ReportResolutionResult resolution)
    {
        InitializeComponent();

        var record = resolution.ValidatedRecord
            ?? throw new ArgumentException(
                "Approved Record Preview requires an approved resolution result.", nameof(resolution));

        HeaderText.Text =
            $"Report #{record.ReportNumber} – {record.InspectionDate:MM/dd/yy} – {record.InspectorFirstName}";

        foreach (var section in ReportTemplateContract.BodySections)
        {
            var value = ValueOf(record, section.Field);
            var headingBox = HeadingBoxFor(section.Field);
            var valueBox = ValueBoxFor(section.Field);

            headingBox.Text = section.Heading;
            valueBox.Text = value;
        }

        ManualChangesText.Text = resolution.ManuallyEditedFields.Count == 0
            ? "Manual changes: None"
            : "Manual changes:" + Environment.NewLine +
              string.Join(Environment.NewLine, resolution.ManuallyEditedFields.Select(field => "  • " + field));

        NaFallbackText.Text = resolution.NaFallbackFields.Count == 0
            ? "N/A fallback: None"
            : "N/A fallback:" + Environment.NewLine +
              string.Join(Environment.NewLine, resolution.NaFallbackFields.Select(field => "  • " + field));

        ParserIssuesText.Text = resolution.ParserIssues.Count == 0
            ? "Parser diagnostics: none."
            : "Parser diagnostics: " + string.Join("; ", resolution.ParserIssues.Select(issue =>
                $"[{issue.Kind}] {(issue.Field?.ToString() ?? "Document")}: {issue.Message}"));
    }

    private static string ValueOf(ValidatedReportRecord record, ReportField field) => field switch
    {
        ReportField.DescriptionOfWork => record.DescriptionOfWork,
        ReportField.DrawingReferences => record.DrawingReferences,
        ReportField.GeneralObservations => record.GeneralObservations,
        ReportField.Discrepancies => record.Discrepancies,
        ReportField.PreviousDiscrepancyCorrections => record.PreviousDiscrepancyCorrections,
        _ => string.Empty,
    };

    private TextBlock HeadingBoxFor(ReportField field) => field switch
    {
        ReportField.DescriptionOfWork => DescriptionOfWorkHeading,
        ReportField.DrawingReferences => DrawingReferencesHeading,
        ReportField.GeneralObservations => GeneralObservationsHeading,
        ReportField.Discrepancies => DiscrepanciesHeading,
        ReportField.PreviousDiscrepancyCorrections => PreviousDiscrepancyCorrectionsHeading,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unexpected body field."),
    };

    private TextBlock ValueBoxFor(ReportField field) => field switch
    {
        ReportField.DescriptionOfWork => DescriptionOfWorkValue,
        ReportField.DrawingReferences => DrawingReferencesValue,
        ReportField.GeneralObservations => GeneralObservationsValue,
        ReportField.Discrepancies => DiscrepanciesValue,
        ReportField.PreviousDiscrepancyCorrections => PreviousDiscrepancyCorrectionsValue,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unexpected body field."),
    };

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}