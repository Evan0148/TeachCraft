namespace MeetRoster.Core.Models;

using MeetRoster.Core.Scheduling;

/// <summary>内存构造的合成演示数据，不含任何真实学校、教师或学生信息。</summary>
public static class MeetSamples
{
    private static readonly string[] FemaleNames =
    [
        "苏晚晴", "林见夏", "顾之遥", "周予安", "许听澜", "宋知雨",
        "叶书宁", "江晚舟", "白芷汀", "程梨落", "沈望舒", "陆疏桐",
        "秦昭昭", "唐见微", "黎清晏", "夏栀", "林栖月", "顾青梧",
        "苏念安", "沈知遥", "温辞", "叶南絮", "闻溪", "孟星回",
    ];

    private static readonly string[] MaleNames =
    [
        "陈屿", "江朔", "周牧野", "许砚", "沈观澜", "陆行舟",
        "顾屿川", "林越", "程野", "苏景行", "唐砚舟", "秦朗",
        "白澈", "宋亦舟", "韩序", "黎川", "夏柏舟", "叶听风",
        "陈述", "顾长川", "沈砚青", "陆时安", "周砚", "林照野",
    ];

    private static readonly string[][] FemalePatterns =
    [
        ["100米", "跳高"],
        ["200米", "实心球"],
        ["400米", "800米"],
        ["100米", "立定跳远"],
    ];

    private static readonly string[][] MalePatterns =
    [
        ["100米", "200米"],
        ["400米", "1000米"],
        ["跳高", "实心球"],
        ["100米", "立定跳远"],
    ];

    private static readonly string[] ClassNames =
        ["七年级1班", "七年级2班", "八年级1班", "八年级2班", "九年级1班", "九年级2班"];

    private static readonly string[] Leaders =
        ["沈青禾", "陆明川", "顾知远", "苏映棠", "程叙白", "叶临溪"];

    private static readonly string[] Coaches =
        ["韩知微", "温以宁", "罗清和", "韩知微", "温以宁", "罗清和"];

    public static MeetProject CreateDemo()
    {
        var project = new MeetProject
        {
            Title = "示例中学第五届田径运动会",
            Venue = "田径场",
            Note = "每班每个单项最多男女各报 2 人，每人限报 2 个单项；团体项目最多报 3 项（4x100 接力、20*50 混合接力、一分钟跳绳）。",
        };

        foreach (var eventName in MeetCatalog.IndividualEvents)
        {
            project.Events.Add(new MeetEvent
            {
                Name = eventName,
                Gender = "男女",
                Category = MeetCatalog.IndividualCategories[eventName],
            });
        }

        foreach (var eventName in MeetCatalog.TeamEvents)
        {
            project.Events.Add(new MeetEvent
            {
                Name = eventName,
                Gender = eventName.Contains("混合", StringComparison.Ordinal) ? "混合" : "男女",
                Category = MeetEventCategory.Team,
            });
        }

        for (var classIndex = 0; classIndex < ClassNames.Length; classIndex++)
        {
            var meetClass = new MeetClass
            {
                Name = ClassNames[classIndex],
                Group = ClassNames[classIndex][..3],
                Code = "（不填）",
                LeaderContact = $"{Leaders[classIndex]} · 138 0000 000{classIndex + 1}",
                Coach = Coaches[classIndex],
            };

            AddAthletes(meetClass, classIndex);
            project.Classes.Add(meetClass);
        }

        BuildDays(project);
        MeetScheduleBuilder.RefreshEntries(project);
        return project;
    }

