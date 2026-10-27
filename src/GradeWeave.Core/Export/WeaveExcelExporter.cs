using ClosedXML.Excel;
using GradeWeave.Core.Models;

namespace GradeWeave.Core.Export;

/// <summary>导出工作簿：总表、分工表、班级课表、教师课表与冲突页。</summary>
public sealed class WeaveExcelExporter
{
    private static readonly XLColor Paper = XLColor.FromHtml("#FAF9F5");
    private static readonly XLColor Ink = XLColor.FromHtml("#141413");
    private static readonly XLColor Clay = XLColor.FromHtml("#D97757");
    private static readonly XLColor Muted = XLColor.FromHtml("#5F5A4E");
    private static readonly XLColor Line = XLColor.FromHtml("#D8D5CC");

    public void Export(WeaveProject project, IReadOnlyList<WeaveConflict> conflicts, string path, ExportOptions? options = null)
    {
        try
        {
            var opts = options ?? new ExportOptions();
            WeaveExportSupport.Prepare(project, path);

            var periods = WeaveExportSupport.PeriodsOf(project);
            var conflictItems = conflicts ?? Array.Empty<WeaveConflict>();
            var names = new SheetNameAllocator();

            var allClasses = project.Classes.ToList();
            var allTeachers = project.Teachers.ToList();
            var classPageScope = ScopeClasses(project, opts, allClasses);
            var teacherPageScope = ScopeTeachers(project, opts, allTeachers);

            using var workbook = new XLWorkbook();

            if (opts.IncludeMasterSheet)
                AddMasterSheet(workbook, project, allClasses, periods, names);

            if (opts.IncludeAssignmentSheet)
                AddAssignmentSheet(workbook, project, allClasses, names);

            if (opts.IncludeClassSheets)
            {
                var sheets = classPageScope.ToDictionary(c => c.Id, c => names.Allocate(c.Name, "班级课表"));
                AddClassIndexSheet(workbook, project, classPageScope, sheets, names);
                foreach (var cls in classPageScope)
                    AddClassSheet(workbook, project, cls, sheets[cls.Id], periods);
            }

            if (opts.IncludeTeacherSheets)
            {
                var sheets = teacherPageScope.ToDictionary(t => t.Id, t => names.Allocate(t.Name, "教师课表"));
                AddTeacherIndexSheet(workbook, project, teacherPageScope, sheets, names);
                foreach (var teacher in teacherPageScope)
                    AddTeacherSheet(workbook, project, teacher, sheets[teacher.Id], periods);
            }

            if (opts.IncludeConflictSheet)
                AddConflictSheet(workbook, project, conflictItems, names);

            workbook.SaveAs(path);
        }
        catch (GradeWeaveException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new GradeWeaveException("导出课表工作簿失败，请检查保存位置后重试。", ex);
        }
    }

    // ----------------------------- 页签范围 -----------------------------

    private static List<SchoolClass> ScopeClasses(WeaveProject project, ExportOptions options, List<SchoolClass> all)
    {
        if (string.IsNullOrWhiteSpace(options.OnlyClassId)) return all;

        var target = project.FindClass(options.OnlyClassId);
        if (target is null) throw new GradeWeaveException("未找到指定的班级，请重新选择后再导出。");
        return [target];
    }

    private static List<Teacher> ScopeTeachers(WeaveProject project, ExportOptions options, List<Teacher> all)
    {
        if (string.IsNullOrWhiteSpace(options.OnlyTeacherId)) return all;

        var target = project.FindTeacher(options.OnlyTeacherId);
        if (target is null) throw new GradeWeaveException("未找到指定的教师，请重新选择后再导出。");
        return [target];
    }

    // ----------------------------- 总表 -----------------------------

