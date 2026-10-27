using GradeWeave.Core.Models;

namespace GradeWeave.Core.Scheduling;

/// <summary>检查教师冲突、班级冲突、教研/会议占用、走班同步与课时缺口。</summary>
public sealed class ConflictAnalyzer
{
    public List<WeaveConflict> Analyze(WeaveProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var conflicts = new List<WeaveConflict>();
        var teachingPeriods = project.Periods
            .Where(p => !p.IsExtension)
            .Select(p => p.Index)
            .ToHashSet();

        DetectTeacherClashes(project, conflicts);
        DetectClassClashes(project, conflicts);
        DetectTeacherUnavailable(project, conflicts);
        DetectLayeredClashes(project, conflicts);
        DetectMissingTeachers(project, conflicts);
        DetectHourMismatch(project, teachingPeriods, conflicts);
        DetectUnplaced(project, teachingPeriods, conflicts);

        conflicts.Sort(CompareConflicts);
        return conflicts;
    }

    private static void DetectTeacherClashes(WeaveProject project, List<WeaveConflict> conflicts)
    {
        foreach (var group in project.Lessons
                     .Where(l => !string.IsNullOrWhiteSpace(l.TeacherId))
                     .GroupBy(l => (l.TeacherId, l.DayIndex, l.Period)))
        {
            if (group.Count() < 2) continue;

            // 同一走班组、同一时段的 A/B 层记录只算一次占用。
            var bookings = group.GroupBy(BookingKey).ToList();
            if (bookings.Count <= 1) continue;

            var classes = group.Select(l => ClassName(project, l.ClassId)).Distinct().ToList();
            var subjects = group.Select(l => LessonLabel(project, l)).Distinct().ToList();

            conflicts.Add(new WeaveConflict
            {
                Kind = ConflictKind.TeacherClash,
                TeacherId = group.Key.TeacherId,
                DayIndex = group.Key.DayIndex,
                Period = group.Key.Period,
                Message = $"教师冲突：{TeacherName(project, group.Key.TeacherId)} 在 {SlotText(group.Key.DayIndex, group.Key.Period)} " +
                          $"同时承担 {string.Join("、", classes)} 的 {string.Join("、", subjects)}。",
            });
        }
    }

    private static void DetectClassClashes(WeaveProject project, List<WeaveConflict> conflicts)
    {
        foreach (var group in project.Lessons.GroupBy(l => (l.ClassId, l.DayIndex, l.Period)))
        {
            if (group.Count() < 2) continue;

            var bookings = group.GroupBy(BookingKey).ToList();
            if (bookings.Count <= 1) continue;

            var subjects = group.Select(l => LessonLabel(project, l)).Distinct().ToList();
            var teachers = group
                .Where(l => !string.IsNullOrWhiteSpace(l.TeacherId))
                .Select(l => TeacherName(project, l.TeacherId))
                .Distinct()
                .ToList();

            var teacherText = teachers.Count > 0 ? $"，任课教师 {string.Join("、", teachers)}" : string.Empty;
            conflicts.Add(new WeaveConflict
            {
                Kind = ConflictKind.ClassClash,
                ClassId = group.Key.ClassId,
                DayIndex = group.Key.DayIndex,
                Period = group.Key.Period,
                Message = $"班级冲突：{ClassName(project, group.Key.ClassId)} 在 {SlotText(group.Key.DayIndex, group.Key.Period)} " +
                          $"同时排了 {string.Join("、", subjects)}{teacherText}。",
            });
        }
    }

    private static void DetectTeacherUnavailable(WeaveProject project, List<WeaveConflict> conflicts)
    {
        foreach (var group in project.Lessons
                     .Where(l => !string.IsNullOrWhiteSpace(l.TeacherId))
                     .GroupBy(l => (l.TeacherId, l.DayIndex, l.Period)))
        {
            var reason = ConstraintRules.TeacherUnavailable(
                project, group.Key.TeacherId, group.Key.DayIndex, group.Key.Period);
            if (reason is null) continue;

            var details = group
                .Select(l => $"{ClassName(project, l.ClassId)} {LessonLabel(project, l)}")
                .Distinct()
                .ToList();

            conflicts.Add(new WeaveConflict
            {
                Kind = ConflictKind.TeacherUnavailable,
                TeacherId = group.Key.TeacherId,
                DayIndex = group.Key.DayIndex,
                Period = group.Key.Period,
                Message = $"教研/会议占用：{TeacherName(project, group.Key.TeacherId)} 在 {SlotText(group.Key.DayIndex, group.Key.Period)} " +
                          $"应参加「{reason}」，却排了 {string.Join("、", details)}。",
            });
        }
    }

