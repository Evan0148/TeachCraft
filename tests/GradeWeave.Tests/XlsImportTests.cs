using GradeWeave.Core;
using GradeWeave.Core.Import;
using GradeWeave.Core.Models;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;

namespace GradeWeave.Tests;

/// <summary>.xls 旧格式导入回归：用 NPOI 生成合成 .xls，走与 .xlsx 相同的解析管线。</summary>
public sealed class XlsImportTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GradeWeave.Tests", Guid.NewGuid().ToString("N"));

    public XlsImportTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void XlsWorkbook_ImportsStaffingSheet()
    {
        var path = Path.Combine(_directory, "示例中学九年级分工表.xls");
        WriteStaffingXls(path);

        var result = new WeaveWorkbookImporter().Import(path);

        var project = result.Project;
        Assert.Equal(3, project.Classes.Count);
        Assert.Contains(project.Classes, item => item.Name == "九（1）班" && item.HeadTeacher == "沈青禾");

        var chinese = project.FindSubjectByName("语文");
        Assert.NotNull(chinese);
        Assert.Equal(6, chinese!.WeeklyHours);

        Assert.Contains(project.Teachers, item => item.Name == "沈青禾");
        Assert.Contains(project.Assignments, item =>
            item.ClassId == project.Classes[0].Id && item.SubjectId == chinese.Id);
    }

    [Fact]
    public void XlsWorkbook_RejectsOtherExtensions()
    {
        var path = Path.Combine(_directory, "not-a-book.csv");
        File.WriteAllText(path, "班级,班主任\n九（1）班,沈青禾\n");

        var error = Assert.Throws<GradeWeaveException>(() => new WeaveWorkbookImporter().Import(path));
        Assert.Contains("xlsx", error.Message);
    }

    private static void WriteStaffingXls(string path)
    {
        var workbook = new HSSFWorkbook();

        var sheet = workbook.CreateSheet("分工表");

        var title = sheet.CreateRow(0).CreateCell(0);
        title.SetCellValue("示例中学 九年级 2026 年秋季学期 年级课表教师分工表");
        sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 5));

        string[] headers = ["班级", "班主任", "语文", "数学", "英语", "物理"];
        var headerRow = sheet.CreateRow(1);
        for (var i = 0; i < headers.Length; i++)
            headerRow.CreateCell(i).SetCellValue(headers[i]);

        (string Class, string Head, string Chinese, string Math)[] rows =
        [
            ("九（1）班", "沈青禾", "沈青禾", "顾知远"),
            ("九（2）班", "顾知远", "沈青禾", "顾知远"),
            ("九（3）班", "程叙白", "陆明川", "苏映棠"),
        ];

        var rowIndex = 2;
        foreach (var (className, head, chinese, math) in rows)
        {
            var row = sheet.CreateRow(rowIndex++);
            row.CreateCell(0).SetCellValue(className);
            row.CreateCell(1).SetCellValue(head);
            row.CreateCell(2).SetCellValue(chinese);
            row.CreateCell(3).SetCellValue(math);

            var hoursRow = sheet.CreateRow(rowIndex++);
            hoursRow.CreateCell(0).SetCellValue("课时-37节");
            hoursRow.CreateCell(2).SetCellValue(6);
            hoursRow.CreateCell(3).SetCellValue(6);
            hoursRow.CreateCell(4).SetCellValue(6);
            hoursRow.CreateCell(5).SetCellValue(4);

            var countRow = sheet.CreateRow(rowIndex++);
            countRow.CreateCell(0).SetCellValue("人数-9人");
        }

        using var stream = File.Create(path);
        workbook.Write(stream);
    }
}
