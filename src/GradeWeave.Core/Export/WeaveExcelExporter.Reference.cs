using ClosedXML.Excel;
using GradeWeave.Core.Models;
using GradeWeave.Core.Scheduling;

namespace GradeWeave.Core.Export;

/// <summary>
/// 「参考校样式」版式与教师职位表。参考工作簿的结构：
/// 表一分工表、表二横向课程总表、表三教师职位表；宋体 / 楷体、黑色细框、无底纹。
/// </summary>
public sealed partial class WeaveExcelExporter
{
    // ----------------------------- 参考校：课程总表 -----------------------------

    private static void AddReferenceMasterSheet(
        XLWorkbook workbook,
        WeaveProject project,
        List<SchoolClass> classes,
        List<PeriodSlot> periods,
        SheetNameAllocator names)
    {
        var ws = AddReferenceSheet(workbook, names.Allocate("2-课程表", "课程表"));
        var days = WeaveDefaults.Days.Length;

        var teaching = periods.Where(p => !p.IsExtension).OrderBy(p => p.Index).ToList();
        if (teaching.Count == 0) teaching = WeaveDefaults.CreatePeriods();
        var morning = teaching.Where(p => !p.IsAfternoon).ToList();
        if (morning.Count == 0) morning = teaching.Take(Math.Min(4, teaching.Count)).ToList();
        var afternoon = teaching.Except(morning).ToList();
        var servicePeriods = periods
            .Where(p => p.IsExtension && p.Index is 8 or 9)
            .OrderBy(p => p.Index)
            .ToList();
        var latePeriods = periods
            .Where(p => p.IsExtension && p.Index >= 10)
            .OrderBy(p => p.Index)
            .ToList();

        // 参考样式：每天 7 节 + 课后服务 2 列 + 晚修 1 列；总表横向铺开。
        var perDay = teaching.Count + 3;
        var columnCount = 1 + days * perDay;

        var titleRange = ws.Range(1, 1, 1, columnCount);
        if (columnCount > 1) titleRange.Merge();
        titleRange.Cell(1, 1).Value = ReferenceHeading(project, "教师课程表", "表二");
        titleRange.Style.Font.FontName = "楷体";
        titleRange.Style.Font.FontSize = 20;
        titleRange.Style.Font.FontColor = XLColor.Black;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titleRange.Style.Alignment.WrapText = true;
        ws.Row(1).Height = 25.5;

        var corner = ws.Range(2, 1, 4, 1);
        corner.Merge();
        corner.Cell(1, 1).Value = "课  时\n\u3000 程 间\n班级";
        corner.Style.Font.FontName = "宋体";
        corner.Style.Font.FontSize = 10;
        corner.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        corner.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        corner.Style.Alignment.WrapText = true;

        ws.Row(2).Height = 19;
        ws.Row(3).Height = 19;
        ws.Row(4).Height = 19;

        for (var day = 1; day <= days; day++)
        {
            var start = 2 + (day - 1) * perDay;

            var dayRange = ws.Range(2, start, 2, start + perDay - 1);
            dayRange.Merge();
            dayRange.Cell(1, 1).Value = WeaveDefaults.DayName(day);
            dayRange.Style.Font.FontName = "楷体";
            dayRange.Style.Font.FontSize = 11;

            var morningRange = ws.Range(3, start, 3, start + morning.Count - 1);
            if (morning.Count > 1) morningRange.Merge();
            morningRange.Cell(1, 1).Value = "上午";
            morningRange.Style.Font.FontName = "楷体";
            morningRange.Style.Font.FontSize = 11;

            var afternoonStart = start + morning.Count;
            var afternoonEnd = start + morning.Count + afternoon.Count + 1;
            var afternoonRange = ws.Range(3, afternoonStart, 3, afternoonEnd);
            if (afternoonEnd > afternoonStart) afternoonRange.Merge();
            afternoonRange.Cell(1, 1).Value = "下午";
            afternoonRange.Style.Font.FontName = "楷体";
            afternoonRange.Style.Font.FontSize = 11;

            var lateColumn = start + perDay - 1;
            var lateRange = ws.Range(3, lateColumn, 4, lateColumn);
            lateRange.Merge();
            lateRange.Cell(1, 1).Value = "晚修";
            lateRange.Style.Font.FontName = "楷体";
            lateRange.Style.Font.FontSize = 11;

            for (var index = 0; index < teaching.Count; index++)
            {
                var cell = ws.Cell(4, start + index);
                cell.Value = teaching[index].Index;
                cell.Style.Font.FontName = "楷体";
                cell.Style.Font.FontSize = 8;
            }

            var serviceStart = start + morning.Count + afternoon.Count;
            var serviceRange = ws.Range(4, serviceStart, 4, serviceStart + 1);
            serviceRange.Merge();
            serviceRange.Cell(1, 1).Value = "课后服务";
            serviceRange.Style.Font.FontName = "楷体";
            serviceRange.Style.Font.FontSize = 8;
        }

        var row = 5;
        foreach (var cls in classes)
        {
            ws.Cell(row, 1).Value = cls.Name;
            ws.Cell(row, 1).Style.Font.FontName = "宋体";
            ws.Cell(row, 1).Style.Font.FontSize = 10;

            for (var day = 1; day <= days; day++)
            {
                var start = 2 + (day - 1) * perDay;

                for (var index = 0; index < teaching.Count; index++)
                {
                    var text = ReferenceLessonCellText(project, cls.Id, day, teaching[index].Index);
                    if (text.Length > 0) ws.Cell(row, start + index).Value = text;
                }

                var serviceStart = start + morning.Count + afternoon.Count;
                for (var index = 0; index < 2; index++)
                {
                    var slot = servicePeriods.Count > index ? servicePeriods[index] : null;
                    if (slot is null) continue;
                    var text = ReferenceLessonCellText(project, cls.Id, day, slot.Index);
                    if (text.Length > 0) ws.Cell(row, serviceStart + index).Value = text;
                }

                var lateColumn = start + perDay - 1;
                var lateText = ReferenceLateCellText(project, cls.Id, day, latePeriods);
                if (lateText.Length > 0) ws.Cell(row, lateColumn).Value = lateText;
            }

            ws.Row(row).Height = 43;
            row++;
        }

        var notesRange = ws.Range(row, 1, row, columnCount);
        notesRange.Merge();
        notesRange.Cell(1, 1).Value = BuildMeetingNotes(project);
        notesRange.Style.Font.FontName = "楷体";
        notesRange.Style.Font.FontSize = 12;
        notesRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        notesRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        notesRange.Style.Alignment.WrapText = true;
        ws.Row(row).Height = 72;

        ApplyReferenceGrid(ws, 1, row, columnCount);
        ws.Range(1, 1, 1, columnCount).Style.Font.FontName = "楷体";
        ws.Range(1, 1, 1, columnCount).Style.Font.FontSize = 20;
        ws.Range(2, 2, 3, columnCount).Style.Font.FontName = "楷体";
        ws.Range(2, 2, 3, columnCount).Style.Font.FontSize = 11;
        ws.Range(4, 2, 4, columnCount).Style.Font.FontName = "楷体";
        ws.Range(4, 2, 4, columnCount).Style.Font.FontSize = 8;
        ws.Cell(row, 1).Style.Font.FontName = "楷体";
        ws.Cell(row, 1).Style.Font.FontSize = 12;
        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        ws.Column(1).Width = 9.2;
        for (var column = 2; column <= columnCount; column++) ws.Column(column).Width = 5.2;

        ws.SheetView.FreezeRows(4);
        ws.SheetView.FreezeColumns(1);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.Scale = 55;
        ws.PageSetup.CenterHorizontally = true;
        ws.PageSetup.PrintAreas.Add(1, 1, row, columnCount);
    }

