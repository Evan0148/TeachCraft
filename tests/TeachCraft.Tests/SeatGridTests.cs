using ClosedXML.Excel;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using SeatGrid.Core.Export;
using SeatGrid.Core.Models;
using SeatGrid.Core.Rendering;
using SeatGrid.Core.Roster;
using SeatGrid.Core.Seating;

namespace TeachCraft.Tests;

public sealed class SeatGridTests : IDisposable
{
    private readonly string _root;

    public SeatGridTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TeachCraft.Tests", "SeatGrid", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void RosterImporter_ReadsChineseHeadersAndFields()
    {
        var path = Path.Combine(_root, "roster.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("六班");
            string[] headers = { "姓名", "性别", "学号", "身高", "视力", "总分", "备注" };
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "陈思远"; sheet.Cell(2, 2).Value = "男"; sheet.Cell(2, 3).Value = "2024001";
            sheet.Cell(2, 4).Value = 175; sheet.Cell(2, 5).Value = 4.8; sheet.Cell(2, 6).Value = 560; sheet.Cell(2, 7).Value = "近视";
            sheet.Cell(3, 1).Value = "李雨桐"; sheet.Cell(3, 2).Value = "女生"; sheet.Cell(3, 3).Value = "2024002";
            sheet.Cell(3, 4).Value = "162cm"; sheet.Cell(3, 5).Value = "5.0/4.9"; sheet.Cell(3, 6).Value = 548;
            workbook.SaveAs(path);
        }

        var result = RosterImporter.Import(path);

        Assert.Equal(2, result.Students.Count);
        Assert.Equal("陈思远", result.Students[0].Name);
        Assert.Equal(Gender.Male, result.Students[0].Gender);
        Assert.Equal("2024001", result.Students[0].Number);
        Assert.Equal(175, result.Students[0].Height);
        Assert.Equal(4.8, result.Students[0].Vision);
        Assert.Equal(Gender.Female, result.Students[1].Gender);
        Assert.Equal(162, result.Students[1].Height);
        Assert.Equal(4.9, result.Students[1].Vision);
    }

    [Fact]
    public void RosterImporter_DetectsHeaderlessTwoColumnRoster()
    {
        var path = Path.Combine(_root, "no-header.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Sheet1");
            sheet.Cell(1, 1).Value = "张三"; sheet.Cell(1, 2).Value = "男"; sheet.Cell(1, 3).Value = "2024001";
            sheet.Cell(2, 1).Value = "李四"; sheet.Cell(2, 2).Value = "女"; sheet.Cell(2, 3).Value = "2024002";
            sheet.Cell(3, 1).Value = "王五"; sheet.Cell(3, 2).Value = "男"; sheet.Cell(3, 3).Value = "2024003";
            workbook.SaveAs(path);
        }

        var result = RosterImporter.Import(path);

