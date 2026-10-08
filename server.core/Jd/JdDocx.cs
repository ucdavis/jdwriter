using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Server.Core.Jd;

/// <summary>
/// A finished JD as a Word document, for units that edit, route or attach JDs as .docx.
///
/// The same sections in the same order as the on-screen and printed JD, so the PDF and the Word
/// file never disagree. Built with plain paragraphs and the built-in Heading and List Bullet
/// styles rather than a template: the result is editable and reads correctly in Word, Google Docs
/// and LibreOffice, and nothing in it depends on a file that could drift out of the repository.
/// </summary>
public static class JdDocx
{
    public static byte[] Build(SavedJd saved)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            AddStyles(main);
            var body = new Body();
            main.Document = new Document(body);

            var jd = saved.Jd;
            body.Append(Paragraph(saved.WorkingTitle.Length > 0 ? saved.WorkingTitle : saved.Title, "Title"));

            var identity = new List<string>();
            if (saved.Department.Length > 0)
            {
                identity.Add(saved.Department);
            }

            identity.Add($"UC Job Code {saved.UcJobCode}");
            if (saved.Title.Length > 0 && saved.Title != saved.WorkingTitle)
            {
                identity.Add(saved.Title);
            }

            body.Append(Paragraph(string.Join(" · ", identity)));

            var facts = new List<string>();
            if (!string.IsNullOrEmpty(saved.SalaryGrade))
            {
                facts.Add($"Salary Grade: {saved.SalaryGrade}");
            }

            if (!string.IsNullOrEmpty(saved.FlsaStatus))
            {
                facts.Add($"FLSA: {saved.FlsaStatus}");
            }

            if (!string.IsNullOrEmpty(saved.BargainingUnit))
            {
                facts.Add($"Bargaining Unit: {saved.BargainingUnit}");
            }

            if (facts.Count > 0)
            {
                body.Append(Paragraph(string.Join(" · ", facts)));
            }

            body.Append(Paragraph("Job Summary", "Heading1"));
            body.Append(Paragraph(jd.JobSummary));

            // The total is part of the document, not decoration: HRTMS rejects a JD off 100%.
            var total = jd.KeyResponsibilities.Sum(r => r.PctTime);
            body.Append(Paragraph($"Key Responsibilities — Total {total}%", "Heading1"));
            foreach (var r in jd.KeyResponsibilities)
            {
                body.Append(Paragraph($"{r.PctTime}%  {r.FunctionName}", "Heading2"));
                foreach (var d in r.Duties)
                {
                    body.Append(Paragraph(d, "ListBullet"));
                }
            }

            body.Append(Paragraph("Qualifications", "Heading1"));
            SubList(body, "Licenses & Certifications", jd.LicensesCertifications);
            SubList(body, "Education", jd.Education);
            SubList(body, "Work Experience", jd.WorkExperience);
            SubList(body, "Minimum Knowledge, Skills & Abilities", jd.MinKSA);
            SubList(body, "Preferred Knowledge, Skills & Abilities", jd.PrefKSA);

            Section(body, "Conditions of Employment", jd.ConditionsOfEmployment);
            Section(body, "Work Environment", jd.WorkEnvironment);
            Section(body, "Physical Requirements", jd.PhysicalRequirements);

