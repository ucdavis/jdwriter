using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Server.Core.Vrf;

/// <summary>
/// A Vacancy Request Form as a Word document: every field of the campus VRF, labelled and in its
/// order, so HR can copy each one into the Smartsheet form. Not the official form's layout — HR
/// re-keys it either way — but nothing is left out: a required field left blank says so, a salary
/// field left blank is HR's to fill in, and a field that doesn't apply to this request says why.
/// </summary>
public static class VrfDocx
{
    public const string NotProvided = "Not provided — required";
    public const string ForHr = "To be filled in by HR";

    public static byte[] Build(VrfForm form, DateOnly today)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            AddStyles(main);
            var body = new Body();
            main.Document = new Document(body);

            body.Append(Paragraph("Vacancy Request Form (VRF) — Davis Campus", "Title"));
            body.Append(Paragraph(Position(form)));
            body.Append(Paragraph(
                $"Prepared in JDWriter on {today:MMMM d, yyyy}. Each field below matches the campus VRF, in its order. " +
                "Attach this, the job description and an org chart to your AggieService ticket."));

            Table? table = null;
            string? section = null;
            foreach (var field in VrfCatalog.Fields)
            {
                var na = field.NotApplicable?.Invoke(form);
                if (na == "")
                {
                    continue;
                }

                if (field.Section != section)
                {
                    section = field.Section;
                    body.Append(Paragraph(SectionTitle(section), "Heading1"));
                    table = Table();
                    body.Append(table);
                }

                table!.Append(Row(field.Label, Value(field, form, na)));
            }

            body.Append(Paragraph("Attach to your AggieService ticket", "Heading1"));
            foreach (var (when, documents) in VrfCatalog.Attachments)
            {
                body.Append(Paragraph($"{when}: {string.Join(", ", documents)}", "ListBullet"));
            }

            main.Document.Save();
        }

        // As Word writes them, so Apple's readers (Quick Look, Pages, Mail) open it too.
        return Server.Core.Jd.JdDocx.RelativeTargets(stream.ToArray());
    }

    /// <summary>What a field says: the answer, or why there isn't one.</summary>
    public static string Value(VrfCatalog.Field field, VrfForm form, string? notApplicable)
    {
        if (notApplicable is not null)
        {
            return notApplicable;
        }

        var value = field.Value(form).Trim();
        if (value.Length > 0)
        {
            return value;
        }

        return field.Section == "Salary" ? ForHr : field.Required ? NotProvided : "—";
    }

    public static string FileName(VrfForm form)
    {
        var title = form.WorkingTitle.Trim().Length > 0 ? form.WorkingTitle.Trim() : form.PayrollTitle.Trim();
        var name = title.Length > 0 ? $"VRF - {title}" : "VRF";
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        return new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim() + ".docx";
    }

    private static string Position(VrfForm f)
    {
        var parts = new[] { f.WorkingTitle, f.PayrollTitle.Length > 0 && f.JobCode.Length > 0 ? $"{f.PayrollTitle} ({f.JobCode})" : f.PayrollTitle, f.Department }
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct();
        return string.Join(" · ", parts);
    }

    private static string SectionTitle(string section) => section switch
    {
        "Position" => "The position",
        "Request" => "The request",
        "Budget" => "Budget",
        "Salary" => "Salary",
        "Why" => "Why this position is needed",
        _ => "People and notes",
    };

    private static Table Table() =>
        new(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" }),
            new TableLayout { Type = TableLayoutValues.Fixed }),
            new TableGrid(new GridColumn { Width = "3000" }, new GridColumn { Width = "6360" }));

    private static TableRow Row(string label, string value)
    {
        TableCell Cell(string width, OpenXmlElement content) =>
            new(new TableCellProperties(new TableCellWidth { Width = width, Type = TableWidthUnitValues.Dxa }), content);

        var labelRun = new Run(new RunProperties(new Bold()), new Text(label) { Space = SpaceProcessingModeValues.Preserve });
        var valueParagraph = value is NotProvided
            ? new Paragraph(new Run(new RunProperties(new Italic(), new Color { Val = "B00020" }), new Text(value)))
            : Paragraph(value);
        return new TableRow(
            Cell("3000", new Paragraph(labelRun)),
            Cell("6360", valueParagraph));
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

    /// <summary>The same small style set as the JD's Word file (see JdDocx).</summary>
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
            Style("ListBullet", "List Bullet", null,
                new StyleParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 }),
                    new SpacingBetweenLines { After = "60" })));
    }

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
