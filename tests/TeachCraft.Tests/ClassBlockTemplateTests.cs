using System.Collections.Generic;
using ClassBlock.Core;
using ClassBlock.Core.Import;
using ClassBlock.Core.Models;
using ClassBlock.Core.Timetable;

namespace TeachCraft.Tests;

public sealed class ClassBlockTemplateTests : IDisposable
{
    private readonly string _root;

    public ClassBlockTemplateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TeachCraft.Tests", "ClassBlockTemplate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static TimetableTemplate SampleTemplate() => new()
    {
        Id = "t1",
        Name = "测试作息",
        IsBuiltin = false,
        GradeTag = "七",
        Rows =
        {
            new TemplateRowSpec { Times = { "7:20—7:50" }, Label = "早读", Slot = TemplateSlot.EarlyReading },
            new TemplateRowSpec { Times = { "10:50—11:00" }, Label = "朗读", StaticText = "朗读", MergeDays = true },
            new TemplateRowSpec { Times = { "9:30—10:00" }, Kind = TimetableRowKind.Break, StaticText = "大课间" },
            new TemplateRowSpec { Kind = TimetableRowKind.Lunch, StaticText = "11:30—12:00　午餐" },
            new TemplateRowSpec { Times = { "19:20—20:20", "20:30—21:30" }, Label = "晚修", Kind = TimetableRowKind.Evening, Slot = TemplateSlot.Evening },
        },
    };

    [Fact]
    public void TemplateJson_RoundTrips()
    {
        var template = SampleTemplate();

        var restored = TimetableTemplateIO.FromJson(TimetableTemplateIO.ToJson(template));

        Assert.Equal(template.Id, restored.Id);
        Assert.Equal(template.Name, restored.Name);
        Assert.Equal(template.IsBuiltin, restored.IsBuiltin);
        Assert.Equal(template.GradeTag, restored.GradeTag);
        Assert.Equal(template.Rows.Count, restored.Rows.Count);

        Assert.Equal(template.Rows[0].Times, restored.Rows[0].Times);
        Assert.Equal(template.Rows[1].StaticText, restored.Rows[1].StaticText);
        Assert.True(restored.Rows[1].MergeDays);
        Assert.Equal(TimetableRowKind.Break, restored.Rows[2].Kind);
        Assert.Equal(TemplateSlot.EarlyReading, restored.Rows[0].Slot);
        Assert.Equal(TemplateSlot.Evening, restored.Rows[4].Slot);
        Assert.Equal(2, restored.Rows[4].Times.Count);
    }

    [Fact]
    public void TemplateJson_RejectsGarbage()
    {
        Assert.Throws<ClassBlock.Core.ClassBlockException>(() => TimetableTemplateIO.FromJson("{ not json"));
    }

    [Fact]
    public void Builtins_ContainThreeGradePresets()
    {
        var builtins = TimetableTemplates.Builtins;

        Assert.Equal(3, builtins.Count);
        Assert.Equal(new[] { "七", "八", "九" }, builtins.Select(t => t.GradeTag).ToArray());
        Assert.All(builtins, t => Assert.True(t.IsBuiltin));
    }

    [Fact]
    public void DefaultForGrade_MatchesTagAndFallsBackToGrade9()
    {
        Assert.Equal("builtin-grade-7", TimetableTemplates.DefaultForGrade("七年级").Id);
        Assert.Equal("builtin-grade-8", TimetableTemplates.DefaultForGrade("八年级").Id);
        Assert.Equal("builtin-grade-9", TimetableTemplates.DefaultForGrade("九年级").Id);
        Assert.Equal("builtin-grade-9", TimetableTemplates.DefaultForGrade("").Id);
        Assert.Equal("builtin-grade-9", TimetableTemplates.DefaultForGrade("十年级").Id);
    }