    private static void AddMasterSheet(
        XLWorkbook workbook,
        WeaveProject project,
        List<SchoolClass> classes,
        List<PeriodSlot> periods,
        SheetNameAllocator names)
    {
        var ws = AddSheet(workbook, names.Allocate("总表", "总表"));
        var days = WeaveDefaults.Days.Length;
        var columnCount = 1 + days * periods.Count;

        WriteTitle(ws, columnCount,
            WeaveExportSupport.Heading(project, "总课表"),
            $"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm} · 本机生成，文件不上传");

        var corner = ws.Range(3, 1, 4, 1);
        if (corner.Cells().Count() > 1) corner.Merge();
        corner.Cell(1, 1).Value = "班级";

        for (var day = 1; day <= days; day++)
        {
            var start = 2 + (day - 1) * periods.Count;
            var end = start + periods.Count - 1;
            var dayRange = ws.Range(3, start, 3, end);
            if (end > start) dayRange.Merge();
            dayRange.Cell(1, 1).Value = WeaveDefaults.DayName(day);

            for (var i = 0; i < periods.Count; i++)
                ws.Cell(4, start + i).Value = periods[i].Label;
        }

        StyleHeader(ws.Range(3, 1, 4, columnCount));

        var row = 5;
        foreach (var cls in classes)
        {
            ws.Cell(row, 1).Value = cls.Name;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            for (var day = 1; day <= days; day++)
            {
                var start = 2 + (day - 1) * periods.Count;
                for (var i = 0; i < periods.Count; i++)
                {
                    var lesson = project.LessonAt(cls.Id, day, periods[i].Index);
                    var text = WeaveExportSupport.CellText(project, cls.Id, day, periods[i].Index, lesson);
                    if (text.Length == 0) continue;

                    var cell = ws.Cell(row, start + i);
                    cell.Value = text;
                    if (lesson?.IsActivity == true || lesson is null || WeaveExportSupport.IsLayered(lesson))
                        cell.Style.Font.FontColor = Clay;
                }
            }

            ws.Row(row).Height = 46;
            row++;
        }

        StyleGrid(ws.Range(3, 1, Math.Max(4, row - 1), columnCount));
        ws.Column(1).Width = 14;
        for (var column = 2; column <= columnCount; column++) ws.Column(column).Width = 10.5;

        ws.SheetView.FreezeRows(4);
        ws.SheetView.FreezeColumns(1);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
    }

    // ----------------------------- 分工表 -----------------------------

