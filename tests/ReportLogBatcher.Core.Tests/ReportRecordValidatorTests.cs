using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Core.Tests;

public sealed class ReportRecordValidatorTests
{
    private readonly IReportRecordValidator _validator = new ReportRecordValidator();

    private static ReportRecord CompleteRecord() => new()
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

    [Fact]
    public void FullyPopulatedRecord_IsValid()
    {
        var result = _validator.Validate(CompleteRecord());

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingFields);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void NullBlankOrWhitespaceReportNumber_IsMissing(string? value)
        => AssertMissingField(ReportField.ReportNumber, record => record with { ReportNumber = value });

    [Fact]
    public void MissingInspectionDate_IsMissing()
        => AssertMissingField(ReportField.InspectionDate, record => record with { InspectionDate = null });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullBlankOrWhitespaceInspectorFirstName_IsMissing(string? value)
        => AssertMissingField(ReportField.InspectorFirstName, record => record with { InspectorFirstName = value });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullBlankOrWhitespaceDescriptionOfWork_IsMissing(string? value)
        => AssertMissingField(ReportField.DescriptionOfWork, record => record with { DescriptionOfWork = value });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullBlankOrWhitespaceDrawingReferences_IsMissing(string? value)
        => AssertMissingField(ReportField.DrawingReferences, record => record with { DrawingReferences = value });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullBlankOrWhitespaceGeneralObservations_IsMissing(string? value)
        => AssertMissingField(ReportField.GeneralObservations, record => record with { GeneralObservations = value });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullBlankOrWhitespaceDiscrepancies_IsMissing(string? value)
        => AssertMissingField(ReportField.Discrepancies, record => record with { Discrepancies = value });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullBlankOrWhitespacePreviousDiscrepancyCorrections_IsMissing(string? value)
        => AssertMissingField(ReportField.PreviousDiscrepancyCorrections, record => record with { PreviousDiscrepancyCorrections = value });

    [Fact]
    public void LiteralNa_IsValid()
    {
        var result = _validator.Validate(CompleteRecord() with { Discrepancies = "N/A" });

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingFields);
    }

    [Fact]
    public void Validation_DoesNotAlterSourceValues()
    {
        var record = CompleteRecord() with { DrawingReferences = " S6 " };

        _validator.Validate(record);

        Assert.Equal(" S6 ", record.DrawingReferences);
    }

    [Fact]
    public void Validation_ReportsAllMissingFields_NotJustTheFirst()
    {
        var record = new ReportRecord { ReportNumber = "319" };

        var result = _validator.Validate(record);

        Assert.False(result.IsValid);
        Assert.Contains(ReportField.InspectionDate, result.MissingFields);
        Assert.Contains(ReportField.InspectorFirstName, result.MissingFields);
        Assert.Contains(ReportField.DescriptionOfWork, result.MissingFields);
        Assert.Contains(ReportField.DrawingReferences, result.MissingFields);
        Assert.Contains(ReportField.GeneralObservations, result.MissingFields);
        Assert.Contains(ReportField.Discrepancies, result.MissingFields);
        Assert.Contains(ReportField.PreviousDiscrepancyCorrections, result.MissingFields);
        Assert.Equal(7, result.MissingFields.Count);
        Assert.Equal(7, result.Issues.Count);
    }

    [Fact]
    public void Validation_MissingFieldsFollowCanonicalOrder()
    {
        var result = _validator.Validate(new ReportRecord { ReportNumber = "319" });

        Assert.Equal(ReportTemplateContract.AllFields.Where(f => f != ReportField.ReportNumber), result.MissingFields);
    }

    private void AssertMissingField(ReportField field, Func<ReportRecord, ReportRecord> mutate)
    {
        var result = _validator.Validate(mutate(CompleteRecord()));

        Assert.False(result.IsValid);
        Assert.Contains(field, result.MissingFields);
        Assert.Contains(result.Issues, issue => issue.Field == field);
    }
}