        Assert.Equal(3, result.Students.Count);
        Assert.Equal(new[] { "张三", "李四", "王五" }, result.Students.Select(s => s.Name));
        Assert.Equal(Gender.Male, result.Students[0].Gender);
        Assert.Equal(Gender.Female, result.Students[1].Gender);
    }

    [Fact]
    public void SeatingEngine_HeightPutsShortestStudentsInFrontRow()
    {
        var students = new List<Student>
        {
            new() { Name = "高个", Height = 190 },
            new() { Name = "最矮", Height = 150 },
            new() { Name = "中等", Height = 170 },
            new() { Name = "较矮", Height = 160 },
        };

        var layout = new SeatChartLayout(1, 2, 2); // 前排在 Row=0
        var seats = SeatingEngine.Arrange(students, layout, ArrangeMode.Height);

        Assert.Equal("最矮", seats[new SeatPosition(0, 0, 0)].Name);
        Assert.Equal("较矮", seats[new SeatPosition(0, 0, 1)].Name);
        Assert.Equal("中等", seats[new SeatPosition(0, 1, 0)].Name);
        Assert.Equal("高个", seats[new SeatPosition(0, 1, 1)].Name);
    }

    [Fact]
    public void SeatingEngine_GenderAlternateProducesAlternatingGenders()
    {
        var students = new List<Student>
        {
            new() { Name = "男1", Gender = Gender.Male },
            new() { Name = "女1", Gender = Gender.Female },
            new() { Name = "男2", Gender = Gender.Male },
            new() { Name = "女2", Gender = Gender.Female },
        };

        var layout = new SeatChartLayout(1, 2, 2);
        var seats = SeatingEngine.Arrange(students, layout, ArrangeMode.GenderAlternate);
        var ordered = layout.FillOrder().Select(p => seats[p].Gender).ToList();

        Assert.Equal(new[] { Gender.Male, Gender.Female, Gender.Male, Gender.Female }, ordered);
    }

    [Fact]
    public void Config_RoundTripsSettingsStudentsAndSeats()
    {
        var settings = new SeatChartSettings
        {
            Title = "九年级六班座位表",
            HeadTeacher = "王老师",
            Monitor = "陈思远",
            Groups = 2,
            Rows = 2,
            Columns = 2,
            CellScalePercent = 120,
            SeatBackground = "#FFF2CC",
            MarkFemales = false,
        };

        var students = new List<Student>
        {
            new() { Name = "陈思远", Gender = Gender.Male, Number = "1", Height = 175 },
            new() { Name = "李雨桐", Gender = Gender.Female, Number = "2", Vision = 4.8 },
            new() { Name = "王浩然", Gender = Gender.Male, Number = "3" },
        };

        var assignments = new Dictionary<SeatPosition, Student>
        {
            [new SeatPosition(0, 0, 0)] = students[0],
            [new SeatPosition(0, 0, 1)] = students[1],
        };

        var path = Path.Combine(_root, "config.xlsx");
        SeatGridConfigIO.Export(path, settings, students, assignments);
        var imported = SeatGridConfigIO.Import(path);

        Assert.Equal("九年级六班座位表", imported.Settings.Title);
        Assert.Equal("王老师", imported.Settings.HeadTeacher);
        Assert.Equal(2, imported.Settings.Groups);
        Assert.Equal(120, imported.Settings.CellScalePercent);
        Assert.Equal("#FFF2CC", imported.Settings.SeatBackground);
        Assert.False(imported.Settings.MarkFemales);
        Assert.Equal(3, imported.Students.Count);
        Assert.Equal(2, imported.Assignments.Count);
        Assert.Equal("李雨桐", imported.Assignments[new SeatPosition(0, 0, 1)].Name);
    }

    [Fact]
    public void Composer_StudentViewReversesGroupsAndRows()
    {
        var settings = new SeatChartSettings
        {
            Groups = 2,
            Rows = 2,
            Columns = 1,
            View = ViewMode.Teacher,
        };
        var empty = new Dictionary<SeatPosition, Student>();

        var teacher = SeatChartComposer.Compose(settings, empty, 0);
        Assert.False(teacher.PodiumOnTop);
        Assert.Equal(new[] { 1, 2 }, teacher.Groups.Select(g => g.Number));
        Assert.Equal(new[] { 2, 1 }, teacher.Groups[0].Rows.Select(r => r.Number));

        settings.View = ViewMode.Student;
        var student = SeatChartComposer.Compose(settings, empty, 0);
        Assert.True(student.PodiumOnTop);
        Assert.Equal(new[] { 2, 1 }, student.Groups.Select(g => g.Number));
        Assert.Equal(new[] { 1, 2 }, student.Groups[0].Rows.Select(r => r.Number));
    }

    [Fact]
    public void Exports_ProducePngPdfAndExcel()
    {
        var settings = new SeatChartSettings
        {
            Title = "九年级六班座位表",
            Groups = 4,
            Rows = 2,
            Columns = 2,
        };
        var students = Enumerable.Range(0, 8).Select(i => new Student
        {
            Name = $"同学{i + 1}",
            Gender = i % 2 == 0 ? Gender.Male : Gender.Female,
        }).ToList();
        var assignments = SeatingEngine.Arrange(students, settings.GetLayout(), ArrangeMode.RosterOrder);
        var document = SeatChartComposer.Compose(settings, assignments, students.Count);

        var pngPath = Path.Combine(_root, "seat.png");
        SeatChartRenderer.RenderPng(document, pngPath);
        using (var codec = SKCodec.Create(pngPath))
        {
            Assert.NotNull(codec);
            Assert.True(codec!.Info.Width > 300);
        }

        var pdfPath = Path.Combine(_root, "seat.pdf");
        SeatChartRenderer.RenderPdf(document, pdfPath);
        Assert.True(new FileInfo(pdfPath).Length > 1024);
        Assert.Equal("%PDF", File.ReadAllText(pdfPath, System.Text.Encoding.ASCII)[..4]);
        Assert.True(new FileInfo(pdfPath).Length < 5 * 1024 * 1024);

        var excelPath = Path.Combine(_root, "seat.xlsx");
        SeatChartExcelExporter.ExportSeatChart(document, assignments, students, excelPath);
        using var workbook = new XLWorkbook(excelPath);
        Assert.True(workbook.Worksheets.Contains("座位表"));
        Assert.True(workbook.Worksheets.Contains("名单"));
        var sheet = workbook.Worksheet("座位表");
        Assert.Contains("同学1", sheet.CellsUsed().Select(c => c.GetFormattedString()));
    }
}
