using System.Diagnostics;
using GradeWeave.Core.Models;

namespace GradeWeave.Core.Scheduling;

/// <summary>
/// 以「每个班占满全部教学时段」为硬约束的排列式编排。
/// 每个班的 35 节课是一个排列，交换同一班的两节即可在不动班级网格的前提下
/// 消除教师冲突、教研/会议占用和同科每日超限，避免贪心留下的班级空洞。
/// </summary>
internal sealed class PermutationScheduler
{
    private const int Days = 5;
    private readonly WeaveProject _project;
    private readonly ScheduleOptions _options;
    private readonly CancellationToken _cancellation;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly List<int> _periods;
    private readonly Dictionary<int, int> _periodPosition = new();
    private readonly List<ClassPlan> _plans = [];
    private readonly List<WeaveTask> _tasks = [];
    private readonly List<UnplacedLesson> _unplaced = [];
    private readonly Dictionary<(string Teacher, int Slot), int> _teacherSlotCount = new();
    private readonly Dictionary<(string Class, string Subject, int Day), int> _subjectDayCount = new();
    private readonly Dictionary<(string Class, int Day), int> _classDayCount = new();
    private readonly Dictionary<string, int> _teacherDemand = new(StringComparer.Ordinal);
    private readonly List<string> _flexWarnings = [];
    private int _slotCount;

    public PermutationScheduler(
        WeaveProject project, ScheduleOptions options, CancellationToken cancellation)
    {
        _project = project;
        _options = options;
        _cancellation = cancellation;
        _periods = project.Periods.Where(p => !p.IsExtension).Select(p => p.Index).OrderBy(p => p).ToList();
        for (var i = 0; i < _periods.Count; i++) _periodPosition[_periods[i]] = i;
        _slotCount = Days * _periods.Count;
    }

    public ScheduleResult Run()
    {
        var result = new ScheduleResult();

        // 替换式活动先落成固定占位课；重排时这些格子会被跳过，活动不会丢。
        ActivityResolver.Apply(_project);
        PrepareFixedLessons();
        foreach (var warning in _flexWarnings)
            result.Messages.Add(warning);

        BuildTasks();
        result.TotalCount = _tasks.Count;

        if (_cancellation.IsCancellationRequested)
        {
            result.Messages.Add("编排已取消：只保留固定走班，未排新课时。");
            result.Elapsed = _clock.Elapsed;
            return result;
        }

        if (_tasks.Count == 0)
        {
            result.Messages.Add("没有需要编排的课时。");
            result.Elapsed = _clock.Elapsed;
            return result;
        }

        BuildClassPlans();

        var deadline = _options.TimeLimit <= TimeSpan.Zero
            ? TimeSpan.Zero
            : _clock.Elapsed + _options.TimeLimit;

        var conflicts = CollectConflicts();
        var iterations = 0;
        var stagnant = 0;
        var random = new Random(_options.RandomSeed);

        // 只以「硬冲突是否清零」作为结束条件；软惩罚不参与停机判断，避免代价累计误差提前退出。
        while (conflicts.Count > 0 && _clock.Elapsed < deadline && !_cancellation.IsCancellationRequested)
        {
            var (plan, slot) = conflicts[random.Next(conflicts.Count)];
            var target = FindBestSwap(plan, slot, random, out var delta);
            if (target is not null && delta < 0)
            {
                Swap(plan, slot, target.Value);
                stagnant = 0;
            }
            else
            {
                stagnant++;
                // 局部最优时做一次随机游走，避免在教师冲突之间来回卡住。
                if (stagnant > 400 || random.NextDouble() < 0.03)
                {
                    var usable = UsableSlots(plan);
                    if (usable.Count >= 2)
                    {
                        var a = usable[random.Next(usable.Count)];
                        var b = usable[random.Next(usable.Count)];
                        if (a != b)
                        {
                            Swap(plan, a, b);
                            stagnant = 0;
                        }
                    }
                }
            }

            iterations++;
            if ((iterations & 127) == 0)
                conflicts = CollectConflicts();
        }

        ResolveByRemoval();
        WriteLessons();
        result.PlacedCount = result.TotalCount - _unplaced.Sum(u => u.Count);
        result.Unplaced = _unplaced;
        result.Score = Math.Max(0, 1000 - Math.Min(1000, TotalCost()));
        result.Elapsed = _clock.Elapsed;
        result.Messages.Add(result.Unplaced.Count == 0
            ? $"自动编排完成：共 {result.TotalCount} 节，用时 {result.Elapsed.TotalSeconds:F1} 秒。"
            : $"自动编排完成：已排 {result.PlacedCount}/{result.TotalCount} 节，未排 {result.TotalCount - result.PlacedCount} 节，原因见清单。");

        return result;
    }

