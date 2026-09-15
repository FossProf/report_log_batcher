using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Core.Tests;

public sealed class ReportRecordResolverTests
{
    private readonly IReportRecordResolver _resolver = new ReportRecordResolver(new ReportRecordValidator());

    private static ReportRecord FullRecord() => new()
    {
        ReportNumber = "319",
        InspectionDate = new DateOnly(2026, 9, 11),
        InspectorFirstName = "Anthony",
        DescriptionOfWork = "Description body text.",
        DrawingReferences = "S6",
        GeneralObservations = "General observations body text.",
        Discrepancies = "N/A",
        PreviousDiscrepancyCorrections = "Corrections body text.",
    };

    private static IReadOnlyDictionary<ReportField, string?> Edited(
        ReportRecord record,
        params (ReportField Field, string? Value)[] overrides)
    {
        var values = new Dictionary<ReportField, string?>
        {
            [ReportField.ReportNumber] = record.ReportNumber,
            [ReportField.InspectionDate] = record.InspectionDate?.ToString("MM/dd/yyyy"),
            [ReportField.InspectorFirstName] = record.InspectorFirstName,
            [ReportField.DescriptionOfWork] = record.DescriptionOfWork,
            [ReportField.DrawingReferences] = record.DrawingReferences,
            [ReportField.GeneralObservations] = record.GeneralObservations,
            [ReportField.Discrepancies] = record.Discrepancies,
            [ReportField.PreviousDiscrepancyCorrections] = record.PreviousDiscrepancyCorrections,
        };

        foreach (var (field, value) in overrides)
            values[field] = value;

        return values;
    }

