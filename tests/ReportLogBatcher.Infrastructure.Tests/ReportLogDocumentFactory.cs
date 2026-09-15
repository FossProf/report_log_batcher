using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReportLogBatcher.Core.Models;
using ReportLogBatcher.Core.Services;

namespace ReportLogBatcher.Infrastructure.Tests;

/// <summary>
/// Builds .docx fixtures shaped like the production report-log template and
/// report logs: split-run placeholders, bold/underlined placeholder paragraphs,
/// five section headings, trailing empty paragraphs and a final section break.
/// </summary>
public sealed class ReportLogDocumentFactory
{
    private readonly string _tempRoot;

    public ReportLogDocumentFactory(string tempRoot)
    {
        _tempRoot = tempRoot;
    }

    /// <summary>Canonical template. Header and General section placeholders are split across runs.</summary>
    public string CreateTemplate(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var body = new Body();
        body.Append(new Paragraph());

        body.Append(BuildHeaderParagraph());
        foreach (var section in ReportTemplateContract.BodySections)
        {
            body.Append(BuildHeading(section.Heading));
            body.Append(section.Field == ReportField.GeneralObservations
                ? BuildSplitBodyPlaceholder()
                : BuildBodyPlaceholder());
        }

        body.Append(new Paragraph());
        body.Append(new Paragraph());
        body.Append(BuildSectionProperties());

        document.AddMainDocumentPart().Document = new Document(body);
        document.Save();
        return path;
    }

    /// <summary>Template where every placeholder lives in a single run.</summary>
    public string CreateSingleRunTemplate(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var body = new Body();
        body.Append(BuildHeaderParagraphSingleRun());
        foreach (var section in ReportTemplateContract.BodySections)
        {
            body.Append(BuildHeading(section.Heading));
            body.Append(BuildBodyPlaceholder());
        }
        body.Append(BuildSectionProperties());

        document.AddMainDocumentPart().Document = new Document(body);
        document.Save();
        return path;
    }

    /// <summary>Template that never contains the header placeholders.</summary>
    public string CreateTemplateWithoutHeader(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var body = new Body();
        body.Append(BuildParagraph("No header placeholders here"));
        foreach (var section in ReportTemplateContract.BodySections)
        {
            body.Append(BuildHeading(section.Heading));
            body.Append(BuildBodyPlaceholder());
        }
        body.Append(BuildSectionProperties());

        document.AddMainDocumentPart().Document = new Document(body);
        document.Save();
        return path;
    }

    /// <summary>Template whose body references a document relationship.</summary>
    public string CreateTemplateWithEmbeddedObject(string fileName)
    {
        var path = CreateTemplate(fileName);
        using var document = WordprocessingDocument.Open(path, true);
        var firstParagraph = document.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().First();
        firstParagraph.SetAttribute(new OpenXmlAttribute(
            "r",
            "embed",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
            "rId99"));
        document.Save();
        return path;
    }