    private static void AddAssignmentSheet(
        XLWorkbook workbook,
        WeaveProject project,
        List<SchoolClass> classes,
        SheetNameAllocator names)
    {
        var ws = AddSheet(workbook, names.Allocate("分工表", "分工表"));
        var subjects = project.Subjects;
        var columnCount = 2 + subjects.Count;

        WriteTitle(ws, columnCount,
            WeaveExportSupport.Heading(project, "分工表"),
            $"共 {classes.Count} 个班级 · {subjects.Count} 个学科");

        ws.Cell(3, 1).Value = "班级";
        ws.Cell(3, 2).Value = "班主任";
        for (var i = 0; i < subjects.Count; i++)
            ws.Cell(3, 3 + i).Value = subjects[i].Name;
        StyleHeader(ws.Range(3, 1, 3, columnCount));

        var row = 4;
        foreach (var cls in classes)
        {
            ws.Cell(row, 1).Value = cls.Name;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(cls.HeadTeacher) ? "—" : cls.HeadTeacher;

            for (var i = 0; i < subjects.Count; i++)
            {
                var subject = subjects[i];
                var teacherNames = project.Assignments
                    .Where(a => a.ClassId == cls.Id && a.SubjectId == subject.Id)
                    .Select(a => project.FindTeacher(a.TeacherId)?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                ws.Cell(row, 3 + i).Value = teacherNames.Count > 0 ? string.Join("、", teacherNames) : "—";
            }

            ws.Row(row).Height = 24;
            row++;
        }

        var classIds = classes.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        var hoursRow = row + 1;
        ws.Cell(hoursRow, 1).Value = "课时";
        for (var i = 0; i < subjects.Count; i++)
            ws.Cell(hoursRow, 3 + i).Value = subjects[i].WeeklyHours;

        var teacherCountRow = hoursRow + 1;
        ws.Cell(teacherCountRow, 1).Value = "教师人数";
        for (var i = 0; i < subjects.Count; i++)
        {
            var count = project.Assignments
                .Where(a => a.SubjectId == subjects[i].Id && classIds.Contains(a.ClassId))
                .Select(a => a.TeacherId)
                .Distinct(StringComparer.Ordinal)
                .Count();
            ws.Cell(teacherCountRow, 3 + i).Value = count;
        }

        StyleGrid(ws.Range(3, 1, Math.Max(3, row - 1), columnCount));
        StyleGrid(ws.Range(hoursRow, 1, teacherCountRow, columnCount));
        foreach (var summaryRow in new[] { hoursRow, teacherCountRow })
        {
            ws.Range(summaryRow, 1, summaryRow, columnCount).Style.Font.Bold = true;
            ws.Range(summaryRow, 1, summaryRow, columnCount).Style.Fill.BackgroundColor = Paper;
            ws.Row(summaryRow).Height = 24;
        }

        ws.Column(1).Width = 14;
        ws.Column(2).Width = 12;
        for (var column = 3; column <= columnCount; column++) ws.Column(column).Width = 12;

        ws.SheetView.FreezeRows(3);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
    }

    // ----------------------------- 班级课表 -----------------------------

    private static void AddClassIndexSheet(
        XLWorkbook workbook,
        WeaveProject project,
        List<SchoolClass> classes,
        IReadOnlyDictionary<string, string> sheetNames,
        SheetNameAllocator names)
    {
        var ws = AddSheet(workbook, names.Allocate("班级课表", "班级课表"));

        WriteTitle(ws, 4, "班级课表", $"共 {classes.Count} 个班级 · 点击下方页签查看各班课表");

        ws.Cell(3, 1).Value = "班级";
        ws.Cell(3, 2).Value = "班主任";
        ws.Cell(3, 3).Value = "已排 / 应排";
        ws.Cell(3, 4).Value = "页签";
        StyleHeader(ws.Range(3, 1, 3, 4));

        var row = 4;
        foreach (var cls in classes)
        {
            var required = project.AssignmentsOfClass(cls.Id).Sum(a => a.WeeklyHours);
            var placed = project.Lessons.Count(l => l.ClassId == cls.Id && !l.IsActivity);

            ws.Cell(row, 1).Value = cls.Name;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(cls.HeadTeacher) ? "—" : cls.HeadTeacher;
            ws.Cell(row, 3).Value = $"{placed} / {required}";
            ws.Cell(row, 4).Value = sheetNames[cls.Id];
            ws.Row(row).Height = 22;
            row++;
        }

        StyleGrid(ws.Range(3, 1, Math.Max(3, row - 1), 4));
        ws.Column(1).Width = 16;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 14;
        ws.Column(4).Width = 22;
        ws.SheetView.FreezeRows(3);
    }

    private static void AddClassSheet(
        XLWorkbook workbook,
        WeaveProject project,
        SchoolClass cls,
        string sheetName,
        List<PeriodSlot> periods)
    {
        var ws = AddSheet(workbook, sheetName);
        var days = WeaveDefaults.Days.Length;
        var columnCount = 1 + days;

        var subtitleParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(project.GradeName)) subtitleParts.Add(project.GradeName);
        if (!string.IsNullOrWhiteSpace(project.TermName)) subtitleParts.Add(project.TermName);
        if (!string.IsNullOrWhiteSpace(cls.HeadTeacher)) subtitleParts.Add($"班主任：{cls.HeadTeacher}");

        WriteTitle(ws, columnCount, $"{cls.Name} 课程表", string.Join(" · ", subtitleParts));

        ws.Cell(3, 1).Value = "节次 / 星期";
        for (var day = 1; day <= days; day++)
            ws.Cell(3, 1 + day).Value = WeaveDefaults.DayName(day);
        StyleHeader(ws.Range(3, 1, 3, columnCount));

        var row = 4;
        foreach (var slot in periods)
        {
            ws.Cell(row, 1).Value = string.IsNullOrWhiteSpace(slot.TimeText)
                ? slot.Label
                : $"{slot.Label}\n{slot.TimeText}";
            ws.Cell(row, 1).Style.Font.Bold = true;

            for (var day = 1; day <= days; day++)
            {
                var lesson = project.LessonAt(cls.Id, day, slot.Index);
                var text = WeaveExportSupport.CellText(project, cls.Id, day, slot.Index, lesson);
                if (text.Length == 0) continue;

                var cell = ws.Cell(row, 1 + day);
                cell.Value = text;
                if (lesson?.IsActivity == true || lesson is null || WeaveExportSupport.IsLayered(lesson))
                    cell.Style.Font.FontColor = Clay;
            }

            ws.Row(row).Height = 52;
            row++;
        }

        StyleGrid(ws.Range(3, 1, Math.Max(3, row - 1), columnCount));
        ws.Column(1).Width = 15;
        for (var column = 2; column <= columnCount; column++) ws.Column(column).Width = 22;

        ws.SheetView.FreezeRows(3);
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
    }

