namespace ReportLogBatcher.Core.Models;

/// <summary>
/// The eight canonical fields that every finalized SPIN report must contribute
/// to a report-log entry. All fields are REQUIRED; no entry may have a blank field.
/// </summary>
public enum ReportField
{
    /// <summary>The report number, e.g. #123.</summary>
    ReportNumber,

    /// <summary>The inspection date formatted as mm/dd/yy.</summary>
    InspectionDate,

    /// <summary>The inspector's first name.</summary>
    InspectorFirstName,

    /// <summary>Body text: description and location(s) of work inspected.</summary>
    DescriptionOfWork,

    /// <summary>Body text: drawing sheets and sections related to this work.</summary>
    DrawingReferences,

    /// <summary>Body text: general observations/remarks.</summary>
    GeneralObservations,

    /// <summary>Body text: discrepancies and direction given.</summary>
    Discrepancies,

    /// <summary>Body text: observations/remarks on correction of previous discrepancies.</summary>
    PreviousDiscrepancyCorrections,
}