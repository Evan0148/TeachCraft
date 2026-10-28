namespace MeetRoster.Core.Models;

/// <summary>报名表模板中的固定选项，与附件模板的隐藏工作表保持一致。</summary>
public static class MeetCatalog
{
    public static IReadOnlyList<string> Genders { get; } = ["男子", "女子"];

    public static IReadOnlyList<string> IndividualEvents { get; } =
        ["1000米", "100米", "200米", "400米", "800米", "实心球", "立定跳远", "跳高"];

    public static IReadOnlyList<string> TeamEvents { get; } =
        ["4x100米接力", "20*50米男女混合接力", "一分钟跳绳"];

    public static IReadOnlyList<string> FemaleIndividualEvents { get; } =
        ["100米", "200米", "400米", "800米", "实心球", "立定跳远", "跳高"];

    public static IReadOnlyList<string> MaleIndividualEvents { get; } =
        ["100米", "200米", "400米", "1000米", "实心球", "立定跳远", "跳高"];

    /// <summary>
    /// 把任意顺序的团体项目分布到固定槽位（每个团体项目对应一个单元格）：
    /// 4x100米接力 → 团体项目1、20*50米男女混合接力 → 团体项目2、一分钟跳绳 → 团体项目3。
    /// 目录之外的项目按原顺序填入空槽，不丢弃。导入与写回共用，保证列位稳定。
    /// </summary>
    public static string[] ArrangeTeamSlots(IReadOnlyList<string> values)
    {
        var result = new[] { string.Empty, string.Empty, string.Empty };
        var overflow = new List<string>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var index = IndexOfTeamEvent(value);
            if (index >= 0) result[index] = value;
            else overflow.Add(value);
        }

        var cursor = 0;
        foreach (var value in overflow)
        {
            while (cursor < result.Length && result[cursor].Length > 0) cursor++;
            if (cursor < result.Length) result[cursor] = value;
        }

        return result;
    }

    /// <summary>团体项目在固定槽位中的序号；目录之外返回 -1。</summary>
    public static int IndexOfTeamEvent(string eventName)
    {
        for (var i = 0; i < TeamEvents.Count; i++)
        {
            if (TeamEvents[i] == eventName) return i;
        }

        return -1;
    }

    public static IReadOnlyDictionary<string, MeetEventCategory> IndividualCategories { get; } =
        new Dictionary<string, MeetEventCategory>
        {
            ["1000米"] = MeetEventCategory.Track,
            ["100米"] = MeetEventCategory.Track,
            ["200米"] = MeetEventCategory.Track,
            ["400米"] = MeetEventCategory.Track,
            ["800米"] = MeetEventCategory.Track,
            ["实心球"] = MeetEventCategory.Field,
            ["立定跳远"] = MeetEventCategory.Field,
            ["跳高"] = MeetEventCategory.Field,
        };
}
