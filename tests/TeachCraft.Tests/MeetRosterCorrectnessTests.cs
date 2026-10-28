using ClosedXML.Excel;
using MeetRoster.Core.Import;

namespace TeachCraft.Tests;

/// <summary>批次②（数据正确性）回归：项目名归一化统一、SlotKey 多时段分配、组块结构容错。</summary>
public sealed class MeetRosterCorrectnessTests : IDisposable
{
    private readonly string _root;

    public MeetRosterCorrectnessTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetRoster.CorrectTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* 清理失败不影响测试结论 */ }
    }

    // ---------- Plan 2.2 归一化唯一实现 ----------

    [Theory]
    [InlineData("100M", "100米")]
    [InlineData("100m", "100米")]
    [InlineData("１００米", "100米")]
    [InlineData("100O米", "1000米")]
    [InlineData("20x50米接力", "20*50米男女混合接力")]
    [InlineData("20＊50米男女混合接力", "20*50米男女混合接力")]
    [InlineData("4x100M接力", "4x100米接力")]
    [InlineData("1分钟跳绳（团体）", "一分钟跳绳")]
    [InlineData("跳绳", "一分钟跳绳")]
    public void Normalizer_FoldsCrossFileVariants(string raw, string expected)
    {
        // 报名侧与赛程侧必须得到同一结果（旧实现已漂移：赛程侧缺 M→米 等）。
        Assert.Equal(expected, EventNameNormalizer.Normalize(raw));
        Assert.Equal(expected, RegistrationWorkbookImporter.NormalizeEvent(raw));
    }

    [Fact]
    public void Normalizer_PreservesUnknownNames()
    {
        Assert.Equal("跳高", EventNameNormalizer.Normalize("跳高"));
        Assert.Equal("实心球", EventNameNormalizer.Normalize(" 实心球 "));
    }

    // ---------- Plan 2.1 多时段分配 ----------

    [Fact]
    public void ScheduleImport_SameDayTwoRounds_AssignsInOrderAndNotifies()
    {
        var path = Path.Combine(_root, "同日两轮.xlsx");
        CreateSchedule(path, sameDayTwoRounds: true, withDatesInGrouping: false);

        var result = ScheduleWorkbookImporter.Import(path);

        var round1 = result.Days[0].Slots.Single(s => s.Time == "10:20");
        var round2 = result.Days[0].Slots.Single(s => s.Time == "14:30");
        // 旧实现：第二个同名 slot 的名单被静默丢弃。新实现按顺序分配并提醒。
        Assert.NotEmpty(round1.Entries);
        Assert.NotEmpty(round2.Entries);
        Assert.Equal("八年级3班", round1.Entries[0].ClassName);
        Assert.Equal("八年级4班", round2.Entries[0].ClassName);
        Assert.Contains(result.Issues, i =>
            i.Message.Contains("2 个时段", StringComparison.Ordinal) &&
            !i.IsError);
    }

    [Fact]
    public void ScheduleImport_DatedGroupingSheet_AlignsByDate()
    {
        var path = Path.Combine(_root, "跨天同名.xlsx");
        CreateSchedule(path, sameDayTwoRounds: false, withDatesInGrouping: true);

        var result = ScheduleWorkbookImporter.Import(path);

        Assert.Equal(2, result.Days.Count);
        foreach (var day in result.Days)
        {
            var slot = day.Slots.Single(s => s.EventName == "100米");
            Assert.NotEmpty(slot.Entries);
        }
        Assert.Equal("八年级3班", result.Days[0].Slots[0].Entries[0].ClassName);
        Assert.Equal("八年级4班", result.Days[1].Slots[0].Entries[0].ClassName);
    }

    // ---------- Plan 2.3 组块结构容错 ----------

    [Fact]
    public void ScheduleImport_ExtraNoteLineInGroupBlock_StillParsesNextGroup()
    {
        // 单个时段里连排两个组，第 1 组的班级行后多一行备注——
        // 旧实现盲跳 3 行会把"第 2 组"错位读坏，新实现按内容探测。
        var path = Path.Combine(_root, "组块多一行.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var schedule = workbook.AddWorksheet("竞赛日程");
            schedule.Cell(1, 1).Value = "示例中学田径运动会";
            schedule.Cell(4, 1).Value = "2025-11-19  上午  径赛";
            AddScheduleHeader(schedule, 6);
            AddScheduleSlot(schedule, 7, "10:20");

            var grouping = workbook.AddWorksheet("项目分组表");
            grouping.Cell(1, 1).Value = "七年级";
            grouping.Cell(1, 3).Value = "女子100米";
            grouping.Cell(1, 6).Value = "预赛";
            grouping.Cell(1, 7).Value = "4人/2组/取 7 名";
            grouping.Cell(2, 2).Value = "第1道";
            grouping.Cell(2, 3).Value = "第2道";
            WriteGroupRow(grouping, 3, "第 1 组", 1, ("八年级3班", "苏晚晴"), ("八年级4班", "林见夏"));
            grouping.Cell(7, 1).Value = "备注：风速 0.8";
            WriteGroupRow(grouping, 8, "第 2 组", 3, ("八年级5班", "顾之遥"), ("八年级6班", "周予安"));

            workbook.SaveAs(path);
        }

        var result = ScheduleWorkbookImporter.Import(path);

        var slot = result.Days[0].Slots.Single(s => s.EventName == "100米");
        Assert.Equal(4, slot.Entries.Count);
        Assert.Equal(2, slot.Entries.Count(e => e.Lane.Contains("第 1 组", StringComparison.Ordinal)));
        Assert.Equal(2, slot.Entries.Count(e => e.Lane.Contains("第 2 组", StringComparison.Ordinal)));
    }

    /// <summary>写一行组号 + 号码布/姓名/班级各占一行的组块（两道次）。</summary>
    private static void WriteGroupRow(IXLWorksheet sheet, int groupRow, string groupLabel,
        int firstBib, params (string Class, string Name)[] athletes)
    {
        sheet.Cell(groupRow, 1).Value = groupLabel;
        for (var i = 0; i < athletes.Length; i++)
        {
            sheet.Cell(groupRow, 2 + i).Value = firstBib + i;                    // 号码
            sheet.Cell(groupRow + 1, 2 + i).Value = $"00{firstBib + i}";         // 号码布
            sheet.Cell(groupRow + 2, 2 + i).Value = athletes[i].Name;            // 姓名
            sheet.Cell(groupRow + 3, 2 + i).Value = athletes[i].Class;           // 班级
        }
    }

    // ---------- 团体项目固定槽位 ----------

    [Fact]
    public void TeamSlotArrangement_FixedMappingRegardlessOfOrder()
    {
        var arranged = MeetRoster.Core.Models.MeetCatalog.ArrangeTeamSlots(
            ["20*50米男女混合接力", "4x100米接力"]);

        // 每个团体项目固定对应一个单元格，与选择顺序无关。
        Assert.Equal("4x100米接力", arranged[0]);
        Assert.Equal("20*50米男女混合接力", arranged[1]);
        Assert.Equal(string.Empty, arranged[2]);
    }

    [Fact]
    public void TeamSlotArrangement_OnlyOneEvent_GoesToItsOwnColumn()
    {
        var arranged = MeetRoster.Core.Models.MeetCatalog.ArrangeTeamSlots(["20*50米男女混合接力"]);

        Assert.Equal(string.Empty, arranged[0]); // 不再占 4x100 的格子
        Assert.Equal("20*50米男女混合接力", arranged[1]);
        Assert.Equal(string.Empty, arranged[2]);
    }

    [Fact]
    public void Import_RearrangesTeamSlotsToFixedColumns()
    {
        // 模板里用户把 20*50 写进了"团体项目1"列、4x100 写进"团体项目2"列——导入后归位。
        var path = Path.Combine(_root, "乱序团体.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("导入模板");
            sheet.Cell(1, 1).Value = "示例中学田径运动会";
            sheet.Cell(2, 1).Value = "参赛单位";
            sheet.Cell(2, 2).Value = "七年级9班";
            sheet.Cell(7, 1).Value = "姓名";
            sheet.Cell(7, 2).Value = "性别";
            sheet.Cell(7, 3).Value = "团体项目1";
            sheet.Cell(7, 4).Value = "团体项目2";
            sheet.Cell(7, 5).Value = "团体项目3";
            sheet.Cell(7, 6).Value = "比赛项目1";
            sheet.Cell(7, 7).Value = "比赛项目2";
            sheet.Cell(8, 1).Value = "陈昱洋";
            sheet.Cell(8, 2).Value = "男子";
            sheet.Cell(8, 3).Value = "20*50米男女混合接力";
            sheet.Cell(8, 4).Value = "4x100米接力";
            workbook.SaveAs(path);
        }

        var result = RegistrationWorkbookImporter.Import(path);

        var athlete = result.Class.Athletes.Single();
        Assert.Equal("4x100米接力", athlete.TeamEvent1);
        Assert.Equal("20*50米男女混合接力", athlete.TeamEvent2);
        Assert.Equal(string.Empty, athlete.TeamEvent3);
    }

    [Fact]
    public void UiWriteBack_PutsEachTeamEventInItsOwnColumn()
    {
        var row = new MeetRoster.App.AthleteRowViewModel(
            new MeetRoster.Core.Models.Athlete { Name = "测试生", Gender = "男子" });

        row.SetTeamEvents(["20*50米男女混合接力"]);

        Assert.Equal(string.Empty, row.RawEvent(MeetRoster.App.AthleteRowViewModel.SlotTeam1));
        Assert.Equal("20*50米男女混合接力", row.RawEvent(MeetRoster.App.AthleteRowViewModel.SlotTeam2));
        Assert.Equal(string.Empty, row.RawEvent(MeetRoster.App.AthleteRowViewModel.SlotTeam3));
    }

    // ---------- fixtures ----------

    private static void AddScheduleHeader(IXLWorksheet sheet, int headerRow)
    {
        string[] heads = ["序号", "组别", "性别", "比赛项目", "赛次", "人数", "组数", "录取", "时间"];
        for (var i = 0; i < heads.Length; i++)
            sheet.Cell(headerRow, 1 + i).Value = heads[i];
    }

    private static void AddScheduleSlot(IXLWorksheet sheet, int row, string time)
    {
        sheet.Cell(row, 1).Value = 1;
        sheet.Cell(row, 2).Value = "七年级";
        sheet.Cell(row, 3).Value = "女子";
        sheet.Cell(row, 4).Value = "100米";
        sheet.Cell(row, 5).Value = "预赛";
        sheet.Cell(row, 6).Value = 1;
        sheet.Cell(row, 7).Value = 1;
        sheet.Cell(row, 8).Value = 7;
        sheet.Cell(row, 9).Value = time;
    }

    /// <summary>个人项目分组块：块头 / 道次 / 组号+号码 / 号码布 / 姓名 / 班级，返回下一行号。</summary>
    private static int AddIndividualBlock(IXLWorksheet sheet, int startRow, string className, bool noteLine = false)
    {
        sheet.Cell(startRow, 1).Value = "七年级";
        sheet.Cell(startRow, 3).Value = "女子100米";
        sheet.Cell(startRow, 6).Value = "预赛";
        sheet.Cell(startRow, 7).Value = "1人/1组/取 7 名";
        sheet.Cell(startRow + 1, 2).Value = "第1道";
        sheet.Cell(startRow + 2, 1).Value = "第 1 组";
        sheet.Cell(startRow + 2, 2).Value = 1;
        sheet.Cell(startRow + 3, 2).Value = "001";
        sheet.Cell(startRow + 4, 2).Value = "苏晚晴";
        sheet.Cell(startRow + 5, 2).Value = className;
        var next = startRow + 6;
        if (noteLine)
        {
            sheet.Cell(next, 1).Value = "备注：风速 0.8";
            next++;
        }
        return next;
    }

    private void CreateSchedule(string path, bool sameDayTwoRounds, bool withDatesInGrouping,
        bool noteLineInFirstGroup = false)
    {
        using var workbook = new XLWorkbook();
        var schedule = workbook.AddWorksheet("竞赛日程");
        schedule.Cell(1, 1).Value = "示例中学田径运动会";
        schedule.Cell(4, 1).Value = "2025-11-19  上午  径赛";
        AddScheduleHeader(schedule, 6);
        AddScheduleSlot(schedule, 7, "10:20");
        var nextRow = 8;
        if (sameDayTwoRounds)
        {
            schedule.Cell(nextRow + 1, 1).Value = "2025-11-19  下午  径赛";
            AddScheduleHeader(schedule, nextRow + 3);
            AddScheduleSlot(schedule, nextRow + 4, "14:30");
        }
        else
        {
            schedule.Cell(nextRow + 1, 1).Value = "2025-11-20  上午  径赛";
            AddScheduleHeader(schedule, nextRow + 3);
            AddScheduleSlot(schedule, nextRow + 4, "10:20");
        }

        var grouping = workbook.AddWorksheet("项目分组表");
        var row = 1;
        if (withDatesInGrouping)
        {
            grouping.Cell(1, 1).Value = "2025-11-19  上午  径赛";
            row = 3;
        }
        row = AddIndividualBlock(grouping, row, "八年级3班", noteLineInFirstGroup);
        if (withDatesInGrouping)
        {
            grouping.Cell(row, 1).Value = "2025-11-20  上午  径赛";
            row += 2;
        }
        AddIndividualBlock(grouping, row, "八年级4班");

        workbook.SaveAs(path);
    }
}
