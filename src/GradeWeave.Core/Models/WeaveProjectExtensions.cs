namespace GradeWeave.Core.Models;

public static class WeaveProjectExtensions
{
    public static SchoolClass? FindClass(this WeaveProject project, string id) =>
        project.Classes.FirstOrDefault(c => c.Id == id);

    public static Subject? FindSubject(this WeaveProject project, string id) =>
        project.Subjects.FirstOrDefault(s => s.Id == id);

    public static Teacher? FindTeacher(this WeaveProject project, string id) =>
        project.Teachers.FirstOrDefault(t => t.Id == id);

    public static Subject? FindSubjectByName(this WeaveProject project, string name) =>
        project.Subjects.FirstOrDefault(s => s.Name == WeaveDefaults.SubjectAlias(name));

    public static Teacher? FindTeacherByName(this WeaveProject project, string name)
    {
        var normalized = WeaveDefaults.NormalizeTeacherName(name);
        return project.Teachers.FirstOrDefault(t => t.Name == normalized);
    }

    public static LessonPlacement? LessonAt(this WeaveProject project, string classId, int dayIndex, int period) =>
        project.Lessons.FirstOrDefault(l =>
            l.ClassId == classId && l.DayIndex == dayIndex && l.Period == period);

    public static void RemoveLessonAt(this WeaveProject project, string classId, int dayIndex, int period) =>
        project.Lessons.RemoveAll(l => l.ClassId == classId && l.DayIndex == dayIndex && l.Period == period);

    public static IEnumerable<TeachingAssignment> AssignmentsOfClass(this WeaveProject project, string classId) =>
        project.Assignments.Where(a => a.ClassId == classId);

    public static int TeachingPeriodCount(this WeaveProject project) =>
        project.Periods.Count(p => !p.IsExtension);

    public static int TotalTeachingSlots(this WeaveProject project) =>
        WeaveDefaults.Days.Length * project.TeachingPeriodCount();

    /// <summary>某位教师在指定时段是否被教研或会议锁定。</summary>
    public static string? UnavailableReason(this WeaveProject project, string teacherId, int dayIndex, int period)
    {
        var teacher = project.FindTeacher(teacherId);
        if (teacher is null) return "教师不存在";

        var subjectIds = project.ResearchBlocks
            .Where(b => b.DayIndex == dayIndex && period >= b.StartPeriod && period <= b.EndPeriod)
            .SelectMany(b => b.SubjectIds)
            .ToHashSet(StringComparer.Ordinal);

        if (subjectIds.Count > 0)
        {
            var subjects = project.Assignments
                .Where(a => a.TeacherId == teacherId)
                .Select(a => a.SubjectId)
                .Concat(project.Subjects.Where(s => s.Name == teacher.Subject).Select(s => s.Id))
                .ToHashSet(StringComparer.Ordinal);
            if (subjects.Overlaps(subjectIds))
                return "学科教研";
        }

        foreach (var block in project.MeetingBlocks.Where(b =>
                     b.DayIndex == dayIndex && period >= b.StartPeriod && period <= b.EndPeriod))
        {
            if (block.AppliesToAllTeachers) return block.Name;
            if (block.TeacherIds.Contains(teacherId, StringComparer.Ordinal)) return block.Name;
            if (TeacherRoleMatcher.Matches(teacher.Roles, block.Roles)) return block.Name;
        }

        return null;
    }

    public static IEnumerable<LessonPlacement> LessonsOfTeacher(this WeaveProject project, string teacherId) =>
        project.Lessons.Where(l => l.TeacherId == teacherId);

