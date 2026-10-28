using ClosedXML.Excel;
using MeetRoster.App;
using MeetRoster.Core.Import;

namespace TeachCraft.Tests;

/// <summary>名单导入（花名册）与「从名单添加运动员」的回归。</summary>
public sealed class MeetRosterRosterTests : IDisposable
{
    private readonly string _root;

    public MeetRosterRosterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MeetRoster.RosterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* 清理失败不影响测试结论 */ }
    }

    // ---------- 解析 ----------

    [Fact]
    public void RosterImport_ReadsNameGenderAndClass()
    {
        var path = RosterFile("七年级1班花名册.xlsx",
            ["姓名", "性别", "班级"],
            ["苏晚晴", "女", "七年级1班"],
            ["陈屿", "男", "七年级1班"],
            ["顾之遥", "女子", "七年级1班"]);

        var result = RosterNameImporter.Import(path);

        Assert.Equal(3, result.Entries.Count);
        Assert.Equal("苏晚晴", result.Entries[0].Name);
        Assert.Equal("女子", result.Entries[0].Gender);
        Assert.Equal("七年级1班", result.Entries[0].ClassName);
        Assert.Equal("女子", result.Entries[2].Gender);
        Assert.DoesNotContain(result.Issues, i => i.IsError);
    }

    [Fact]
    public void RosterImport_MissingGenderColumn_LeavesGenderEmptyWithNotice()
    {
        var path = RosterFile("名单.xlsx",
            ["姓名"],
            ["苏晚晴"],
            ["陈屿"]);

        var result = RosterNameImporter.Import(path);

        Assert.Equal(2, result.Entries.Count);
        Assert.All(result.Entries, e => Assert.Equal(string.Empty, e.Gender));
        Assert.Contains(result.Issues, i => i.Message.Contains("性别", StringComparison.Ordinal) && !i.IsError);
    }

    [Fact]
    public void RosterImport_DuplicateName_KeepsFirstAndNotifies()
    {
        var path = RosterFile("名单.xlsx",
            ["姓名", "性别"],
            ["苏晚晴", "女"],
            ["苏晚晴", "女"]);

        var result = RosterNameImporter.Import(path);

        Assert.Single(result.Entries);
        Assert.Contains(result.Issues, i => i.Message.Contains("出现了多次", StringComparison.Ordinal));
    }

    [Fact]
    public void RosterImport_ThrowsWhenNoNameHeader()
    {
        var path = RosterFile("无表头.xlsx",
            ["编号", "备注"],
            ["1", "x"]);

        var ex = Assert.Throws<InvalidDataException>(() => RosterNameImporter.Import(path));
        Assert.Contains("姓名", ex.Message, StringComparison.Ordinal);
    }

    // ---------- 从名单加入报名表 ----------

    [Fact]
    public async Task AddAthletesFromRoster_AddsOnlyNewNames()
    {
        var vm = new MeetRosterViewModel();
        await vm.ImportRegistrationFilesAsync([RegistrationFile()]);

        var rosterPath = RosterFile("名单.xlsx",
            ["姓名", "性别", "班级"],
            ["苏晚晴", "女", "七年级1班"],   // 已在报名表里
            ["新同学甲", "女", "七年级1班"],
            ["新同学乙", "男", "七年级1班"]);
        await vm.ImportRosterFileAsync(rosterPath);
        Assert.True(vm.HasRoster);
        // 导入状况写进「导入提示」面板：摘要 + 性别列缺失的汇总提示，持久可见。
        Assert.True(vm.HasImportNotices);
        Assert.Contains(vm.ImportNotices, n => n.Message.Contains("已导入名单 3 人", StringComparison.Ordinal));

        var candidates = vm.BuildRosterCandidates();
        Assert.Equal(3, candidates.Count);
        Assert.True(candidates.Single(c => c.Name == "苏晚晴").AlreadyInClass);
        Assert.True(candidates.Single(c => c.Name == "新同学甲").CanSelect);

        var added = vm.AddAthletesFromRoster(
            candidates.Where(c => c.CanSelect).Select(c => c.Entry).ToList());

        Assert.Equal(2, added.Count);
        Assert.Equal(3, vm.SelectedClass!.AthleteCount);
        Assert.Contains(vm.SelectedClass.Athletes, a => a.Name == "新同学甲" && a.Gender == "女子");
    }

    [Fact]
    public async Task BuildRosterCandidates_PrefersEntriesTaggedWithCurrentClass()
    {
        var vm = new MeetRosterViewModel();
        await vm.ImportRegistrationFilesAsync([RegistrationFile()]);

        var rosterPath = RosterFile("全年级名单.xlsx",
            ["姓名", "性别", "班级"],
            ["一班学生", "女", "七年级1班"],
            ["二班学生", "男", "七年级2班"]);
        await vm.ImportRosterFileAsync(rosterPath);

        var candidates = vm.BuildRosterCandidates();

        Assert.Contains(candidates, c => c.Name == "一班学生");
        Assert.DoesNotContain(candidates, c => c.Name == "二班学生");
    }

    [Fact]
    public void RosterImport_TitleRowAboveHeader_ParsesAndInfersClass()
    {
        // 常见形态：第 1 行是“XX班花名册”标题、第 2 行空、第 3 行才是表头。
        var path = RosterFileWithTitle("七年级1班花名册.xlsx", "七年级1班花名册",
            ["姓名", "性别"],
            ["苏晚晴", "女"],
            ["陈屿", "男"]);

        var result = RosterNameImporter.Import(path);

        Assert.Equal("七年级1班", result.InferredClassName);
        Assert.Equal(2, result.Entries.Count);
        // 名单没有班级列时，用标题推断的班级归班。
        Assert.All(result.Entries, e => Assert.Equal("七年级1班", e.ClassName));
    }

    [Fact]
    public void RosterImport_TitleContainingNameWord_IsNotTakenAsHeader()
    {
        var path = RosterFileWithTitle("名单.xlsx", "七年级1班学生姓名一览",
            ["姓名", "性别"],
            ["苏晚晴", "女"],
            ["陈屿", "男"]);

        var result = RosterNameImporter.Import(path);

        // 标题里的“姓名”不是整格“姓名”，不应被当表头，否则数据会少一行。
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("七年级1班", result.InferredClassName);
    }

    [Fact]
    public void RosterImport_ClassFromFileName_WhenTitleHasNoClass()
    {
        var path = RosterFileWithTitle("八年级2班名单.xlsx", "学生花名册",
            ["姓名"],
            ["周予安"]);

        var result = RosterNameImporter.Import(path);

        Assert.Equal("八年级2班", result.InferredClassName);
        Assert.Equal("八年级2班", result.Entries[0].ClassName);
    }

    [Fact]
    public async Task ImportRoster_SecondClassAccumulates_SameClassReplaces()
    {
        var vm = new MeetRosterViewModel();
        await vm.ImportRegistrationFilesAsync([RegistrationFile()]);

        await vm.ImportRosterFileAsync(RosterFileWithTitle("七年级1班花名册.xlsx", "七年级1班花名册",
            ["姓名", "性别"], ["甲同学", "女"], ["乙同学", "男"]));
        Assert.Equal(2, vm.RosterCount);

        await vm.ImportRosterFileAsync(RosterFileWithTitle("七年级2班花名册.xlsx", "七年级2班花名册",
            ["姓名", "性别"], ["丙同学", "女"]));
        Assert.Equal(3, vm.RosterCount); // 不同班累加

        await vm.ImportRosterFileAsync(RosterFileWithTitle("七年级1班花名册副本.xlsx", "七年级1班花名册",
            ["姓名", "性别"], ["甲同学", "女"]));
        Assert.Equal(2, vm.RosterCount); // 同班替换，不叠加
    }

    [Fact]
    public void RosterImport_HeaderlessSheetWithIdAndName_InfersColumns()
    {
        // 真实花名册常见形态：第 1 行合并标题，第 2 行起直接是「学号 + 姓名」，没有表头。
        var path = Path.Combine(_root, "八（6）班学生名单.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell(1, 1).Value = "八（6）班学生名单";
            sheet.Range(1, 1, 1, 5).Merge();
            string[][] rows =
            [
                ["20240601", "曾文君"],
                ["20240602", "陈琳曦"],
                ["20240603", "陈昱洋"],
                ["20240604", "陈钟磬"],
            ];
            for (var i = 0; i < rows.Length; i++)
            {
                sheet.Cell(2 + i, 1).Value = rows[i][0];
                sheet.Cell(2 + i, 2).Value = rows[i][1];
            }
            workbook.SaveAs(path);
        }

        var result = RosterNameImporter.Import(path);

        Assert.Equal(4, result.Entries.Count);
        Assert.Equal("曾文君", result.Entries[0].Name);
        Assert.Equal("八（6）班", result.InferredClassName);
        Assert.All(result.Entries, e => Assert.Equal("八（6）班", e.ClassName));
        // 整份没有性别列：只汇总一条提示，不逐人刷屏。
        Assert.Single(result.Issues);
        Assert.Contains("没有性别列", result.Issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RosterImport_HeaderlessWithGenderColumn_ReadsGender()
    {
        var path = Path.Combine(_root, "无表头带性别.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell(1, 1).Value = "九（1）班名单";
            string[][] rows =
            [
                ["20250101", "苏晚晴", "女"],
                ["20250102", "陈屿", "男"],
                ["20250103", "顾之遥", "女"],
            ];
            for (var i = 0; i < rows.Length; i++)
            {
                for (var j = 0; j < rows[i].Length; j++)
                    sheet.Cell(2 + i, 1 + j).Value = rows[i][j];
            }
            workbook.SaveAs(path);
        }

        var result = RosterNameImporter.Import(path);

        Assert.Equal(3, result.Entries.Count);
        Assert.Equal("女子", result.Entries[0].Gender);
        Assert.Equal("男子", result.Entries[1].Gender);
        Assert.Equal("九（1）班", result.InferredClassName);
    }

    // ---------- fixtures ----------

    private string RosterFile(string fileName, params string[][] rows)
    {
        var path = Path.Combine(_root, fileName);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        for (var i = 0; i < rows.Length; i++)
        {
            for (var j = 0; j < rows[i].Length; j++)
            {
                if (!string.IsNullOrEmpty(rows[i][j]))
                    sheet.Cell(1 + i, 1 + j).Value = rows[i][j];
            }
        }
        workbook.SaveAs(path);
        return path;
    }

    /// <summary>第 1 行标题、第 2 行空、第 3 行起表头与数据（用户常见的“XX班花名册”形态）。</summary>
    private string RosterFileWithTitle(string fileName, string title, params string[][] rows)
    {
        var path = Path.Combine(_root, fileName);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell(1, 1).Value = title;
        for (var i = 0; i < rows.Length; i++)
        {
            for (var j = 0; j < rows[i].Length; j++)
            {
                if (!string.IsNullOrEmpty(rows[i][j]))
                    sheet.Cell(3 + i, 1 + j).Value = rows[i][j];
            }
        }
        workbook.SaveAs(path);
        return path;
    }

    private string RegistrationFile()
    {
        var path = Path.Combine(_root, "七年级1班运动会报名表.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("导入模板");
        sheet.Cell(2, 1).Value = "参赛单位";
        sheet.Cell(2, 2).Value = "七年级1班";
        sheet.Cell(7, 1).Value = "姓名";
        sheet.Cell(7, 2).Value = "性别";
        sheet.Cell(8, 1).Value = "苏晚晴";
        sheet.Cell(8, 2).Value = "女子";
        sheet.Cell(8, 6).Value = "100米";
        workbook.SaveAs(path);
        return path;
    }
}
