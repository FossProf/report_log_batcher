using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Infrastructure.Word;

/// <summary>
/// Inspects a report-log template .docx read-only and validates it against
/// <see cref="ReportTemplateContract"/>.
///
/// Word may split visually continuous text across multiple Open XML runs, so
/// each paragraph's text is reconstructed by concatenating its run text before
/// matching. The document is NEVER rewritten or normalized during inspection.
///
/// The five body placeholders are textually identical ({body of text under the
/// same header}); they are distinguished deterministically by the section
/// (expected heading) they follow.
/// </summary>
public sealed class TemplateInspectionService : ITemplateInspectionService
{
    public TemplateValidationResult Inspect(string templatePath)
    {
        var pathValidation = PathValidationService.ValidateReportLogTemplate(templatePath);
        if (!pathValidation.IsValid)
        {
            var failure = string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath)
                ? TemplateValidationFailure.FileNotFound
                : TemplateValidationFailure.InvalidExtension;

            return new TemplateValidationResult(false, failure, pathValidation.ErrorMessage);
        }

        IReadOnlyList<string> paragraphs;
        try
        {
            paragraphs = ReadParagraphTexts(templatePath);
        }
        catch (Exception)
        {
            return new TemplateValidationResult(
                false,
                TemplateValidationFailure.CannotOpen,
                "The template could not be opened as a valid Word document.");
        }

        return ValidateContract(paragraphs);
    }

    private static IReadOnlyList<string> ReadParagraphTexts(string templatePath)
    {
        using var stream = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var document = WordprocessingDocument.Open(stream, false);

        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
            return Array.Empty<string>();

        return body.Descendants<Paragraph>()
            .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text ?? string.Empty)))
            .ToList();
    }

    private static TemplateValidationResult ValidateContract(IReadOnlyList<string> paragraphs)
    {
        var issues = new List<TemplateIssue>();
        var trimmed = paragraphs.Select(p => p.Trim()).ToList();
        var fullText = string.Join('\n', paragraphs);

        foreach (var (field, placeholder) in ReportTemplateContract.HeaderPlaceholders)
        {
            var count = CountOccurrences(fullText, placeholder);
            if (count == 0)
            {
                issues.Add(new TemplateIssue(
                    field,
                    TemplateIssueKind.MissingPlaceholder,
                    $"Missing required placeholder {placeholder}."));
            }
            else if (count > 1)
            {
                issues.Add(new TemplateIssue(
                    field,
                    TemplateIssueKind.DuplicatePlaceholder,
                    $"Duplicate placeholder {placeholder} found {count} times; it must occur exactly once."));
            }
        }

        var bodyPlaceholderParagraphs = trimmed
            .Select((text, index) => (text, index))
            .Where(x => x.text == ReportTemplateContract.BodyPlaceholder)
            .Select(x => x.index)
            .ToList();

        var sections = ReportTemplateContract.BodySections;
        var headingPositions = new int[sections.Count];
        Array.Fill(headingPositions, -1);

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var heading = sections[sectionIndex].Heading;
            for (var i = 0; i < trimmed.Count; i++)
            {
                if (trimmed[i] == heading)
                {
                    headingPositions[sectionIndex] = i;
                    break;
                }
            }
        }

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            if (headingPositions[sectionIndex] < 0)
            {
                issues.Add(new TemplateIssue(
                    sections[sectionIndex].Field,
                    TemplateIssueKind.MissingSectionHeading,
                    $"Missing section heading \"{sections[sectionIndex].Heading}\""));
            }
        }

        for (var sectionIndex = 0; sectionIndex < sections.Count - 1; sectionIndex++)
        {
            var current = headingPositions[sectionIndex];
            var next = headingPositions[sectionIndex + 1];
            if (current >= 0 && next >= 0 && current >= next)
            {
                issues.Add(new TemplateIssue(
                    sections[sectionIndex].Field,
                    TemplateIssueKind.SectionOrderViolation,
                    $"Sections out of order: \"{sections[sectionIndex].Heading}\" must precede \"{sections[sectionIndex + 1].Heading}\"."));
            }
        }

        var coveredPlaceholders = new HashSet<int>();
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            var headingIndex = headingPositions[sectionIndex];
            if (headingIndex < 0)
            {
                if (sectionIndex == 0)
                    continue;
                continue;
            }

            var sectionStart = headingIndex;
            var sectionEnd = paragraphs.Count;
            if (sectionIndex < sections.Count - 1 && headingPositions[sectionIndex + 1] > headingIndex)
                sectionEnd = headingPositions[sectionIndex + 1];

            var inSection = bodyPlaceholderParagraphs
                .Where(i => i > sectionStart && i < sectionEnd)
                .ToList();

            foreach (var paragraphIndex in inSection)
                coveredPlaceholders.Add(paragraphIndex);

            if (inSection.Count == 0)
            {
                issues.Add(new TemplateIssue(
                    section.Field,
                    TemplateIssueKind.MissingPlaceholder,
                    $"Missing body placeholder {ReportTemplateContract.BodyPlaceholder} under section \"{section.Heading}\"."));
            }
            else if (inSection.Count > 1)
            {
                issues.Add(new TemplateIssue(
                    section.Field,
                    TemplateIssueKind.DuplicatePlaceholder,
                    $"Section \"{section.Heading}\" contains {inSection.Count} body placeholders; it must occur exactly once."));
            }
        }

        var stray = bodyPlaceholderParagraphs.Where(i => !coveredPlaceholders.Contains(i)).ToList();
        if (stray.Count > 0)
        {
            issues.Add(new TemplateIssue(
                null,
                TemplateIssueKind.MisplacedPlaceholder,
                $"Found {stray.Count} body placeholder(s) not associated with any expected section heading."));
        }

        return new TemplateValidationResult(issues.Count == 0, TemplateValidationFailure.None, null, issues);
    }

    private static int CountOccurrences(string text, string placeholder)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(placeholder))
            return 0;

        var count = 0;
        var startIndex = 0;
        while ((startIndex = text.IndexOf(placeholder, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += placeholder.Length;
        }

        return count;
    }
}