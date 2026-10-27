using ClosedXML.Excel;
using MeetRoster.Core.Export;
using MeetRoster.Core.Import;

namespace TeachCraft.Tests;

public sealed class MeetRosterImportTests : IDisposable
{
    private readonly string _root;

    public MeetRosterImportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetRoster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* 清理失败不影响测试结论 */ }
    }

    [Fact]
    public void RegistrationImporter_ReadsTemplateAndValidatesRules()
    {
        var source = Path.Combine(_root, "七年级1班运动会报名表.xlsx");
        CreateRegistrationWorkbook(source);

        var result = RegistrationWorkbookImporter.Import(source);

        Assert.Equal("七年级1班", result.Class.Name);
        Assert.Equal("七年级", result.Class.Group);
        Assert.Equal(5, result.Class.Athletes.Count);
        Assert.Contains(result.Issues, i => i.IsError && i.Message.Contains("男子 100米", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, i =>
            i.Message.Contains("团体项目", StringComparison.Ordinal) &&
            i.Message.Contains("忽略", StringComparison.Ordinal));
        Assert.Equal("4x100米接力", result.Class.Athletes[0].TeamEvent1);
        Assert.Equal("20*50米男女混合接力", result.Class.Athletes[0].TeamEvent2);
        Assert.Equal("一分钟跳绳", result.Class.Athletes[0].TeamEvent3);
        Assert.All(result.Class.Athletes.Skip(1), a => Assert.Equal(string.Empty, a.TeamEvent2));
    }

    [Fact]
    public void RegistrationExporter_WritesPerClassFileNameAndData()
    {
        var source = Path.Combine(_root, "七年级1班运动会报名表.xlsx");
        CreateRegistrationWorkbook(source);
        var result = RegistrationWorkbookImporter.Import(source);
        var output = Path.Combine(_root, "导出");

        var target = RegistrationWorkbookExporter.Export(result.Class, output);

        Assert.Equal("七年级1班运动会报名表.xlsx", Path.GetFileName(target));
        Assert.True(File.Exists(target));

        using var workbook = new XLWorkbook(target);
        var sheet = workbook.Worksheet("导入模板");
        Assert.Equal("七年级1班", sheet.Cell(2, 2).GetString());
        Assert.Equal("苏晚晴", sheet.Cell(9, 1).GetString());
        Assert.Equal("4x100米接力", sheet.Cell(9, 3).GetString());
        Assert.Equal("20*50米男女混合接力", sheet.Cell(9, 4).GetString());
    }

    [Fact]
    public void RegistrationExporter_SaveWritesBackAndKeepsBackup()
    {
        var source = Path.Combine(_root, "七年级1班运动会报名表.xlsx");
        CreateRegistrationWorkbook(source);
        var imported = RegistrationWorkbookImporter.Import(source);

        imported.Class.Athletes[0].IndividualEvent1 = "400米";
        var saved = RegistrationWorkbookExporter.Save(imported.Class);

        Assert.Equal(source, saved);
        Assert.True(File.Exists(source + ".bak"));
        Assert.False(File.Exists(source + ".saving.xlsx"));

        var reloaded = RegistrationWorkbookImporter.Import(source);
        Assert.Equal("400米", reloaded.Class.Athletes[0].IndividualEvent1);
    }

    [Fact]
    public void ScheduleImporter_ReadsDaysSlotsAndGroupingEntries()
    {
        var source = Path.Combine(_root, "示例中学运动会赛程.xlsx");
        CreateScheduleWorkbook(source);

        var result = ScheduleWorkbookImporter.Import(source);

        Assert.Single(result.Days);
        Assert.Equal(3, result.Days[0].Slots.Count);
        Assert.Equal(15, result.Days[0].Slots.Sum(s => s.Entries.Count));
        Assert.Equal(6, result.ClassNames.Count);

        var female100 = result.Days[0].Slots.Single(s =>
            s.EventName == "100米" && s.Gender == "女子" && s.Stage == "预赛");
        Assert.Equal(6, female100.Entries.Count);
        Assert.Equal("七年级1班", female100.Entries[0].ClassName);
        Assert.Equal("1", female100.Entries[0].Group);
        Assert.Contains("第 1 道", female100.Entries[0].Lane, StringComparison.Ordinal);

        var jumpRope = result.Days[0].Slots.Single(s => s.EventName == "一分钟跳绳");
        Assert.Equal(MeetRoster.Core.Models.MeetEventCategory.Fun, jumpRope.Category);
        Assert.Equal(3, jumpRope.Entries.Count);
        Assert.Equal("跳绳队", jumpRope.Entries[0].AthleteName);
    }

    private static void CreateRegistrationWorkbook(string path)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("导入模板");
        sheet.Cell(1, 1).Value = "示例中学田径运动会";
        sheet.Cell(2, 1).Value = "参赛单位";
        sheet.Cell(2, 2).Value = "七年级1班";
        sheet.Cell(3, 1).Value = "参赛单位编码";
        sheet.Cell(3, 2).Value = "（不填）";
        sheet.Cell(4, 1).Value = "领队（班主任）&电话";
        sheet.Cell(4, 2).Value = "沈青禾 · 138 0000 0001";
        sheet.Cell(5, 1).Value = "任课体育老师";
        sheet.Cell(5, 2).Value = "韩知微";
        sheet.Cell(7, 1).Value = "姓名";
        sheet.Cell(7, 2).Value = "性别";
        sheet.Cell(7, 3).Value = "团体项目1";
        sheet.Cell(7, 4).Value = "团体项目2";
        sheet.Cell(7, 5).Value = "团体项目3";
        sheet.Cell(7, 6).Value = "比赛项目1";
        sheet.Cell(7, 7).Value = "比赛项目2";
        sheet.Cell(8, 1).Value = "例：张三";

        string[][] rows =
        [
            ["苏晚晴", "女子", "4x100米接力", "20*50米男女混合接力", "一分钟跳绳", "100米", "跳高"],
            ["林见夏", "女子", "4x100米接力", "", "", "200米", "实心球"],
            ["陈屿", "男子", "4x100米接力", "", "", "100米", "200米"],
            ["许砚", "男子", "4x100米接力", "", "", "100米", "立定跳远"],
            ["沈观澜", "男子", "4x100米接力", "", "", "100米", "200米"],
        ];

        for (var i = 0; i < rows.Length; i++)
        {
            for (var j = 0; j < rows[i].Length; j++)
                sheet.Cell(9 + i, 1 + j).Value = rows[i][j];
        }

        workbook.SaveAs(path);
    }

    private static void CreateScheduleWorkbook(string path)
    {
        using var workbook = new XLWorkbook();
        var schedule = workbook.AddWorksheet("竞赛日程");
        schedule.Cell(1, 1).Value = "示例中学田径运动会";
        schedule.Cell(4, 1).Value = "2025-11-19  上午  径赛";
        schedule.Cell(6, 1).Value = "序号";
        schedule.Cell(6, 2).Value = "组别";
        schedule.Cell(6, 3).Value = "性别";
        schedule.Cell(6, 4).Value = "比赛项目";
        schedule.Cell(6, 5).Value = "赛次";
        schedule.Cell(6, 6).Value = "人数";
        schedule.Cell(6, 7).Value = "组数";
        schedule.Cell(6, 8).Value = "录取";
        schedule.Cell(6, 9).Value = "时间";
        schedule.Cell(7, 1).Value = 1;
        schedule.Cell(7, 2).Value = "七年级";
        schedule.Cell(7, 3).Value = "女子";
        schedule.Cell(7, 4).Value = "100米";
        schedule.Cell(7, 5).Value = "预赛";
        schedule.Cell(7, 6).Value = 6;
        schedule.Cell(7, 7).Value = 1;
        schedule.Cell(7, 8).Value = 7;
        schedule.Cell(7, 9).Value = "10:20";
        schedule.Cell(8, 1).Value = 2;
        schedule.Cell(8, 2).Value = "七年级";
        schedule.Cell(8, 3).Value = "男子";
        schedule.Cell(8, 4).Value = "100米";
        schedule.Cell(8, 5).Value = "预赛";
        schedule.Cell(8, 6).Value = 6;
        schedule.Cell(8, 7).Value = 1;
        schedule.Cell(8, 8).Value = 7;
        schedule.Cell(8, 9).Value = "10:30";
        schedule.Cell(10, 1).Value = "2025-11-19  下午  趣味赛";
        schedule.Cell(12, 1).Value = "序号";
        schedule.Cell(12, 2).Value = "组别";
        schedule.Cell(12, 3).Value = "性别";
        schedule.Cell(12, 4).Value = "比赛项目";
        schedule.Cell(12, 5).Value = "赛次";
        schedule.Cell(12, 6).Value = "人数";
        schedule.Cell(12, 7).Value = "组数";
        schedule.Cell(12, 8).Value = "录取";
        schedule.Cell(12, 9).Value = "时间";
        schedule.Cell(13, 1).Value = 1;
        schedule.Cell(13, 2).Value = "七年级";
        schedule.Cell(13, 3).Value = "混合";
        schedule.Cell(13, 4).Value = "跳绳";
        schedule.Cell(13, 5).Value = "决赛";
        schedule.Cell(13, 6).Value = "3队";
        schedule.Cell(13, 7).Value = 1;
        schedule.Cell(13, 8).Value = 8;
        schedule.Cell(13, 9).Value = "14:30";

        var grouping = workbook.AddWorksheet("项目分组表");
        WriteIndividualBlock(grouping, 1, "女子100米", ["1班", "2班", "3班", "4班", "5班", "6班"]);
        WriteIndividualBlock(grouping, 8, "男子100米", ["2班", "3班", "4班", "5班", "6班", "1班"]);

        grouping.Cell(15, 1).Value = "七年级";
        grouping.Cell(15, 3).Value = "混合跳绳";
        grouping.Cell(15, 6).Value = "决赛";
        grouping.Cell(15, 7).Value = "3队/1组/取 8 名";
        grouping.Cell(16, 1).Value = "第 1 组";
        grouping.Cell(16, 2).Value = 1;
        grouping.Cell(16, 3).Value = 2;
        grouping.Cell(16, 4).Value = 3;
        grouping.Cell(17, 2).Value = "1班";
        grouping.Cell(17, 3).Value = "2班";
        grouping.Cell(17, 4).Value = "3班";

        workbook.SaveAs(path);
    }

    private static void WriteIndividualBlock(IXLWorksheet sheet, int startRow, string eventName, string[] classes)
    {
        sheet.Cell(startRow, 1).Value = "七年级";
        sheet.Cell(startRow, 3).Value = eventName;
        sheet.Cell(startRow, 6).Value = "预赛";
        sheet.Cell(startRow, 7).Value = $"{classes.Length}人/1组/取 7 名";
        for (var i = 0; i < classes.Length; i++)
        {
            sheet.Cell(startRow + 1, 2 + i).Value = $"第{i + 1}道";
        }

        sheet.Cell(startRow + 2, 1).Value = "第 1 组";
        for (var i = 0; i < classes.Length; i++)
        {
            var column = 2 + i;
            sheet.Cell(startRow + 2, column).Value = i + 1;
            sheet.Cell(startRow + 3, column).Value = $"00{i + 1}";
            sheet.Cell(startRow + 4, column).Value = new[]
            {
                "苏晚晴", "林见夏", "顾之遥", "周予安", "许听澜", "宋知雨",
            }[i];
            sheet.Cell(startRow + 5, column).Value = classes[i];
        }
    }
}
