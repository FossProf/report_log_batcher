using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

/// <summary>
/// A body section of the report-log template: its canonical <see cref="Models.ReportField"/>,
/// its expected heading text, and its placeholder text.
/// </summary>
public sealed record ReportTemplateSection(ReportField Field, string Heading, string Placeholder)
{
    /// <summary>Expected relative position (1-based) of this section in the template.</summary>
    public int Position { get; init; }
}

/// <summary>
/// The authoritative report-log template contract.
///
/// This is the SINGLE source of truth for the eight required fields, their
/// placeholders, and the section context that disambiguates the five identical
/// body placeholders. Other components (parser, validator, renderer, writer,
/// inspection) MUST reference this contract rather than duplicating strings.
///
/// The template uses human-readable placeholders:
///
///   Report #{report.number} – {report.date mm/dd/yy}– {report.inspector.first_name}
///
///   Description and location(s) of work inspected:
///   {body of text under the same header}
///   ...
///
/// Word may split visually continuous placeholder text across multiple Open XML
/// runs. Inspection historically reconstructs paragraph text before matching.
/// </summary>
public static class ReportTemplateContract
{
    public const string ReportNumberPlaceholder = "{report.number}";

    public const string InspectionDatePlaceholder = "{report.date mm/dd/yy}";

    public const string InspectorFirstNamePlaceholder = "{report.inspector.first_name}";

    public const string BodyPlaceholder = "{body of text under the same header}";

    public const string DescriptionOfWorkHeading = "Description and location(s) of work inspected:";

    public const string DrawingReferencesHeading = "Drawing sheets and sections related to this work:";

    public const string GeneralObservationsHeading = "General observations/remarks:";

    public const string DiscrepanciesHeading = "Discrepancies and direction given:";

    public const string PreviousDiscrepancyCorrectionsHeading =
        "Observations/Remarks on correction of discrepancies noted in previous inspections:";

    /// <summary>The three header fields, each with its own unique placeholder.</summary>
    public static IReadOnlyDictionary<ReportField, string> HeaderPlaceholders { get; } =
        new Dictionary<ReportField, string>
        {
            [ReportField.ReportNumber] = ReportNumberPlaceholder,
            [ReportField.InspectionDate] = InspectionDatePlaceholder,
            [ReportField.InspectorFirstName] = InspectorFirstNamePlaceholder,
        };

    /// <summary>
    /// The five body sections in their required document order. The five body
    /// placeholders are textually identical, so their meaning is established by
    /// association with the immediately following/expected heading context.
    /// </summary>
    public static IReadOnlyList<ReportTemplateSection> BodySections { get; } = new[]
    {
        new ReportTemplateSection(ReportField.DescriptionOfWork, DescriptionOfWorkHeading, BodyPlaceholder) { Position = 1 },
        new ReportTemplateSection(ReportField.DrawingReferences, DrawingReferencesHeading, BodyPlaceholder) { Position = 2 },
        new ReportTemplateSection(ReportField.GeneralObservations, GeneralObservationsHeading, BodyPlaceholder) { Position = 3 },
        new ReportTemplateSection(ReportField.Discrepancies, DiscrepanciesHeading, BodyPlaceholder) { Position = 4 },
        new ReportTemplateSection(ReportField.PreviousDiscrepancyCorrections, PreviousDiscrepancyCorrectionsHeading, BodyPlaceholder) { Position = 5 },
    };

    /// <summary>All eight required fields in canonical order.</summary>
    public static IReadOnlyList<ReportField> AllFields { get; } = new[]
    {
        ReportField.ReportNumber,
        ReportField.InspectionDate,
        ReportField.InspectorFirstName,
        ReportField.DescriptionOfWork,
        ReportField.DrawingReferences,
        ReportField.GeneralObservations,
        ReportField.Discrepancies,
        ReportField.PreviousDiscrepancyCorrections,
    };
}

/// <summary>
/// Extraction policy for later SPIN-parsing slices:
///
///   "Source report text is records data and must be preserved verbatim during
///   deterministic extraction. Parsing must not silently correct spelling,
///   grammar, punctuation, capitalization, engineering terminology, or wording."
///
/// This policy is based on the reviewed production SPIN report. It is recorded
/// here so Slices 5+ preserve source text verbatim.
/// </summary>
public static class SourceTextPolicy
{
    public const string Statement =
        "Source report text is records data and must be preserved verbatim during " +
        "deterministic extraction. Parsing must not silently correct spelling, grammar, " +
        "punctuation, capitalization, engineering terminology, or wording.";
}

/// <summary>
/// Known parser boundary for Slice 5 (SPIN parsing). Documented but NOT implemented:
///
/// Slice 5 will extract all eight <see cref="Models.ReportField"/> values from finalized
/// SPIN reports. The reviewed production SPIN structure has explicit headings matching the
/// five body fields of <see cref="ReportTemplateContract.BodySections"/>. Body sections may
/// span Word page boundaries; page boundaries MUST NOT define parser boundaries. The future
/// parser must stop before unrelated certification/signature/photo-documentation content.
/// </summary>
public static class SliceFiveParserBoundary
{
}