    // ----------------------------- 固定课与任务 -----------------------------

    private void PrepareFixedLessons()
    {
        _project.Lessons.RemoveAll(l => !l.IsFixed);

        // 先落用户固定时段的走班组（显式约束优先），再为自动安排组挑选共同空位。
        foreach (var group in _project.LayeredGroups.Where(g => !g.IsFlexible))
            BuildFixedGroupLessons(group);

        foreach (var group in _project.LayeredGroups.Where(g => g.IsFlexible))
            ResolveFlexibleGroup(group);

        // 走班的 A/B 层记录是同一班同一节的重复记录：班级占用只算一次，
        // 教师占用按「组 + 节次 + 层」只算一次（同层跨班合班上课）。
        var layeredClassSlots = new HashSet<(string ClassId, int Day, int Period)>();
        var layeredTeacherSlots = new HashSet<(string GroupId, int Slot, string Layer)>();

        foreach (var lesson in _project.Lessons.Where(l => l.IsFixed))
        {
            var slot = SlotOf(lesson.DayIndex, lesson.Period);
            if (slot < 0) continue;

            if (lesson.IsLayered)
            {
                if (!string.IsNullOrEmpty(lesson.TeacherId)
                    && layeredTeacherSlots.Add((lesson.GroupId, slot, lesson.LayerName)))
                    BumpTeacher(lesson.TeacherId, slot, 1);
            }
            else if (!string.IsNullOrEmpty(lesson.TeacherId))
            {
                BumpTeacher(lesson.TeacherId, slot, 1);
            }

            // 活动占位只锁班级格子与活动教师，不参与学科日分布。
            if (lesson.IsActivity) continue;

            // 固定课也计入班级的学科日分布，避免自动课把同一天堆到超限。
            if (lesson.DayIndex is >= 1 and <= Days)
            {
                var classSlot = (lesson.ClassId, lesson.DayIndex, lesson.Period);
                if (lesson.IsLayered && !layeredClassSlots.Add(classSlot)) continue;

                var key = (lesson.ClassId, lesson.SubjectId, lesson.DayIndex);
                _subjectDayCount[key] = _subjectDayCount.GetValueOrDefault(key) + 1;
                var dayKey = (lesson.ClassId, lesson.DayIndex);
                _classDayCount[dayKey] = _classDayCount.GetValueOrDefault(dayKey) + 1;
            }
        }
    }