    private static void DetectLayeredClashes(WeaveProject project, List<WeaveConflict> conflicts)
    {
        foreach (var group in project.LayeredGroups)
        {
            var groupName = string.IsNullOrWhiteSpace(group.Name) ? group.Id : group.Name;
            var slots = GroupSlots(group);
            var (firstDay, firstPeriod) = slots.Count > 0 ? slots[0] : (0, 0);

            if (project.FindSubject(group.SubjectId) is null)
            {
                conflicts.Add(new WeaveConflict
                {
                    Kind = ConflictKind.LayeredClash,
                    DayIndex = firstDay,
                    Period = firstPeriod,
                    Message = $"走班组「{groupName}」引用的学科不存在，无法核对分层授课。",
                });
                continue;
            }

            if (group.ClassIds.Count == 0 || group.Layers.Count == 0)
            {
                conflicts.Add(new WeaveConflict
                {
                    Kind = ConflictKind.LayeredClash,
                    DayIndex = firstDay,
                    Period = firstPeriod,
                    Message = $"走班组「{groupName}」未指定参与班级或分层。",
                });
            }

            foreach (var classId in group.ClassIds.Distinct(StringComparer.Ordinal))
            {
                if (project.FindClass(classId) is not null) continue;
                conflicts.Add(new WeaveConflict
                {
                    Kind = ConflictKind.LayeredClash,
                    DayIndex = firstDay,
                    Period = firstPeriod,
                    Message = $"走班组「{groupName}」引用了不存在的班级 {classId}。",
                });
            }

            var validLayers = new List<LayeredClassLayer>();
            foreach (var layer in group.Layers)
            {
                var layerName = string.IsNullOrWhiteSpace(layer.Name) ? "未命名层" : layer.Name;
                if (string.IsNullOrWhiteSpace(layer.TeacherId))
                {
                    conflicts.Add(new WeaveConflict
                    {
                        Kind = ConflictKind.LayeredClash,
                        DayIndex = firstDay,
                        Period = firstPeriod,
                        Message = $"缺少层教师：走班组「{groupName}」的 {layerName} 未指定教师。",
                    });
                    continue;
                }

                if (project.FindTeacher(layer.TeacherId) is null)
                {
                    conflicts.Add(new WeaveConflict
                    {
                        Kind = ConflictKind.LayeredClash,
                        TeacherId = layer.TeacherId,
                        DayIndex = firstDay,
                        Period = firstPeriod,
                        Message = $"缺少层教师：走班组「{groupName}」的 {layerName} 教师编号 {layer.TeacherId} 不存在。",
                    });
                    continue;
                }

                validLayers.Add(layer);
            }

            // 自动安排组尚未落位（编排时找不到共同空位）：调度器已给出说明，这里不再逐节核对。
            if (group.IsFlexible && slots.Count == 0) continue;

            // 层教师在同一时段被排了走班组之外的课。
            foreach (var layer in validLayers)
            {
                foreach (var (day, period) in slots)
                {
                    var other = project.Lessons.FirstOrDefault(l =>
                        l.TeacherId == layer.TeacherId &&
                        l.DayIndex == day &&
                        l.Period == period &&
                        !(l.IsLayered && l.GroupId == group.Id));
                    if (other is null) continue;

                    conflicts.Add(new WeaveConflict
                    {
                        Kind = ConflictKind.LayeredClash,
                        TeacherId = layer.TeacherId,
                        DayIndex = day,
                        Period = period,
                        Message = $"层教师冲突：{layer.Name} 教师 {TeacherName(project, layer.TeacherId)} 在 " +
                                  $"{SlotText(day, period)} 同时被排了 " +
                                  $"{ClassName(project, other.ClassId)} {SubjectName(project, other.SubjectId)}。",
                    });
                }
            }

            foreach (var classId in group.ClassIds.Distinct(StringComparer.Ordinal))
            {
                if (project.FindClass(classId) is null) continue;

                foreach (var (day, period) in slots)
                {
                    var lessons = project.Lessons
                        .Where(l => l.ClassId == classId &&
                                    l.DayIndex == day &&
                                    l.Period == period &&
                                    l.GroupId == group.Id)
                        .ToList();
                    var slot = SlotText(day, period);

                    if (lessons.Count == 0)
                    {
                        conflicts.Add(new WeaveConflict
                        {
                            Kind = ConflictKind.LayeredClash,
                            ClassId = classId,
                            DayIndex = day,
                            Period = period,
                            Message = $"走班缺记录：{ClassName(project, classId)} {slot} 未生成 " +
                                      $"{SubjectName(project, group.SubjectId)} 的分层授课记录。",
                        });
                        continue;
                    }

                    foreach (var lesson in lessons.Where(l => l.SubjectId != group.SubjectId))
                    {
                        conflicts.Add(new WeaveConflict
                        {
                            Kind = ConflictKind.LayeredClash,
                            ClassId = classId,
                            TeacherId = lesson.TeacherId,
                            DayIndex = day,
                            Period = period,
                            Message = $"走班学科不符：{ClassName(project, classId)} {slot} 应上 " +
                                      $"{SubjectName(project, group.SubjectId)}，实为 {SubjectName(project, lesson.SubjectId)}。",
                        });
                        break;
                    }

                    foreach (var layer in group.Layers)
                    {
                        var layerName = string.IsNullOrWhiteSpace(layer.Name) ? "未命名层" : layer.Name;
                        var named = lessons.FirstOrDefault(l => l.LayerName == layer.Name);
                        if (named is null)
                        {
                            conflicts.Add(new WeaveConflict
                            {
                                Kind = ConflictKind.LayeredClash,
                                ClassId = classId,
                                DayIndex = day,
                                Period = period,
                                Message = $"走班缺记录：{ClassName(project, classId)} {slot} 缺少 {layerName} 记录。",
                            });
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(layer.TeacherId) && named.TeacherId != layer.TeacherId)
                        {
                            conflicts.Add(new WeaveConflict
                            {
                                Kind = ConflictKind.LayeredClash,
                                ClassId = classId,
                                TeacherId = named.TeacherId,
                                DayIndex = day,
                                Period = period,
                                Message = $"走班层教师不符：{ClassName(project, classId)} {slot} 的 {layerName} 应为 " +
                                          $"{TeacherName(project, layer.TeacherId)}，实为 {TeacherName(project, named.TeacherId)}。",
                            });
                        }
                    }
                }
            }
        }
    }

