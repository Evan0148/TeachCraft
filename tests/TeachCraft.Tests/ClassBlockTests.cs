using ClosedXML.Excel;
using ClassBlock.Core.Import;
using ClassBlock.Core.Models;
using ClassBlock.Core.Rendering;
using ClassBlock.Core.Timetable;
using SkiaSharp;

namespace TeachCraft.Tests;

public sealed class ClassBlockTests : IDisposable
{
    private static readonly string[] DayHeaders = { "星期一", "星期二", "星期三", "星期四", "星期五" };

    private readonly string _root;

    public ClassBlockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TeachCraft.Tests", "ClassBlock", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void MasterImporter_ReadsClassesServicesAndHeadTeachers()
    {
        var path = CreateMasterWorkbook("master.xlsx");

        var master = MasterScheduleImporter.Import(path);

        Assert.Equal("九年级", master.Info.Grade);
        Assert.Equal("2026年春季学期", master.Info.Term);
        Assert.Equal("2026年5月", master.Info.Month);
        Assert.Equal(2, master.Classes.Count);
        Assert.Equal("九（1）班", master.Classes[0].Name);
        Assert.Equal("九1", master.Classes[0].Key);

        var first = master.Classes[0];
        Assert.Equal(5, first.Days.Count);
        Assert.Equal("语文", first.Days[0].Periods[0].Subject);
        Assert.Equal("张兰", first.Days[0].Periods[0].Teacher);
        Assert.Equal("培优辅差", first.Days[0].AfterSchool1.Subject);
        Assert.Equal("张兰", first.Days[0].EveningStudy);

        Assert.Equal("王兰", master.HeadTeachers["九1"]);
        Assert.Equal(new[] { "周兰", "吴梅", "郑兰", "王梅", "周兰" }, master.LunchStudy["九1"]);
        Assert.Equal("赵兰", master.AfterSchool["九1"][0].Service1);
        Assert.Equal("钱红", master.AfterSchool["九1"][0].Service2);
        Assert.Equal("语文", master.EarlyReading[1]);
        Assert.Equal("英语", master.EarlyReading[2]);
        Assert.Equal("升旗", master.EarlyReading[0]);
    }

    [Fact]
    public void TimetableBuilder_FollowsReferenceRowOrder()
    {
        var master = MasterScheduleImporter.Import(CreateMasterWorkbook("master.xlsx"));
        var timetable = TimetableBuilder.Build(master, "九1", new TimetableSettings { ShowTeachers = true });

        Assert.Equal("九（1）班课程表", timetable.Title);
        Assert.Contains("2026年春季学期", timetable.Subtitle);
        Assert.Contains("班主任 王兰", timetable.Subtitle);

        var labels = timetable.Rows.Select(r => r.Label).ToArray();
        Assert.Equal(
            new[] { "早读", "1", "2", "", "3", "4", "", "午学", "5", "6", "7", "课服1", "课服2", "", "", "晚修" },
            labels);

        var lunch = timetable.Rows.Single(r => r.Kind == TimetableRowKind.Lunch);
        Assert.True(lunch.IsFullWidth);
        Assert.Contains("午餐", lunch.MergedText);

        var lunchStudy = timetable.Rows.Single(r => r.Label == "午学");
        Assert.Equal("周兰老师", lunchStudy.Cells[0]);

        var service = timetable.Rows.Single(r => r.Label == "课服1");
        Assert.Equal("培优辅差\n赵兰", service.Cells[0]);

        var evening = timetable.Rows.Single(r => r.Kind == TimetableRowKind.Evening);
        Assert.Equal(2, evening.Times.Count);
        Assert.Equal("张兰老师", evening.Cells[0]);

        var noTeachers = TimetableBuilder.Build(master, "九1", new TimetableSettings());
        Assert.Equal("语文", noTeachers.Rows.Single(r => r.Label == "1").Cells[0]);
    }

    [Fact]
    public void ClassBlock_RendersPngAndPdf()
    {
        var master = MasterScheduleImporter.Import(CreateMasterWorkbook("master.xlsx"));
        var timetable = TimetableBuilder.Build(master, "九1", new TimetableSettings());

        var png = Path.Combine(_root, "timetable.png");
        TimetableRenderer.RenderPng(timetable, png, 2.0f);

        Assert.True(File.Exists(png));
        Assert.True(new FileInfo(png).Length > 10_000);

        using (var bitmap = SKBitmap.Decode(png))
        {
            Assert.NotNull(bitmap);
            Assert.True(bitmap.Width >= 900);
            Assert.True(bitmap.Height > bitmap.Width);
        }

        var pdf = Path.Combine(_root, "timetable.pdf");
        TimetableRenderer.RenderPdf(timetable, pdf);

        Assert.True(File.Exists(pdf));
        Assert.True(new FileInfo(pdf).Length > 10_000);
        Assert.Equal("%PDF", File.ReadAllText(pdf)[..4]);
    }

