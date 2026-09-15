using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Core.Models;

/// <summary>
/// A record that has passed required-field validation: every one of the eight
/// required fields carries a resolved, non-blank value and
/// <see cref="InspectionDate"/> is present.
///
/// This is the ONLY shape the future template renderer / report-log writer may
/// accept. The constructor is private and instances are produced exclusively by
/// <see cref="TryCreate"/>, which re-runs the validator before construction, so
/// an incomplete <see cref="ReportRecord"/> can never be wrapped into this type.
/// </summary>
public sealed class ValidatedReportRecord
{
    public string ReportNumber { get; }

    public DateOnly InspectionDate { get; }

    public string InspectorFirstName { get; }

    public string DescriptionOfWork { get; }

    public string DrawingReferences { get; }

    public string GeneralObservations { get; }

    public string Discrepancies { get; }

    public string PreviousDiscrepancyCorrections { get; }

    private ValidatedReportRecord(
        string reportNumber,
        DateOnly inspectionDate,
        string inspectorFirstName,
        string descriptionOfWork,
        string drawingReferences,
        string generalObservations,
        string discrepancies,
        string previousDiscrepancyCorrections)
    {
        ReportNumber = reportNumber;
        InspectionDate = inspectionDate;
        InspectorFirstName = inspectorFirstName;
        DescriptionOfWork = descriptionOfWork;
        DrawingReferences = drawingReferences;
        GeneralObservations = generalObservations;
        Discrepancies = discrepancies;
        PreviousDiscrepancyCorrections = previousDiscrepancyCorrections;
    }

    /// <summary>
    /// Validates <paramref name="record"/> and, only when it passes, constructs a
    /// <see cref="ValidatedReportRecord"/>. Returns false otherwise. The validation
    /// run is the mandatory gate: no path can bypass it to construct this type.
    /// </summary>
    internal static bool TryCreate(
        ReportRecord record,
        IReportRecordValidator validator,
        out ValidatedReportRecord? result)
    {
        result = null;
        if (record is null || validator is null || !validator.Validate(record).IsValid)
            return false;

        result = new ValidatedReportRecord(
            record.ReportNumber!,
            record.InspectionDate!.Value,
            record.InspectorFirstName!,
            record.DescriptionOfWork!,
            record.DrawingReferences!,
            record.GeneralObservations!,
            record.Discrepancies!,
            record.PreviousDiscrepancyCorrections!);
        return true;
    }
}