    // ----------------------------- 教师课表 -----------------------------

    private static void AddTeacherIndexSheet(
        XLWorkbook workbook,
        WeaveProject project,
        List<Teacher> teachers,
        IReadOnlyDictionary<string, string> sheetNames,
        SheetNameAllocator names)
    {
        var ws = AddSheet(workbook, names.Allocate("教师课表", "教师课表"));

        WriteTitle(ws, 5, "教师课表", $"共 {teachers.Count} 位教师 · 点击下方页签查看个人课表");

        ws.Cell(3, 1).Value = "教师";
        ws.Cell(3, 2).Value = "学科";
        ws.Cell(3, 3).Value = "已排";
        ws.Cell(3, 4).Value = "应排";
        ws.Cell(3, 5).Value = "页签";
        StyleHeader(ws.Range(3, 1, 3, 5));

        var row = 4;
        foreach (var teacher in teachers)
        {
            var (placed, required) = WeaveExportSupport.TeacherLoad(project, teacher);

            ws.Cell(row, 1).Value = teacher.Name;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = WeaveExportSupport.SubjectText(project, teacher);
            ws.Cell(row, 3).Value = placed;
            ws.Cell(row, 4).Value = required;
            ws.Cell(row, 5).Value = sheetNames[teacher.Id];
            ws.Row(row).Height = 22;
            row++;
        }

        StyleGrid(ws.Range(3, 1, Math.Max(3, row - 1), 5));
        ws.Column(1).Width = 14;
        ws.Column(2).Width = 18;
        ws.Column(3).Width = 9;
        ws.Column(4).Width = 9;
        ws.Column(5).Width = 22;
        ws.SheetView.FreezeRows(3);
    }

    private static void AddTeacherSheet(
        XLWorkbook workbook,
        WeaveProject project,
        Teacher teacher,
        string sheetName,
        List<PeriodSlot> periods)
    {
        var ws = AddSheet(workbook, sheetName);
        var days = WeaveDefaults.Days.Length;
        var columnCount = 1 + days;
        var (placed, required) = WeaveExportSupport.TeacherLoad(project, teacher);

        WriteTitle(ws, columnCount,
            $"{teacher.Name} 老师 课表",
            $"{WeaveExportSupport.SubjectText(project, teacher)} · 周课时 已排 {placed} / 应排 {required}");

        ws.Cell(3, 1).Value = "节次 / 星期";
        for (var day = 1; day <= days; day++)
            ws.Cell(3, 1 + day).Value = WeaveDefaults.DayName(day);
        StyleHeader(ws.Range(3, 1, 3, columnCount));

        var row = 4;
        foreach (var slot in periods)
        {
            ws.Cell(row, 1).Value = string.IsNullOrWhiteSpace(slot.TimeText)
                ? slot.Label
                : $"{slot.Label}\n{slot.TimeText}";
            ws.Cell(row, 1).Style.Font.Bold = true;

            for (var day = 1; day <= days; day++)
            {
                var lessons = project.Lessons
                    .Where(l => l.TeacherId == teacher.Id && l.DayIndex == day && l.Period == slot.Index)
                    .ToList();
                if (lessons.Count == 0) continue;

                var text = string.Join("/", lessons
                    .Select(l => WeaveExportSupport.TeacherCell(project, l))
                    .Distinct(StringComparer.Ordinal));
                var cell = ws.Cell(row, 1 + day);
                cell.Value = text;
                if (lessons.Any(WeaveExportSupport.IsLayered))
                    cell.Style.Font.FontColor = Clay;
            }

            ws.Row(row).Height = 40;
            row++;
        }

        StyleGrid(ws.Range(3, 1, Math.Max(3, row - 1), columnCount));
        ws.Column(1).Width = 15;
        for (var column = 2; column <= columnCount; column++) ws.Column(column).Width = 18;

        ws.SheetView.FreezeRows(3);
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
    }

