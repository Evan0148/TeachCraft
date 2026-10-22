using ClosedXML.Excel;
using GradeStrip.Core.Layout;
using GradeStrip.Core.Model;

namespace GradeStrip.Core.Export;

public sealed class StripExportOptions
{
    /// <summary>第一张表放几人（含大标题与图例）。</summary>
    public int FirstGroupSize { get; set; } = 7;

    /// <summary>后续每张表放几人。</summary>
    public int GroupSize { get; set; } = 8;

    public bool IncludeTitle { get; set; } = true;
    public bool IncludeLegend { get; set; } = true;
    public bool FreezeHeader { get; set; } = true;

    /// <summary>是否导出校排（默认 false 导出班排）。</summary>
    public bool UseSchoolRank { get; set; } = false;

    /// <summary>跨考对比基准注记（如 "对比上一场考试" 或 "对比第1次考试"）。</summary>
    public string? ComparisonNote { get; set; }

    /// <summary>大标题；为空时使用「班级 · 考试 · 个人成绩条」。</summary>
    public string? Title { get; set; }

    public double PageScale { get; set; } = 96;

    public double FirstGroupRowHeight { get; set; } = 15;
    public double ScoreRowHeight { get; set; } = 21.95;
    public double DeltaRowHeight { get; set; } = 14.1;
    public double SpacerRowHeight { get; set; } = 6;
}

public sealed record StripSheetInfo(string SheetName, int FirstNo, int LastNo, int Count);

public sealed class StripExportResult
{
    public required IReadOnlyList<StripSheetInfo> Sheets { get; init; }
    public required int StudentCount { get; init; }
}

/// <summary>把 ExamData 渲染成多张「成绩条」工作表。</summary>
public sealed class ScoreStripExporter
{
    private const int HeaderRow = 1;
    private const int LegendRow = 2;
    private const int SwatchRow = 3;
    private const int FirstStripRow = 5;

    public static StripExportResult Export(ExamData data, string outputPath, StripExportOptions? options = null)
    {
        options ??= new StripExportOptions();
        if (data.Students.Count == 0)
            throw new InvalidOperationException("没有可导出的学生数据。");

        var subjectCount = data.Subjects.Count;
        if (subjectCount == 0)
            throw new InvalidOperationException("没有识别到任何科目。");

        var groups = StripGrouping.Split(data.Students, options.FirstGroupSize, options.GroupSize);

        using var workbook = new XLWorkbook();
        var sheets = new List<StripSheetInfo>();

        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var isFirst = g == 0;
            var ws = workbook.Worksheets.Add(SafeSheetName(group[0].No, group[^1].No));

            var row = 1;
            if (isFirst && options.IncludeTitle)
                row = RenderHeader(ws, data, options, subjectCount);

            for (var i = 0; i < group.Count; i++)
            {
                RenderStrip(ws, row, group[i], data.Subjects, options);
                row += 3;
                if (i < group.Count - 1)
                {
                    ws.Row(row).Height = options.SpacerRowHeight;
                    row++;
                }
            }

            var lastRow = row - 1;
            ConfigurePage(ws, lastRow, subjectCount, options, freezeRows: isFirst && options.IncludeTitle ? 4 : 0);

            sheets.Add(new StripSheetInfo(ws.Name, group[0].No, group[^1].No, group.Count));
        }