            main.Document.Save();
        }

        return RelativeTargets(stream.ToArray());
    }

    /// <summary>
    /// Rewrite the package's relationship targets as Word writes them: relative ("word/document.xml")
    /// rather than the absolute form ("/word/document.xml") System.IO.Packaging produces. Both are
    /// valid, and Word opens either — but Apple's reader, behind Quick Look, Pages and Mail previews,
    /// refuses the absolute form with "the file isn't in the correct format".
    /// </summary>
    private static byte[] RelativeTargets(byte[] package)
    {
        using var stream = new MemoryStream();
        stream.Write(package);
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            // The package's own relationships resolve from the root; the main part's from word/.
            Rewrite(zip, "_rels/.rels", "Target=\"/", "Target=\"");
            Rewrite(zip, "word/_rels/document.xml.rels", "Target=\"/word/", "Target=\"");
        }

        return stream.ToArray();
    }

    private static void Rewrite(System.IO.Compression.ZipArchive zip, string entryName, string from, string to)
    {
        var entry = zip.GetEntry(entryName);
        if (entry is null)
        {
            return;
        }

        string xml;
        using (var reader = new StreamReader(entry.Open()))
        {
            xml = reader.ReadToEnd();
        }

        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(entryName).Open(), new System.Text.UTF8Encoding(false));
        writer.Write(xml.Replace(from, to, StringComparison.Ordinal));
    }

    /// <summary>"Analyst 3 - 005183.docx": safe on every file system, still recognisable in Downloads.</summary>
    public static string FileName(SavedJd saved)
    {
        var name = $"{(saved.WorkingTitle.Length > 0 ? saved.WorkingTitle : saved.Title)} - {saved.UcJobCode}";
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var clean = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim();
        return (clean.Length > 0 ? clean : "Job description") + ".docx";
    }

    private static void SubList(Body body, string label, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        body.Append(Paragraph(label, "Heading2"));
        foreach (var item in items)
        {
            body.Append(Paragraph(item, "ListBullet"));
        }
    }

    private static void Section(Body body, string title, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        body.Append(Paragraph(title, "Heading1"));
        foreach (var item in items)
        {
            body.Append(Paragraph(item, "ListBullet"));
        }
    }

    private static Paragraph Paragraph(string text, string? style = null)
    {
        var p = new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        if (style is not null)
        {
            p.PrependChild(new ParagraphProperties(new ParagraphStyleId { Val = style }));
        }

        return p;
    }

    /// <summary>
    /// The handful of styles the document uses, defined here so a fresh document carries them. List
    /// Bullet uses a real numbering definition, so bullets survive editing in Word.
    /// </summary>
    private static void AddStyles(MainDocumentPart main)
    {
        var numbering = main.AddNewPart<NumberingDefinitionsPart>();
        numbering.Numbering = new Numbering(
            new AbstractNum(
                new Level(
                    new NumberingFormat { Val = NumberFormatValues.Bullet },
                    new LevelText { Val = "•" },
                    new LevelJustification { Val = LevelJustificationValues.Left },
                    new PreviousParagraphProperties(new Indentation { Left = "720", Hanging = "360" }))
                { LevelIndex = 0 })
            { AbstractNumberId = 1 },
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });

        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri" },
                    new FontSize { Val = "22" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "120" }))),
            Style("Normal", "Normal", null, isDefault: true),
            Style("Title", "Title", new StyleRunProperties(new Bold(), new Color { Val = "022851" }, new FontSize { Val = "36" })),
            Style("Heading1", "heading 1", new StyleRunProperties(new Bold(), new Color { Val = "022851" }, new FontSize { Val = "28" }),
                new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "320", After = "120" }, new OutlineLevel { Val = 0 })),
            Style("Heading2", "heading 2", new StyleRunProperties(new Bold(), new FontSize { Val = "24" }),
                new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "200", After = "80" }, new OutlineLevel { Val = 1 })),
            Style("ListBullet", "List Bullet", null,
                new StyleParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }),
                    new SpacingBetweenLines { After = "60" })));
    }

    /// <summary>
    /// A paragraph style, its children in the order the schema requires (name, basedOn, qFormat,
    /// pPr, rPr). Word rejects or "repairs" a file whose elements are out of order; the schema
    /// validation test holds this to it.
    /// </summary>
    private static Style Style(
        string id, string name, StyleRunProperties? run, StyleParagraphProperties? paragraph = null, bool isDefault = false)
    {
        var style = new Style(new StyleName { Val = name }) { Type = StyleValues.Paragraph, StyleId = id };
        if (isDefault)
        {
            style.Default = true;
        }
        else
        {
            style.Append(new BasedOn { Val = "Normal" });
        }

        style.Append(new PrimaryStyle());
        if (paragraph is not null)
        {
            style.Append(paragraph);
        }

        if (run is not null)
        {
            style.Append(run);
        }

        return style;
    }
}
