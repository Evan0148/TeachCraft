using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MeetRoster.Core.Models;

namespace MeetRoster.Core.Import;

public sealed class ScheduleImportResult
{
    public required List<MeetDay> Days { get; init; }
    public required IReadOnlyList<RegistrationIssue> Issues { get; init; }
    public required IReadOnlyList<string> ClassNames { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? SourcePath { get; init; }
}

/// <summary>读取往届赛程工作簿：竞赛日程 + 项目分组表。</summary>
public static class ScheduleWorkbookImporter
{
    public static ScheduleImportResult Import(string path)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("找不到赛程工作簿：" + path);

        // stream 构造 + 中文异常包装（Plan 4.1 / 4.4）。
        using var stream = WorkbookIO.OpenReadShared(path);
        using var workbook = WorkbookIO.OpenWorkbook(path, stream);
        var issues = new List<RegistrationIssue>();
        var scheduleSheet = FindSheet(workbook, "竞赛日程")
            ?? throw new InvalidDataException("赛程工作簿里没有找到“竞赛日程”工作表。");

        var days = ParseSchedule(scheduleSheet, issues);
        if (days.Count == 0)
            throw new InvalidDataException("“竞赛日程”里没有读到任何比赛时段。");

        var groupingSheet = FindSheet(workbook, "项目分组表");
        if (groupingSheet is not null)
            AttachGrouping(groupingSheet, days, issues);
        else
            issues.Add(new RegistrationIssue("没有找到“项目分组表”，赛程将只显示项目时段。", false));

        var classNames = days
            .SelectMany(d => d.Slots)
            .SelectMany(s => s.Entries)
            .Select(e => e.ClassName)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        return new ScheduleImportResult
        {
            Days = days,
            Issues = issues
                .GroupBy(i => (i.Message, i.IsError))
                .Select(g => g.First())
                .ToList(),
            ClassNames = classNames,
            Title = Clean(scheduleSheet.Cell(1, 1).GetString()),
            SourcePath = Path.GetFullPath(path),
        };
    }

    private static List<MeetDay> ParseSchedule(IXLWorksheet sheet, List<RegistrationIssue> issues)
    {
        var byDate = new SortedDictionary<DateOnly, List<MeetSlot>>();
        DateOnly? currentDate = null;
        string currentPeriod = string.Empty;
        var currentCategory = MeetEventCategory.Other;

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var row = 1; row <= lastRow; row++)
        {
            var first = Clean(sheet.Cell(row, 1).GetString());
            var section = ParseSection(first);
            if (section is not null)
            {
                currentDate = section.Value.Date;
                currentPeriod = section.Value.Period;
                currentCategory = section.Value.Category;
                if (!byDate.ContainsKey(currentDate.Value))
                    byDate[currentDate.Value] = [];
                continue;
            }

            if (currentDate is null) continue;
            if (first.Contains("序号", StringComparison.Ordinal) &&
                Clean(sheet.Cell(row, 4).GetString()).Contains("比赛项目", StringComparison.Ordinal))
                continue;

            var grade = NormalizeGrade(Clean(sheet.Cell(row, 2).GetString()));
            var eventName = NormalizeEventName(Clean(sheet.Cell(row, 4).GetString()));
            if (grade.Length == 0 || eventName.Length == 0) continue;

            var gender = NormalizeGender(Clean(sheet.Cell(row, 3).GetString()));
            var stage = Clean(sheet.Cell(row, 5).GetString());
            var time = ReadTime(sheet.Cell(row, 9));
            if (time.Length == 0) continue;

            byDate[currentDate.Value].Add(new MeetSlot
            {
                Time = time,
                Period = currentPeriod,
                Grade = grade,
                EventName = eventName,
                Gender = gender,
                Category = currentCategory,
                Stage = stage,
                Venue = VenueFor(eventName, currentCategory),
                EntryCountText = Clean(sheet.Cell(row, 6).GetString()),
                GroupCount = Clean(sheet.Cell(row, 7).GetString()),
                AdvanceCount = Clean(sheet.Cell(row, 8).GetString()),
            });
        }

        var days = new List<MeetDay>();
        var index = 0;
        foreach (var (date, slots) in byDate)
        {
            index++;
            days.Add(new MeetDay
            {
                Name = index switch { 1 => "第一天", 2 => "第二天", 3 => "第三天", _ => $"第 {index} 天" },
                DateText = $"{date.Month} 月 {date.Day} 日",
                Date = date,
            });

            var day = days[^1];
            foreach (var slot in slots)
                day.Slots.Add(slot);
        }

