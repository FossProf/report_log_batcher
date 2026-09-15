using System.Globalization;
using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

/// <summary>
/// Turns a parsed report plus the user's final edited field values into an
/// approved record, applying the application's resolution rules:
///
///  - STRING fields: a blank/null/whitespace value becomes the literal "N/A"
///    fallback, but only at approval time and only after the user has seen the
///    resolution dialog. A non-blank value is used verbatim. Whether the field
///    become the fallback or the user typed/supplied a value is tracked.
///  - Inspection Date: a real date is required. A blank or unparsable date
///    blocks approval (no invented "today", no sentinel, no filename guess).
///  - Metadata: manual edits and N/A fallbacks are tracked separately; a source
///    "N/A" is never classified as a fallback. The original parsed record is
///    never modified.
///
/// The approved record is a <see cref="ValidatedReportRecord"/>, produced only
/// after the final values pass <see cref="ReportRecordValidator"/>.
/// </summary>
public sealed class ReportRecordResolver : IReportRecordResolver
{
    private const string NaFallback = "N/A";

    /// <summary>Manual-entry date formats accepted in the resolution dialog.</summary>
    private static readonly string[] DateFormats =
    {
        "MM/dd/yyyy",
        "M/d/yyyy",
        "yyyy-MM-dd",
    };

    private readonly IReportRecordValidator _validator;

    public ReportRecordResolver(IReportRecordValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public ReportResolutionResult Resolve(
        ReportRecord original,
        IReadOnlyDictionary<ReportField, string?> editedValues,
        IReadOnlyList<ParseIssue> parserIssues)
    {
        if (original is null) throw new ArgumentNullException(nameof(original));
        if (editedValues is null) throw new ArgumentNullException(nameof(editedValues));
        if (parserIssues is null) throw new ArgumentNullException(nameof(parserIssues));

        var manual = new List<ReportField>();
        var naFallback = new List<ReportField>();
        var invalidFields = new List<ReportField>();

        string? EditedOf(ReportField field)
            => editedValues.TryGetValue(field, out var value) ? value : null;

        string ResolveString(ReportField field, string? originalValue)
        {
            var edited = EditedOf(field);
            if (string.IsNullOrWhiteSpace(edited))
            {
                if (originalValue is null)
                    naFallback.Add(field);
                else
                    manual.Add(field);
                return NaFallback;
            }

            if (!string.Equals(edited, originalValue, StringComparison.Ordinal))
                manual.Add(field);
            return edited;
        }

        var inspectionDate = ResolveDate(EditedOf(ReportField.InspectionDate), original.InspectionDate, manual, invalidFields);

        var resolved = new ReportRecord
        {
            ReportNumber = ResolveString(ReportField.ReportNumber, original.ReportNumber),
            InspectionDate = inspectionDate,
            InspectorFirstName = ResolveString(ReportField.InspectorFirstName, original.InspectorFirstName),
            DescriptionOfWork = ResolveString(ReportField.DescriptionOfWork, original.DescriptionOfWork),
            DrawingReferences = ResolveString(ReportField.DrawingReferences, original.DrawingReferences),
            GeneralObservations = ResolveString(ReportField.GeneralObservations, original.GeneralObservations),
            Discrepancies = ResolveString(ReportField.Discrepancies, original.Discrepancies),
            PreviousDiscrepancyCorrections = ResolveString(ReportField.PreviousDiscrepancyCorrections, original.PreviousDiscrepancyCorrections),
        };

        var validation = _validator.Validate(resolved);
        foreach (var missing in validation.MissingFields)
        {
            if (!invalidFields.Contains(missing))
                invalidFields.Add(missing);
        }

        if (invalidFields.Count == 0
            && ValidatedReportRecord.TryCreate(resolved, _validator, out var validated)
            && validated is not null)
        {
            return new ReportResolutionResult(validated, manual, naFallback, Array.Empty<ReportField>(), parserIssues);
        }

        return new ReportResolutionResult(null, manual, naFallback, invalidFields, parserIssues);
    }

    private static DateOnly? ResolveDate(
        string? edited,
        DateOnly? original,
        ICollection<ReportField> manual,
        ICollection<ReportField> invalid)
    {
        if (DateOnly.TryParseExact(edited, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            if (parsed != original)
                manual.Add(ReportField.InspectionDate);
            return parsed;
        }

        invalid.Add(ReportField.InspectionDate);
        return null;
    }
}