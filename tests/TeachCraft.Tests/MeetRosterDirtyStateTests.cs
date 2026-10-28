using ClosedXML.Excel;
using MeetRoster.App;

namespace TeachCraft.Tests;

/// <summary>
/// 批次①（数据安全）回归：报名导入的合并语义（Plan 1.1）与
/// 保存 / 另存为收尾对 IsDirty 的处理（Plan 1.2 + 1.3）。
/// </summary>
public sealed class MeetRosterDirtyStateTests : IDisposable
{
    private readonly string _root;

    public MeetRosterDirtyStateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetRoster.DirtyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* 清理失败不影响测试结论 */ }
    }

    // ---------- Plan 1.1 合并语义 ----------

    [Fact]
    public async Task Import_NewClass_AfterExisting_MergesInsteadOfClearing()
    {
        var vm = new MeetRosterViewModel();
        await vm.ImportRegistrationFilesAsync([Wb("七年级1班"), Wb("七年级2班")]);
        Assert.Equal(2, vm.AllClasses.Count);

        await vm.ImportRegistrationFilesAsync([Wb("八年级1班")]);

        Assert.Equal(
            new[] { "七年级1班", "七年级2班", "八年级1班" },
            vm.AllClasses.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task Import_SameName_ReplacesClass_KeepsOthers()
    {
        var vm = new MeetRosterViewModel();
        await vm.ImportRegistrationFilesAsync([Wb("七年级1班"), Wb("七年级2班")]);
        var untouchedSource = vm.AllClasses.First(c => c.Name == "七年级2班").Source;
        var replacedSource = vm.AllClasses.First(c => c.Name == "七年级1班").Source;

        await vm.ImportRegistrationFilesAsync([Wb("七年级1班", athletes: 3)]);

        Assert.Equal(2, vm.AllClasses.Count);
        var replaced = vm.AllClasses.Single(c => c.Name == "七年级1班");
        Assert.Equal(3, replaced.AthleteCount);
        // 数据层语义：未被替换的 MeetClass 对象原样保留（连同未保存的编辑），
        // 被替换的班换成新导入的 MeetClass。VM 包装每次导入都会重建，属现行为。
        Assert.Same(untouchedSource, vm.AllClasses.First(c => c.Name == "七年级2班").Source);
        Assert.NotSame(replacedSource, replaced.Source);
    }

    [Fact]
    public async Task Import_AfterSchedule_KeepsScheduleOnlyClasses()
    {
        var vm = new MeetRosterViewModel();
        await vm.ImportRegistrationFilesAsync([Wb("七年级1班")]);
        await vm.ImportScheduleFileAsync(SchedulePath());
        Assert.Contains(vm.AllClasses, c => c.Name == "八年级3班" && c.AthleteCount == 0);

        await vm.ImportRegistrationFilesAsync([Wb("七年级2班")]);

        var names = vm.AllClasses.Select(c => c.Name).ToList();
        Assert.Contains("七年级1班", names);
        Assert.Contains("七年级2班", names);
        // 赛程导入时按班级名建出的无源班不再被报名导入清掉（P0-1 的另一触发面）。
        Assert.Contains("八年级3班", names);
    }

    [Fact]
    public void PeekClassName_ReadsUnitCell_AndFailsClosed()
    {
        var path = Wb("七年级1班");
        Assert.Equal("七年级1班", MeetRoster.Core.Import.RegistrationWorkbookImporter.PeekClassName(path));
        Assert.Null(MeetRoster.Core.Import.RegistrationWorkbookImporter.PeekClassName(
            Path.Combine(_root, "不存在.xlsx")));
    }

    // ---------- Plan 1.2 / 1.3 IsDirty 收尾 ----------

    [Fact]
    public async Task Save_ClearsDirty_WhenEverythingWritten()
    {
        var vm = new MeetRosterViewModel();
        await vm.ImportRegistrationFilesAsync([Wb("七年级1班")]);
        EditFirstAthlete(vm);
        Assert.True(vm.IsDirty);

        await vm.SaveToSourceAsync();

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task Save_KeepsDirty_WhenOneClassFailsToWrite()
    {
        var vm = new MeetRosterViewModel();
        await vm.ImportRegistrationFilesAsync([Wb("七年级1班"), Wb("七年级2班")]);
        // 把第二个班的来源文件指到不存在的路径，模拟写回失败（文件被占用/被删）。
        vm.AllClasses[1].Source.SourcePath = Path.Combine(_root, "不存在.xlsx");
        EditFirstAthlete(vm);
        Assert.True(vm.IsDirty);

        await vm.SaveToSourceAsync();

        // 部分失败：脏标记必须保留，状态栏说明未保存。
        Assert.True(vm.IsDirty);
        Assert.Contains("尚未保存", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("七年级2班", vm.StatusDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_KeepsDirty_AndNamesClassesWithoutSource()
    {
        var vm = new MeetRosterViewModel();
        await vm.ImportRegistrationFilesAsync([Wb("七年级1班")]);
        await vm.ImportScheduleFileAsync(SchedulePath());
        // 给赛程带来的无源班录入一名运动员，制造"有数据但写不出去"的状态。
        var orphan = vm.AllClasses.First(c => string.IsNullOrWhiteSpace(c.Source.SourcePath));
        orphan.AddAthlete();
        orphan.Athletes[0].Name = "测试生";
        EditFirstAthlete(vm);
        Assert.True(vm.IsDirty);

        await vm.SaveToSourceAsync();

        Assert.True(vm.IsDirty);
        Assert.Contains("八年级3班", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("没有原始报名表", vm.StatusText, StringComparison.Ordinal);
    }

    // ---------- fixtures ----------

    private string Wb(string className, int athletes = 5)
    {
        var path = Path.Combine(_root, className + "运动会报名表.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("导入模板");
        sheet.Cell(1, 1).Value = "示例中学田径运动会";
        sheet.Cell(2, 1).Value = "参赛单位";
        sheet.Cell(2, 2).Value = className;
        sheet.Cell(3, 1).Value = "参赛单位编码";
        sheet.Cell(3, 2).Value = "（不填）";
        sheet.Cell(4, 1).Value = "领队（班主任）&电话";
        sheet.Cell(4, 2).Value = "沈青禾";
        sheet.Cell(5, 1).Value = "任课体育老师";
        sheet.Cell(5, 2).Value = "韩知微";
        sheet.Cell(7, 1).Value = "姓名";
        sheet.Cell(7, 2).Value = "性别";
        sheet.Cell(7, 3).Value = "团体项目1";
        sheet.Cell(7, 4).Value = "团体项目2";
        sheet.Cell(7, 5).Value = "团体项目3";
        sheet.Cell(7, 6).Value = "比赛项目1";
        sheet.Cell(7, 7).Value = "比赛项目2";

        for (var i = 0; i < athletes; i++)
        {
            sheet.Cell(8 + i, 1).Value = $"{className}生{i + 1}";
            sheet.Cell(8 + i, 2).Value = i % 2 == 0 ? "女子" : "男子";
            sheet.Cell(8 + i, 6).Value = i % 2 == 0 ? "100米" : "200米";
        }

        workbook.SaveAs(path);
        return path;
    }

    /// <summary>最小赛程工作簿：一个时段（七年级 女子 100米 预赛），分组表里带"八年级3班"。</summary>
    private string SchedulePath()
    {
        var path = Path.Combine(_root, "往届赛程.xlsx");
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
        schedule.Cell(7, 6).Value = 1;
        schedule.Cell(7, 7).Value = 1;
        schedule.Cell(7, 8).Value = 7;
        schedule.Cell(7, 9).Value = "10:20";

        var grouping = workbook.AddWorksheet("项目分组表");
        // 块结构：块头 / 道次 / 组号+号码 / 号码布 / 姓名 / 班级。
        grouping.Cell(1, 1).Value = "七年级";
        grouping.Cell(1, 3).Value = "女子100米";
        grouping.Cell(1, 6).Value = "预赛";
        grouping.Cell(1, 7).Value = "1人/1组/取 7 名";
        grouping.Cell(2, 2).Value = "第1道";
        grouping.Cell(3, 1).Value = "第 1 组";
        grouping.Cell(3, 2).Value = 1;
        grouping.Cell(4, 2).Value = "001";
        grouping.Cell(5, 2).Value = "苏晚晴";
        grouping.Cell(6, 2).Value = "八年级3班";

        workbook.SaveAs(path);
        return path;
    }

    private static void EditFirstAthlete(MeetRosterViewModel vm) =>
        vm.AllClasses[0].Athletes[0].Name = "改名后";
}
