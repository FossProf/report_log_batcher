using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Infrastructure.Tests;

/// <summary>
/// Builds approved <see cref="ValidatedReportRecord"/> fixtures through the real
/// resolver/validator (the only supported construction path).
/// </summary>
public static class ReportRecordFixture
{
    public const string EnDash = "\u2013";

    public static ValidatedReportRecord Create(
        string reportNumber = "319",
        DateOnly? inspectionDate = null,
        string inspectorFirstName = "Anthony",
        string descriptionOfWork = "CMF Structural Repairs inspection",
        string drawingReferences = "Sheet S6",
        string generalObservations = "General note: the site was safe and accessible.",
        string discrepancies = "N/A",
        string previousDiscrepancyCorrections = "No previous discrepancies.")
    {
        var date = inspectionDate ?? new DateOnly(2026, 9, 11);

        var record = new ReportRecord
        {
            ReportNumber = reportNumber,
            InspectionDate = date,
            InspectorFirstName = inspectorFirstName,
            DescriptionOfWork = descriptionOfWork,
            DrawingReferences = drawingReferences,
            GeneralObservations = generalObservations,
            Discrepancies = discrepancies,
            PreviousDiscrepancyCorrections = previousDiscrepancyCorrections,
        };

        var edited = new Dictionary<ReportField, string?>
        {
            [ReportField.ReportNumber] = record.ReportNumber,
            [ReportField.InspectionDate] = date.ToString("MM/dd/yyyy"),
            [ReportField.InspectorFirstName] = record.InspectorFirstName,
            [ReportField.DescriptionOfWork] = record.DescriptionOfWork,
            [ReportField.DrawingReferences] = record.DrawingReferences,
            [ReportField.GeneralObservations] = record.GeneralObservations,
            [ReportField.Discrepancies] = record.Discrepancies,
            [ReportField.PreviousDiscrepancyCorrections] = record.PreviousDiscrepancyCorrections,
        };

        var resolver = new ReportRecordResolver(new ReportRecordValidator());
        var resolved = resolver.Resolve(record, edited, Array.Empty<ParseIssue>());
        if (resolved.ValidatedRecord is null)
            throw new InvalidOperationException("Test fixture failed validation unexpectedly.");

        return resolved.ValidatedRecord;
    }

    public static string Normalize(string value) =>
        value.Replace("\r\n", "\n").Replace("\r", "\n");
}