    // ----------------------------- 合成总表 -----------------------------

    /// <summary>按真实总表的骨架造一份小号工作簿：课程表 + 分工表 + 午学 + 课后服务。</summary>
    private string CreateMasterWorkbook(string fileName)
    {
        var path = Path.Combine(_root, fileName);

        using var workbook = new XLWorkbook();
        BuildScheduleSheet(workbook);
        BuildHeadTeacherSheet(workbook);
        BuildLunchStudySheet(workbook);
        BuildAfterSchoolSheet(workbook);
        workbook.SaveAs(path);

        return path;
    }

    private static void BuildScheduleSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("2-课程表");

        sheet.Cell(1, 1).Value = "表二：示例中学2026年春季学期九年级教师课程表（时间：2026年5月）";

        var starts = new[] { 2, 12, 22, 32, 42 };
        for (var day = 0; day < 5; day++)
        {
            var start = starts[day];
            sheet.Range(sheet.Cell(2, start), sheet.Cell(2, start + 9)).Merge();
            sheet.Cell(2, start).Value = DayHeaders[day];

            for (var period = 0; period < 7; period++)
                sheet.Cell(4, start + period).Value = period + 1;

            sheet.Range(sheet.Cell(4, start + 7), sheet.Cell(4, start + 8)).Merge();
            sheet.Cell(4, start + 7).Value = "课后服务";
        }

        sheet.Cell(5, 1).Value = "九（1）班";
        sheet.Cell(6, 1).Value = "九（2）班";

        string[] subjects = { "语文张兰", "数学李梅", "英语王芳", "物理赵强", "化学陈静", "历史周敏", "体育吴刚" };
        for (var day = 0; day < 5; day++)
        {
            var start = starts[day];
            for (var period = 0; period < 7; period++)
            {
                sheet.Cell(5, start + period).Value = subjects[period];
                sheet.Cell(6, start + period).Value = subjects[(period + 1) % 7];
            }

            sheet.Cell(5, start + 9).Value = "张兰";
            sheet.Cell(6, start + 9).Value = "李梅";

            sheet.Cell(5, start + 8).Value = "阳光体育";
            sheet.Cell(6, start + 8).Value = "自习";
        }

        sheet.Range("I5:I6").Merge();
        sheet.Cell(5, 9).Value = "培优辅差";

        sheet.Cell(8, 1).Value = "备注：早读（二、四语文，三、五英语），周一升旗。";
    }

    private static void BuildHeadTeacherSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("1-分工表");
        sheet.Cell(2, 1).Value = "班级";
        sheet.Cell(2, 2).Value = "班主任";
        sheet.Cell(3, 1).Value = "九（1）班";
        sheet.Cell(3, 2).Value = "王兰";
        sheet.Cell(4, 1).Value = "九（2）班";
        sheet.Cell(4, 2).Value = "李梅";
    }

    private static void BuildLunchStudySheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("4-午学");
        sheet.Cell(2, 1).Value = "班级";
        string[] values = { "周兰", "吴梅", "郑兰", "王梅", "周兰" };

        for (var day = 0; day < 5; day++)
            sheet.Cell(2, day + 2).Value = $"{DayHeaders[day]} 12:00—12:30";

        sheet.Cell(3, 1).Value = "九（1）班";
        sheet.Cell(4, 1).Value = "九（2）班";
        for (var day = 0; day < 5; day++)
        {
            sheet.Cell(3, day + 2).Value = values[day];
            sheet.Cell(4, day + 2).Value = values[(day + 1) % 5];
        }
    }

    private static void BuildAfterSchoolSheet(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add("5-课后服务");

        var starts = new[] { 2, 5, 8, 11, 14 };
        for (var day = 0; day < 5; day++)
        {
            var start = starts[day];
            sheet.Range(sheet.Cell(2, start), sheet.Cell(2, start + 2)).Merge();
            sheet.Cell(2, start).Value = DayHeaders[day];
            sheet.Cell(3, start).Value = "课后服务1";
            sheet.Cell(3, start + 1).Value = "课后服务2";
            sheet.Cell(3, start + 2).Value = "晚修";
        }

        sheet.Cell(4, 1).Value = "班级";
        sheet.Cell(5, 1).Value = "九（1）班";
        sheet.Cell(6, 1).Value = "九（2）班";

        for (var day = 0; day < 5; day++)
        {
            var start = starts[day];
            sheet.Cell(5, start).Value = "赵兰";
            sheet.Cell(5, start + 1).Value = "钱红";
            sheet.Cell(5, start + 2).Value = "孙丽";
            sheet.Cell(6, start).Value = "李梅";
            sheet.Cell(6, start + 1).Value = "周敏";
            sheet.Cell(6, start + 2).Value = "吴刚";
        }
    }
}