    // ----------------------------- 冲突 -----------------------------

    private static void AddConflictSheet(
        XLWorkbook workbook,
        WeaveProject project,
        IReadOnlyList<WeaveConflict> conflicts,
        SheetNameAllocator names)
    {
        var ws = AddSheet(workbook, names.Allocate("冲突", "冲突"));
        const int columnCount = 5;

        WriteTitle(ws, columnCount,
            "冲突",
            $"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm} · 冲突 {conflicts.Count} 条 · 未排课 {conflicts.Count(c => c.Kind == ConflictKind.Unplaced)} 条");

        ws.Cell(3, 1).Value = "类型";
        ws.Cell(3, 2).Value = "位置";
        ws.Cell(3, 3).Value = "班级";
        ws.Cell(3, 4).Value = "教师";
        ws.Cell(3, 5).Value = "说明";
        StyleHeader(ws.Range(3, 1, 3, columnCount));

        if (conflicts.Count == 0)
        {
            var message = ws.Range(4, 1, 4, columnCount);
            message.Merge();
            message.Cell(1, 1).Value = "未发现冲突";
            message.Cell(1, 1).Style.Font.Bold = true;
            message.Cell(1, 1).Style.Font.FontColor = Clay;
            message.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(4).Height = 28;
        }
        else
        {
            var row = 4;
            foreach (var conflict in conflicts)
            {
                ws.Cell(row, 1).Value = WeaveExportSupport.ConflictKindText(conflict.Kind);
                ws.Cell(row, 2).Value = string.IsNullOrWhiteSpace(conflict.Location) ? "—" : conflict.Location;
                ws.Cell(row, 3).Value = ClassName(project, conflict.ClassId);
                ws.Cell(row, 4).Value = TeacherName(project, conflict.TeacherId);
                ws.Cell(row, 5).Value = string.IsNullOrWhiteSpace(conflict.Message) ? "—" : conflict.Message;
                ws.Row(row).Height = 26;
                row++;
            }

            StyleGrid(ws.Range(3, 1, row - 1, columnCount));
        }

        ws.Column(1).Width = 16;
        ws.Column(2).Width = 18;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 12;
        ws.Column(5).Width = 56;
        ws.SheetView.FreezeRows(3);
    }

    private static string ClassName(WeaveProject project, string? classId) =>
        string.IsNullOrWhiteSpace(classId) ? "—" : project.FindClass(classId)?.Name ?? "—";

    private static string TeacherName(WeaveProject project, string? teacherId) =>
        string.IsNullOrWhiteSpace(teacherId) ? "—" : project.FindTeacher(teacherId)?.Name ?? "—";

    // ----------------------------- 样式 -----------------------------

    private static IXLWorksheet AddSheet(XLWorkbook workbook, string name)
    {
        var ws = workbook.Worksheets.Add(name);
        ws.Style.Font.FontName = "Microsoft YaHei";
        ws.Style.Font.FontSize = 10;
        ws.Style.Font.FontColor = Ink;
        return ws;
    }

    private static void WriteTitle(IXLWorksheet ws, int columnCount, string title, string subtitle)
    {
        var titleRange = ws.Range(1, 1, 1, columnCount);
        if (columnCount > 1) titleRange.Merge();
        titleRange.Cell(1, 1).Value = title;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 16;
        titleRange.Style.Font.FontColor = Ink;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 32;

        var subtitleRange = ws.Range(2, 1, 2, columnCount);
        if (columnCount > 1) subtitleRange.Merge();
        subtitleRange.Cell(1, 1).Value = subtitle;
        subtitleRange.Style.Font.FontSize = 9.5;
        subtitleRange.Style.Font.FontColor = Muted;
        subtitleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        subtitleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(2).Height = 20;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = Paper;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = Line;
        range.Style.Border.InsideBorderColor = Line;
    }

    private static void StyleGrid(IXLRange range)
    {
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = Line;
        range.Style.Border.InsideBorderColor = Line;
    }
}
