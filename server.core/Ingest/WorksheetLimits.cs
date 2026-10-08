using ClosedXML.Excel;

namespace Server.Core.Ingest;

/// <summary>
/// The used range of an uploaded worksheet, refused when absurdly large.
///
/// The readers walk every cell up to the last used row and column, and both come from the file. A
/// 6 KB workbook with one value in A1 and one in XFD1048576 claims 17 billion cells; walking them
/// exhausts memory long before it finishes. Real PD forms and standards workbooks are a few hundred
/// rows by a few dozen columns, so the limits sit far above anything legitimate.
/// </summary>
public static class WorksheetLimits
{
    public const int MaxRows = 20_000;
    public const int MaxColumns = 256;

    public static (int Rows, int Columns) UsedRange(IXLWorksheet ws)
    {
        var rows = ws.LastRowUsed()?.RowNumber() ?? 0;
        var columns = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (rows > MaxRows || columns > MaxColumns)
        {
            throw new InvalidDataException(
                $"Sheet “{ws.Name}” spans {rows:N0} rows by {columns:N0} columns, beyond the {MaxRows:N0} × {MaxColumns} this reads.");
        }

        return (rows, columns);
    }
}
