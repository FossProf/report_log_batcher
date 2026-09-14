using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ReportLogBatcher.Infrastructure.Tests;

/// <summary>
/// Builds in-memory .docx SPIN-shaped documents for parser tests.
///
/// Paragraph and cell text segments may contain the markers:
/// - <c>[TAB]</c>      a single structural tab character
/// - <c>[BR]</c>       a text-wrapping line break
/// - <c>[PAGEBREAK]</c> an explicit page break
///
/// Content is appended in order so tables and paragraphs can be interleaved,
/// mirroring the production layout (title paragraph, header table, sections).
/// </summary>
public sealed class SpinDocFactory
{
    private static readonly Regex MarkerSplit = new(
        @"(\[TAB\]|\[BR\]|\[PAGEBREAK\])",
        RegexOptions.Compiled);

    private readonly string _tempRoot;

    public SpinDocFactory(string tempRoot)
    {
        _tempRoot = tempRoot;
    }

    public string Create(string fileName, Action<SpinDocBuilder> configure)
    {
        var path = Path.Combine(_tempRoot, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var builder = new SpinDocBuilder();
        configure(builder);
        var body = new Body();
        foreach (var element in builder.Elements)
            body.AppendChild(element);

        main.Document = new Document(body);
        main.Document.Save();
        return path;
    }

    public string CreateFile(string fileName, byte[] content)
    {
        var path = Path.Combine(_tempRoot, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Builds a paragraph from marker-aware segments.</summary>
    public static Paragraph BuildParagraph(params string[] segments)
    {
        var paragraph = new Paragraph();
        var buffer = new StringBuilder();

        void Flush()
        {
            if (buffer.Length == 0)
                return;
            paragraph.AppendChild(new Run(
                new Text(buffer.ToString()) { Space = SpaceProcessingModeValues.Preserve }));
            buffer.Clear();
        }

        foreach (var segment in segments)
        {
            foreach (var token in MarkerSplit.Split(segment))
            {
                switch (token)
                {
                    case "[TAB]":
                        Flush();
                        paragraph.AppendChild(new Run(new TabChar()));
                        break;
                    case "[BR]":
                        Flush();
                        paragraph.AppendChild(new Run(new Break()));
                        break;
                    case "[PAGEBREAK]":
                        Flush();
                        paragraph.AppendChild(new Run(new Break { Type = BreakValues.Page }));
                        break;
                    default:
                        buffer.Append(token);
                        break;
                }
            }
        }

        Flush();
        return paragraph;
    }
}

/// <summary>
/// Ordered document content: paragraphs and tables in document order.
/// </summary>
public sealed class SpinDocBuilder
{
    private readonly List<OpenXmlElement> _elements = new();

    public IReadOnlyList<OpenXmlElement> Elements => _elements;

    public SpinDocBuilder Paragraph(params string[] segments)
    {
        _elements.Add(SpinDocFactory.BuildParagraph(segments));
        return this;
    }

    /// <summary>
    /// Adds a table. Each row is an array whose entries are cell texts.
    /// Cells are single paragraphs in a single table cell.
    /// </summary>
    public SpinDocBuilder Table(params string[][] rows)
    {
        var table = new Table();
        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            foreach (var cellText in row)
            {
                tableRow.AppendChild(new TableCell(
                    new TableCellProperties(),
                    SpinDocFactory.BuildParagraph(cellText)));
            }

            table.AppendChild(tableRow);
        }

        _elements.Add(table);
        return this;
    }
}