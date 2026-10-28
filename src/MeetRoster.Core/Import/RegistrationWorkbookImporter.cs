using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MeetRoster.Core.Models;

namespace MeetRoster.Core.Import;

public sealed record RegistrationIssue(string Message, bool IsError = true);

public sealed class RegistrationImportResult
{
    public required MeetClass Class { get; init; }
    public required IReadOnlyList<RegistrationIssue> Issues { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? SourcePath { get; init; }
}

/// <summary>读取班级报名表模板，字段位置与附件模板保持一致。</summary>
public static class RegistrationWorkbookImporter
{
    /// <summary>
    /// 只读"参赛单位"一格，供导入确认弹窗预读班级名。
    /// 任何失败（文件损坏 / 被占用 / 找不到字段）都返回 null，调用方按"新增班"对待；
    /// 实际导入仍以完整解析结果为准。
    /// </summary>
    public static string? PeekClassName(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = WorkbookIO.OpenReadShared(path);
            using var workbook = new XLWorkbook(stream);
            var sheet = FindSheet(workbook);
            if (!TryFindHeader(sheet, out var headerRow)) return null;
            var className = Clean(FindField(sheet, headerRow, "参赛单位"));
            return className.Length > 0 ? className : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryFindHeader(IXLWorksheet sheet, out int headerRow)
    {
        try
        {
            headerRow = FindHeaderRow(sheet);
            return true;
        }
        catch (InvalidDataException)
        {
            headerRow = 0;
            return false;
        }
    }

    public static RegistrationImportResult Import(string path)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("找不到报名表文件：" + path);

        // stream 构造：ClosedXML 把整个包读进内存后即可释放文件句柄，
        // 避免导入期间文件被 Excel 占用导致失败（Plan 4.1）。
        using var stream = WorkbookIO.OpenReadShared(path);
        using var workbook = WorkbookIO.OpenWorkbook(path, stream);
        var sheet = FindSheet(workbook);
        var headerRow = FindHeaderRow(sheet);
        var issues = new List<RegistrationIssue>();

        var className = FindField(sheet, headerRow, "参赛单位");
        if (string.IsNullOrWhiteSpace(className))
            className = ClassNameFromFileName(path);

        className = Clean(className);
        if (string.IsNullOrWhiteSpace(className))
            throw new InvalidDataException("报名表里没有找到“参赛单位”，请检查模板是否完整。");

        var meetClass = new MeetClass
        {
            Name = className,
            Group = GroupFromClassName(className),
            Code = Clean(FindField(sheet, headerRow, "参赛单位编码")) is { Length: > 0 } code ? code : "（不填）",
            LeaderContact = Clean(FindField(sheet, headerRow, "领队")),
            Coach = Clean(FindField(sheet, headerRow, "体育老师", "教练")),
            SourcePath = Path.GetFullPath(path),
        };

        ValidateFileName(path, className, issues);

        var dataStart = headerRow + 1;
        if (IsExampleRow(sheet, dataStart)) dataStart++;

        var lastRow = Math.Max(sheet.LastRowUsed()?.RowNumber() ?? headerRow, dataStart);
        for (var row = dataStart; row <= lastRow; row++)
        {
            var name = Clean(sheet.Cell(row, 1).GetString());
            var gender = NormalizeGender(sheet.Cell(row, 2).GetString());
            var team1 = NormalizeEvent(sheet.Cell(row, 3).GetString());
            var team2 = NormalizeEvent(sheet.Cell(row, 4).GetString());
            var team3 = NormalizeEvent(sheet.Cell(row, 5).GetString());
            var individual1 = NormalizeEvent(sheet.Cell(row, 6).GetString());
            var individual2 = NormalizeEvent(sheet.Cell(row, 7).GetString());

            if (name.StartsWith("例", StringComparison.Ordinal)) continue;
            if (name.Length == 0 && team1.Length == 0 && team2.Length == 0 && team3.Length == 0 &&
                individual1.Length == 0 && individual2.Length == 0)
                continue;

            if (name.Length == 0)
            {
                issues.Add(new RegistrationIssue($"第 {row} 行有项目但没有姓名，已跳过。", false));
                continue;
            }

            // 团体项目重新分布到固定槽位：即使用户把 20*50 写进"团体项目1"列，
            // 导入后也会归位到"团体项目2"，与 UI 写回、导出保持同一列位契约。
            var arrangedTeam = MeetCatalog.ArrangeTeamSlots(
            [
                CleanTeamEvent(team1, row, issues),
                CleanTeamEvent(team2, row, issues),
                CleanTeamEvent(team3, row, issues),
            ]);

            var athlete = new Athlete
            {
                Name = name,
                Gender = gender,
                TeamEvent1 = arrangedTeam[0],
                TeamEvent2 = arrangedTeam[1],
                TeamEvent3 = arrangedTeam[2],
                IndividualEvent1 = individual1,
                IndividualEvent2 = individual2,
            };

            if (gender.Length == 0)
                issues.Add(new RegistrationIssue($"{name} 没有填写性别。"));
            else if (gender != "男子" && gender != "女子")
                issues.Add(new RegistrationIssue($"{name} 的性别“{gender}”无法识别。"));

            ValidateAthleteEvents(athlete, issues);
            meetClass.Athletes.Add(athlete);
        }

        issues.AddRange(RegistrationValidator.Validate(meetClass));
        return new RegistrationImportResult
        {
            Class = meetClass,
            Issues = Deduplicate(issues),
            Title = Clean(sheet.Cell(1, 1).GetString()),
            SourcePath = meetClass.SourcePath,
        };
    }

