using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Server.Core.Ai;

/// <summary>
/// The label every document JDWriter produces carries, saying AI helped write it. Kept small: a
/// footer on every page of a Word file, a line at the foot of the on-screen and printed JD
/// (client/src/features/build/FinalJd.tsx carries the same words).
/// </summary>
public static class AiDisclosure
{
    public const string Text =
        "Portions of this document were generated and/or assisted with the use of artificial intelligence (AI) technologies.";

    /// <summary>
    /// Put the label in the page footer. Call last, once the body is complete: the section
    /// properties that point at the footer must be the body's final element.
    /// </summary>
    public static void AddFooter(MainDocumentPart main, Body body)
    {
        var footerPart = main.AddNewPart<FooterPart>();
        footerPart.Footer = new Footer(new Paragraph(
            new Run(
                new RunProperties(new Color { Val = "595959" }, new FontSize { Val = "16" }),
                new Text(Text))));

        // US Letter with 1-inch margins, stated rather than left to the reader: Word assumes them,
        // but a reader set up for A4 would otherwise lay the page out differently.
        body.Append(new SectionProperties(
            new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footerPart) },
            new PageSize { Width = 12240U, Height = 15840U },
            new PageMargin { Top = 1440, Right = 1440U, Bottom = 1440, Left = 1440U, Header = 720U, Footer = 720U, Gutter = 0U }));
    }
}