    [Fact]
    public void CompleteUnchangedRecord_ApprovesWithNoManualOrFallbackMetadata()
    {
        var original = FullRecord();
        var result = _resolver.Resolve(original, Edited(original), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.NotNull(result.ValidatedRecord);
        Assert.Equal("319", result.ValidatedRecord!.ReportNumber);
        Assert.Equal(new DateOnly(2026, 9, 11), result.ValidatedRecord.InspectionDate);
        Assert.Empty(result.ManuallyEditedFields);
        Assert.Empty(result.NaFallbackFields);
        Assert.Empty(result.InvalidFields);
    }

    [Fact]
    public void ManualReplacement_OfUnresolvedStringField_IsApprovedAndTracked()
    {
        var original = FullRecord() with { DrawingReferences = null };
        var result = _resolver.Resolve(original, Edited(original, (ReportField.DrawingReferences, "S-03-0802")), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Equal("S-03-0802", result.ValidatedRecord!.DrawingReferences);
        Assert.Equal(new[] { ReportField.DrawingReferences }, result.ManuallyEditedFields);
        Assert.Empty(result.NaFallbackFields);
    }

    [Fact]
    public void ExplicitNa_ForUnresolvedStringField_IsManualNotFallback()
    {
        var original = FullRecord() with { Discrepancies = null };
        var result = _resolver.Resolve(original, Edited(original, (ReportField.Discrepancies, "N/A")), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Equal("N/A", result.ValidatedRecord!.Discrepancies);
        Assert.Equal(new[] { ReportField.Discrepancies }, result.ManuallyEditedFields);
        Assert.Empty(result.NaFallbackFields);
    }

    [Fact]
    public void BlankUnresolvedStringField_BecomesNaFallback()
    {
        var original = FullRecord() with { Discrepancies = null };
        var result = _resolver.Resolve(original, Edited(original, (ReportField.Discrepancies, "")), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Equal("N/A", result.ValidatedRecord!.Discrepancies);
        Assert.Equal(new[] { ReportField.Discrepancies }, result.NaFallbackFields);
        Assert.Empty(result.ManuallyEditedFields);
    }

    [Fact]
    public void WhitespaceUnresolvedStringField_BecomesNaFallback()
    {
        var original = FullRecord() with { GeneralObservations = null };
        var result = _resolver.Resolve(original, Edited(original, (ReportField.GeneralObservations, "   \t ")), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Equal("N/A", result.ValidatedRecord!.GeneralObservations);
        Assert.Equal(new[] { ReportField.GeneralObservations }, result.NaFallbackFields);
        Assert.Empty(result.ManuallyEditedFields);
    }

    [Fact]
    public void ExistingSourceNa_IsNotClassifiedAsFallbackOrManual()
    {
        var original = FullRecord();
        var result = _resolver.Resolve(original, Edited(original), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Equal("N/A", result.ValidatedRecord!.Discrepancies);
        Assert.Empty(result.ManuallyEditedFields);
        Assert.Empty(result.NaFallbackFields);
    }

    [Fact]
    public void EditedExistingValue_IsClassifiedAsManual()
    {
        var original = FullRecord() with { DrawingReferences = "S6" };
        var result = _resolver.Resolve(original, Edited(original, (ReportField.DrawingReferences, "S6, S7")), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Equal("S6, S7", result.ValidatedRecord!.DrawingReferences);
        Assert.Equal(new[] { ReportField.DrawingReferences }, result.ManuallyEditedFields);
        Assert.Empty(result.NaFallbackFields);
    }

    [Fact]
    public void ErasedExistingStringValue_IsClassifiedAsManualNotFallback()
    {
        var original = FullRecord();
        var result = _resolver.Resolve(original, Edited(original, (ReportField.DescriptionOfWork, "   ")), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Equal("N/A", result.ValidatedRecord!.DescriptionOfWork);
        Assert.Equal(new[] { ReportField.DescriptionOfWork }, result.ManuallyEditedFields);
        Assert.Empty(result.NaFallbackFields);
    }

    [Fact]
    public void BlankOrInvalidDate_BlocksApproval()
    {
        var original = FullRecord() with { InspectionDate = null };

        var blank = _resolver.Resolve(original, Edited(original, (ReportField.InspectionDate, "")), Array.Empty<ParseIssue>());
        Assert.False(blank.IsApproved);
        Assert.Null(blank.ValidatedRecord);
        Assert.Contains(ReportField.InspectionDate, blank.InvalidFields);

        var invalid = _resolver.Resolve(original, Edited(original, (ReportField.InspectionDate, "not-a-date")), Array.Empty<ParseIssue>());
        Assert.False(invalid.IsApproved);
        Assert.Null(invalid.ValidatedRecord);
        Assert.Contains(ReportField.InspectionDate, invalid.InvalidFields);
    }

    [Theory]
    [InlineData("10/05/2026")]
    [InlineData("10/5/2026")]
    [InlineData("2026-10-05")]
    public void ValidManuallySuppliedDate_IsApprovedAndTracked(string dateText)
    {
        var original = FullRecord() with { InspectionDate = null };
        var result = _resolver.Resolve(original, Edited(original, (ReportField.InspectionDate, dateText)), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Equal(new DateOnly(2026, 10, 5), result.ValidatedRecord!.InspectionDate);
        Assert.Equal(new[] { ReportField.InspectionDate }, result.ManuallyEditedFields);
        Assert.Empty(result.InvalidFields);
    }

    [Fact]
    public void UnchangedDate_IsNotMarkedManual()
    {
        var original = FullRecord();
        var result = _resolver.Resolve(original, Edited(original), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.DoesNotContain(ReportField.InspectionDate, result.ManuallyEditedFields);
    }

    [Fact]
    public void OriginalParsedRecord_RemainsUnchanged()
    {
        var original = FullRecord();
        var before = original with { };

        var result = _resolver.Resolve(original, Edited(original, (ReportField.DrawingReferences, "S6, S7")), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Equal(before, original);
        Assert.Equal("S6", original.DrawingReferences);
    }

    [Fact]
    public void AllEightFinalFields_SatisfyValidator()
    {
        var validator = new ReportRecordValidator();
        var original = FullRecord() with { Discrepancies = null, DrawingReferences = null };
        var result = _resolver.Resolve(original, Edited(original), Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        var record = result.ValidatedRecord!;

        Assert.False(string.IsNullOrWhiteSpace(record.ReportNumber));
        Assert.False(string.IsNullOrWhiteSpace(record.InspectorFirstName));
        Assert.False(string.IsNullOrWhiteSpace(record.DescriptionOfWork));
        Assert.False(string.IsNullOrWhiteSpace(record.DrawingReferences));
        Assert.False(string.IsNullOrWhiteSpace(record.GeneralObservations));
        Assert.False(string.IsNullOrWhiteSpace(record.Discrepancies));
        Assert.False(string.IsNullOrWhiteSpace(record.PreviousDiscrepancyCorrections));
        Assert.NotEqual(default, record.InspectionDate);

        var validation = validator.Validate(new ReportRecord
        {
            ReportNumber = record.ReportNumber,
            InspectionDate = record.InspectionDate,
            InspectorFirstName = record.InspectorFirstName,
            DescriptionOfWork = record.DescriptionOfWork,
            DrawingReferences = record.DrawingReferences,
            GeneralObservations = record.GeneralObservations,
            Discrepancies = record.Discrepancies,
            PreviousDiscrepancyCorrections = record.PreviousDiscrepancyCorrections,
        });
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void MultipleManualEdits_AreAllTracked()
    {
        var original = FullRecord() with { DrawingReferences = "S6" };
        var result = _resolver.Resolve(
            original,
            Edited(original,
                (ReportField.DrawingReferences, "S6, S7"),
                (ReportField.DescriptionOfWork, "Edited description.")),
            Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Contains(ReportField.DrawingReferences, result.ManuallyEditedFields);
        Assert.Contains(ReportField.DescriptionOfWork, result.ManuallyEditedFields);
        Assert.Equal(2, result.ManuallyEditedFields.Count);
    }

    [Fact]
    public void MultipleNaFallbacks_AreAllTracked()
    {
        var original = FullRecord() with { Discrepancies = null, GeneralObservations = null };
        var result = _resolver.Resolve(
            original,
            Edited(original,
                (ReportField.Discrepancies, ""),
                (ReportField.GeneralObservations, "  ")),
            Array.Empty<ParseIssue>());

        Assert.True(result.IsApproved);
        Assert.Contains(ReportField.Discrepancies, result.NaFallbackFields);
        Assert.Contains(ReportField.GeneralObservations, result.NaFallbackFields);
        Assert.Empty(result.ManuallyEditedFields);
    }

    [Fact]
    public void ParserDiagnostics_ArePreservedThroughApproval()
    {
        var issues = new List<ParseIssue>
        {
            new(ParseIssueKind.Missing, ReportField.DrawingReferences, "Drawing references section not found."),
        };
        var original = FullRecord() with { DrawingReferences = null };
        var result = _resolver.Resolve(original, Edited(original, (ReportField.DrawingReferences, "S-03-0802")), issues);

        Assert.True(result.IsApproved);
        Assert.Same(issues, result.ParserIssues);
        Assert.Single(result.ParserIssues);
    }
}