    [Fact]
    public void BuiltinGrade9_PreservesLegacyRowLayout()
    {
        var master = MasterScheduleImporter.Import(ClassBlockFixtures.CreateMasterWorkbook(_root));
        var legacy = TimetableBuilder.Build(master, "九1", new TimetableSettings { ShowTeachers = true });
        var templated = TimetableBuilder.Build(
            master, "九1", TimetableTemplates.DefaultForGrade("九年级"), new TimetableSettings { ShowTeachers = true });

        Assert.Equal(legacy.Rows.Count, templated.Rows.Count);
        for (var i = 0; i < legacy.Rows.Count; i++)
        {
            var a = legacy.Rows[i];
            var b = templated.Rows[i];
            Assert.Equal(a.Label, b.Label);
            Assert.Equal(a.Times, b.Times);
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.MergedText, b.MergedText);
            Assert.Equal(a.MergeDays, b.MergeDays);
            Assert.Equal(a.Cells, b.Cells);
        }
    }

    [Fact]
    public void BuiltinGrade7_InsertsReadingRowAfterThirdPeriod()
    {
        var master = MasterScheduleImporter.Import(ClassBlockFixtures.CreateMasterWorkbook(_root));
        var timetable = TimetableBuilder.Build(
            master, "九1", TimetableTemplates.DefaultForGrade("七年级"), new TimetableSettings());

        var reading = timetable.Rows.Single(r => r.Label == "朗读");
        var index = timetable.Rows.ToList().IndexOf(reading);

        Assert.True(index > 0);
        Assert.Equal("3", timetable.Rows[index - 1].Label);
        Assert.Equal("4", timetable.Rows[index + 1].Label);
        Assert.Equal(new[] { "10:50—11:00" }, reading.Times);
        Assert.All(reading.Cells, c => Assert.Equal("朗读", c));
        Assert.True(reading.MergeDays);
        Assert.Equal(new[] { "11:00—11:40" }, timetable.Rows[index + 1].Times);
    }

    [Fact]
    public void BuiltinGrade8_ReadingRunsTwentyMinutes()
    {
        var master = MasterScheduleImporter.Import(ClassBlockFixtures.CreateMasterWorkbook(_root));
        var timetable = TimetableBuilder.Build(
            master, "九1", TimetableTemplates.DefaultForGrade("八年级"), new TimetableSettings());

        var reading = timetable.Rows.Single(r => r.Label == "朗读");
        Assert.Equal(new[] { "10:50—11:10" }, reading.Times);
        Assert.Equal(new[] { "11:10—11:50" }, timetable.Rows[timetable.Rows.ToList().IndexOf(reading) + 1].Times);
    }

    [Fact]
    public void ShowToggles_HideOnlyBoundRows()
    {
        var master = MasterScheduleImporter.Import(ClassBlockFixtures.CreateMasterWorkbook(_root));
        var timetable = TimetableBuilder.Build(
            master, "九1", TimetableTemplates.DefaultForGrade("七年级"),
            new TimetableSettings { ShowEarlyReading = false, ShowAfterSchool = false });

        var labels = timetable.Rows.Select(r => r.Label).ToArray();
        Assert.DoesNotContain("早读", labels);
        Assert.DoesNotContain("课服1", labels);
        Assert.DoesNotContain("课服2", labels);
        Assert.Contains("朗读", labels);
        Assert.Contains("午学", labels);
        Assert.Contains("晚修", labels);
        Assert.Contains(timetable.Rows, r => r.Kind == TimetableRowKind.Dinner);
    }

    [Fact]
    public void CustomTemplates_SaveLoadDeleteRoundTrip()
    {
        var dir = Path.Combine(_root, "templates");
        var template = SampleTemplate();
        template.Id = "custom-1";

        TimetableTemplateIO.SaveCustom(template, dir);

        var (templates, warnings) = TimetableTemplateIO.LoadCustomTemplates(dir);
        Assert.Empty(warnings);
        Assert.Single(templates);
        Assert.Equal("custom-1", templates[0].Id);
        Assert.Equal("测试作息", templates[0].Name);

        TimetableTemplateIO.DeleteCustom("custom-1", dir);
        var (after, _) = TimetableTemplateIO.LoadCustomTemplates(dir);
        Assert.Empty(after);
    }

    [Fact]
    public void CorruptCustomTemplate_IsSkippedWithWarning()
    {
        var dir = Path.Combine(_root, "broken");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json");

        TimetableTemplateIO.SaveCustom(new TimetableTemplate
        {
            Id = "ok",
            Name = "好的",
            Rows = { new TemplateRowSpec { Times = { "7:20—7:50" }, Label = "早读", Slot = TemplateSlot.EarlyReading } },
        }, dir);

        var (templates, warnings) = TimetableTemplateIO.LoadCustomTemplates(dir);
        Assert.Single(templates);
        Assert.Equal("ok", templates[0].Id);
        Assert.Single(warnings);
        Assert.Contains("broken", warnings[0]);
    }

    [Fact]
    public void Prefs_LastTemplateByGrade_RoundTrips()
    {
        var dir = Path.Combine(_root, "prefs");
        var prefs = new ClassBlockPrefs();
        prefs.LastTemplateByGrade["七年级"] = "custom-1";
        prefs.LastTemplateByGrade["九年级"] = "builtin-grade-9";

        ClassBlockPrefsStore.Save(prefs, dir);
        var restored = ClassBlockPrefsStore.Load(dir);

        Assert.Equal("custom-1", restored.LastTemplateByGrade["七年级"]);
        Assert.Equal("builtin-grade-9", restored.LastTemplateByGrade["九年级"]);
        Assert.Empty(ClassBlockPrefsStore.Load(Path.Combine(_root, "missing")).LastTemplateByGrade);
    }
}
