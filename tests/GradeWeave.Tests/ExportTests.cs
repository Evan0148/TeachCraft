using System.Text;
using ClosedXML.Excel;
using GradeWeave.Core;
using GradeWeave.Core.Export;
using GradeWeave.Core.Models;
using SkiaSharp;

namespace GradeWeave.Tests;

/// <summary>导出层回归：只用 WeaveSamples 的合成演示数据，不触碰任何真实姓名或学校。</summary>
public sealed class ExportTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GradeWeave.Tests", Guid.NewGuid().ToString("N"));

    public ExportTests() => Directory.CreateDirectory(_directory);

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
    public void ExcelExport_WritesExpectedSheets()
    {
        var project = CreateProject();
        var path = Path.Combine(_directory, "weave.xlsx");

        new WeaveExcelExporter().Export(project, [], path);

        Assert.True(File.Exists(path));
        using var workbook = new XLWorkbook(path);
        var sheetNames = workbook.Worksheets.Select(w => w.Name).ToList();

        Assert.Contains("总表", sheetNames);
        Assert.Contains("分工表", sheetNames);
        Assert.Contains("班级课表", sheetNames);
        Assert.Contains("教师课表", sheetNames);
        Assert.Contains("冲突", sheetNames);
        Assert.Contains("九（1）班", sheetNames);
        Assert.Contains("沈青禾", sheetNames);

        var masterText = SheetText(workbook.Worksheet("总表"));
        Assert.Contains("九（1）班", masterText);
        Assert.Contains("语文", masterText);
        Assert.Contains("沈青禾", masterText);

        var classText = SheetText(workbook.Worksheet("九（1）班"));
        Assert.Contains("英语 A/B", classText);

        Assert.Contains("未发现冲突", SheetText(workbook.Worksheet("冲突")));
    }

    [Fact]
    public void ExcelExport_ShowsOverlayActivityInClassAndMasterSheets()
    {
        var project = CreateProject();
        project.Activities.Add(new ActivityBlock
        {
            Name = "书法课",
            DayIndex = 1,
            StartTime = "10:50",
            EndTime = "11:10",
            Mode = ActivityMode.Overlay,
        });

        var path = Path.Combine(_directory, "activity.xlsx");
        new WeaveExcelExporter().Export(project, [], path);

        using var workbook = new XLWorkbook(path);
        var classText = SheetText(workbook.Worksheet("九（1）班"));
        Assert.Contains("书法课", classText);
        Assert.Contains("10:50—11:10", classText);

        var masterText = SheetText(workbook.Worksheet("总表"));
        Assert.Contains("书法课", masterText);
    }

    [Fact]
    public void ExcelExport_HonoursOnlyClassOption()
    {
        var project = CreateProject();
        var path = Path.Combine(_directory, "one-class.xlsx");

        new WeaveExcelExporter().Export(project, [], path, new ExportOptions { OnlyClassId = "c2" });

        using var workbook = new XLWorkbook(path);
        var sheetNames = workbook.Worksheets.Select(w => w.Name).ToList();
        Assert.Contains("九（2）班", sheetNames);
        Assert.DoesNotContain("九（1）班", sheetNames);
    }

    [Fact]
    public void PngRenderers_ProduceDecodableImages()
    {
        var project = CreateProject();
        var renderer = new WeaveImageRenderer();

        var classPng = renderer.RenderClassPng(project, "c1");
        var teacherPng = renderer.RenderTeacherPng(project, "t1");

        foreach (var bytes in new[] { classPng, teacherPng })
        {
            Assert.NotEmpty(bytes);
            using var bitmap = SKBitmap.Decode(bytes);
            Assert.NotNull(bitmap);
            Assert.True(bitmap!.Width > 0);
            Assert.True(bitmap.Height > 0);
        }
    }

    [Fact]
    public void PdfExport_WritesPdfFiles()
    {
        var project = CreateProject();
        var exporter = new WeavePdfExporter();

        var classPath = Path.Combine(_directory, "class.pdf");
        exporter.ExportClassPdf(project, "c1", classPath);

        var teacherPath = Path.Combine(_directory, "teacher.pdf");
        exporter.ExportTeacherPdf(project, "t1", teacherPath);

        foreach (var path in new[] { classPath, teacherPath })
        {
            Assert.True(File.Exists(path));
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 4);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        }
    }

    [Fact]
    public void ConflictReport_WritesHeaderAndRows()
    {
        var project = CreateProject();
        var path = Path.Combine(_directory, "conflicts.xlsx");
        var conflicts = new List<WeaveConflict>
        {
            new()
            {
                Kind = ConflictKind.TeacherClash,
                Message = "沈青禾 星期一第1节 同时出现在两个班",
                ClassId = "c1",
                TeacherId = "t1",
                DayIndex = 1,
                Period = 1,
            },
            new()
            {
                Kind = ConflictKind.Unplaced,
                Message = "九（4）班 劳动 尚缺 1 节",
                ClassId = "c4",
                TeacherId = "t9",
            },
        };

        new WeaveConflictReportExporter().Export(project, conflicts, path);

        using var workbook = new XLWorkbook(path);
        var text = SheetText(workbook.Worksheet(1));

        Assert.Contains("类型", text);
        Assert.Contains("位置", text);
        Assert.Contains("冲突总数", text);
        Assert.Contains("未排课总数", text);
        Assert.Contains("教师冲突", text);
        Assert.Contains("沈青禾", text);
        Assert.Contains("星期一第1节", text);
    }

    [Fact]
    public void ExportToSourcePath_Throws()
    {
        var project = CreateProject();

        var excelPath = Path.Combine(_directory, "source.xlsx");
        File.WriteAllText(excelPath, "placeholder");
        project.SourceFile = excelPath;
        var excelError = Assert.Throws<GradeWeaveException>(
            () => new WeaveExcelExporter().Export(project, [], excelPath));
        Assert.Contains("覆盖", excelError.Message);

        var pdfPath = Path.Combine(_directory, "source.pdf");
        project.SourceFile = pdfPath;
        Assert.Throws<GradeWeaveException>(() => new WeavePdfExporter().ExportClassPdf(project, "c1", pdfPath));

        Assert.Throws<GradeWeaveException>(
            () => new WeaveConflictReportExporter().Export(project, [], pdfPath));
    }

    [Fact]
    public void ExcelExport_WritesTeacherPositionSheet()
    {
        var project = CreateProject();
        var path = Path.Combine(_directory, "positions.xlsx");

        new WeaveExcelExporter().Export(project, [], path);

        using var workbook = new XLWorkbook(path);
        Assert.Contains("教师职位表", workbook.Worksheets.Select(w => w.Name));

        var text = SheetText(workbook.Worksheet("教师职位表"));
        Assert.Contains("兼职岗位", text);
        Assert.Contains("任教班级", text);
        Assert.Contains("学科周课时", text);
        Assert.Contains("班主任", text);
        Assert.Contains("班", text);
    }

    [Fact]
    public void ExcelExport_HonoursSheetToggles()
    {
        var project = CreateProject();
        var path = Path.Combine(_directory, "conflict-only.xlsx");

        new WeaveExcelExporter().Export(project, [], path, new ExportOptions
        {
            IncludeMasterSheet = false,
            IncludeAssignmentSheet = false,
            IncludeTeacherPositionSheet = false,
            IncludeClassSheets = false,
            IncludeTeacherSheets = false,
            IncludeConflictSheet = true,
        });

        using var workbook = new XLWorkbook(path);
        var sheetNames = workbook.Worksheets.Select(w => w.Name).ToList();
        Assert.Single(sheetNames);
        Assert.Equal("冲突", sheetNames[0]);
    }

    [Fact]
    public void ExcelExport_ReferenceStyle_WritesReferenceLayout()
    {
        var project = CreateProject();
        var path = Path.Combine(_directory, "shenhe.xlsx");

        new WeaveExcelExporter().Export(project, [], path, new ExportOptions
        {
            Style = WeaveExportStyle.Reference,
        });

        using var workbook = new XLWorkbook(path);
        var sheetNames = workbook.Worksheets.Select(w => w.Name).ToList();
        Assert.Contains("1-分工表", sheetNames);
        Assert.Contains("2-课程表", sheetNames);
        Assert.Contains("3-教师职位表", sheetNames);
        Assert.Contains("班级课表", sheetNames);
        Assert.Contains("教师课表", sheetNames);
        Assert.Contains("冲突", sheetNames);

        var master = workbook.Worksheet("2-课程表");
        var masterText = SheetText(master);
        Assert.Contains("表二", masterText);
        Assert.Contains("星期一", masterText);
        Assert.Contains("上午", masterText);
        Assert.Contains("下午", masterText);
        Assert.Contains("晚修", masterText);
        Assert.Contains("课后服务", masterText);
        Assert.Contains("语文", masterText);
        Assert.Contains("青禾", masterText);
        Assert.Contains("班主任会议", masterText);
        Assert.Equal("楷体", master.Cell("A1").Style.Font.FontName);
        Assert.Equal("宋体", master.Cell("A5").Style.Font.FontName);
        Assert.True(master.MergedRanges.Count > 20);

        var assignmentText = SheetText(workbook.Worksheet("1-分工表"));
        Assert.Contains("表一", assignmentText);
        Assert.Contains("课时-", assignmentText);
        Assert.Contains("人数-", assignmentText);
        Assert.Contains("备课组长", assignmentText);

        var positionText = SheetText(workbook.Worksheet("3-教师职位表"));
        Assert.Contains("表三", positionText);
        Assert.Contains("兼职岗位", positionText);
        Assert.Contains("科组长", positionText);
    }

    [Fact]
    public void ExcelExport_ReferenceStyle_HonoursTeacherPositionToggle()
    {
        var project = CreateProject();
        var path = Path.Combine(_directory, "shenhe-no-position.xlsx");

        new WeaveExcelExporter().Export(project, [], path, new ExportOptions
        {
            Style = WeaveExportStyle.Reference,
            IncludeTeacherPositionSheet = false,
            IncludeClassSheets = false,
            IncludeTeacherSheets = false,
            IncludeConflictSheet = false,
        });

        using var workbook = new XLWorkbook(path);
        var sheetNames = workbook.Worksheets.Select(w => w.Name).ToList();
        Assert.Contains("1-分工表", sheetNames);
        Assert.Contains("2-课程表", sheetNames);
        Assert.DoesNotContain("3-教师职位表", sheetNames);
    }

    [Fact]
    public void ExcelExport_WritesMeetingNotesRow()
    {
        var project = CreateProject();
        var path = Path.Combine(_directory, "notes.xlsx");

        new WeaveExcelExporter().Export(project, [], path);

        using var workbook = new XLWorkbook(path);
        var masterText = SheetText(workbook.Worksheet("总表"));
        Assert.Contains("会议活动备注", masterText);
        Assert.Contains("班主任会议", masterText);
    }

    private static string SheetText(IXLWorksheet worksheet) =>
        string.Join("\n", worksheet.CellsUsed().Select(cell => cell.GetString()));

    private static WeaveProject CreateProject()
    {
        var project = WeaveSamples.CreateDemo(4);
        var layeredGroup = project.LayeredGroups[0];

        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t1",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 1, Period = 2, SubjectId = "s2", TeacherId = "t2",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 2, Period = 3, SubjectId = "s4", TeacherId = "t4",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c1", DayIndex = 4, Period = 1, SubjectId = "s3", TeacherId = "t3",
            GroupId = layeredGroup.Id, LayerName = "A层", IsLayered = true,
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c2", DayIndex = 1, Period = 1, SubjectId = "s1", TeacherId = "t2",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c2", DayIndex = 1, Period = 2, SubjectId = "s2", TeacherId = "t3",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c3", DayIndex = 3, Period = 4, SubjectId = "s5", TeacherId = "t5",
        });
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = "c4", DayIndex = 5, Period = 7, SubjectId = "s9", TeacherId = "t9",
        });

        return project;
    }
}