    /// <summary>走班组占用的槽位：固定时段用配置区间；自动安排组用编排落位结果。</summary>
    private static List<(int Day, int Period)> GroupSlots(LayeredClassGroup group) =>
        group.Slots().ToList();

    private static void DetectMissingTeachers(WeaveProject project, List<WeaveConflict> conflicts)
    {
        foreach (var lesson in project.Lessons)
        {
            // 活动可以没有任课教师（例如全年级广播的书法课），不按普通课要求教师。
            if (lesson.IsActivity) continue;

            if (string.IsNullOrWhiteSpace(lesson.TeacherId))
            {
                conflicts.Add(new WeaveConflict
                {
                    Kind = ConflictKind.MissingTeacher,
                    ClassId = lesson.ClassId,
                    DayIndex = lesson.DayIndex,
                    Period = lesson.Period,
                    Message = $"缺少教师：{ClassName(project, lesson.ClassId)} " +
                              $"{SlotText(lesson.DayIndex, lesson.Period)} 的 " +
                              $"{SubjectName(project, lesson.SubjectId)} 未指定任课教师。",
                });
                continue;
            }

            if (project.FindTeacher(lesson.TeacherId) is not null) continue;

            conflicts.Add(new WeaveConflict
            {
                Kind = ConflictKind.MissingTeacher,
                ClassId = lesson.ClassId,
                TeacherId = lesson.TeacherId,
                DayIndex = lesson.DayIndex,
                Period = lesson.Period,
                Message = $"缺少教师：{ClassName(project, lesson.ClassId)} " +
                          $"{SlotText(lesson.DayIndex, lesson.Period)} 的 " +
                          $"{SubjectName(project, lesson.SubjectId)} 任课教师编号 {lesson.TeacherId} 不存在。",
            });
        }

        foreach (var assignment in project.Assignments)
        {
            if (!string.IsNullOrWhiteSpace(assignment.TeacherId) &&
                project.FindTeacher(assignment.TeacherId) is not null)
                continue;

            var teacherText = string.IsNullOrWhiteSpace(assignment.TeacherId)
                ? "未指定任课教师"
                : $"任课教师编号 {assignment.TeacherId} 不存在";
            conflicts.Add(new WeaveConflict
            {
                Kind = ConflictKind.MissingTeacher,
                ClassId = assignment.ClassId,
                TeacherId = assignment.TeacherId,
                Message = $"缺少教师：{ClassName(project, assignment.ClassId)} " +
                          $"{SubjectName(project, assignment.SubjectId)} 的{teacherText}。",
            });
        }
    }