    // ----------------------------- 参考校：分工表 -----------------------------

    private static void AddReferenceAssignmentSheet(
        XLWorkbook workbook,
        WeaveProject project,
        List<SchoolClass> classes,
        SheetNameAllocator names)
    {
        var ws = AddReferenceSheet(workbook, names.Allocate("1-分工表", "分工表"));
        var subjects = project.Subjects;
        var columnCount = 2 + subjects.Count;

        var titleRange = ws.Range(1, 1, 1, columnCount);
        if (columnCount > 1) titleRange.Merge();
        titleRange.Cell(1, 1).Value = ReferenceHeading(project, "教师分工安排表", "表一");
        titleRange.Style.Font.FontName = "宋体";
        titleRange.Style.Font.FontSize = 14;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 17;

        ws.Cell(2, 1).Value = "班级";
        ws.Cell(2, 2).Value = "班主任";
        for (var index = 0; index < subjects.Count; index++)
            ws.Cell(2, 3 + index).Value = subjects[index].Name;
        ws.Row(2).Height = 17;

        var row = 3;
        foreach (var cls in classes)
        {
            ws.Cell(row, 1).Value = cls.Name;
            ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(cls.HeadTeacher) ? "—" : cls.HeadTeacher;

            for (var index = 0; index < subjects.Count; index++)
            {
                var subject = subjects[index];
                var teacherNames = project.Assignments
                    .Where(a => a.ClassId == cls.Id && a.SubjectId == subject.Id)
                    .Select(a => project.FindTeacher(a.TeacherId)?.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                ws.Cell(row, 3 + index).Value = teacherNames.Count > 0
                    ? string.Join("、", teacherNames)
                    : "—";
            }

            ws.Row(row).Height = 18;
            row++;
        }

        row = AddReferenceAssignmentSummaries(ws, project, classes, subjects, row);

        ApplyReferenceGrid(ws, 1, row - 1, columnCount, fontSize: 12);
        ws.Range(1, 1, 1, columnCount).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, columnCount).Style.Font.Bold = true;
        ws.Range(2, 1, 2, columnCount).Style.Font.FontSize = 14;

        ws.Column(1).Width = 17.8;
        ws.Column(2).Width = 12.7;
        for (var column = 3; column <= columnCount; column++) ws.Column(column).Width = 12.7;

        ws.SheetView.FreezeRows(2);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.CenterHorizontally = true;
        ws.PageSetup.PrintAreas.Add(1, 1, row - 1, columnCount);
    }

