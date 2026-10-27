using ClosedXML.Excel;
using GradeWeave.Core.Models;

namespace GradeWeave.Core.Export;

/// <summary>冲突报告：未排课程、教师冲突、班级冲突与教研/会议占用。</summary>
public sealed class WeaveConflictReportExporter
{
    private static readonly XLColor Paper = XLColor.FromHtml("#FAF9F5");
    private static readonly XLColor Ink = XLColor.FromHtml("#141413");
    private static readonly XLColor Clay = XLColor.FromHtml("#D97757");
    private static readonly XLColor Muted = XLColor.FromHtml("#5F5A4E");
    private static readonly XLColor Line = XLColor.FromHtml("#D8D5CC");

    public void Export(WeaveProject project, IReadOnlyList<WeaveConflict> conflicts, string path)
    {
        try
        {
            WeaveExportSupport.Prepare(project, path);
            var items = conflicts ?? Array.Empty<WeaveConflict>();
            var unplacedCount = items.Count(c => c.Kind == ConflictKind.Unplaced);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("冲突报告");
            ws.Style.Font.FontName = "Microsoft YaHei";
            ws.Style.Font.FontSize = 10;
            ws.Style.Font.FontColor = Ink;

            var title = ws.Range(1, 1, 1, 5);
            title.Merge();
            title.Cell(1, 1).Value = "冲突报告";
            title.Cell(1, 1).Style.Font.Bold = true;
            title.Cell(1, 1).Style.Font.FontSize = 16;
            title.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            title.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(1).Height = 32;

            var meta = ws.Range(2, 1, 2, 5);
            meta.Merge();
            meta.Cell(1, 1).Value =
                $"{WeaveExportSupport.Heading(project, "冲突报告")} · 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm}";
            meta.Cell(1, 1).Style.Font.FontSize = 9.5;
            meta.Cell(1, 1).Style.Font.FontColor = Muted;
            meta.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(2).Height = 20;

            var stats = ws.Range(3, 1, 3, 5);
            stats.Merge();
            stats.Cell(1, 1).Value = $"冲突总数：{items.Count} 条 · 未排课总数：{unplacedCount} 条";
            stats.Cell(1, 1).Style.Font.Bold = true;
            stats.Cell(1, 1).Style.Font.FontColor = items.Count == 0 ? Muted : Clay;
            stats.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Row(3).Height = 20;

            ws.Cell(4, 1).Value = "类型";
            ws.Cell(4, 2).Value = "位置";
            ws.Cell(4, 3).Value = "班级";
            ws.Cell(4, 4).Value = "教师";
            ws.Cell(4, 5).Value = "说明";

            var header = ws.Range(4, 1, 4, 5);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = Paper;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            header.Style.Alignment.WrapText = true;
            header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            header.Style.Border.OutsideBorderColor = Line;
            header.Style.Border.InsideBorderColor = Line;
            ws.Row(4).Height = 22;

            if (items.Count == 0)
            {
                var empty = ws.Range(5, 1, 5, 5);
                empty.Merge();
                empty.Cell(1, 1).Value = "未发现冲突";
                empty.Cell(1, 1).Style.Font.Bold = true;
                empty.Cell(1, 1).Style.Font.FontColor = Clay;
                empty.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                empty.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Row(5).Height = 28;
            }
            else
            {
                var row = 5;
                foreach (var conflict in items)
                {
                    ws.Cell(row, 1).Value = WeaveExportSupport.ConflictKindText(conflict.Kind);
                    ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(conflict.Location) ? "—" : conflict.Location;
                    ws.Cell(row, 3).Value = ClassName(project, conflict.ClassId);
                    ws.Cell(row, 4).Value = TeacherName(project, conflict.TeacherId);
                    ws.Cell(row, 5).Value = string.IsNullOrWhiteSpace(conflict.Message) ? "—" : conflict.Message;
                    ws.Row(row).Height = 26;
                    row++;
                }

                var body = ws.Range(4, 1, row - 1, 5);
                body.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                body.Style.Alignment.WrapText = true;
                body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                body.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                body.Style.Border.OutsideBorderColor = Line;
                body.Style.Border.InsideBorderColor = Line;
            }

            ws.Column(1).Width = 16;
            ws.Column(2).Width = 18;
            ws.Column(3).Width = 12;
            ws.Column(4).Width = 12;
            ws.Column(5).Width = 56;
            ws.SheetView.FreezeRows(4);
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;

            workbook.SaveAs(path);
        }
        catch (GradeWeaveException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new GradeWeaveException("导出冲突报告失败，请检查保存位置后重试。", ex);
        }
    }

    private static string ClassName(WeaveProject project, string? classId) =>
        string.IsNullOrWhiteSpace(classId) ? "—" : project.FindClass(classId)?.Name ?? "—";

    private static string TeacherName(WeaveProject project, string? teacherId) =>
        string.IsNullOrWhiteSpace(teacherId) ? "—" : project.FindTeacher(teacherId)?.Name ?? "—";
}
