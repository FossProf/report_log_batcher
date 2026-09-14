using System.Globalization;

namespace ReportLogBatcher.Core.Models;

/// <summary>
/// The canonical result of deterministically parsing ONE finalized SPIN report.
///
/// The eight properties map 1:1 to the authoritative field schema in
/// <see cref="Services.ReportTemplateContract.AllFields"/>. Nothing here is
/// invented: an extracted value is stored verbatim; a value that could not be
/// resolved deterministically remains <c>null</c>. The parser NEVER substitutes
/// <c>N/A</c> — an explicit <c>N/A</c> read from the source is preserved as the
/// literal string <c>"N/A"</c>, and an unresolved value stays <c>null</c>.
/// </summary>
public sealed record ReportRecord
{
    /// <summary>e.g. "319" from "Special Inspection Report #319".</summary>
    public string? ReportNumber { get; init; }

    /// <summary>The inspection date as parsed from the "Inspection Date" field.</summary>
    public DateOnly? InspectionDate { get; init; }

    /// <summary>The first whitespace-delimited token of "Cornerstone Inspector(s)".</summary>
    public string? InspectorFirstName { get; init; }

    public string? DescriptionOfWork { get; init; }

    public string? DrawingReferences { get; init; }

    public string? GeneralObservations { get; init; }

    public string? Discrepancies { get; init; }

    public string? PreviousDiscrepancyCorrections { get; init; }

    /// <summary>
    /// True when at least one of the eight required fields could not be resolved.
    /// Used to decide <see cref="SpinParseResult.Status"/>.
    /// </summary>
    public bool HasAnyUnresolved =>
        ReportNumber is null
        || InspectionDate is null
        || InspectorFirstName is null
        || DescriptionOfWork is null
        || DrawingReferences is null
        || GeneralObservations is null
        || Discrepancies is null
        || PreviousDiscrepancyCorrections is null;
}