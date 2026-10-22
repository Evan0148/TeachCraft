using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace GradeStrip.Core.Export;

/// <summary>
/// ClosedXML 在合并单元格与列宽上的写入结果和 Excel 原生文件有细微差异：
/// 1) 合并区域内的从属单元格边框会退回黑色；
/// 2) 列宽会带上 5px 内边距的换算误差，导致渲染宽度略窄。
/// 保存后直接修正这两处，保证与参考文件一致。
/// </summary>
internal static class WorkbookPostProcessor
{
    public static void Apply(string path, int lastColumn)
    {
        using var document = SpreadsheetDocument.Open(path, true);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null) return;

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            var worksheet = worksheetPart.Worksheet;
            var changed = FixMergedCellStyles(worksheet);
            RewriteColumns(worksheet, lastColumn);
            changed = true;

            if (changed) worksheet.Save();
        }
    }

    private static bool FixMergedCellStyles(Worksheet worksheet)
    {
        var mergeCells = worksheet.Descendants<MergeCells>().FirstOrDefault();
        if (mergeCells is null) return false;

        var cells = worksheet.Descendants<Cell>().ToList();
        var changed = false;

        foreach (var mergeCell in mergeCells.Elements<MergeCell>())
        {
            if (mergeCell.Reference?.Value is not { } reference) continue;
            var range = reference.Split(':');
            if (range.Length != 2) continue;

            var (topRow, topCol) = Parse(range[0]);
            var (bottomRow, bottomCol) = Parse(range[1]);
            if (topCol != bottomCol || bottomRow <= topRow) continue;

            var topCell = Find(cells, topRow, topCol);
            if (topCell?.StyleIndex?.Value is not { } styleIndex) continue;

            for (var row = topRow + 1; row <= bottomRow; row++)
            {
                var cell = Find(cells, row, topCol);
                if (cell is null) continue;
                cell.StyleIndex = styleIndex;
                changed = true;
            }
        }

        return changed;
    }

    private static void RewriteColumns(Worksheet worksheet, int lastColumn)
    {
        worksheet.GetFirstChild<Columns>()?.Remove();

        var columns = new Columns(
            new Column { Min = 1, Max = 1, Width = 16, CustomWidth = true },
            new Column { Min = 2, Max = (uint)(lastColumn - 3), Width = 8.625, CustomWidth = true },
            new Column { Min = (uint)(lastColumn - 2), Max = (uint)(lastColumn - 2), Width = 10.5, CustomWidth = true },
            new Column { Min = (uint)(lastColumn - 1), Max = (uint)lastColumn, Width = 8, CustomWidth = true });

        var sheetData = worksheet.GetFirstChild<SheetData>();
        if (sheetData is not null)
            worksheet.InsertBefore(columns, sheetData);
        else
            worksheet.Append(columns);

        // ClosedXML 默认行高用的是 15（Calibri），参考文件是 13.5（宋体），
        // 这会让未显式设高的留白行偏高，进而影响分页与缩放比例
        var format = worksheet.GetFirstChild<SheetFormatProperties>();
        if (format is not null)
        {
            format.DefaultRowHeight = 13.5;
            format.CustomHeight = false;
        }
    }

    private static Cell? Find(IEnumerable<Cell> cells, int row, int column)
    {
        var reference = $"{ColumnName(column)}{row}";
        return cells.FirstOrDefault(c => c.CellReference?.Value == reference);
    }

    private static (int Row, int Column) Parse(string reference)
    {
        var split = 0;
        while (split < reference.Length && char.IsLetter(reference[split])) split++;
        var column = 0;
        foreach (var ch in reference[..split].ToUpperInvariant())
            column = column * 26 + (ch - 'A' + 1);
        return (int.Parse(reference[split..]), column);
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            var remainder = (column - 1) % 26;
            name = (char)('A' + remainder) + name;
            column = (column - 1) / 26;
        }

        return name;
    }
}
