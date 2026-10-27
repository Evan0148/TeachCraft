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
