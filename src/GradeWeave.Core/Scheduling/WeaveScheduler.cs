using GradeWeave.Core.Models;
using GradeWeave.Core.Timetable;

namespace GradeWeave.Core.Scheduling;

/// <summary>在教研、走班、会议约束下自动编排全年级课表。</summary>
public sealed class WeaveScheduler
{
    public ScheduleResult Generate(
        WeaveProject project,
        ScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!TimetableService.IsConfirmed(project))
            throw new GradeWeaveException("请先在「作息时间」里确认时间表，再生成课表。");

        options ??= new ScheduleOptions();
        return new PermutationScheduler(project, options, cancellationToken).Run();
    }
}
