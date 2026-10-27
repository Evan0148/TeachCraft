using GradeStrip.Core.Model;

namespace GradeStrip.Core.Export;

/// <summary>按「首表 N 人、之后每表 M 人」切分学生，预览与导出共用同一套分组规则。</summary>
public static class StripGrouping
{
    public static List<IReadOnlyList<StudentRecord>> Split(
        IReadOnlyList<StudentRecord> students, int firstSize, int groupSize)
    {
        firstSize = Math.Max(1, firstSize);
        groupSize = Math.Max(1, groupSize);

        var groups = new List<IReadOnlyList<StudentRecord>>();
        if (students.Count == 0) return groups;

        var index = Math.Min(firstSize, students.Count);
        groups.Add(students.Take(index).ToList());
        while (index < students.Count)
        {
            groups.Add(students.Skip(index).Take(groupSize).ToList());
            index += groupSize;
        }

        return groups;
    }
}
