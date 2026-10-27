using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClassBlock.Core.Models;

namespace ClassBlock.Core.Timetable;

/// <summary>模板行的数据来源：None = 纯静态行（朗读、大课间等）。</summary>
public enum TemplateSlot
{
    None,
    Period1, Period2, Period3, Period4, Period5, Period6, Period7,
    EarlyReading, LunchStudy, AfterSchool1, AfterSchool2, Evening,
}

public sealed class TemplateRowSpec
{
    /// <summary>时间列；晚修两段，其余一行一条。</summary>
    public List<string> Times { get; init; } = new();

    public string Label { get; init; } = string.Empty;

    public TimetableRowKind Kind { get; init; } = TimetableRowKind.Normal;

    public TemplateSlot Slot { get; init; } = TemplateSlot.None;

    /// <summary>Slot=None 时的内容：普通行是每格文字，Break/Lunch/Dinner/News 是整行文字。</summary>
    public string StaticText { get; init; } = string.Empty;

    /// <summary>普通静态行五天内容相同则合并为一格。</summary>
    public bool MergeDays { get; init; }
}

public sealed class TimetableTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsBuiltin { get; set; }
    /// <summary>七 / 八 / 九；空 = 不参与年级自动匹配。</summary>
    public string GradeTag { get; set; } = string.Empty;
    public List<TemplateRowSpec> Rows { get; set; } = new();
}

public static class TimetableTemplates
{
    private static readonly Lazy<IReadOnlyList<TimetableTemplate>> BuiltinsLazy = new(LoadBuiltins);

    public static IReadOnlyList<TimetableTemplate> Builtins => BuiltinsLazy.Value;

    public static IReadOnlyList<TimetableTemplate> LoadBuiltins()
    {
        var assembly = typeof(TimetableTemplates).Assembly;
        var list = new List<TimetableTemplate>();

        foreach (var grade in new[] { "7", "8", "9" })
        {
            var name = $"ClassBlock.Core.Timetable.Templates.builtin-grade-{grade}.json";
            var stream = assembly.GetManifestResourceStream(name)
                ?? throw new ClassBlock.Core.ClassBlockException($"内置作息模板缺失：builtin-grade-{grade}。");

            using (stream)
            using (var reader = new StreamReader(stream))
            {
                list.Add(TimetableTemplateIO.FromJson(reader.ReadToEnd()));
            }
        }

        return list;
    }

    /// <summary>按年级前缀匹配内置预设；未识别时回退九年级（即历史默认作息）。</summary>
    public static TimetableTemplate DefaultForGrade(string? grade)
    {
        var text = (grade ?? string.Empty).Trim();
        var match = Builtins.FirstOrDefault(t =>
            t.GradeTag.Length > 0 && text.StartsWith(t.GradeTag, StringComparison.Ordinal));

        return match ?? Builtins.Single(t => t.Id == "builtin-grade-9");
    }

    public static TimetableTemplate? Find(IReadOnlyList<TimetableTemplate> templates, string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : templates.FirstOrDefault(t => t.Id == id);
}