    /// <summary>
    /// 某学科在本年级的任课教师：优先取分工表里的任课关系，并补上教师档案学科一致的教师。
    /// 走班组编辑用它过滤下拉，避免把全年级教师都列出来。
    /// </summary>
    public static List<Teacher> TeachersOfSubject(this WeaveProject project, Subject? subject)
    {
        if (subject is null) return [];

        var teacherIds = project.Assignments
            .Where(assignment => assignment.SubjectId == subject.Id)
            .Select(assignment => assignment.TeacherId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        var subjectName = WeaveDefaults.SubjectAlias(subject.Name);
        foreach (var teacher in project.Teachers)
        {
            if (string.Equals(
                    WeaveDefaults.SubjectAlias(teacher.Subject ?? string.Empty),
                    subjectName,
                    StringComparison.Ordinal))
                teacherIds.Add(teacher.Id);
        }

        return project.Teachers
            .Where(teacher => teacherIds.Contains(teacher.Id))
            .OrderBy(teacher => teacher.Name, StringComparer.Ordinal)
            .ToList();
    }

    public static IEnumerable<Subject> SubjectsOfTeacher(this WeaveProject project, string teacherId) =>
        project.Assignments.Where(a => a.TeacherId == teacherId)
            .Select(a => project.FindSubject(a.SubjectId))
            .OfType<Subject>()
            .DistinctBy(s => s.Id);

    /// <summary>
    /// 自动保存用的工程快照：复制全部可变集合，后台序列化时不会与 UI 编辑并发枚举。
    /// 只复制自动保存需要的字段，不承担通用深拷贝职责。
    /// </summary>
    public static WeaveProject Snapshot(this WeaveProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new WeaveProject
        {
            SchoolName = project.SchoolName,
            GradeName = project.GradeName,
            TermName = project.TermName,
            SourceFile = project.SourceFile,
            ImportedAt = project.ImportedAt,
            Periods = project.Periods.Select(period => new PeriodSlot
            {
                Index = period.Index,
                Name = period.Name,
                TimeText = period.TimeText,
                IsAfternoon = period.IsAfternoon,
                IsExtension = period.IsExtension,
            }).ToList(),
            Timetable = project.Timetable?.Clone(),
            Classes = project.Classes.Select(item => new SchoolClass
            {
                Id = item.Id,
                Name = item.Name,
                HeadTeacher = item.HeadTeacher,
            }).ToList(),
            Subjects = project.Subjects.Select(item => new Subject
            {
                Id = item.Id,
                Name = item.Name,
                WeeklyHours = item.WeeklyHours,
                IsMain = item.IsMain,
                IsLayered = item.IsLayered,
                Color = item.Color,
            }).ToList(),
            Teachers = project.Teachers.Select(item => new Teacher
            {
                Id = item.Id,
                Name = item.Name,
                Subject = item.Subject,
                WeeklyHours = item.WeeklyHours,
                IsLeader = item.IsLeader,
                Roles = [.. item.Roles],
            }).ToList(),
            Assignments = project.Assignments.Select(item => new TeachingAssignment
            {
                ClassId = item.ClassId,
                SubjectId = item.SubjectId,
                TeacherId = item.TeacherId,
                WeeklyHours = item.WeeklyHours,
                IsLayered = item.IsLayered,
                LayerName = item.LayerName,
            }).ToList(),
            ResearchBlocks = project.ResearchBlocks.Select(item => new ResearchBlock
            {
                Id = item.Id,
                SubjectIds = [.. item.SubjectIds],
                DayIndex = item.DayIndex,
                StartPeriod = item.StartPeriod,
                EndPeriod = item.EndPeriod,
                Note = item.Note,
            }).ToList(),
            MeetingBlocks = project.MeetingBlocks.Select(item => new MeetingBlock
            {
                Id = item.Id,
                Name = item.Name,
                DayIndex = item.DayIndex,
                StartPeriod = item.StartPeriod,
                EndPeriod = item.EndPeriod,
                Roles = [.. item.Roles],
                TeacherIds = [.. item.TeacherIds],
                AppliesToAllTeachers = item.AppliesToAllTeachers,
                IsLeadership = item.IsLeadership,
                Note = item.Note,
            }).ToList(),
            LayeredGroups = project.LayeredGroups.Select(item => new LayeredClassGroup
            {
                Id = item.Id,
                Name = item.Name,
                SubjectId = item.SubjectId,
                ClassIds = [.. item.ClassIds],
                Layers = item.Layers.Select(layer => new LayeredClassLayer
                {
                    Name = layer.Name,
                    TeacherId = layer.TeacherId,
                    Room = layer.Room,
                }).ToList(),
                DayIndex = item.DayIndex,
                StartPeriod = item.StartPeriod,
                EndPeriod = item.EndPeriod,
                Note = item.Note,
                IsFlexible = item.IsFlexible,
                WeeklyHours = item.WeeklyHours,
                ResolvedSlots = [.. item.ResolvedSlots],
            }).ToList(),
            Activities = project.Activities.Select(item => new ActivityBlock
            {
                Id = item.Id,
                Name = item.Name,
                DayIndex = item.DayIndex,
                StartTime = item.StartTime,
                EndTime = item.EndTime,
                Period = item.Period,
                ClassIds = [.. item.ClassIds],
                TeacherId = item.TeacherId,
                Room = item.Room,
                Mode = item.Mode,
                Note = item.Note,
            }).ToList(),
            Lessons = project.Lessons.Select(item => new LessonPlacement
            {
                ClassId = item.ClassId,
                DayIndex = item.DayIndex,
                Period = item.Period,
                SubjectId = item.SubjectId,
                TeacherId = item.TeacherId,
                LayerName = item.LayerName,
                GroupId = item.GroupId,
                IsLayered = item.IsLayered,
                IsFixed = item.IsFixed,
                Note = item.Note,
                IsActivity = item.IsActivity,
                ActivityId = item.ActivityId,
                ActivityName = item.ActivityName,
                ActivityTime = item.ActivityTime,
            }).ToList(),
            Notes = [.. project.Notes],
            Warnings = [.. project.Warnings],
        };
    }
}
