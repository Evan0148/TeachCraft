using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using GradeWeave.Core.Models;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;

namespace GradeWeave.Core.Import;

/// <summary>读取「分工表 + 确认表 + 课程表 + 会议活动备注」结构的年级课表工作簿。</summary>
public sealed class WeaveWorkbookImporter
{
    private const int MaxHeaderScanRows = 30;

    public WeaveImportResult Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new GradeWeaveException("找不到这份课表文件，请确认路径后重试。");

        var extension = Path.GetExtension(path);
        var isXlsx = string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase);
        var isXls = string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase);
        if (!isXlsx && !isXls)
            throw new GradeWeaveException("织序支持 .xlsx 与 .xls 工作簿，其他格式请先在 Excel 中另存。");

        try
        {
            // .xls 用 NPOI 读取（含合并区域），转成内存工作簿后与 .xlsx 走同一条解析管线。
            using var workbook = isXls ? BuildWorkbookFromXls(path) : OpenXlsx(path);
            return ImportWorkbook(workbook, path);
        }
        catch (GradeWeaveException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new GradeWeaveException(
                "无法读取这份工作簿：文件可能已损坏或被加密。请用 Excel 打开后另存为 xlsx 再试。", ex);
        }
    }

    private static XLWorkbook OpenXlsx(string path)
    {
        // ClosedXML 构造时把整个包读入内存，读完即可关闭文件句柄。
        using var stream = File.OpenRead(path);
        return new XLWorkbook(stream);
    }

    private static XLWorkbook BuildWorkbookFromXls(string path)
    {
        using var stream = File.OpenRead(path);
        var source = new HSSFWorkbook(stream);

        var target = new XLWorkbook();
        for (var sheetIndex = 0; sheetIndex < source.NumberOfSheets; sheetIndex++)
        {
            var sheet = source.GetSheetAt(sheetIndex);
            var ws = target.Worksheets.Add(SanitizeSheetName(sheet.SheetName, sheetIndex));
            CopySheetCells(sheet, ws);
            CopyMergedRegions(sheet, ws);
        }

        return target;
    }

    private static void CopySheetCells(ISheet sheet, IXLWorksheet target)
    {
        for (var rowIndex = sheet.FirstRowNum; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);
            if (row is null) continue;

            for (var column = row.FirstCellNum; column < row.LastCellNum; column++)
            {
                var cell = row.GetCell(column);
                if (cell is null) continue;

                var value = ReadCellValue(cell);
                if (value is not null)
                    target.Cell(rowIndex + 1, column + 1).Value = value.Value;
            }
        }
    }

    private static XLCellValue? ReadCellValue(ICell cell)
    {
        var type = cell.CellType == CellType.Formula
            ? cell.CachedFormulaResultType
            : cell.CellType;

        try
        {
            return type switch
            {
                CellType.String => cell.StringCellValue,
                CellType.Boolean => cell.BooleanCellValue,
                CellType.Numeric => DateUtil.IsCellDateFormatted(cell)
                    ? cell.DateCellValue ?? default
                    : cell.NumericCellValue,
                _ => string.IsNullOrWhiteSpace(cell.ToString()) ? null : cell.ToString(),
            };
        }
        catch
        {
            return null;
        }
    }

    private static void CopyMergedRegions(ISheet sheet, IXLWorksheet target)
    {
        for (var index = 0; index < sheet.NumMergedRegions; index++)
        {
            var region = sheet.GetMergedRegion(index);
            if (region.FirstRow < 0 || region.FirstColumn < 0) continue;
            if (region.LastRow < region.FirstRow || region.LastColumn < region.FirstColumn) continue;

            target.Range(
                region.FirstRow + 1, region.FirstColumn + 1,
                region.LastRow + 1, region.LastColumn + 1).Merge();
        }
    }

    private static string SanitizeSheetName(string raw, int index)
    {
        var text = (raw ?? string.Empty).Trim();
        foreach (var invalid in new[] { '\\', '/', '?', '*', '[', ']', ':' })
            text = text.Replace(invalid, ' ');
        if (text.Length > 31) text = text[..31];
        return text.Length == 0 ? $"Sheet{index + 1}" : text;
    }

    // ----------------------------- 主流程 -----------------------------

    private static WeaveImportResult ImportWorkbook(XLWorkbook workbook, string path)
    {
        var project = new WeaveProject
        {
            // 保存完整路径，导出层才能可靠拒绝覆盖原工作簿。
            SourceFile = Path.GetFullPath(path),
            ImportedAt = DateTime.Now,
        };
        var result = new WeaveImportResult { Project = project };

        var warnings = new List<string>();
        var warningSet = new HashSet<string>(StringComparer.Ordinal);
        void Warn(string message)
        {
            if (warningSet.Add(message)) warnings.Add(message);
        }

        var staffing = FindStaffingLayout(workbook);
        var confirm = FindConfirmLayout(workbook);
        var schedule = FindScheduleLayout(workbook);

        if (staffing is null && schedule is null)
            throw new GradeWeaveException(
                "这份工作簿里没有找到班级-学科-教师分工表。请确认工作簿中有「班级 / 班主任 / 语文·数学·英语…」表头的分工表。");

        var title = ReadTitle(workbook, staffing?.Sheet ?? schedule?.Sheet);
        ApplyTitle(project, title);

        var teacherMap = new Dictionary<string, Teacher>(StringComparer.Ordinal);
        var staffClassRows = new List<StaffingClassRow>();

        if (staffing is not null)
        {
            BuildSubjects(staffing, project);
            staffClassRows = BuildClasses(staffing, project);
        }

        if (confirm is not null)
            ReadConfirmTeachers(confirm, project, teacherMap, Warn);
        else if (staffing is not null)
            Warn("没有找到教师确认表，兼职岗位与学科周课时将无法读取。");

        if (staffing is not null)
            ReadStaffingAssignments(staffing, staffClassRows, project, teacherMap, Warn);

        if (schedule is not null)
        {
            InferSubjectsFromSchedule(schedule, project);
            EnsureScheduleClasses(schedule, project, Warn);
            ReadScheduleLessons(schedule, project, Warn);
        }

        if (project.Classes.Count == 0)
            throw new GradeWeaveException(
                "这份工作簿里没有找到班级-学科-教师分工表。课程表里也没有可识别的班级行。");

        var noteText = FindMeetingNote(workbook, schedule?.Sheet);
        if (!string.IsNullOrWhiteSpace(noteText))
        {
            project.Notes.Add(noteText);
            var parsed = new MeetingNoteParser().Parse(noteText, project);
            result.SuggestedResearchBlocks.AddRange(parsed.ResearchBlocks);
            result.SuggestedMeetingBlocks.AddRange(parsed.MeetingBlocks);
            result.UnparsedNotes.AddRange(parsed.Unparsed);
        }

        AddSheetSummaries(result, staffing, confirm, schedule);

        project.Warnings = warnings;
        result.Warnings = warnings;
        return result;
    }

    // ----------------------------- 工作表识别 -----------------------------

    private sealed class StaffingLayout
    {
        public IXLWorksheet Sheet = null!;
        public int HeaderRow;
        public int ClassColumn;
        public int HeadTeacherColumn;
        public int LastRow;
        public int ClassCount;
        public List<SubjectColumn> SubjectColumns { get; } = [];
    }

    private sealed class SubjectColumn
    {
        public int Column;
        public string Header = string.Empty;
        public Subject? Subject;
    }

    private sealed class StaffingClassRow
    {
        public required SchoolClass Class { get; init; }
        public required int Row { get; init; }
    }

    private sealed class ConfirmLayout
    {
        public IXLWorksheet Sheet = null!;
        public int HeaderRow;
        public int NameColumn;
        public int SubjectColumn;
        public int RoleColumn;
        public int HoursColumn;
    }

    private sealed class ScheduleLayout
    {
        public IXLWorksheet Sheet = null!;
        public int HeaderRow;
        public int PeriodRow;
        public int ClassColumn;
        public int LastRow;
        public int LastColumn;
        public List<(int Day, int Column)> DayStarts { get; } = [];
        public List<ScheduleClassRow> ClassRows { get; } = [];
    }

    private sealed class ScheduleClassRow
    {
        public required int Row { get; init; }
        public required string Name { get; init; }
    }

    private static StaffingLayout? FindStaffingLayout(XLWorkbook workbook)
    {
        StaffingLayout? best = null;
        var bestScore = int.MinValue;

        foreach (var sheet in workbook.Worksheets)
        {
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow < 3 || lastColumn < 5) continue;

            for (var headerRow = 1; headerRow <= Math.Min(MaxHeaderScanRows, lastRow); headerRow++)
            {
                var classColumn = FindExactColumn(sheet, headerRow, lastColumn, "班级");
                var headColumn = FindExactColumn(sheet, headerRow, lastColumn, "班主任");
                if (classColumn < 0 || headColumn < 0) continue;

                var subjectColumns = new List<SubjectColumn>();
                for (var column = 1; column <= lastColumn; column++)
                {
                    var header = sheet.Cell(headerRow, column).GetString().Trim();
                    if (header.Length == 0 || WeaveImportText.IsMetaHeader(header)) continue;
                    if (WeaveImportText.SubjectName(header).Length == 0) continue;
                    subjectColumns.Add(new SubjectColumn { Column = column, Header = header });
                }

                var knownSubjects = subjectColumns.Count(sc => WeaveImportText.IsKnownSubject(sc.Header));
                if (knownSubjects < 3) continue;

                var classCount = CountClassRows(sheet, headerRow, classColumn, lastRow);
                if (classCount == 0) continue;

                var score = classCount * 100 + knownSubjects * 10 + subjectColumns.Count;
                if (score <= bestScore) continue;

                bestScore = score;
                best = new StaffingLayout
                {
                    Sheet = sheet,
                    HeaderRow = headerRow,
                    ClassColumn = classColumn,
                    HeadTeacherColumn = headColumn,
                    LastRow = lastRow,
                    ClassCount = classCount,
                };
                best.SubjectColumns.AddRange(subjectColumns);
            }
        }

        return best;
    }

    private static ConfirmLayout? FindConfirmLayout(XLWorkbook workbook)
    {
        ConfirmLayout? best = null;

        foreach (var sheet in workbook.Worksheets)
        {
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow < 2 || lastColumn < 4) continue;

            for (var headerRow = 1; headerRow <= Math.Min(MaxHeaderScanRows, lastRow); headerRow++)
            {
                var nameColumn = FindExactColumn(sheet, headerRow, lastColumn, "姓名");
                var subjectColumn = FindSubjectHeaderColumn(sheet, headerRow, lastColumn);
                var roleColumn = FindContainsColumn(sheet, headerRow, lastColumn, "兼职岗位");
                if (nameColumn < 0 || subjectColumn < 0 || roleColumn < 0) continue;

                var layout = new ConfirmLayout
                {
                    Sheet = sheet,
                    HeaderRow = headerRow,
                    NameColumn = nameColumn,
                    SubjectColumn = subjectColumn,
                    RoleColumn = roleColumn,
                    HoursColumn = FindContainsColumn(sheet, headerRow, lastColumn, "周课时"),
                };

                if (best is null || layout.HeaderRow < best.HeaderRow) best = layout;
                break;
            }
        }

        return best;
    }

    private static ScheduleLayout? FindScheduleLayout(XLWorkbook workbook)
    {
        ScheduleLayout? best = null;
        var bestScore = int.MinValue;

        foreach (var sheet in workbook.Worksheets)
        {
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow < 5 || lastColumn < 5) continue;

            for (var headerRow = 1; headerRow <= Math.Min(15, lastRow); headerRow++)
            {
                var dayStarts = FindDayStarts(sheet, headerRow, lastColumn);
                if (dayStarts.Count < 5) continue;

                var periodRow = FindPeriodRow(sheet, headerRow, dayStarts, lastRow, lastColumn);
                // 没有逐节数字时，按每天 10 列一组、1..7 节顺序回退。
                if (periodRow < 0) periodRow = headerRow;

                var classColumn = FindScheduleClassColumn(sheet, headerRow, periodRow, lastRow, lastColumn);
                if (classColumn < 0) continue;

                var classRows = ReadScheduleClassRows(sheet, periodRow, classColumn, lastRow);
                if (classRows.Count == 0) continue;

                var score = classRows.Count * 100 + (periodRow - headerRow) * 5 + 30;
                if (score <= bestScore) continue;

                bestScore = score;
                best = new ScheduleLayout
                {
                    Sheet = sheet,
                    HeaderRow = headerRow,
                    PeriodRow = periodRow,
                    ClassColumn = classColumn,
                    LastRow = lastRow,
                    LastColumn = lastColumn,
                };
                best.DayStarts.AddRange(dayStarts);
                best.ClassRows.AddRange(classRows);
            }
        }

        return best;
    }

    // ----------------------------- 学科与班级 -----------------------------

    private static void BuildSubjects(StaffingLayout staffing, WeaveProject project)
    {
        foreach (var subjectColumn in staffing.SubjectColumns)
        {
            var name = WeaveImportText.SubjectName(subjectColumn.Header);
            if (name.Length == 0) continue;

            var subject = project.Subjects.FirstOrDefault(s => s.Name == name);
            if (subject is null)
            {
                subject = new Subject
                {
                    Id = "s" + (project.Subjects.Count + 1),
                    Name = name,
                    IsMain = WeaveImportText.IsMainSubject(name),
                };
                project.Subjects.Add(subject);
            }

            subjectColumn.Subject = subject;
        }

        staffing.SubjectColumns.RemoveAll(sc => sc.Subject is null);
    }

    private static List<StaffingClassRow> BuildClasses(StaffingLayout staffing, WeaveProject project)
    {
        var rows = new List<StaffingClassRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var row = staffing.HeaderRow + 1; row <= staffing.LastRow; row++)
        {
            var raw = WeaveImportText.GetMergedText(staffing.Sheet, row, staffing.ClassColumn);
            if (!WeaveImportText.LooksLikeClass(raw)) continue;

            var key = WeaveImportText.Compact(raw);
            if (!seen.Add(key)) continue;

            var schoolClass = new SchoolClass
            {
                Id = "C" + (project.Classes.Count + 1),
                Name = raw.Trim(),
            };
            project.Classes.Add(schoolClass);
            rows.Add(new StaffingClassRow { Class = schoolClass, Row = row });
        }

        return rows;
    }

    // ----------------------------- 确认表 -----------------------------

    private static void ReadConfirmTeachers(
        ConfirmLayout confirm,
        WeaveProject project,
        Dictionary<string, Teacher> teacherMap,
        Action<string> warn)
    {
        var sheet = confirm.Sheet;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;

        for (var row = confirm.HeaderRow + 1; row <= lastRow; row++)
        {
            var rawName = WeaveImportText.GetMergedText(sheet, row, confirm.NameColumn);
            if (rawName.Length == 0) continue;
            if (rawName is "姓名" or "合计" or "小计" or "备注" or "说明") continue;
            if (!WeaveImportText.LooksLikeTeacherName(rawName)) continue;

            var subject = WeaveImportText.SubjectName(
                WeaveImportText.GetMergedText(sheet, row, confirm.SubjectColumn));

            var teacher = GetOrCreateTeacher(project, teacherMap, rawName, subject, warn);

            foreach (var role in ParseRoles(WeaveImportText.GetMergedText(sheet, row, confirm.RoleColumn)))
            {
                if (!teacher.Roles.Contains(role, StringComparer.Ordinal))
                    teacher.Roles.Add(role);
            }

            if (confirm.HoursColumn > 0)
            {
                var hours = ParseInt(WeaveImportText.GetMergedText(sheet, row, confirm.HoursColumn));
                if (hours > 0) teacher.WeeklyHours = hours;
            }
        }
    }

    private static List<string> ParseRoles(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        return Regex.Split(raw, @"[/／、]+")
            .Select(WeaveImportText.Compact)
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    // ----------------------------- 分工表任课关系 -----------------------------

    private static void ReadStaffingAssignments(
        StaffingLayout staffing,
        IReadOnlyList<StaffingClassRow> classRows,
        WeaveProject project,
        Dictionary<string, Teacher> teacherMap,
        Action<string> warn)
    {
        var sheet = staffing.Sheet;
        var subjectHours = ReadHoursRow(staffing);
        var hoursFound = subjectHours.Count > 0;

        foreach (var subjectColumn in staffing.SubjectColumns)
        {
            var subject = subjectColumn.Subject!;
            if (subjectHours.TryGetValue(subjectColumn.Column, out var hours) && hours > 0)
                subject.WeeklyHours = hours;
        }

        foreach (var classRow in classRows)
        {
            var headRaw = WeaveImportText.GetMergedText(sheet, classRow.Row, staffing.HeadTeacherColumn);
            if (headRaw.Length > 0)
            {
                var headToken = WeaveImportText.SplitTeacherTokens(headRaw)
                    .FirstOrDefault(WeaveImportText.LooksLikeTeacherName);
                if (headToken is not null)
                {
                    var headTeacher = GetOrCreateTeacher(project, teacherMap, headToken, string.Empty, warn);
                    classRow.Class.HeadTeacher = headTeacher.Name;
                }
                else
                {
                    warn($"分工表中「{classRow.Class.Name}」的班主任「{headRaw.Trim()}」无法识别。");
                }
            }

            foreach (var subjectColumn in staffing.SubjectColumns)
            {
                var subject = subjectColumn.Subject!;
                var rawTeacher = WeaveImportText.GetMergedText(sheet, classRow.Row, subjectColumn.Column);
                if (rawTeacher.Length == 0) continue;

                var tokens = WeaveImportText.SplitTeacherTokens(rawTeacher);
                var validTokens = tokens.Where(WeaveImportText.LooksLikeTeacherName).ToList();
                var rotation = rawTeacher.Contains("轮流", StringComparison.Ordinal);

                if (validTokens.Count == 0)
                {
                    warn($"分工表中「{classRow.Class.Name} / {subject.Name}」的任课教师无法识别（原文：{rawTeacher.Trim()}），已跳过。");
                    continue;
                }

                if (validTokens.Count > 1)
                    warn($"分工表中「{classRow.Class.Name} / {subject.Name}」登记了多位教师（{rawTeacher.Trim()}），已采用第一位「{validTokens[0]}」。");
                if (rotation)
                    warn($"分工表中「{classRow.Class.Name} / {subject.Name}」标注为轮流上课，已采用第一位教师「{validTokens[0]}」。");

                var teacher = GetOrCreateTeacher(project, teacherMap, validTokens[0], subject.Name, warn);
                project.Assignments.Add(new TeachingAssignment
                {
                    ClassId = classRow.Class.Id,
                    SubjectId = subject.Id,
                    TeacherId = teacher.Id,
                    WeeklyHours = subject.WeeklyHours,
                });
            }
        }

        if (!hoursFound && project.Subjects.Count > 0)
            warn("分工表没有找到「课时」行，学科周课时按 0 处理。");
        else if (project.Subjects.Any(s => s.WeeklyHours == 0))
            warn("部分学科的课时行没有可读数字，周课时按 0 处理。");
    }

    private static Dictionary<int, int> ReadHoursRow(StaffingLayout staffing)
    {
        var sheet = staffing.Sheet;

        // 课时行是整张分工表的全局行（通常在班级行下方），不能按单个班级的邻近行去猜。
        for (var row = staffing.HeaderRow + 1; row <= staffing.LastRow; row++)
        {
            var label = WeaveImportText.GetMergedText(sheet, row, staffing.ClassColumn);
            if (!label.Contains("课时", StringComparison.Ordinal)) continue;

            var hours = new Dictionary<int, int>();
            foreach (var subjectColumn in staffing.SubjectColumns)
            {
                var value = ParseInt(WeaveImportText.GetMergedText(sheet, row, subjectColumn.Column));
                if (value > 0) hours[subjectColumn.Column] = value;
            }

            if (hours.Count > 0) return hours;
        }

        return [];
    }

    private static Teacher GetOrCreateTeacher(
        WeaveProject project,
        Dictionary<string, Teacher> teacherMap,
        string rawName,
        string subjectName,
        Action<string> warn)
    {
        var name = WeaveDefaults.NormalizeTeacherName(rawName);
        if (teacherMap.TryGetValue(name, out var existing))
        {
            if (subjectName.Length > 0)
            {
                if (existing.Subject.Length == 0)
                {
                    existing.Subject = subjectName;
                }
                else if (existing.Subject != subjectName)
                {
                    warn($"教师「{name}」同时出现在「{existing.Subject}」和「{subjectName}」学科，已保留第一个学科。");
                }
            }

            return existing;
        }

        var teacher = new Teacher
        {
            Id = "t" + (project.Teachers.Count + 1),
            Name = name,
            Subject = subjectName,
        };
        project.Teachers.Add(teacher);
        teacherMap[name] = teacher;
        return teacher;
    }

    // ----------------------------- 课程表基线 -----------------------------

    /// <summary>
    /// 只有课程表、没有分工表时，从每格开头出现的学科名反推学科清单，
    /// 让课程表仍能作为可读的参考基线；不会反推任课关系。
    /// </summary>
    private static void InferSubjectsFromSchedule(ScheduleLayout schedule, WeaveProject project)
    {
        if (project.Subjects.Count > 0) return;

        var aliases = WeaveImportText.SubjectAliasInputs
            .Where(a => a != "班会")
            .OrderByDescending(a => a.Length)
            .ToList();

        var sheet = schedule.Sheet;

        foreach (var classRow in schedule.ClassRows)
        {
            for (var dayPosition = 0; dayPosition < schedule.DayStarts.Count; dayPosition++)
            {
                var (_, dayStart) = schedule.DayStarts[dayPosition];
                var groupEnd = DayGroupEnd(schedule.DayStarts, dayPosition, schedule.LastColumn);

                for (var period = 1; period <= 7; period++)
                {
                    var column = FindPeriodColumn(
                        sheet, schedule.PeriodRow, dayStart, groupEnd, period);
                    if (column < 1) continue;

                    var compact = WeaveImportText.Compact(
                        WeaveImportText.GetMergedText(sheet, classRow.Row, column));
                    if (compact.Length == 0 || IsIgnoredScheduleCell(compact)) continue;

                    var input = aliases.FirstOrDefault(
                        a => compact.StartsWith(a, StringComparison.Ordinal));
                    if (input is null) continue;

                    var name = WeaveDefaults.SubjectAlias(input);
                    if (name.Length == 0 || project.Subjects.Any(s => s.Name == name)) continue;

                    project.Subjects.Add(new Subject
                    {
                        Id = "s" + (project.Subjects.Count + 1),
                        Name = name,
                        IsMain = WeaveImportText.IsMainSubject(name),
                    });
                }
            }
        }
    }

    private static void EnsureScheduleClasses(
        ScheduleLayout schedule,
        WeaveProject project,
        Action<string> warn)
    {
        var hadClasses = project.Classes.Count > 0;

        foreach (var row in schedule.ClassRows)
        {
            var existing = FindClass(project, row.Name);
            if (existing is not null) continue;

            var schoolClass = new SchoolClass
            {
                Id = "C" + (project.Classes.Count + 1),
                Name = row.Name,
            };
            project.Classes.Add(schoolClass);

            if (hadClasses)
                warn($"课程表里的班级「{row.Name}」不在分工表中，已作为独立班级补充。");
        }
    }

    private static void ReadScheduleLessons(
        ScheduleLayout schedule,
        WeaveProject project,
        Action<string> warn)
    {
        var sheet = schedule.Sheet;
        var aliases = BuildSubjectAliases(project);

        foreach (var classRow in schedule.ClassRows)
        {
            var schoolClass = FindClass(project, classRow.Name);
            if (schoolClass is null) continue;

            for (var dayPosition = 0; dayPosition < schedule.DayStarts.Count; dayPosition++)
            {
                var (dayIndex, dayStart) = schedule.DayStarts[dayPosition];
                var groupEnd = DayGroupEnd(schedule.DayStarts, dayPosition, schedule.LastColumn);

                for (var period = 1; period <= 7; period++)
                {
                    var column = FindPeriodColumn(
                        sheet, schedule.PeriodRow, dayStart, groupEnd, period);
                    if (column < 1) continue;

                    var raw = WeaveImportText.GetMergedText(sheet, classRow.Row, column);
                    if (raw.Length == 0) continue;

                    var compact = WeaveImportText.Compact(raw);
                    var location = $"{schoolClass.Name} {WeaveDefaults.SlotText(dayIndex, period, period)}";

                    if (IsIgnoredScheduleCell(compact))
                    {
                        warn($"课程表「{location}」的内容「{raw.Trim()}」不是学科课程，已作为参考基线忽略。");
                        continue;
                    }

                    var subject = MatchSubject(compact, aliases, out var rest);
                    if (subject is null)
                    {
                        var onlyTeacher = MatchTeacher(compact, project);
                        if (onlyTeacher is not null)
                        {
                            AddScheduleLesson(project, schoolClass.Id, dayIndex, period, null, onlyTeacher, raw);
                            continue;
                        }

                        warn($"课程表「{location}」的内容「{raw.Trim()}」无法识别学科，已忽略。");
                        continue;
                    }

                    var teacher = MatchTeacher(rest, project);
                    if (teacher is null)
                        warn($"课程表「{location}」的「{subject.Name}」未匹配到任课教师（原文：{raw.Trim()}），教师留空。");

                    AddScheduleLesson(project, schoolClass.Id, dayIndex, period, subject, teacher, raw);
                }
            }
        }
    }

    private static void AddScheduleLesson(
        WeaveProject project,
        string classId,
        int dayIndex,
        int period,
        Subject? subject,
        Teacher? teacher,
        string raw)
    {
        project.Lessons.Add(new LessonPlacement
        {
            ClassId = classId,
            DayIndex = dayIndex,
            Period = period,
            SubjectId = subject?.Id ?? string.Empty,
            TeacherId = teacher?.Id ?? string.Empty,
            IsFixed = false,
            Note = subject is null ? raw.Trim() : string.Empty,
        });
    }

    private static List<(string Text, Subject Subject)> BuildSubjectAliases(WeaveProject project)
    {
        var aliases = new List<(string Text, Subject Subject)>();

        foreach (var subject in project.Subjects)
        {
            aliases.Add((subject.Name, subject));
            foreach (var input in WeaveImportText.SubjectAliasInputs)
            {
                if (WeaveDefaults.SubjectAlias(input) == subject.Name)
                    aliases.Add((input, subject));
            }
        }

        return aliases
            .GroupBy(x => x.Text, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(x => x.Text.Length)
            .ToList();
    }

    private static Subject? MatchSubject(
        string compact,
        IReadOnlyList<(string Text, Subject Subject)> aliases,
        out string rest)
    {
        foreach (var (text, subject) in aliases)
        {
            if (compact.StartsWith(text, StringComparison.Ordinal))
            {
                rest = compact[text.Length..];
                return subject;
            }
        }

        rest = compact;
        return null;
    }

    private static Teacher? MatchTeacher(string remaining, WeaveProject project)
    {
        var text = WeaveImportText.Compact(remaining);
        if (text.Length == 0) return null;

        var ordered = project.Teachers.OrderByDescending(t => t.Name.Length).ToList();

        foreach (var teacher in ordered)
        {
            if (text.Contains(teacher.Name, StringComparison.Ordinal)) return teacher;
        }

        foreach (var teacher in ordered)
        {
            var shortName = teacher.Name.Length >= 2 ? teacher.Name[^2..] : teacher.Name;
            if (shortName.Length >= 2 && text.Contains(shortName, StringComparison.Ordinal))
                return teacher;
        }

        return null;
    }

    private static bool IsIgnoredScheduleCell(string compact) =>
        compact.Contains("班会", StringComparison.Ordinal)
        || compact.Contains("具体安排", StringComparison.Ordinal)
        || compact.Contains("见附件", StringComparison.Ordinal)
        || compact.Contains("附件", StringComparison.Ordinal)
        || compact.Contains("轮流", StringComparison.Ordinal)
        || compact.Contains("自习", StringComparison.Ordinal)
        || compact.Contains("考试", StringComparison.Ordinal)
        || compact.Contains("放假", StringComparison.Ordinal)
        || compact.Contains("午学", StringComparison.Ordinal)
        || compact.Contains("晚修", StringComparison.Ordinal)
        || compact.Contains("课服", StringComparison.Ordinal)
        || compact.Contains("课后服务", StringComparison.Ordinal)
        || compact.Contains("升旗", StringComparison.Ordinal)
        || compact.Contains("早读", StringComparison.Ordinal);

    private static SchoolClass? FindClass(WeaveProject project, string rawName)
    {
        var compact = WeaveImportText.Compact(rawName);
        var direct = project.Classes.FirstOrDefault(
            c => WeaveImportText.Compact(c.Name) == compact);
        if (direct is not null) return direct;

        var key = WeaveDefaults.ClassKey(compact);
        return project.Classes.FirstOrDefault(
            c => WeaveDefaults.ClassKey(WeaveImportText.Compact(c.Name)) == key);
    }

    // ----------------------------- 标题与备注 -----------------------------

    private static string ReadTitle(XLWorkbook workbook, IXLWorksheet? preferred)
    {
        var sheets = new List<IXLWorksheet>();
        if (preferred is not null) sheets.Add(preferred);
        sheets.AddRange(workbook.Worksheets.Where(s => !ReferenceEquals(s, preferred)));

        var fallback = string.Empty;

        foreach (var sheet in sheets)
        {
            var lastRow = Math.Min(4, sheet.LastRowUsed()?.RowNumber() ?? 0);
            var lastColumn = Math.Min(10, sheet.LastColumnUsed()?.ColumnNumber() ?? 0);

            for (var row = 1; row <= lastRow; row++)
            {
                for (var column = 1; column <= lastColumn; column++)
                {
                    var text = WeaveImportText.GetMergedText(sheet, row, column);
                    if (text.Length < 2) continue;

                    if (fallback.Length == 0 && text.Length > 2) fallback = text;

                    if (text.Contains("年级", StringComparison.Ordinal)
                        || text.Contains("学期", StringComparison.Ordinal)
                        || text.Contains("课表", StringComparison.Ordinal)
                        || text.Contains("分工", StringComparison.Ordinal)
                        || text.Contains("中学", StringComparison.Ordinal)
                        || text.Contains("学校", StringComparison.Ordinal))
                    {
                        return text;
                    }
                }
            }
        }

        return fallback;
    }

    private static void ApplyTitle(WeaveProject project, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;

        var schoolMatch = Regex.Match(
            title, @"([\u4e00-\u9fa5]{2,16}?(?:初级中学|实验学校|中学|小学|学校))");
        if (schoolMatch.Success) project.SchoolName = schoolMatch.Groups[1].Value;

        var gradeMatch = Regex.Match(title, @"([七八九])年级");
        if (!gradeMatch.Success)
            gradeMatch = Regex.Match(title, @"([一二三四五六七八九]|\d{1,2})\s*年级");
        if (gradeMatch.Success) project.GradeName = gradeMatch.Groups[1].Value + "年级";

        var termMatch = Regex.Match(title, @"(\d{4})\s*年\s*(春季|秋季)\s*学期");
        if (termMatch.Success)
        {
            project.TermName = $"{termMatch.Groups[1].Value}年{termMatch.Groups[2].Value}学期";
        }
        else
        {
            var schoolYear = Regex.Match(title, @"(\d{4})\s*[-—]\s*(\d{4})\s*学年");
            if (schoolYear.Success)
                project.TermName = $"{schoolYear.Groups[1].Value}-{schoolYear.Groups[2].Value}学年";
        }
    }

    private static string FindMeetingNote(XLWorkbook workbook, IXLWorksheet? preferred)
    {
        var sheets = new List<IXLWorksheet>();
        if (preferred is not null) sheets.Add(preferred);
        sheets.AddRange(workbook.Worksheets.Where(s => !ReferenceEquals(s, preferred)));

        foreach (var sheet in sheets)
        {
            var cell = sheet.CellsUsed().FirstOrDefault(
                c => c.GetString().TrimStart().StartsWith("会议活动备注", StringComparison.Ordinal));
            if (cell is not null) return cell.GetString().Trim();
        }

        return string.Empty;
    }

    private static void AddSheetSummaries(
        WeaveImportResult result,
        StaffingLayout? staffing,
        ConfirmLayout? confirm,
        ScheduleLayout? schedule)
    {
        var entries = new List<(IXLWorksheet Sheet, string Kind, string Detail)>();

        if (staffing is not null)
        {
            entries.Add((staffing.Sheet, "分工表",
                $"{staffing.ClassCount} 个班级 · {staffing.SubjectColumns.Count} 个学科列"));
        }

        if (confirm is not null)
            entries.Add((confirm.Sheet, "确认表", "教师名单、兼职岗位与学科周课时"));

        if (schedule is not null)
        {
            entries.Add((schedule.Sheet, "课程表",
                $"{schedule.ClassRows.Count} 个班级 · {schedule.DayStarts.Count} 天 × 7 节"));
        }

        foreach (var group in entries.GroupBy(e => e.Sheet))
        {
            result.Sheets.Add(new SheetSummary
            {
                Name = group.Key.Name.Trim(),
                Kind = string.Join(" / ", group.Select(e => e.Kind)),
                Rows = group.Key.LastRowUsed()?.RowNumber() ?? 0,
                Columns = group.Key.LastColumnUsed()?.ColumnNumber() ?? 0,
                Detail = string.Join("；", group.Select(e => e.Detail)),
            });
        }
    }

    // ----------------------------- 表格定位小工具 -----------------------------

    private static int FindExactColumn(IXLWorksheet sheet, int row, int lastColumn, string header)
    {
        for (var column = 1; column <= lastColumn; column++)
        {
            if (WeaveImportText.Compact(sheet.Cell(row, column).GetString()) == header)
                return column;
        }

        return -1;
    }

    private static int FindContainsColumn(IXLWorksheet sheet, int row, int lastColumn, string text)
    {
        for (var column = 1; column <= lastColumn; column++)
        {
            if (sheet.Cell(row, column).GetString().Contains(text, StringComparison.Ordinal))
                return column;
        }

        return -1;
    }

    private static int FindSubjectHeaderColumn(IXLWorksheet sheet, int row, int lastColumn)
    {
        for (var column = 1; column <= lastColumn; column++)
        {
            var text = WeaveImportText.Compact(sheet.Cell(row, column).GetString());
            if (text == "学科") return column;
            if (text.StartsWith("学科", StringComparison.Ordinal)
                && !text.Contains("课时", StringComparison.Ordinal))
            {
                return column;
            }
        }

        return -1;
    }

    private static int CountClassRows(
        IXLWorksheet sheet, int headerRow, int classColumn, int lastRow)
    {
        var count = 0;
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            if (WeaveImportText.LooksLikeClass(
                    WeaveImportText.GetMergedText(sheet, row, classColumn)))
            {
                count++;
            }
        }

        return count;
    }

    private static List<(int Day, int Column)> FindDayStarts(
        IXLWorksheet sheet, int row, int lastColumn)
    {
        var found = new List<(int Day, int Column)>();
        var seen = new HashSet<int>();

        for (var column = 1; column <= lastColumn; column++)
        {
            var day = WeaveImportText.DayIndex(sheet.Cell(row, column).GetString());
            if (day <= 0 || !seen.Add(day)) continue;
            found.Add((day, column));
        }

        return found.OrderBy(x => x.Day).ToList();
    }

    private static int FindPeriodRow(
        IXLWorksheet sheet,
        int headerRow,
        IReadOnlyList<(int Day, int Column)> dayStarts,
        int lastRow,
        int lastColumn)
    {
        var bestRow = -1;
        var bestCount = 0;

        for (var row = headerRow + 1; row <= Math.Min(headerRow + 4, lastRow); row++)
        {
            var count = 0;
            for (var dayPosition = 0; dayPosition < dayStarts.Count; dayPosition++)
            {
                var start = dayStarts[dayPosition].Column;
                var end = DayGroupEnd(dayStarts, dayPosition, lastColumn);
                for (var column = start; column <= end; column++)
                {
                    if (int.TryParse(sheet.Cell(row, column).GetString().Trim(), out var period)
                        && period is >= 1 and <= 7)
                    {
                        count++;
                    }
                }
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestRow = row;
            }
        }

        return bestCount >= 6 ? bestRow : -1;
    }

    private static int FindScheduleClassColumn(
        IXLWorksheet sheet, int headerRow, int periodRow, int lastRow, int lastColumn)
    {
        var exact = FindExactColumn(sheet, headerRow, lastColumn, "班级");
        if (exact > 0) return exact;

        var bestColumn = -1;
        var bestCount = 0;
        var searchLimit = Math.Min(10, lastColumn);

        for (var column = 1; column <= searchLimit; column++)
        {
            var count = 0;
            for (var row = periodRow + 1; row <= lastRow; row++)
            {
                if (WeaveImportText.LooksLikeClass(
                        WeaveImportText.GetMergedText(sheet, row, column)))
                {
                    count++;
                }
            }

            if (count > bestCount)
            {
                bestCount = count;
                bestColumn = column;
            }
        }

        return bestCount >= 2 ? bestColumn : -1;
    }

    private static List<ScheduleClassRow> ReadScheduleClassRows(
        IXLWorksheet sheet, int periodRow, int classColumn, int lastRow)
    {
        var rows = new List<ScheduleClassRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var row = periodRow + 1; row <= lastRow; row++)
        {
            var raw = WeaveImportText.GetMergedText(sheet, row, classColumn).Trim();
            if (!WeaveImportText.LooksLikeClass(raw)) continue;

            var key = WeaveImportText.Compact(raw);
            if (!seen.Add(key)) continue;

            rows.Add(new ScheduleClassRow { Row = row, Name = raw });
        }

        return rows;
    }

    private static int DayGroupEnd(
        IReadOnlyList<(int Day, int Column)> dayStarts, int position, int lastColumn)
    {
        var start = dayStarts[position].Column;
        var end = Math.Min(start + 9, lastColumn);

        if (position + 1 < dayStarts.Count)
            end = Math.Min(end, dayStarts[position + 1].Column - 1);

        return end;
    }

    private static int FindPeriodColumn(
        IXLWorksheet sheet, int periodRow, int dayStart, int groupEnd, int period)
    {
        for (var column = dayStart; column <= groupEnd; column++)
        {
            if (int.TryParse(sheet.Cell(periodRow, column).GetString().Trim(), out var value)
                && value == period)
            {
                return column;
            }
        }

        var fallback = Math.Min(dayStart + period - 1, groupEnd);
        return fallback >= dayStart ? fallback : -1;
    }

    private static int ParseInt(string raw)
    {
        var text = WeaveImportText.Compact(raw);
        if (text.Length == 0) return 0;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        var digits = new string(text.Where(char.IsDigit).ToArray());
        return digits.Length > 0
               && int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}