    private void BuildFixedGroupLessons(LayeredClassGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Id)) group.Id = Guid.NewGuid().ToString("N");
        if (_project.FindSubject(group.SubjectId) is null) return;
        if (group.ClassIds.Count == 0 || group.Layers.Count == 0) return;

        foreach (var classId in group.ClassIds.Distinct(StringComparer.Ordinal))
        {
            if (_project.FindClass(classId) is null) continue;
            foreach (var period in group.Periods)
            {
                foreach (var layer in group.Layers)
                {
                    if (string.IsNullOrWhiteSpace(layer.TeacherId)) continue;
                    if (_project.Lessons.Any(l => l.IsFixed && l.IsLayered && l.GroupId == group.Id
                            && l.ClassId == classId && l.DayIndex == group.DayIndex
                            && l.Period == period && l.LayerName == layer.Name))
                        continue;

                    _project.Lessons.Add(new LessonPlacement
                    {
                        ClassId = classId,
                        DayIndex = group.DayIndex,
                        Period = period,
                        SubjectId = group.SubjectId,
                        TeacherId = layer.TeacherId,
                        LayerName = layer.Name,
                        GroupId = group.Id,
                        IsLayered = true,
                        IsFixed = true,
                        Note = group.Name,
                    });
                }
            }
        }
    }

    /// <summary>
    /// 自动安排组：不占固定时段，按「参与班都空闲 + 各层教师都空闲 + 不撞教研会议 + 同科每日不超限」
    /// 挑出 WeeklyHours 个共同槽位。一个都挑不出时整组退化为普通课（课时由任课关系正常排），
    /// 并写入一条说明消息，绝不静默丢失走班安排。
    /// </summary>
    private void ResolveFlexibleGroup(LayeredClassGroup group)
    {
        group.ResolvedSlots.Clear();
        if (string.IsNullOrWhiteSpace(group.Id)) group.Id = Guid.NewGuid().ToString("N");

        if (_project.FindSubject(group.SubjectId) is null ||
            group.ClassIds.Count == 0 || group.Layers.Count == 0)
            return;

        var classIds = group.ClassIds.Distinct(StringComparer.Ordinal)
            .Where(id => _project.FindClass(id) is not null)
            .ToList();
        var layers = group.Layers
            .Where(l => !string.IsNullOrWhiteSpace(l.TeacherId))
            .ToList();
        if (classIds.Count == 0 || layers.Count == 0) return;

        var hours = Math.Clamp(group.WeeklyHours <= 0 ? 1 : group.WeeklyHours, 1, _slotCount);

        var classBusy = new HashSet<(string ClassId, int Day, int Period)>();
        var teacherBusy = new HashSet<(string TeacherId, int Day, int Period)>();
        var subjectDay = new Dictionary<(string ClassId, int Day), int>();
        foreach (var lesson in _project.Lessons)
        {
            if (SlotOf(lesson.DayIndex, lesson.Period) < 0) continue;
            classBusy.Add((lesson.ClassId, lesson.DayIndex, lesson.Period));
            if (!string.IsNullOrEmpty(lesson.TeacherId))
                teacherBusy.Add((lesson.TeacherId, lesson.DayIndex, lesson.Period));
            if (lesson.SubjectId == group.SubjectId)
            {
                var key = (lesson.ClassId, lesson.DayIndex);
                subjectDay[key] = subjectDay.GetValueOrDefault(key) + 1;
            }
        }

        var chosen = new List<(int Day, int Period)>();
        foreach (var day in Enumerable.Range(1, Days))
        {
            foreach (var period in _periods)
            {
                if (chosen.Count >= hours) break;
                if (classIds.Any(id => classBusy.Contains((id, day, period)))) continue;
                if (layers.Any(l => teacherBusy.Contains((l.TeacherId, day, period)))) continue;
                if (layers.Any(l =>
                        ConstraintRules.TeacherUnavailable(_project, l.TeacherId, day, period) is not null))
                    continue;
                if (classIds.Any(id =>
                        subjectDay.GetValueOrDefault((id, day)) >= _options.MaxSameSubjectPerDay))
                    continue;

                chosen.Add((day, period));
                foreach (var id in classIds)
                {
                    classBusy.Add((id, day, period));
                    subjectDay[(id, day)] = subjectDay.GetValueOrDefault((id, day)) + 1;
                }
                foreach (var layer in layers)
                    teacherBusy.Add((layer.TeacherId, day, period));
            }

            if (chosen.Count >= hours) break;
        }

        if (chosen.Count == 0)
        {
            _flexWarnings.Add(
                $"走班组「{group.Name}」找不到所有参与班与层教师共同空闲的时段，该组已按普通课编排。");
            return;
        }

        if (chosen.Count < hours)
            _flexWarnings.Add(
                $"走班组「{group.Name}」只自动安排了 {chosen.Count}/{hours} 节，其余课时按普通课编排。");

        foreach (var (day, period) in chosen)
        {
            foreach (var classId in classIds)
            {
                foreach (var layer in layers)
                {
                    if (_project.Lessons.Any(l => l.IsFixed && l.IsLayered && l.GroupId == group.Id
                            && l.ClassId == classId && l.DayIndex == day
                            && l.Period == period && l.LayerName == layer.Name))
                        continue;

                    _project.Lessons.Add(new LessonPlacement
                    {
                        ClassId = classId,
                        DayIndex = day,
                        Period = period,
                        SubjectId = group.SubjectId,
                        TeacherId = layer.TeacherId,
                        LayerName = layer.Name,
                        GroupId = group.Id,
                        IsLayered = true,
                        IsFixed = true,
                        Note = group.Name,
                    });
                }
            }
        }

        group.ResolvedSlots.AddRange(
            chosen.Select(slot => LayeredClassGroup.EncodeSlot(slot.Day, slot.Period)));
    }

    private void BuildTasks()
    {
        var index = 0;
        foreach (var assignment in _project.Assignments)
        {
            var subject = _project.FindSubject(assignment.SubjectId);
            if (subject is null) continue;
            if (_project.FindTeacher(assignment.TeacherId) is null)
            {
                AddUnplaced(assignment.ClassId, assignment.SubjectId, assignment.TeacherId,
                    Math.Max(1, assignment.WeeklyHours), "任课教师缺失");
                continue;
            }

            var remaining = Math.Max(0, assignment.WeeklyHours
                - ConstraintRules.LayeredHours(_project, assignment.ClassId, assignment.SubjectId));

            // 用户固定的普通课同样消耗课时。
            remaining -= _project.Lessons.Count(l =>
                l.IsFixed && !l.IsLayered && l.ClassId == assignment.ClassId
                && l.SubjectId == assignment.SubjectId && IsTeachingPeriod(l.Period));

            for (var i = 0; i < remaining; i++)
            {
                _tasks.Add(new WeaveTask
                {
                    Index = index++,
                    ClassId = assignment.ClassId,
                    SubjectId = assignment.SubjectId,
                    TeacherId = assignment.TeacherId,
                    IsMain = subject.IsMain,
                });
                _teacherDemand[assignment.TeacherId] =
                    _teacherDemand.GetValueOrDefault(assignment.TeacherId) + 1;
            }
        }
    }

    private void BuildClassPlans()
    {
        foreach (var schoolClass in _project.Classes)
        {
            var fixedSlots = new HashSet<int>();
            foreach (var lesson in _project.Lessons.Where(l => l.IsFixed && l.ClassId == schoolClass.Id))
            {
                var slot = SlotOf(lesson.DayIndex, lesson.Period);
                if (slot >= 0) fixedSlots.Add(slot);
            }

            var plan = new ClassPlan
            {
                ClassId = schoolClass.Id,
                Slots = new WeaveTask?[_slotCount],
                Usable = new bool[_slotCount],
            };
            for (var slot = 0; slot < _slotCount; slot++)
            {
                plan.Usable[slot] = !fixedSlots.Contains(slot);
                if (plan.Usable[slot]) plan.UsableList.Add(slot);
            }

            var classTasks = _tasks.Where(t => t.ClassId == schoolClass.Id).ToList();
            var usable = UsableSlots(plan);
            if (classTasks.Count > usable.Count)
            {
                var overflow = classTasks.Skip(usable.Count).ToList();
                foreach (var task in overflow)
                    AddUnplaced(task, "班级教学时段不足");
                classTasks = classTasks.Take(usable.Count).ToList();
            }

            AssignInitial(plan, classTasks);
            _plans.Add(plan);
        }
    }

    private void AssignInitial(ClassPlan plan, List<WeaveTask> tasks)
    {
        var free = new List<int>(UsableSlots(plan));
        var ordered = tasks
            .GroupBy(t => t.SubjectId, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .SelectMany(g => g.OrderByDescending(t => _teacherDemand.GetValueOrDefault(t.TeacherId)))
            .ToList();

        // 不用 string.GetHashCode：.NET 的字符串哈希按进程随机化，会让同一工程在不同进程里排出不同结果。
        var random = new Random(unchecked(_options.RandomSeed ^ StableHash(plan.ClassId)));
        foreach (var task in ordered)
        {
            if (free.Count == 0) break;

            var bestPos = 0;
            var bestScore = int.MaxValue;
            for (var i = 0; i < free.Count; i++)
            {
                var slot = free[i];
                var (day, period) = DayPeriod(slot);
                var score = 0;
                var dayKey = (task.ClassId, task.SubjectId, day);
                score += _subjectDayCount.GetValueOrDefault(dayKey) * 400;
                score += _classDayCount.GetValueOrDefault((task.ClassId, day)) * 6;
                if (ConstraintRules.TeacherUnavailable(_project, task.TeacherId, day, period) is not null)
                    score += 5000;
                score += _teacherSlotCount.GetValueOrDefault((task.TeacherId, slot)) * 9000;
                if (_options.PreferMainMorning && task.IsMain && period > 4) score += 2;
                score += random.Next(0, 3);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestPos = i;
                }
            }

            var chosen = free[bestPos];
            free.RemoveAt(bestPos);
            plan.Slots[chosen] = task;
            BumpTask(plan, chosen, task, 1);
        }
    }

    // ----------------------------- 局部搜索 -----------------------------

    private List<(ClassPlan Plan, int Slot)> CollectConflicts()
    {
        var result = new List<(ClassPlan, int)>();

        foreach (var plan in _plans)
        {
            foreach (var slot in UsableSlots(plan))
            {
                var task = plan.Slots[slot];
                if (task is null) continue;

                var (day, period) = DayPeriod(slot);
                if (ConstraintRules.TeacherUnavailable(_project, task.TeacherId, day, period) is not null)
                {
                    result.Add((plan, slot));
                    continue;
                }

                if (_subjectDayCount.GetValueOrDefault((task.ClassId, task.SubjectId, day)) >
                    _options.MaxSameSubjectPerDay)
                {
                    result.Add((plan, slot));
                    continue;
                }

                if (_teacherSlotCount.GetValueOrDefault((task.TeacherId, slot)) > 1)
                    result.Add((plan, slot));
            }
        }

        return result;
    }

    private int? FindBestSwap(ClassPlan plan, int slot, Random random, out int bestDelta)
    {
        var usable = UsableSlots(plan);
        bestDelta = int.MaxValue;
        int? best = null;
        if (usable.Count == 0) return null;

        // 冲突格只占少数，逐个评估同一班的全部可交换格，比随机抽样更容易找到最后几处改进。
        foreach (var target in usable)
        {
            if (target == slot) continue;

            var delta = EvaluateSwap(plan, slot, target, apply: false);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = target;
            }
        }

        return best;
    }

    private int EvaluateSwap(ClassPlan plan, int a, int b, bool apply)
    {
        var taskA = plan.Slots[a];
        var taskB = plan.Slots[b];
        if (taskA is null && taskB is null) return 0;

        var oldClass = ClassCost(plan);
        var oldTeacher = TeacherCostOf(taskA, a) + TeacherCostOf(taskB, b);

        Remove(plan, a);
        Remove(plan, b);
        if (taskA is not null) Apply(plan, b, taskA);
        if (taskB is not null) Apply(plan, a, taskB);

        var newClass = ClassCost(plan);
        var newTeacher = TeacherCostOf(taskB, a) + TeacherCostOf(taskA, b);

        var delta = (newClass - oldClass) + (newTeacher - oldTeacher);

        if (!apply)
        {
            Remove(plan, a);
            Remove(plan, b);
            if (taskA is not null) Apply(plan, a, taskA);
            if (taskB is not null) Apply(plan, b, taskB);
        }

        return delta;
    }

    private void Swap(ClassPlan plan, int a, int b) => EvaluateSwap(plan, a, b, apply: true);

    private int ClassCost(ClassPlan plan)
    {
        var cost = 0;
        foreach (var slot in plan.UsableList)
        {
            var task = plan.Slots[slot];
            if (task is null) continue;

            var (day, period) = DayPeriod(slot);
            if (ConstraintRules.TeacherUnavailable(_project, task.TeacherId, day, period) is not null)
                cost += 1000;

            var count = _subjectDayCount.GetValueOrDefault((task.ClassId, task.SubjectId, day));
            if (count > _options.MaxSameSubjectPerDay)
                cost += (count - _options.MaxSameSubjectPerDay) * 100;

            if (_options.PreferMainMorning && task.IsMain && period > 4) cost += 1;
        }

        return cost;
    }

    private int TeacherCostOf(WeaveTask? task, int slot)
    {
        if (task is null) return 0;
        return Math.Max(0, _teacherSlotCount.GetValueOrDefault((task.TeacherId, slot)) - 1) * 500;
    }

    private int TotalCost()
    {
        var total = _plans.Sum(ClassCost);
        foreach (var (key, count) in _teacherSlotCount)
            total += Math.Max(0, count - 1) * 500;
        return total;
    }

    // ----------------------------- 收尾 -----------------------------

    private void ResolveByRemoval()
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var plan in _plans)
            {
                foreach (var slot in UsableSlots(plan))
                {
                    var task = plan.Slots[slot];
                    if (task is null) continue;

                    var (day, period) = DayPeriod(slot);
                    var unavailable = ConstraintRules.TeacherUnavailable(
                        _project, task.TeacherId, day, period) is not null;
                    var dayCount = _subjectDayCount.GetValueOrDefault((task.ClassId, task.SubjectId, day));
                    var teacherCount = _teacherSlotCount.GetValueOrDefault((task.TeacherId, slot));

                    if (!unavailable && dayCount <= _options.MaxSameSubjectPerDay && teacherCount <= 1)
                        continue;

                    Remove(plan, slot);
                    plan.Slots[slot] = null;
                    AddUnplaced(task, unavailable
                        ? "教研/会议占用"
                        : dayCount > _options.MaxSameSubjectPerDay ? "同科每日节数上限" : "教师冲突");
                    changed = true;
                }
            }

            // 固定课之间的教师冲突无法通过交换解决，但可以在结果里保留并由冲突检查报告。
        }
    }

    private void WriteLessons()
    {
        _project.Lessons.RemoveAll(l => !l.IsFixed);
        foreach (var plan in _plans)
        {
            foreach (var slot in UsableSlots(plan))
            {
                var task = plan.Slots[slot];
                if (task is null) continue;
                var (day, period) = DayPeriod(slot);
                _project.Lessons.Add(new LessonPlacement
                {
                    ClassId = task.ClassId,
                    DayIndex = day,
                    Period = period,
                    SubjectId = task.SubjectId,
                    TeacherId = task.TeacherId,
                });
            }
        }
    }

    // ----------------------------- 计数与工具 -----------------------------

    private void Apply(ClassPlan plan, int slot, WeaveTask task)
    {
        plan.Slots[slot] = task;
        BumpTask(plan, slot, task, 1);
    }

    private void Remove(ClassPlan plan, int slot)
    {
        var task = plan.Slots[slot];
        if (task is null) return;
        BumpTask(plan, slot, task, -1);
        plan.Slots[slot] = null;
    }

    private void BumpTask(ClassPlan plan, int slot, WeaveTask task, int delta)
    {
        var (day, _) = DayPeriod(slot);
        if (!string.IsNullOrEmpty(task.TeacherId)) BumpTeacher(task.TeacherId, slot, delta);

        var subjectKey = (task.ClassId, task.SubjectId, day);
        var count = _subjectDayCount.GetValueOrDefault(subjectKey) + delta;
        if (count <= 0) _subjectDayCount.Remove(subjectKey);
        else _subjectDayCount[subjectKey] = count;

        var dayKey = (plan.ClassId, day);
        var dayCount = _classDayCount.GetValueOrDefault(dayKey) + delta;
        if (dayCount <= 0) _classDayCount.Remove(dayKey);
        else _classDayCount[dayKey] = dayCount;
    }

    private void BumpTeacher(string teacherId, int slot, int delta)
    {
        var key = (teacherId, slot);
        var count = _teacherSlotCount.GetValueOrDefault(key) + delta;
        if (count <= 0) _teacherSlotCount.Remove(key);
        else _teacherSlotCount[key] = count;
    }

    private static List<int> UsableSlots(ClassPlan plan) => plan.UsableList;

    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in text)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }

    private int SlotOf(int day, int period)
    {
        if (day < 1 || day > Days || !_periodPosition.TryGetValue(period, out var position)) return -1;
        return (day - 1) * _periods.Count + position;
    }

    private (int Day, int Period) DayPeriod(int slot)
    {
        var day = slot / _periods.Count + 1;
        var period = _periods[slot % _periods.Count];
        return (day, period);
    }

    private bool IsTeachingPeriod(int period) => _periodPosition.ContainsKey(period);

    private void AddUnplaced(WeaveTask task, string reason) =>
        AddUnplaced(task.ClassId, task.SubjectId, task.TeacherId, 1, reason);

    private void AddUnplaced(string classId, string subjectId, string teacherId, int count, string reason)
    {
        var existing = _unplaced.FirstOrDefault(u =>
            u.ClassId == classId && u.SubjectId == subjectId && u.TeacherId == teacherId && u.Reason == reason);
        if (existing is not null)
        {
            existing.Count += count;
            return;
        }

        _unplaced.Add(new UnplacedLesson
        {
            ClassId = classId,
            SubjectId = subjectId,
            TeacherId = teacherId,
            Count = count,
            Reason = reason,
        });
    }

    private sealed class ClassPlan
    {
        public string ClassId = string.Empty;
        public WeaveTask?[] Slots = [];
        public bool[] Usable = [];
        public List<int> UsableList = [];
    }

    private sealed class WeaveTask
    {
        public int Index { get; init; }
        public string ClassId { get; init; } = string.Empty;
        public string SubjectId { get; init; } = string.Empty;
        public string TeacherId { get; init; } = string.Empty;
        public bool IsMain { get; init; }
    }
}