        workbook.SaveAs(outputPath);
        WorkbookPostProcessor.Apply(outputPath, 4 + subjectCount);
        return new StripExportResult { Sheets = sheets, StudentCount = data.Students.Count };
    }

    private static string SafeSheetName(int firstNo, int lastNo)
    {
        var name = $"{firstNo}-{lastNo}";
        return name.Length <= 31 ? name : name[..31];
    }

    /// <summary>渲染标题、图例、色块，返回第一张成绩条的起始行号。</summary>
    private static int RenderHeader(
        IXLWorksheet ws, ExamData data, StripExportOptions options, int subjectCount)
    {
        var lastCol = 4 + subjectCount;

        ws.Range(HeaderRow, 1, HeaderRow, lastCol).Merge();
        var title = ws.Cell(HeaderRow, 1);
        title.Value = options.Title ?? data.DefaultTitle;
        ApplyFont(title, 15, bold: true, Strips.NameText);
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(HeaderRow).Height = 30;

        var row = 2;
        if (options.IncludeLegend)
        {
            ws.Range(LegendRow, 1, LegendRow, lastCol).Merge();
            var legend = ws.Cell(LegendRow, 1);
            // 用富文本让 ■ 直接带上分档配色，避免图表导出后看不到颜色
            var rich = legend.CreateRichText();
            foreach (var segment in Strips.LegendSegments(options.UseSchoolRank, options.ComparisonNote))
            {
                var run = rich.AddText(segment.Text);
                run.SetFontSize(9);
                run.SetFontName(Strips.FontName);
                run.SetFontColor(segment.Color is null
                    ? XLColor.FromHtml("#" + Strips.HeaderText)
                    : XLColor.FromHtml("#" + segment.Color));
            }

            legend.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            legend.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            legend.Style.Font.FontSize = 9;
            legend.Style.Font.FontName = Strips.FontName;
            ws.Row(LegendRow).Height = 18;

            // 色块行保留，方便打印后对照图例
            ws.Row(SwatchRow).Height = 6;
            for (var i = 0; i < Strips.TierOrder.Length; i++)
                Fill(ws.Cell(SwatchRow, 4 + i), Strips.TierOrder[i]);

            // 图例占 2~3 行，第 4 行留出窄间隙，第一条成绩条从第 5 行开始
            ws.Row(4).Height = 6.5;
            row = FirstStripRow;
        }

        return row;
    }

    private static void RenderStrip(
        IXLWorksheet ws, int row, StudentRecord student,
        IReadOnlyList<string> subjects, StripExportOptions options)
    {
        var lastCol = 4 + subjects.Count;

        ws.Row(row).Height = options.FirstGroupRowHeight;
        ws.Row(row + 1).Height = options.ScoreRowHeight;
        ws.Row(row + 2).Height = options.DeltaRowHeight;

        // 细网格：A 列稍后与合并区域一起处理
        for (var r = row; r <= row + 2; r++)
        {
            for (var c = 2; c <= lastCol; c++)
            {
                var cell = ws.Cell(r, c);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                SetBorder(cell.Style.Border, XLBorderStyleValues.Thin, Strips.GridLine);
            }
        }

        // 成绩条上下沿用中等线强调
        for (var c = 2; c <= lastCol; c++)
        {
            var top = ws.Cell(row, c).Style.Border;
            top.TopBorder = XLBorderStyleValues.Medium;
            top.TopBorderColor = XLColor.FromHtml("#" + Strips.StripEdge);

            var bottom = ws.Cell(row + 2, c).Style.Border;
            bottom.BottomBorder = XLBorderStyleValues.Medium;
            bottom.BottomBorderColor = XLColor.FromHtml("#" + Strips.StripEdge);
        }

        // A 列：序号 + 姓名 + 准考证号（纵向合并 3 行，合并后再套边框）
        var nameRange = ws.Range(row, 1, row + 2, 1);
        nameRange.Merge();
        var nameCell = ws.Cell(row, 1);
        nameCell.Value = Strips.StudentCellText(student);
        ApplyFont(nameCell, 11, bold: true, Strips.NameText);
        nameCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        nameCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        nameCell.Style.Alignment.WrapText = true;
        Fill(nameCell, Strips.NameFill);

        // 合并单元格的描边按行分别写入：上沿中等线、下沿细线、左右细线
        for (var r = row; r <= row + 2; r++)
        {
            var border = ws.Cell(r, 1).Style.Border;
            border.LeftBorder = XLBorderStyleValues.Thin;
            border.LeftBorderColor = XLColor.FromHtml("#" + Strips.GridLine);
            border.RightBorder = XLBorderStyleValues.Thin;
            border.RightBorderColor = XLColor.FromHtml("#" + Strips.GridLine);
            border.BottomBorder = XLBorderStyleValues.Thin;
            border.BottomBorderColor = XLColor.FromHtml("#" + Strips.GridLine);
            border.TopBorder = r == row ? XLBorderStyleValues.Medium : XLBorderStyleValues.Thin;
            border.TopBorderColor = XLColor.FromHtml("#" + (r == row ? Strips.StripEdge : Strips.GridLine));

            // 合并区域内的从属单元格需要显式继承样式，否则会退回默认黑色细线
            if (r > row) ws.Cell(r, 1).Style = ws.Cell(row, 1).Style;
        }

        // 科目表头
        for (var i = 0; i < subjects.Count; i++)
        {
            var header = ws.Cell(row, 2 + i);
            header.Value = subjects[i];
            ApplyFont(header, 9, bold: false, Strips.HeaderText);
            Fill(header, Strips.HeaderFill);
        }

        var totalCol = 2 + subjects.Count;
        SetSummaryHeader(ws.Cell(row, totalCol), Strips.TotalColumnHeader);
        SetSummaryHeader(ws.Cell(row, totalCol + 1), Strips.SchoolRankHeader);
        SetSummaryHeader(ws.Cell(row, totalCol + 2), Strips.ClassRankHeader);

        // 得分行
        for (var i = 0; i < subjects.Count; i++)
        {
            var score = student[subjects[i]];
            var cell = ws.Cell(row + 1, 2 + i);
            var rank = options.UseSchoolRank ? score.SchoolRank : score.ClassRank;
            if (score.Score is null)
            {
                cell.Value = "—";
                ApplyFont(cell, 12, bold: true, Strips.ScoreText);
                Fill(cell, Strips.NoData);
            }
            else
            {
                cell.Value = score.Score.Value;
                ApplyFont(cell, 12, bold: true, Strips.ScoreTextColor(rank));
                Fill(cell, Strips.TierFill(rank));
            }
        }

        // 总分
        var totalCell = ws.Cell(row + 1, totalCol);
        if (student.Total.Score is not null)
            totalCell.Value = student.Total.Score.Value;
        ApplyFont(totalCell, 14, bold: true, Strips.TotalText);
        Fill(totalCell, Strips.TotalFill);

        // 校次（若选校排则以主色强调凸显）
        var schoolRank = ws.Cell(row + 1, totalCol + 1);
        if (student.Total.SchoolRank is { } schoolRankValue)
            schoolRank.Value = schoolRankValue;
        else
            schoolRank.Value = "—";
        ApplyFont(schoolRank, 12, bold: true, options.UseSchoolRank ? Strips.NameText : Strips.ScoreText);
        Fill(schoolRank, options.UseSchoolRank ? Strips.ClassRankFill : Strips.RankFill);

        // 班次（若选班排则以主色强调凸显）
        var classRank = ws.Cell(row + 1, totalCol + 2);
        if (student.Total.ClassRank is { } classRankValue)
            classRank.Value = classRankValue;
        else
            classRank.Value = "—";
        ApplyFont(classRank, 12, bold: true, !options.UseSchoolRank ? Strips.NameText : Strips.ScoreText);
        Fill(classRank, !options.UseSchoolRank ? Strips.ClassRankFill : Strips.RankFill);

        // 科目备注行：班次/校次 + 对应进退步
        for (var i = 0; i < subjects.Count; i++)
        {
            var score = student[subjects[i]];
            var cell = ws.Cell(row + 2, 2 + i);
            var rank = options.UseSchoolRank ? score.SchoolRank : score.ClassRank;
            var delta = options.UseSchoolRank ? score.SchoolDelta : score.ClassDelta;
            cell.Value = Strips.SubjectLine(rank, delta, options.UseSchoolRank);
            ApplyFont(cell, 8, bold: false, Strips.DeltaColor(delta));
        }

        var schoolDelta = ws.Cell(row + 2, totalCol + 1);
        schoolDelta.Value = Strips.DeltaText(student.Total.SchoolDelta);
        ApplyFont(schoolDelta, 8, bold: false, Strips.DeltaColor(student.Total.SchoolDelta));

        var classDelta = ws.Cell(row + 2, totalCol + 2);
        classDelta.Value = Strips.DeltaText(student.Total.ClassDelta);
        ApplyFont(classDelta, 8, bold: false, Strips.DeltaColor(student.Total.ClassDelta));
    }

    private static void SetSummaryHeader(IXLCell cell, string text)
    {
        cell.Value = text;
        ApplyFont(cell, 9, bold: true, Strips.NameText);
        Fill(cell, Strips.NameFill);
    }

    private static void ConfigurePage(
        IXLWorksheet ws, int lastRow, int subjectCount, StripExportOptions options, int freezeRows)
    {
        var lastCol = 4 + subjectCount;

        ws.ShowGridLines = false;

        // ClosedXML 的列宽会额外加 5px（约 0.7109375 字符）内边距，这里换算回 Excel 目标宽度
        const double padding = 0.7109375;
        ws.Column(1).Width = 16 - padding;
        ws.Columns(2, lastCol - 3).Width = 8.625 - padding;
        ws.Column(lastCol - 2).Width = 10.5 - padding;
        ws.Columns(lastCol - 1, lastCol).Width = 8 - padding;

        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.Scale = (int)Math.Round(options.PageScale);
        ws.PageSetup.CenterHorizontally = true;
        ws.PageSetup.PrintAreas.Add(1, 1, lastRow, lastCol);
        ws.PageSetup.Margins.Left = 0.75;
        ws.PageSetup.Margins.Right = 0.75;
        ws.PageSetup.Margins.Top = 1.0;
        ws.PageSetup.Margins.Bottom = 1.0;
        ws.PageSetup.Margins.Header = 0.5;
        ws.PageSetup.Margins.Footer = 0.5;

        if (freezeRows > 0) ws.SheetView.FreezeRows(freezeRows);
    }

    private static void ApplyFont(IXLCell cell, double size, bool bold, string colorHex)
    {
        cell.Style.Font.FontName = Strips.FontName;
        cell.Style.Font.FontSize = size;
        cell.Style.Font.Bold = bold;
        cell.Style.Font.FontColor = XLColor.FromHtml("#" + colorHex);
    }

    private static void Fill(IXLCell cell, string colorHex)
    {
        cell.Style.Fill.PatternType = XLFillPatternValues.Solid;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#" + colorHex);
    }

    private static void SetBorder(IXLBorder border, XLBorderStyleValues style, string colorHex)
    {
        var color = XLColor.FromHtml("#" + colorHex);
        border.LeftBorder = style;
        border.LeftBorderColor = color;
        border.RightBorder = style;
        border.RightBorderColor = color;
        border.TopBorder = style;
        border.TopBorderColor = color;
        border.BottomBorder = style;
        border.BottomBorderColor = color;
    }
}