    private static IXLWorksheet FindSheet(XLWorkbook workbook)
    {
        var named = workbook.Worksheets.FirstOrDefault(w =>
            w.Name.Contains("导入模板", StringComparison.OrdinalIgnoreCase));
        if (named is not null) return named;

        foreach (var sheet in workbook.Worksheets)
        {
            try
            {
                FindHeaderRow(sheet);
                return sheet;
            }
            catch (InvalidDataException)
            {
                // 继续找下一个工作表。
            }
        }

        throw new InvalidDataException("没有找到报名表工作表：需要包含“姓名 / 性别 / 团体项目 / 比赛项目”表头。");
    }

    private static int FindHeaderRow(IXLWorksheet sheet)
    {
        var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 20, 30);
        for (var row = 1; row <= lastRow; row++)
        {
            var hasName = false;
            var hasGender = false;
            for (var column = 1; column <= 10; column++)
            {
                var value = Clean(sheet.Cell(row, column).GetString());
                if (value == "姓名") hasName = true;
                if (value == "性别") hasGender = true;
            }

            if (hasName && hasGender) return row;
        }

        throw new InvalidDataException("报名表缺少表头：第 7 行左右应有“姓名 / 性别 / 团体项目 / 比赛项目”。");
    }

    private static string FindField(IXLWorksheet sheet, int headerRow, params string[] labels)
    {
        for (var row = 1; row < headerRow; row++)
        {
            for (var column = 1; column <= 4; column++)
            {
                var label = Clean(sheet.Cell(row, column).GetString());
                if (label.Length == 0) continue;
                if (labels.Any(l => label.Contains(l, StringComparison.Ordinal)))
                {
                    if (labels.Contains("参赛单位") && label.Contains("编码", StringComparison.Ordinal))
                        continue;

                    var value = Clean(sheet.Cell(row, column + 1).GetString());
                    if (value.Length > 0) return value;
                }
            }
        }

        return string.Empty;
    }

    private static bool IsExampleRow(IXLWorksheet sheet, int row) =>
        Clean(sheet.Cell(row, 1).GetString()).StartsWith("例", StringComparison.Ordinal);

    private static string ClassNameFromFileName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var match = Regex.Match(stem, @"^(?<name>.+?)运动会报名表$");
        if (match.Success) return Clean(match.Groups["name"].Value);

        match = Regex.Match(stem, @"(?<name>[七八九]年级\d+班)");
        return match.Success ? Clean(match.Groups["name"].Value) : Clean(stem);
    }

    private static string GroupFromClassName(string className)
    {
        var match = Regex.Match(className, @"^(?<group>[七八九]年级|高一年级|高二年级|高三年级)");
        return match.Success ? match.Groups["group"].Value : string.Empty;
    }

    private static void ValidateFileName(string path, string className, List<RegistrationIssue> issues)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var match = Regex.Match(stem, @"^(?<name>.+?)运动会报名表$");
        if (!match.Success)
        {
            issues.Add(new RegistrationIssue($"文件名建议改为“{className}运动会报名表.xlsx”。", false));
            return;
        }

        var expected = Clean(match.Groups["name"].Value);
        if (!string.Equals(expected, className, StringComparison.Ordinal))
            issues.Add(new RegistrationIssue($"文件名写的是“{expected}”，表内参赛单位是“{className}”。", false));
    }

    private static void ValidateAthleteEvents(Athlete athlete, List<RegistrationIssue> issues)
    {
        if (athlete.IndividualEvent1.Length > 0 && athlete.IndividualEvent1 == athlete.IndividualEvent2)
            issues.Add(new RegistrationIssue($"{athlete.Name} 重复填写了“{athlete.IndividualEvent1}”。"));

        var allowed = athlete.Gender == "女子" ? MeetCatalog.FemaleIndividualEvents : MeetCatalog.MaleIndividualEvents;
        foreach (var eventName in new[] { athlete.IndividualEvent1, athlete.IndividualEvent2 })
        {
            if (eventName.Length > 0 && !MeetCatalog.IndividualEvents.Contains(eventName))
                issues.Add(new RegistrationIssue($"{athlete.Name} 的比赛项目“{eventName}”不在模板项目列表中。"));
            else if (eventName.Length > 0 && !allowed.Contains(eventName))
                issues.Add(new RegistrationIssue($"{athlete.Name} 是{athlete.Gender}，没有“{eventName}”这一项。"));
        }
    }

    private static string CleanTeamEvent(string eventName, int row, List<RegistrationIssue> issues)
    {
        if (eventName.Length == 0) return string.Empty;
        if (MeetCatalog.TeamEvents.Contains(eventName)) return eventName;

        issues.Add(new RegistrationIssue($"第 {row} 行的团体项目“{eventName}”无法识别。", false));
        return eventName;
    }

    private static string NormalizeGender(string value)
    {
        var text = Clean(value);
        if (text.Contains("女", StringComparison.Ordinal)) return "女子";
        if (text.Contains("男", StringComparison.Ordinal)) return "男子";
        return text;
    }

    /// <summary>委托到统一归一化实现（Plan 2.2）；保留公共 API 兼容。</summary>
    public static string NormalizeEvent(string value) => EventNameNormalizer.Normalize(value);

    private static string Clean(string value) =>
        (value ?? string.Empty).Replace("\u3000", " ", StringComparison.Ordinal).Trim();

    private static IReadOnlyList<RegistrationIssue> Deduplicate(List<RegistrationIssue> issues) =>
        issues
            .GroupBy(i => (i.Message, i.IsError))
            .Select(g => g.First())
            .ToList();
}