        if (days.Count == 0)
            issues.Add(new RegistrationIssue("“竞赛日程”里没有识别到日期段落。", false));

        return days;
    }

    private static void AttachGrouping(IXLWorksheet sheet, List<MeetDay> days, List<RegistrationIssue> issues)
    {
        // 同键可能命中多个时段（跨天同名项目、同日上下午两轮）：候选按出现顺序分配，
        // 不再静默丢弃（Plan 2.1）；分组表若带日期节则先按日期精确对齐。
        var slots = new Dictionary<string, List<(DateOnly? Date, MeetSlot Slot)>>(StringComparer.Ordinal);
        foreach (var day in days)
        {
            foreach (var slot in day.Slots)
            {
                var key = LooseSlotKey(slot.Grade, slot.Gender, slot.EventName, slot.Stage);
                if (!slots.TryGetValue(key, out var list)) slots[key] = list = [];
                list.Add((day.Date, slot));
            }
        }

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var unmatchedBlocks = 0;
        var ambiguousKeys = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new Dictionary<string, int>(StringComparer.Ordinal);
        DateOnly? currentDate = null; // 分组表出现日期节时更新；无日期节的表保持 null

        for (var row = 1; row <= lastRow; row++)
        {
            var section = ParseSection(Clean(sheet.Cell(row, 1).GetString()));
            if (section is not null)
            {
                currentDate = section.Value.Date;
                continue;
            }

            if (!TryReadBlockHeader(sheet, row, out var grade, out var gender, out var eventName, out var stage))
                continue;

            var key = LooseSlotKey(grade, gender, eventName, stage);
            if (!slots.TryGetValue(key, out var candidates))
            {
                unmatchedBlocks++;
                continue;
            }

            if (currentDate is not null)
            {
                var dated = candidates.Where(c => c.Date == currentDate).ToList();
                if (dated.Count > 0) candidates = dated;
            }

            var cursor = cursors.TryGetValue(key, out var value) ? value : 0;
            var slot = candidates[Math.Min(cursor, candidates.Count - 1)].Slot;
            cursors[key] = cursor + 1;

            if (candidates.Count > 1 && ambiguousKeys.Add(key))
                issues.Add(new RegistrationIssue(
                    $"「{grade} {gender} {eventName}」在赛程里命中 {candidates.Count} 个时段，分组名单已按出现顺序分配，请核对。",
                    false));

            var r = row + 1;
            string currentGroup = string.Empty;
            var laneNumbers = new Dictionary<int, int>();
            var teamEvent = IsTeamEvent(eventName);

            while (r <= lastRow)
            {
                if (TryReadBlockHeader(sheet, r, out _, out _, out _, out _)) break;

                var first = Clean(sheet.Cell(r, 1).GetString());
                if (ParseSection(first) is not null) break;

                if (first.Contains("组", StringComparison.Ordinal) &&
                    TryParseGroupNumber(first, out var groupNumber))
                {
                    currentGroup = groupNumber.ToString(CultureInfo.InvariantCulture);
                    laneNumbers = ReadLaneNumbers(sheet, r);
                    r++;

                    if (!teamEvent)
                    {
                        // 此刻 r 指向号码布行；姓名/班级行在其后两行（ReadIndividualGroup 契约）。
                        ReadIndividualGroup(sheet, r, currentGroup, laneNumbers, grade, slot);
                        r = FindIndividualBlockEnd(sheet, r + 2, lastRow);
                    }
                    continue;
                }

                if (teamEvent && currentGroup.Length > 0)
                {
                    var laneRow = ReadLaneNumbers(sheet, r);
                    var hasClass = HasClassCells(sheet, r);
                    if (laneRow.Count > 0 && !hasClass)
                    {
                        laneNumbers = laneRow;
                        r++;
                        continue;
                    }

                    if (hasClass)
                    {
                        ReadTeamGroupRow(sheet, r, currentGroup, laneNumbers, grade, eventName, slot);
                        r++;
                        continue;
                    }
                }

                r++;
            }

            row = r - 1;
        }

        if (unmatchedBlocks > 0)
            issues.Add(new RegistrationIssue(
                $"项目分组表里有 {unmatchedBlocks} 个项目未在竞赛日程中找到对应时段，已跳过这些分组。", false));
    }

    /// <summary>
    /// 个人项目组块结束探测（Plan 2.3）：从班级行之后逐行向前看，最多 5 行。
    /// 返回下一块的起始行（下一组号行 / 块头 / 日期节 / 空行）。
    /// </summary>
    private static int FindIndividualBlockEnd(IXLWorksheet sheet, int classesRow, int lastRow)
    {
        var r = classesRow + 1;
        for (var scanned = 0; r <= lastRow && scanned < 5; r++, scanned++)
        {
            var first = Clean(sheet.Cell(r, 1).GetString());
            if (first.Length == 0) break; // 空行视为块结束，避免盲跳进下一组
            if (TryReadBlockHeader(sheet, r, out _, out _, out _, out _)) break;
            if (first.Contains("组", StringComparison.Ordinal) && TryParseGroupNumber(first, out _)) break;
            if (ParseSection(first) is not null) break;
        }

        return r;
    }

    private static void ReadIndividualGroup(
        IXLWorksheet sheet,
        int firstRow,
        string group,
        IReadOnlyDictionary<int, int> laneNumbers,
        string grade,
        MeetSlot slot)
    {
        var namesRow = firstRow + 1;
        var classesRow = firstRow + 2;
        var bibsRow = firstRow;
        for (var column = 2; column <= 9; column++)
        {
            var name = Clean(sheet.Cell(namesRow, column).GetString());
            var className = Clean(sheet.Cell(classesRow, column).GetString());
            if (name.Length == 0 || className.Length == 0) continue;
            if (name.Contains("第", StringComparison.Ordinal) || className == "班") continue;

            var lane = laneNumbers.TryGetValue(column, out var number) ? number : column - 1;
            slot.Entries.Add(new MeetEntry
            {
                ClassName = FullClassName(grade, className),
                AthleteName = name,
                Bib = Clean(sheet.Cell(bibsRow, column).GetString()),
                Group = group,
                Lane = $"第 {group} 组 · 第 {lane} 道",
            });
        }
    }

    private static void ReadTeamGroupRow(
        IXLWorksheet sheet,
        int row,
        string group,
        IReadOnlyDictionary<int, int> laneNumbers,
        string grade,
        string eventName,
        MeetSlot slot)
    {
        for (var column = 2; column <= 9; column++)
        {
            var className = Clean(sheet.Cell(row, column).GetString());
            if (className.Length == 0 || !className.Contains("班", StringComparison.Ordinal)) continue;

            var lane = laneNumbers.TryGetValue(column, out var number) ? number : column - 1;
            slot.Entries.Add(new MeetEntry
            {
                ClassName = FullClassName(grade, className),
                AthleteName = TeamLabel(eventName),
                Group = group,
                Lane = $"第 {group} 组 · 第 {lane} 队",
            });
        }
    }

    private static bool TryReadBlockHeader(
        IXLWorksheet sheet,
        int row,
        out string grade,
        out string gender,
        out string eventName,
        out string stage)
    {
        grade = string.Empty;
        gender = string.Empty;
        eventName = string.Empty;
        stage = string.Empty;

        var a = Clean(sheet.Cell(row, 1).GetString());
        var c = Clean(sheet.Cell(row, 3).GetString());
        var f = Clean(sheet.Cell(row, 6).GetString());
        if (a.Length == 0 || c.Length == 0 || f.Length == 0) return false;

        var normalizedGrade = NormalizeGrade(a);
        if (normalizedGrade.Length == 0) return false;

        var (parsedGender, parsedEvent) = SplitGenderAndEvent(c);
        var normalizedEvent = NormalizeEventName(parsedEvent);
        if (normalizedEvent.Length == 0) return false;

        grade = normalizedGrade;
        gender = parsedGender;
        eventName = normalizedEvent;
        stage = f;
        return true;
    }

    private static (string Gender, string EventName) SplitGenderAndEvent(string value)
    {
        if (value.StartsWith("女子", StringComparison.Ordinal))
            return ("女子", value[2..]);
        if (value.StartsWith("男子", StringComparison.Ordinal))
            return ("男子", value[2..]);
        if (value.StartsWith("混合", StringComparison.Ordinal))
            return ("混合", value[2..]);
        return ("不限", value);
    }

    private static Dictionary<int, int> ReadLaneNumbers(IXLWorksheet sheet, int row)
    {
        var lanes = new Dictionary<int, int>();
        for (var column = 2; column <= 9; column++)
        {
            var text = Clean(sheet.Cell(row, column).GetString());
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                lanes[column] = number;
        }

        return lanes;
    }

    private static bool HasClassCells(IXLWorksheet sheet, int row)
    {
        for (var column = 2; column <= 9; column++)
        {
            if (Clean(sheet.Cell(row, column).GetString()).Contains("班", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool TryParseGroupNumber(string value, out int group)
    {
        var match = Regex.Match(value, @"第\s*(\d+)\s*组");
        if (match.Success && int.TryParse(match.Groups[1].Value, out group)) return true;
        group = 0;
        return false;
    }

    private static string LooseSlotKey(string grade, string gender, string eventName, string stage) =>
        $"{grade}|{gender}|{eventName}|{NormalizeStage(stage)}";

    private static string NormalizeStage(string stage) => Clean(stage) switch
    {
        "预决赛" => "预赛",
        _ => Clean(stage),
    };

    private static bool IsTeamEvent(string eventName) =>
        eventName.Contains("接力", StringComparison.Ordinal) ||
        eventName.Contains("跳绳", StringComparison.Ordinal);

    private static string TeamLabel(string eventName) => eventName switch
    {
        "4x100米接力" => "4x100 接力队",
        "20*50米男女混合接力" => "混合接力队",
        "一分钟跳绳" => "跳绳队",
        _ => "代表队",
    };

    private static string FullClassName(string grade, string className)
    {
        var text = Clean(className);
        if (text.StartsWith(grade, StringComparison.Ordinal)) return text;
        if (text.EndsWith("班", StringComparison.Ordinal) &&
            !text.Contains("年级", StringComparison.Ordinal))
            return grade + text;
        return text;
    }

    private static string NormalizeGrade(string value)
    {
        var text = Clean(value);
        if (text.StartsWith("七年级", StringComparison.Ordinal) ||
            text.StartsWith("八年级", StringComparison.Ordinal) ||
            text.StartsWith("九年级", StringComparison.Ordinal))
            return text[..3];
        if (text.StartsWith("高一", StringComparison.Ordinal)) return "高一年级";
        if (text.StartsWith("高二", StringComparison.Ordinal)) return "高二年级";
        if (text.StartsWith("高三", StringComparison.Ordinal)) return "高三年级";
        return string.Empty;
    }

    private static string NormalizeGender(string value)
    {
        var text = Clean(value);
        if (text.Contains("女", StringComparison.Ordinal)) return "女子";
        if (text.Contains("男", StringComparison.Ordinal)) return "男子";
        if (text.Contains("混合", StringComparison.Ordinal)) return "混合";
        return text;
    }

    /// <summary>委托到统一归一化实现（Plan 2.2，规则并集含 M→米 / 100O米 / 20x50米接力）。</summary>
    private static string NormalizeEventName(string value) => EventNameNormalizer.Normalize(value);

    private static string VenueFor(string eventName, MeetEventCategory category) => category switch
    {
        MeetEventCategory.Track => "田径场",
        MeetEventCategory.Fun => "趣味赛区",
        MeetEventCategory.Ceremony => "田径场",
        MeetEventCategory.Field => eventName switch
        {
            "跳高" => "跳高区",
            "实心球" => "投掷区",
            "立定跳远" => "沙坑区",
            _ => "田赛区",
        },
        _ => "田径场",
    };

    private static (DateOnly Date, string Period, MeetEventCategory Category)? ParseSection(string value)
    {
        var match = Regex.Match(
            value,
            @"(?<date>\d{4}[-/]\d{1,2}[-/]\d{1,2})\s*(?<period>上午|下午)\s*(?<section>径赛|田赛|趣味赛)");
        if (!match.Success) return null;

        if (!DateTime.TryParse(match.Groups["date"].Value, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return null;

        var category = match.Groups["section"].Value switch
        {
            "田赛" => MeetEventCategory.Field,
            "趣味赛" => MeetEventCategory.Fun,
            _ => MeetEventCategory.Track,
        };

        return (DateOnly.FromDateTime(date), match.Groups["period"].Value, category);
    }

    private static string ReadTime(IXLCell cell)
    {
        var value = cell.Value;
        if (value.IsDateTime)
            return value.GetDateTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        if (value.IsTimeSpan)
            return value.GetTimeSpan().ToString(@"hh\:mm", CultureInfo.InvariantCulture);

        var text = Clean(cell.GetString());
        var match = Regex.Match(text, @"(?<hour>\d{1,2}):(?<minute>\d{2})");
        return match.Success
            ? $"{int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture):00}:{match.Groups["minute"].Value}"
            : text;
    }

    private static IXLWorksheet? FindSheet(XLWorkbook workbook, string keyword) =>
        workbook.Worksheets.FirstOrDefault(w =>
            w.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string value) =>
        (value ?? string.Empty).Replace("\u3000", " ", StringComparison.Ordinal).Trim();
}
