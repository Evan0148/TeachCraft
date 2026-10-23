using System.Collections.Generic;
using System.Linq;
using ClassBlock.Core.Models;

namespace ClassBlock.App;

public sealed class ClassItemViewModel
{
    public ClassItemViewModel(ClassSchedule schedule, string headTeacher)
    {
        Key = schedule.Key;
        Name = schedule.Name;
        HeadTeacher = headTeacher;
        Grade = schedule.Grade;
        Number = schedule.Number;
        PeriodCount = schedule.Days.Sum(day => day.Periods.Count(period => !period.IsEmpty));
    }

    public string Key { get; }
    public string Name { get; }
    public string HeadTeacher { get; }
    public string Grade { get; }
    public int Number { get; }
    public int PeriodCount { get; }

    public string Badge => Key;

    public string Meta => string.IsNullOrWhiteSpace(HeadTeacher)
        ? $"共 {PeriodCount} 节课"
        : $"班主任 {HeadTeacher} · 共 {PeriodCount} 节课";

    public string AutomationName => $"{Name}，{Meta}";
}
