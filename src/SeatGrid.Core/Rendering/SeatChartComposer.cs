using System.Collections.Generic;
using System.Linq;
using SeatGrid.Core.Models;

namespace SeatGrid.Core.Rendering;

public sealed class SeatChartDocument
{
    public required SeatChartSettings Settings { get; init; }
    public required SeatChartLayout Layout { get; init; }
    public required string Title { get; init; }
    public required string InfoLine { get; init; }
    public required bool PodiumOnTop { get; init; }
    public required IReadOnlyList<DisplayGroup> Groups { get; init; }
    public required int StudentCount { get; init; }
}

public sealed class DisplayGroup
{
    public required int Number { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<DisplayRow> Rows { get; init; }
}

public sealed class DisplayRow
{
    public required int Number { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<DisplaySeat> Seats { get; init; }
}

public sealed class DisplaySeat
{
    public required SeatPosition Position { get; init; }
    public required string Name { get; init; }
    public required Student? Student { get; init; }
    public required bool IsEmpty { get; init; }
    public required bool IsFemale { get; init; }
}

/// <summary>
/// 把「物理坐标 + 视角」翻译成可绘制的展示顺序：
/// 教师视角讲台在下方、第一排靠下；学生视角整体旋转 180°，讲台在上方。
/// </summary>
public static class SeatChartComposer
{
    public static SeatChartDocument Compose(
        SeatChartSettings settings,
        IReadOnlyDictionary<SeatPosition, Student> assignments,
        int studentCount)
    {
        settings.Normalize();
        var layout = settings.GetLayout();
        var teacher = settings.View == ViewMode.Teacher;

        var groupNumbers = teacher
            ? Enumerable.Range(0, layout.Groups)
            : Enumerable.Range(0, layout.Groups).Reverse();

        var rowNumbers = teacher
            ? Enumerable.Range(0, layout.Rows).Reverse()
            : Enumerable.Range(0, layout.Rows);

        var groups = groupNumbers.Select(group =>
        {
            var rows = rowNumbers.Select(row =>
            {
                var seats = Enumerable.Range(0, layout.Columns).Select(column =>
                {
                    var position = new SeatPosition(group, row, column);
                    assignments.TryGetValue(position, out var student);
                    return new DisplaySeat
                    {
                        Position = position,
                        Student = student,
                        IsEmpty = student is null,
                        IsFemale = student?.Gender == Gender.Female,
                        Name = student is null ? string.Empty : DisplayName(student.Name, settings.SpaceTwoCharNames),
                    };
                }).ToList();

                return new DisplayRow
                {
                    Number = row + 1,
                    Label = $"第{row + 1}排",
                    Seats = seats,
                };
            }).ToList();

            return new DisplayGroup
            {
                Number = group + 1,
                Label = $"第{group + 1}组",
                Rows = rows,
            };
        }).ToList();

        var infoParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.HeadTeacher))
            infoParts.Add($"班主任：{settings.HeadTeacher.Trim()}");
        infoParts.Add($"人数：{studentCount}");
        if (!string.IsNullOrWhiteSpace(settings.Monitor))
            infoParts.Add($"班长：{settings.Monitor.Trim()}");

        return new SeatChartDocument
        {
            Settings = settings,
            Layout = layout,
            Title = string.IsNullOrWhiteSpace(settings.Title) ? "座位表" : settings.Title.Trim(),
            InfoLine = string.Join("　　", infoParts),
            PodiumOnTop = !teacher,
            Groups = groups,
            StudentCount = studentCount,
        };
    }

    public static string DisplayName(string name, bool spaceTwoCharNames)
    {
        if (!spaceTwoCharNames) return name;

        var trimmed = name.Trim();
        return trimmed.Length == 2 && trimmed.All(IsCjk)
            ? $"{trimmed[0]} {trimmed[1]}"
            : trimmed;
    }

    private static bool IsCjk(char c) => c is >= '\u4e00' and <= '\u9fff';
}
