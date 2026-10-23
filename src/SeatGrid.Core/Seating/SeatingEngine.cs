using SeatGrid.Core.Models;

namespace SeatGrid.Core.Seating;

public static class SeatingEngine
{
    /// <summary>
    /// 按给定顺序把学生填入座位。座位不够时多出的学生保持未安排；
    /// 学生不够时剩余座位留空。
    /// </summary>
    public static Dictionary<SeatPosition, Student> Arrange(
        IReadOnlyList<Student> students,
        SeatChartLayout layout,
        ArrangeMode mode)
    {
        var ordered = Order(students, mode);
        var seats = layout.FillOrder().ToList();
        var result = new Dictionary<SeatPosition, Student>();

        for (var i = 0; i < ordered.Count && i < seats.Count; i++)
        {
            result[seats[i]] = ordered[i];
        }

        return result;
    }

    public static List<Student> Order(IReadOnlyList<Student> students, ArrangeMode mode)
    {
        if (students.Count == 0) return new List<Student>();

        return mode switch
        {
            ArrangeMode.GenderAlternate => AlternateGender(students),
            ArrangeMode.Random => students.OrderBy(_ => Random.Shared.Next()).ToList(),
            ArrangeMode.Height => students
                .OrderBy(s => s.Height is null ? 1 : 0)
                .ThenBy(s => s.Height)
                .ToList(),
            ArrangeMode.Vision => students
                .OrderBy(s => s.Vision is null ? 1 : 0)
                .ThenBy(s => s.Vision)
                .ToList(),
            _ => students.ToList(),
        };
    }

    private static List<Student> AlternateGender(IReadOnlyList<Student> students)
    {
        var males = students.Where(s => s.Gender == Gender.Male).ToList();
        var females = students.Where(s => s.Gender == Gender.Female).ToList();
        var unknowns = students.Where(s => s.Gender == Gender.Unknown).ToList();

        if (males.Count == 0 && females.Count == 0) return students.ToList();

        var result = new List<Student>(students.Count);
        var maleTurn = students[0].Gender != Gender.Female;

        while (males.Count > 0 || females.Count > 0)
        {
            if (maleTurn)
            {
                if (males.Count > 0) result.Add(males[0]);
                else result.Add(females[0]);
            }
            else
            {
                if (females.Count > 0) result.Add(females[0]);
                else result.Add(males[0]);
            }

            if (result[^1].Gender == Gender.Male) males.RemoveAt(0);
            else females.RemoveAt(0);

            maleTurn = !maleTurn;
        }

        result.AddRange(unknowns);
        return result;
    }
}