    private static int AddReferenceAssignmentSummaries(
        IXLWorksheet ws,
        WeaveProject project,
        List<SchoolClass> classes,
        List<Subject> subjects,
        int row)
    {
        var experimenters = TeachersWithRole(project, "实验员")
            .Select(name => project.FindTeacherByName(name))
            .OfType<Teacher>()
            .ToList();
        if (experimenters.Count > 0)
        {
            ws.Cell(row, 1).Value = "实验员";
            foreach (var teacher in experimenters)
            {
                var index = SubjectIndexForTeacher(project, subjects, teacher);
                if (index >= 0) ws.Cell(row, 3 + index).Value = teacher.Name;
            }
            row++;
        }

        var totalHours = subjects.Sum(subject => subject.WeeklyHours);
        ws.Cell(row, 1).Value = $"课时-{totalHours}节";
        for (var index = 0; index < subjects.Count; index++)
            ws.Cell(row, 3 + index).Value = subjects[index].WeeklyHours;
        row++;

        ws.Cell(row, 1).Value = $"人数-{project.Teachers.Count}人";
        ws.Cell(row, 2).Value = classes.Count;
        for (var index = 0; index < subjects.Count; index++)
        {
            var subject = subjects[index];
            ws.Cell(row, 3 + index).Value = project.Assignments
                .Where(a => a.SubjectId == subject.Id)
                .Select(a => a.TeacherId)
                .Distinct(StringComparer.Ordinal)
                .Count();
        }
        row++;

        var hasLessonLeader = project.Teachers.Any(t => t.Roles.Contains("备课组长", StringComparer.Ordinal));
        var hasSubjectLeader = project.Teachers.Any(t => t.Roles.Contains("科组长", StringComparer.Ordinal));
        var leaders = TeachersWithRole(project, "年级长");
        var principal = TeachersWithRole(project, "校长")
            .Concat(TeachersWithRole(project, "管级校长"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (hasLessonLeader)
        {
            ws.Cell(row, 1).Value = "年级";
            ws.Cell(row, 2).Value = "备课组长";
            for (var index = 0; index < subjects.Count; index++)
                ws.Cell(row, 3 + index).Value = RoleNamesForSubject(project, "备课组长", subjects[index]);
            row++;
        }

        if (hasSubjectLeader)
        {
            ws.Cell(row, 1).Value = "全校";
            ws.Cell(row, 2).Value = "科组长";
            for (var index = 0; index < subjects.Count; index++)
                ws.Cell(row, 3 + index).Value = RoleNamesForSubject(project, "科组长", subjects[index]);
            row++;
        }

        if (leaders.Count > 0 || principal.Count > 0)
        {
            ws.Cell(row, 1).Value = "年级管理组";
            ws.Cell(row, 2).Value = "年级长";
            ws.Cell(row + 1, 2).Value = "管级校长";
            ws.Range(row, 1, row + 1, 1).Merge();

            var leaderRange = ws.Range(row, 3, row, 2 + subjects.Count);
            if (subjects.Count > 1) leaderRange.Merge();
            leaderRange.Cell(1, 1).Value = leaders.Count > 0 ? string.Join("   ", leaders) : "—";

            var principalRange = ws.Range(row + 1, 3, row + 1, 2 + subjects.Count);
            if (subjects.Count > 1) principalRange.Merge();
            principalRange.Cell(1, 1).Value = principal.Count > 0 ? string.Join("   ", principal) : "—";

            row += 2;
        }

        return row;
    }

    // ----------------------------- 教师职位表 -----------------------------

    private static void AddTeacherPositionSheet(
        XLWorkbook workbook,
        WeaveProject project,
        List<Teacher> teachers,
        SheetNameAllocator names,
        WeaveExportStyle style)
    {
        if (style == WeaveExportStyle.Reference)
            AddReferenceTeacherPositionSheet(workbook, project, teachers, names);
        else
            AddSoftwareTeacherPositionSheet(workbook, project, teachers, names);
    }

    private static void AddSoftwareTeacherPositionSheet(
        XLWorkbook workbook,
        WeaveProject project,
        List<Teacher> teachers,
        SheetNameAllocator names)
    {
        var ws = AddSheet(workbook, names.Allocate("教师职位表", "教师职位表"));
        const int columnCount = 8;

        WriteTitle(ws, columnCount, "教师职位表",
            $"共 {teachers.Count} 位教师 · 兼职岗位 / 任教班级 / 学科周课时");

        var headers = new[] { "序号", "姓名", "学科", "兼职岗位", "任教班级", "学科周课时", "联系电话", "确认签名" };
        for (var index = 0; index < headers.Length; index++)
            ws.Cell(3, 1 + index).Value = headers[index];
        StyleHeader(ws.Range(3, 1, 3, columnCount));

        var row = 4;
        foreach (var teacher in OrderedTeachers(project, teachers))
        {
            ws.Cell(row, 1).Value = row - 3;
            ws.Cell(row, 2).Value = teacher.Name;
            ws.Cell(row, 2).Style.Font.Bold = true;
            ws.Cell(row, 3).Value = PrimarySubjectText(project, teacher);
            ws.Cell(row, 4).Value = RolesText(project, teacher);
            ws.Cell(row, 5).Value = ClassListText(project, teacher);
            ws.Cell(row, 6).Value = SubjectHoursOfTeacher(project, teacher);
            ws.Cell(row, 8).Value = string.Empty;
            ws.Row(row).Height = 24;
            row++;
        }

        StyleGrid(ws.Range(3, 1, Math.Max(3, row - 1), columnCount));
        ws.Column(1).Width = 7;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 20;
        ws.Column(4).Width = 34;
        ws.Column(5).Width = 30;
        ws.Column(6).Width = 14;
        ws.Column(7).Width = 16;
        ws.Column(8).Width = 16;

        ws.SheetView.FreezeRows(3);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
    }

    private static void AddReferenceTeacherPositionSheet(
        XLWorkbook workbook,
        WeaveProject project,
        List<Teacher> teachers,
        SheetNameAllocator names)
    {
        var ws = AddReferenceSheet(workbook, names.Allocate("3-教师职位表", "教师职位表"));
        const int columnCount = 8;

        var titleRange = ws.Range(1, 1, 1, columnCount);
        titleRange.Merge();
        titleRange.Cell(1, 1).Value = ReferenceHeading(project, "教师职位安排表", "表三");
        titleRange.Style.Font.FontName = "宋体";
        titleRange.Style.Font.FontSize = 16;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 32;

        var headers = new[] { "序号", "姓名", "学科", "兼职岗位", "任教班级", "学科周课时", "联系电话", "确认签名" };
        for (var index = 0; index < headers.Length; index++)
        {
            var header = ws.Cell(2, 1 + index);
            header.Value = headers[index];
            header.Style.Font.FontName = "宋体";
            header.Style.Font.FontSize = 12;
        }
        ws.Row(2).Height = 21;

        var row = 3;
        foreach (var teacher in OrderedTeachers(project, teachers))
        {
            ws.Cell(row, 1).Value = row - 2;
            ws.Cell(row, 2).Value = teacher.Name;
            ws.Cell(row, 3).Value = PrimarySubjectText(project, teacher);
            ws.Cell(row, 4).Value = RolesText(project, teacher);
            ws.Cell(row, 5).Value = ClassListText(project, teacher);
            ws.Cell(row, 6).Value = SubjectHoursOfTeacher(project, teacher);
            ws.Cell(row, 7).Value = string.Empty;
            ws.Cell(row, 8).Value = string.Empty;
            ws.Row(row).Height = 21;
            row++;
        }

        ApplyReferenceGrid(ws, 1, Math.Max(2, row - 1), columnCount, fontSize: 11);
        ws.Range(1, 1, 1, columnCount).Style.Font.FontSize = 16;
        ws.Range(1, 1, 1, columnCount).Style.Font.FontName = "宋体";
        ws.Range(2, 1, 2, columnCount).Style.Font.FontSize = 12;
        ws.Range(2, 1, 2, columnCount).Style.Font.FontName = "宋体";

        ws.Column(1).Width = 6;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 10;
        ws.Column(4).Width = 30;
        ws.Column(5).Width = 30;
        ws.Column(6).Width = 12;
        ws.Column(7).Width = 16;
        ws.Column(8).Width = 14;

        ws.SheetView.FreezeRows(2);
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.Scale = 80;
        ws.PageSetup.CenterHorizontally = true;
        ws.PageSetup.PrintAreas.Add(1, 1, Math.Max(2, row - 1), columnCount);
    }

    // ----------------------------- 参考校：共用工具 -----------------------------

    private static IXLWorksheet AddReferenceSheet(XLWorkbook workbook, string name)
    {
        var ws = AddSheet(workbook, name, WeaveExportStyle.Reference);
        ws.ShowGridLines = false;
        return ws;
    }

    private static void ApplyReferenceGrid(
        IXLWorksheet ws, int firstRow, int lastRow, int lastColumn, int fontSize = 10)
    {
        var range = ws.Range(firstRow, 1, lastRow, lastColumn);
        range.Style.Font.FontName = "宋体";
        range.Style.Font.FontSize = fontSize;
        range.Style.Font.FontColor = XLColor.Black;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = XLColor.Black;
        range.Style.Border.InsideBorderColor = XLColor.Black;
    }

    private static string ReferenceHeading(WeaveProject project, string suffix, string prefix)
    {
        var school = string.IsNullOrWhiteSpace(project.SchoolName) ? "本校" : project.SchoolName.Trim();
        var term = project.TermName.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        var grade = project.GradeName.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        return $"{prefix}：{school}{term}{grade}{suffix}";
    }

    private static string ReferenceLessonCellText(
        WeaveProject project, string classId, int day, int period)
    {
        var lessons = project.Lessons
            .Where(lesson => lesson.ClassId == classId &&
                             lesson.DayIndex == day &&
                             lesson.Period == period)
            .ToList();
        if (lessons.Count == 0) return string.Empty;

        var current = lessons.FirstOrDefault(lesson => !lesson.IsLayered) ?? lessons[0];
        if (current.IsActivity)
            return string.IsNullOrWhiteSpace(current.ActivityName) ? "活动" : current.ActivityName;

        var subject = project.FindSubject(current.SubjectId)?.Name ?? string.Empty;
        var layered = lessons.Where(WeaveExportSupport.IsLayered).ToList();
        if (layered.Count > 0)
        {
            var teachers = layered
                .Select(lesson => ShortTeacherName(project.FindTeacher(lesson.TeacherId)?.Name))
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return subject + string.Join("/", teachers);
        }

        return subject + ShortTeacherName(project.FindTeacher(current.TeacherId)?.Name);
    }

    private static string ReferenceLateCellText(
        WeaveProject project, string classId, int day, List<PeriodSlot> latePeriods)
    {
        var parts = latePeriods
            .Select(slot => ReferenceLessonCellText(project, classId, day, slot.Index))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return parts.Count == 0 ? string.Empty : string.Join("/", parts);
    }

    private static string ShortTeacherName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var normalized = WeaveDefaults.NormalizeTeacherName(name);
        return normalized.Length <= 2 ? normalized : normalized[^2..];
    }

    private static string BuildMeetingNotes(WeaveProject project)
    {
        var parts = new List<string>();
        foreach (var block in project.ResearchBlocks
                     .OrderBy(item => item.DayIndex)
                     .ThenBy(item => item.StartPeriod))
        {
            var subjects = block.SubjectIds
                .Select(id => project.FindSubject(id)?.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (subjects.Count == 0) continue;
            parts.Add($"教研：{WeaveDefaults.SlotText(block.DayIndex, block.StartPeriod, block.EndPeriod)} {string.Join("、", subjects)}");
        }

        foreach (var block in project.MeetingBlocks
                     .OrderBy(item => item.DayIndex)
                     .ThenBy(item => item.StartPeriod))
        {
            parts.Add($"会议：{block.Name} {WeaveDefaults.SlotText(block.DayIndex, block.StartPeriod, block.EndPeriod)}");
        }

        foreach (var activity in project.Activities
                     .OrderBy(item => item.DayIndex)
                     .ThenBy(item => item.StartTime, StringComparer.Ordinal))
        {
            parts.Add($"活动：{activity.Name} {WeaveDefaults.DayName(activity.DayIndex)} {ActivityResolver.TimeText(activity)}");
        }

        foreach (var note in project.Notes.Where(note => !string.IsNullOrWhiteSpace(note)))
            parts.Add(note.Trim());

        if (parts.Count == 0) return "会议活动备注：暂无";
        var numbered = parts.Select((part, index) => $"{index + 1}、{part}");
        return "会议活动备注：" + string.Join("；", numbered);
    }

    private static List<Teacher> OrderedTeachers(WeaveProject project, List<Teacher> teachers)
    {
        var order = project.Subjects
            .Select((subject, index) => (subject.Name, Index: index))
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.Ordinal);
        return teachers
            .OrderBy(teacher => order.GetValueOrDefault(teacher.Subject, int.MaxValue))
            .ThenBy(teacher => teacher.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static string PrimarySubjectText(WeaveProject project, Teacher teacher)
    {
        if (!string.IsNullOrWhiteSpace(teacher.Subject)) return teacher.Subject;
        var text = WeaveExportSupport.SubjectText(project, teacher);
        return text == "—" ? "—" : text;
    }

    private static string RolesText(WeaveProject project, Teacher teacher)
    {
        var roles = teacher.Roles.ToList();
        if (!roles.Contains("班主任", StringComparer.Ordinal) &&
            project.Classes.Any(cls => cls.HeadTeacher == teacher.Name))
            roles.Insert(0, "班主任");
        return roles.Count == 0 ? "—" : string.Join("/", roles);
    }

    private static string ClassListText(WeaveProject project, Teacher teacher)
    {
        var classes = project.Assignments
            .Where(assignment => assignment.TeacherId == teacher.Id)
            .Select(assignment => project.FindClass(assignment.ClassId))
            .Where(cls => cls is not null)
            .Select(cls => ShortClassNumber(cls!.Name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return classes.Count == 0 ? "—" : string.Join("、", classes) + "班";
    }

    private static string ShortClassNumber(string name)
    {
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : name.Trim();
    }

    private static int SubjectHoursOfTeacher(WeaveProject project, Teacher teacher)
    {
        var total = project.Assignments
            .Where(assignment => assignment.TeacherId == teacher.Id)
            .Sum(assignment => assignment.WeeklyHours);
        return total > 0 ? total : teacher.WeeklyHours;
    }

    private static List<string> TeachersWithRole(WeaveProject project, string role)
    {
        return project.Teachers
            .Where(teacher =>
                teacher.Roles.Contains(role, StringComparer.Ordinal) ||
                (role == "班主任" && project.Classes.Any(cls => cls.HeadTeacher == teacher.Name)))
            .Select(teacher => teacher.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string RoleNamesForSubject(WeaveProject project, string role, Subject subject)
    {
        var names = project.Teachers
            .Where(teacher =>
                teacher.Roles.Contains(role, StringComparer.Ordinal) &&
                (teacher.Subject == subject.Name ||
                 project.Assignments.Any(assignment =>
                     assignment.TeacherId == teacher.Id && assignment.SubjectId == subject.Id)))
            .Select(teacher => teacher.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return names.Count == 0 ? "—" : string.Join("、", names);
    }

    private static int SubjectIndexForTeacher(
        WeaveProject project, List<Subject> subjects, Teacher teacher)
    {
        var byName = subjects.FindIndex(subject => subject.Name == teacher.Subject);
        if (byName >= 0) return byName;

        var subjectId = project.Assignments
            .FirstOrDefault(assignment => assignment.TeacherId == teacher.Id)?.SubjectId;
        return string.IsNullOrWhiteSpace(subjectId) ? -1 : subjects.FindIndex(subject => subject.Id == subjectId);
    }
}