    private static void DetectHourMismatch(
        WeaveProject project, HashSet<int> teachingPeriods, List<WeaveConflict> conflicts)
    {
        foreach (var assignment in GroupAssignments(project))
        {
            var expected = assignment.Assignments.Sum(a => a.WeeklyHours);
            var actual = ActualHours(project, assignment.ClassId, assignment.SubjectId, teachingPeriods);
            if (expected == actual) continue;

            conflicts.Add(new WeaveConflict
            {
                Kind = ConflictKind.HourMismatch,
                ClassId = assignment.ClassId,
                TeacherId = assignment.Assignments[0].TeacherId,
                Message = $"课时不符：{ClassName(project, assignment.ClassId)} " +
                          $"{SubjectName(project, assignment.SubjectId)} 应有 {expected} 节，实有 {actual} 节。",
            });
        }
    }

    private static void DetectUnplaced(
        WeaveProject project, HashSet<int> teachingPeriods, List<WeaveConflict> conflicts)
    {
        foreach (var assignment in GroupAssignments(project))
        {
            var expected = assignment.Assignments.Sum(a => a.WeeklyHours);
            var actual = ActualHours(project, assignment.ClassId, assignment.SubjectId, teachingPeriods);
            var gap = expected - actual;
            if (gap <= 0) continue;

            var teacherIds = assignment.Assignments
                .Select(a => a.TeacherId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            var teacherText = teacherIds.Count == 0
                ? "未指定任课教师"
                : "任课教师 " + string.Join("、", teacherIds.Select(id => TeacherName(project, id)));

            conflicts.Add(new WeaveConflict
            {
                Kind = ConflictKind.Unplaced,
                ClassId = assignment.ClassId,
                TeacherId = teacherIds.FirstOrDefault(),
                Message = $"未排课时：{ClassName(project, assignment.ClassId)} " +
                          $"{SubjectName(project, assignment.SubjectId)} 还缺 {gap} 节（{teacherText}）。",
            });
        }
    }

    private static int ActualHours(
        WeaveProject project, string classId, string subjectId, HashSet<int> teachingPeriods) =>
        project.Lessons
            .Where(l => l.ClassId == classId && l.SubjectId == subjectId && teachingPeriods.Contains(l.Period))
            .Select(l => (l.DayIndex, l.Period))
            .Distinct()
            .Count();

    private static List<AssignmentGroup> GroupAssignments(WeaveProject project) =>
        project.Assignments
            .GroupBy(a => (a.ClassId, a.SubjectId))
            .Select(g => new AssignmentGroup(g.Key.ClassId, g.Key.SubjectId, g.ToList()))
            .ToList();

    private sealed record AssignmentGroup(
        string ClassId, string SubjectId, List<TeachingAssignment> Assignments);

    private static object BookingKey(LessonPlacement lesson)
    {
        // 同一个活动跨多个班同时进行（如全年级书法课）只算一次教师占用。
        if (lesson.IsActivity && !string.IsNullOrWhiteSpace(lesson.ActivityId))
            return "activity:" + lesson.ActivityId;

        return lesson.IsLayered && !string.IsNullOrWhiteSpace(lesson.GroupId)
            ? "layered:" + lesson.GroupId
            : lesson;
    }

    private static string LessonLabel(WeaveProject project, LessonPlacement lesson) =>
        lesson.IsActivity
            ? (string.IsNullOrWhiteSpace(lesson.ActivityName) ? "活动" : lesson.ActivityName)
            : SubjectName(project, lesson.SubjectId);

    private static int CompareConflicts(WeaveConflict a, WeaveConflict b)
    {
        var compared = a.Kind.CompareTo(b.Kind);
        if (compared != 0) return compared;

        compared = a.DayIndex.CompareTo(b.DayIndex);
        if (compared != 0) return compared;

        compared = a.Period.CompareTo(b.Period);
        if (compared != 0) return compared;

        compared = string.CompareOrdinal(a.ClassId ?? string.Empty, b.ClassId ?? string.Empty);
        if (compared != 0) return compared;

        return string.CompareOrdinal(a.Message, b.Message);
    }

    private static string SlotText(int dayIndex, int period) =>
        dayIndex >= 1 ? WeaveDefaults.SlotText(dayIndex, period, period) : string.Empty;

    private static string ClassName(WeaveProject project, string id) =>
        project.FindClass(id)?.Name ?? id;

    private static string SubjectName(WeaveProject project, string id) =>
        project.FindSubject(id)?.Name ?? id;

    private static string TeacherName(WeaveProject project, string id) =>
        project.FindTeacher(id)?.Name ?? id;
}