    private static void BuildDays(MeetProject project)
    {
        var first = new MeetDay { Name = "第一天", DateText = "10 月 30 日" };
        AddSlot(first, "08:30", "上午", "开幕式", "不限", MeetEventCategory.Ceremony, "开幕式", "田径场");
        AddSlot(first, "09:00", "上午", "100米", "女子", MeetEventCategory.Track, "预赛", "田径场");
        AddSlot(first, "09:25", "上午", "100米", "男子", MeetEventCategory.Track, "预赛", "田径场");
        AddSlot(first, "09:50", "上午", "800米", "女子", MeetEventCategory.Track, "决赛", "田径场");
        AddSlot(first, "10:20", "上午", "1000米", "男子", MeetEventCategory.Track, "决赛", "田径场");
        AddSlot(first, "10:30", "上午", "跳高", "女子", MeetEventCategory.Field, "决赛", "跳高区");
        AddSlot(first, "10:45", "上午", "立定跳远", "男子", MeetEventCategory.Field, "决赛", "沙坑区");
        AddSlot(first, "11:00", "上午", "实心球", "女子", MeetEventCategory.Field, "决赛", "投掷区");
        AddSlot(first, "14:30", "下午", "100米", "男子", MeetEventCategory.Track, "决赛", "田径场");
        AddSlot(first, "14:50", "下午", "200米", "女子", MeetEventCategory.Track, "预赛", "田径场");
        AddSlot(first, "15:20", "下午", "4x100米接力", "混合", MeetEventCategory.Track, "预赛", "田径场");

        var second = new MeetDay { Name = "第二天", DateText = "10 月 31 日" };
        AddSlot(second, "08:30", "上午", "400米", "女子", MeetEventCategory.Track, "预赛", "田径场");
        AddSlot(second, "09:00", "上午", "200米", "男子", MeetEventCategory.Track, "预赛", "田径场");
        AddSlot(second, "09:30", "上午", "立定跳远", "女子", MeetEventCategory.Field, "决赛", "沙坑区");
        AddSlot(second, "10:00", "上午", "跳高", "男子", MeetEventCategory.Field, "决赛", "跳高区");
        AddSlot(second, "10:30", "上午", "一分钟跳绳", "混合", MeetEventCategory.Fun, "决赛", "篮球场");
        AddSlot(second, "14:30", "下午", "20*50米男女混合接力", "混合", MeetEventCategory.Fun, "决赛", "田径场");
        AddSlot(second, "15:00", "下午", "100米", "女子", MeetEventCategory.Track, "决赛", "田径场");
        AddSlot(second, "15:30", "下午", "4x100米接力", "混合", MeetEventCategory.Track, "决赛", "田径场");
        AddSlot(second, "16:00", "下午", "闭幕式", "不限", MeetEventCategory.Ceremony, "闭幕式", "田径场");

        project.Days.Add(first);
        project.Days.Add(second);
    }

    private static void AddSlot(
        MeetDay day,
        string time,
        string period,
        string eventName,
        string gender,
        MeetEventCategory category,
        string stage,
        string venue)
    {
        day.Slots.Add(new MeetSlot
        {
            Time = time,
            Period = period,
            EventName = eventName,
            Gender = gender,
            Category = category,
            Stage = stage,
            Venue = venue,
        });
    }

    private static void AddAthletes(MeetClass meetClass, int classIndex)
    {
        for (var i = 0; i < 4; i++)
        {
            var female = new Athlete
            {
                Name = FemaleNames[classIndex * 4 + i],
                Gender = "女子",
            };
            ApplyPattern(female, FemalePatterns[(i + classIndex) % 4]);
            ApplyTeamEvents(female, i);
            meetClass.Athletes.Add(female);

            var male = new Athlete
            {
                Name = MaleNames[classIndex * 4 + i],
                Gender = "男子",
            };
            ApplyPattern(male, MalePatterns[(i + classIndex) % 4]);
            ApplyTeamEvents(male, i);
            meetClass.Athletes.Add(male);
        }
    }

    private static void ApplyPattern(Athlete athlete, string[] pattern)
    {
        athlete.IndividualEvent1 = pattern[0];
        athlete.IndividualEvent2 = pattern[1];
    }

    private static void ApplyTeamEvents(Athlete athlete, int index)
    {
        athlete.TeamEvent1 = "4x100米接力";

        // 合成数据里让团体项目数量有差别（1 / 2 / 3 个），便于查看表格只显示已报项目。
        switch (index % 3)
        {
            case 1:
                athlete.TeamEvent2 = "20*50米男女混合接力";
                break;
            case 2:
                athlete.TeamEvent2 = "20*50米男女混合接力";
                athlete.TeamEvent3 = "一分钟跳绳";
                break;
        }
    }
}