    /// <summary>A destination report log with the given paragraphs.</summary>
    public string CreateReportLog(string fileName, params string[] paragraphTexts)
    {
        var path = Path.Combine(_tempRoot, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var body = new Body();

        if (paragraphTexts.Length == 0)
            body.Append(new Paragraph());
        else
            foreach (var text in paragraphTexts)
                body.Append(BuildParagraph(text));

        body.Append(BuildSectionProperties());
        document.AddMainDocumentPart().Document = new Document(body);
        document.Save();
        return path;
    }

    /// <summary>A rendered-entry-shaped document with no placeholders.</summary>
    public string CreateEntryDocument(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var body = new Body();

        body.Append(BuildParagraph("Report #319 – 09/11/26– Anthony"));
        foreach (var section in ReportTemplateContract.BodySections)
        {
            body.Append(BuildHeading(section.Heading));
            body.Append(BuildParagraph("Entry value for " + section.Heading));
        }

        body.Append(new Paragraph());
        body.Append(new Paragraph());
        body.Append(BuildSectionProperties());
        document.AddMainDocumentPart().Document = new Document(body);
        document.Save();
        return path;
    }

    /// <summary>
    /// A rendered-entry-shaped document that still contains one required
    /// placeholder in its body.
    /// </summary>
    public string CreateEntryWithPlaceholder(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var body = new Body();
        body.Append(new Paragraph(new Run(new Text("{report.number}"))));
        body.Append(BuildSectionProperties());
        document.AddMainDocumentPart().Document = new Document(body);
        document.Save();
        return path;
    }

    /// <summary>
    /// An entry whose body references a document relationship, which cannot be
    /// safely copied into another document.
    /// </summary>
    public string CreateEntryWithRelationshipReference(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var body = new Body();
        var paragraph = new Paragraph(new Run(new Text("embedded object")));
        paragraph.SetAttribute(new OpenXmlAttribute(
            "r",
            "embed",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
            "rId5"));
        body.Append(paragraph);
        body.Append(new Paragraph(new Run(new Text("normal text"))));
        body.Append(BuildSectionProperties());
        document.AddMainDocumentPart().Document = new Document(body);
        document.Save();
        return path;
    }

    /// <summary>
    /// A valid entry whose paragraphs contain nested paragraph-level section
    /// properties that must be stripped before the entry can be merged.
    /// </summary>
    public string CreateEntryWithNestedSection(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var body = new Body();
        body.Append(new Paragraph(new Run(new Text("entry text"))));
        var sectionOwner = new Paragraph(
            new ParagraphProperties(new SectionProperties(new PageSize())),
            new Run(new Text("nested section boundary")));
        body.Append(sectionOwner);
        body.Append(new Paragraph(new Run(new Text("tail"))));
        body.Append(BuildSectionProperties());
        document.AddMainDocumentPart().Document = new Document(body);
        document.Save();
        return path;
    }

    /// <summary>Writes arbitrary bytes under a .docx-looking path.</summary>
    public string CreateFile(string fileName, byte[] content)
    {
        var path = Path.Combine(_tempRoot, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Writes an empty (0-byte) .docx file.</summary>
    public string CreateEmptyFile(string fileName)
    {
        var path = Path.Combine(_tempRoot, fileName);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    public static Paragraph BuildParagraph(string text) =>
        new(new Run(new Text(text)
        {
            Space = SpaceProcessingModeValues.Preserve,
        }));

    private static Paragraph BuildHeading(string text) =>
        new(new Run(
            new RunProperties(NewBold(), NewBoldComplex(), new Underline { Val = UnderlineValues.Single }),
            new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Paragraph BuildBodyPlaceholder() =>
        new(new ParagraphProperties(new ParagraphMarkRunProperties(NewBold(), NewBoldComplex())),
            new Run(new Text(ReportTemplateContract.BodyPlaceholder)));

    private static Paragraph BuildSplitBodyPlaceholder() =>
        new(new ParagraphProperties(new ParagraphMarkRunProperties(NewBold(), NewBoldComplex())),
            new Run(new Text("{body of text under the ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new Text("same ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new Text("header")),
            new Run(new Text("}")));

    private static Paragraph BuildHeaderParagraph()
    {
        var paragraph = new Paragraph(new ParagraphProperties(new ParagraphMarkRunProperties(NewBold(), NewBoldComplex())));
        string[] fragments =
        {
            "Report #", "{", "report.number", "}", " – ",
            "{", "report.date", " mm/dd/", "yy", "}", "– ",
            "{", "report.inspector.first_name", "}",
        };
        foreach (var fragment in fragments)
            paragraph.Append(new Run(
                new RunProperties(NewBold(), NewBoldComplex()),
                new Text(fragment) { Space = SpaceProcessingModeValues.Preserve }));
        return paragraph;
    }

    private static Paragraph BuildHeaderParagraphSingleRun() =>
        new(new ParagraphProperties(new ParagraphMarkRunProperties(NewBold(), NewBoldComplex())),
            new Run(
                new RunProperties(NewBold(), NewBoldComplex()),
                new Text("Report #{report.number} – {report.date mm/dd/yy}– {report.inspector.first_name}")
                {
                    Space = SpaceProcessingModeValues.Preserve,
                }));

    private static SectionProperties BuildSectionProperties() =>
        new(
            new PageSize { Width = 12240, Height = 15840 },
            new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 720, Footer = 720, Gutter = 0 });

    private static Bold NewBold() => new();

    private static BoldComplexScript NewBoldComplex() => new();
}