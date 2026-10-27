using ClassBlock.Core.Import;
using ClassBlock.Core.Models;
using ClassBlock.Core.Rendering;
using ClassBlock.Core.Timetable;
using SkiaSharp;

namespace TeachCraft.Tests;

public sealed class ClassBlockTests : IDisposable
{
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
        var path = ClassBlockFixtures.CreateMasterWorkbook(_root, "master.xlsx");

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
        var master = MasterScheduleImporter.Import(ClassBlockFixtures.CreateMasterWorkbook(_root, "master.xlsx"));
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
        var master = MasterScheduleImporter.Import(ClassBlockFixtures.CreateMasterWorkbook(_root, "master.xlsx"));
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
}
