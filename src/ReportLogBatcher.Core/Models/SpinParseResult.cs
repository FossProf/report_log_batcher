namespace ReportLogBatcher.Core.Models;

/// <summary>
/// Overall outcome of parsing one SPIN report, mirroring the three states the
/// preview UI must distinguish.
/// </summary>
public enum SpinParseStatus
{
    /// <summary>The document was readable and all eight required fields were extracted.</summary>
    Parsed,

    /// <summary>The document was readable but one or more fields could not be resolved.</summary>
    ParsedWithUnresolvedFields,

    /// <summary>The document could not be read at all (missing/corrupt/wrong extension).</summary>
    Failed,
}

/// <summary>
/// Structured parse outcome: the extracted <see cref="ReportRecord"/> plus
/// diagnostics and the source path. Never a bare boolean — callers can see
/// exactly which field failed, why, and whether the failure was document-level.
/// </summary>
public sealed class SpinParseResult
{
    public SpinParseResult(
        ReportRecord record,
        SpinParseStatus status,
        string sourcePath,
        IReadOnlyList<ParseIssue> issues)
    {
        Record = record;
        Status = status;
        SourcePath = sourcePath;
        Issues = issues ?? Array.Empty<ParseIssue>();
        UnresolvedFieldCount = CountUnresolved(record);
    }

    public ReportRecord Record { get; }

    public SpinParseStatus Status { get; }

    public string SourcePath { get; }

    public IReadOnlyList<ParseIssue> Issues { get; }

    /// <summary>Number of the eight required fields that remain unresolved (null).</summary>
    public int UnresolvedFieldCount { get; }

    public bool ParsedAllFields => UnresolvedFieldCount == 0;

    private static int CountUnresolved(ReportRecord record)
    {
        var count = 0;
        if (record.ReportNumber is null) count++;
        if (record.InspectionDate is null) count++;
        if (record.InspectorFirstName is null) count++;
        if (record.DescriptionOfWork is null) count++;
        if (record.DrawingReferences is null) count++;
        if (record.GeneralObservations is null) count++;
        if (record.Discrepancies is null) count++;
        if (record.PreviousDiscrepancyCorrections is null) count++;
        return count;
    }
}