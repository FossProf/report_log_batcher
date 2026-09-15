using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Infrastructure.Tests;

/// <summary>
/// Test helpers for reading a rendered/report-log document back out of
/// <see cref="WordDocumentInspector"/> text.
/// </summary>
public static class RenderedDocTestHelper
{
    /// <summary>
    /// Maps each body-section heading (in <see cref="ReportTemplateContract.BodySections"/>) to
    /// the paragraph texts that follow it (until the next heading), joined with
    /// "\n\n". Trailing empty paragraphs are ignored so the template's structural
    /// trailing paragraphs do not pollute comparisons.
    /// </summary>
    public static Dictionary<string, string> ReadSections(IReadOnlyList<string> paragraphTexts)
    {
        var result = new Dictionary<string, string>();
        var headings = ReportTemplateContract.BodySections
            .Select(section => section.Heading)
            .ToHashSet(StringComparer.Ordinal);

        string? current = null;
        var buffer = new List<string>();
        foreach (var text in paragraphTexts)
        {
            if (headings.Contains(text))
            {
                Flush();
                current = text;
                buffer = new List<string>();
                continue;
            }

            if (current is not null)
                buffer.Add(text);
        }

        Flush();

        void Flush()
        {
            if (current is null)
                return;

            while (buffer.Count > 0 && buffer[^1] == string.Empty)
                buffer.RemoveAt(buffer.Count - 1);

            result[current] = string.Join("\n\n", buffer);
        }

        return result;
    }

    public static string? FindHeader(IReadOnlyList<string> paragraphTexts) =>
        paragraphTexts.FirstOrDefault(text => text.StartsWith("Report #", StringComparison.Ordinal));
}