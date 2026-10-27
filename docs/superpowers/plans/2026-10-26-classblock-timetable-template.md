# ClassBlock 作息模板（多年级支持）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 ClassBlock 硬编码的作息骨架改为数据驱动的「作息模板」（内置七/八/九三套预设 + 界面内可视化编辑自定义模板），解决多年级差异（如七年级 10:50—11:00 朗读课）。

**Architecture:** `TimetableTemplate`（行列表，每行含时间/标签/类型/Slot 数据绑定/静态文字）→ `TimetableBuilder` 按 Slot 绑定总表数据；内置预设为 Core 内嵌 JSON 资源，自定义模板存 `%APPDATA%\TeachCraft\ClassBlock\templates\`，`settings.json` 记录每个年级上次使用的模板；UI 为设置面板下拉 + 模态编辑器窗口。渲染层零改动。

**Tech Stack:** C# / .NET 10, Avalonia 12.1.4, CommunityToolkit.Mvvm, System.Text.Json, ClosedXML（既有测试 fixture）, xUnit。

**约定：**
- 每个任务一个 Conventional Commit；若用户未要求提交则跳过 commit 步骤。
- 版本已是 `1.3.0`（`Directory.Build.props`，且无 `v1.3*` tag），本特性并入 1.3.0，不另 bump。
- 提交信息与代码注释用中文语境；不得出现任何真实学生/教师姓名。

---

## File Structure

```
tests/TeachCraft.Tests/
  ClassBlockFixtures.cs            [新建] 合成总表 fixture（从 ClassBlockTests 抽出，供模板测试复用）
  ClassBlockTests.cs               [修改] 改用 ClassBlockFixtures
  ClassBlockTemplateTests.cs       [新建] 模板核心测试（约 9 个用例）

src/ClassBlock.Core/
  Timetable/TimetableTemplate.cs   [新建] TemplateSlot / TemplateRowSpec / TimetableTemplate / TimetableTemplates（内置加载 + 年级匹配）
  Timetable/TimetableTemplateIO.cs [新建] JSON 序列化、自定义模板目录读写、ClassBlockPrefs（年级→模板记忆）
  Timetable/Templates/
    builtin-grade-7.json           [新建] 内置预设（朗读 10:50—11:00，第 4 节占位 11:00—11:40）
    builtin-grade-8.json           [新建] 内置预设（朗读 10:50—11:10，第 4 节占位 11:10—11:50）
    builtin-grade-9.json           [新建] 内置预设 = 现有硬编码输出的逐行快照
  Timetable/TimetableBuilder.cs    [修改] 移除 PeriodSlots/AddXxx，改为模板驱动
  ClassBlock.Core.csproj           [修改] 增加 EmbeddedResource

src/ClassBlock.App/
  ClassBlockViewModel.cs           [修改] Templates/SelectedTemplate、prefs 记忆、Build 传模板
  ClassBlockWindow.axaml           [修改] 设置面板加「作息模板」卡
  ClassBlockWindow.axaml.cs        [修改] 编辑器打开/保存回调
  TimetableTemplateEditorViewModel.cs [新建] 编辑器 VM（行卡编辑、增删移、另存/删除语义）
  TimetableTemplateEditorWindow.axaml      [新建] 编辑器窗口
  TimetableTemplateEditorWindow.axaml.cs   [新建] 保存/删除/关闭

Directory.Build.props              [不改] 已是 1.3.0
AGENTS.md                          [修改] 第七节行序契约改模板驱动
.memory/10-时序课程表.md            [修改] 管线图、行序章节、边界条目
```

---

### Task 1: 抽出测试 fixture（ClassBlockFixtures）

**Files:**
- Create: `tests/TeachCraft.Tests/ClassBlockFixtures.cs`
- Modify: `tests/TeachCraft.Tests/ClassBlockTests.cs`

- [ ] **Step 1: 新建 ClassBlockFixtures.cs，把 ClassBlockTests 中 `CreateMasterWorkbook` 及 4 个 BuildXxxSheet 私有方法原样搬入**

```csharp
using ClosedXML.Excel;

namespace TeachCraft.Tests;

/// <summary>按真实总表骨架造的小号合成工作簿：课程表 + 分工表 + 午学 + 课后服务。全部为合成数据。</summary>
internal static class ClassBlockFixtures
{
    internal static readonly string[] DayHeaders = { "星期一", "星期二", "星期三", "星期四", "星期五" };

    public static string CreateMasterWorkbook(string rootDirectory, string fileName = "master.xlsx")
    {
        var path = Path.Combine(rootDirectory, fileName);

        using var workbook = new XLWorkbook();
        BuildScheduleSheet(workbook);
        BuildHeadTeacherSheet(workbook);
        BuildLunchStudySheet(workbook);
        BuildAfterSchoolSheet(workbook);
        workbook.SaveAs(path);

        return path;
    }

    private static void BuildScheduleSheet(XLWorkbook workbook)
    {
        // ↓↓↓ 从 ClassBlockTests.BuildScheduleSheet 原样复制（第 134–178 行），逐行不动 ↓↓↓
    }

    private static void BuildHeadTeacherSheet(XLWorkbook workbook)
    {
        // ↓↓↓ 从 ClassBlockTests.BuildHeadTeacherSheet 原样复制（第 180–189 行） ↓↓↓
    }

    private static void BuildLunchStudySheet(XLWorkbook workbook)
    {
        // ↓↓↓ 从 ClassBlockTests.BuildLunchStudySheet 原样复制（第 191–207 行），其中 DayHeaders 改引用本类静态字段 ↓↓↓
    }

    private static void BuildAfterSchoolSheet(XLWorkbook workbook)
    {
        // ↓↓↓ 从 ClassBlockTests.BuildAfterSchoolSheet 原样复制（第 209–238 行） ↓↓↓
    }
}
```

> 注意：`BuildLunchStudySheet` 里用到 `DayHeaders`，改为 `ClassBlockFixtures.DayHeaders`；fixture 文件顶部需要 `using System.IO;` 不必要（ImplicitUsings 已含）。

- [ ] **Step 2: ClassBlockTests.cs 删除这 5 个方法与 DayHeaders 字段，调用点改为 fixture**

删除：`private static readonly string[] DayHeaders`（第 12 行）与 5 个方法（第 119–238 行）。所有 `CreateMasterWorkbook("master.xlsx")` 调用改为：

```csharp
var master = MasterScheduleImporter.Import(ClassBlockFixtures.CreateMasterWorkbook(_root, "master.xlsx"));
```

（文件内共 3 处：第 60、93 行与 `CreateMasterWorkbook` 原私有方法体内。）

- [ ] **Step 3: 运行既有测试确认无回归**

Run: `dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release --filter ClassBlockTests`
Expected: ClassBlockTests 3 个用例全部 PASS

- [ ] **Step 4: Commit** `test(classblock): 抽出合成总表 fixture 供模板测试复用`

---

### Task 2: 模板数据模型 + JSON 序列化

**Files:**
- Create: `src/ClassBlock.Core/Timetable/TimetableTemplate.cs`
- Create: `src/ClassBlock.Core/Timetable/TimetableTemplateIO.cs`
- Test: `tests/TeachCraft.Tests/ClassBlockTemplateTests.cs`

- [ ] **Step 1: 写失败测试（新文件 ClassBlockTemplateTests.cs）**

```csharp
using System.Collections.Generic;
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
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release --filter ClassBlockTemplateTests`
Expected: 编译错误 CS0246（找不到 TemplateSlot / TimetableTemplate / TimetableTemplateIO）

- [ ] **Step 3: 实现 TimetableTemplate.cs**

```csharp
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
```

- [ ] **Step 4: 实现 TimetableTemplateIO.cs**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassBlock.Core.Models;

namespace ClassBlock.Core.Timetable;

public static class TimetableTemplateIO
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(TimetableTemplate template) =>
        JsonSerializer.Serialize(template, JsonOptions);

    public static TimetableTemplate FromJson(string json)
    {
        try
        {
            var template = JsonSerializer.Deserialize<TimetableTemplate>(json, JsonOptions)
                ?? throw new ClassBlock.Core.ClassBlockException("模板内容为空，无法读取。");

            if (template.Rows.Count == 0)
                throw new ClassBlock.Core.ClassBlockException("模板里没有任何行，无法使用。");

            return template;
        }
        catch (ClassBlock.Core.ClassBlockException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ClassBlock.Core.ClassBlockException($"模板文件格式不正确：{ex.Message}", ex);
        }
    }

    // ----------------------------- 自定义模板目录 -----------------------------

    public static string GetCustomDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TeachCraft", "ClassBlock", "templates");

    public static (IReadOnlyList<TimetableTemplate> Templates, IReadOnlyList<string> Warnings) LoadCustomTemplates(
        string? directory = null)
    {
        var dir = directory ?? GetCustomDirectory();
        var templates = new List<TimetableTemplate>();
        var warnings = new List<string>();

        if (!Directory.Exists(dir)) return (templates, warnings);

        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                templates.Add(FromJson(File.ReadAllText(file)));
            }
            catch (ClassBlock.Core.ClassBlockException)
            {
                warnings.Add($"自定义模板 {Path.GetFileNameWithoutExtension(file)} 无法读取，已忽略。");
            }
        }

        return (templates, warnings);
    }

    public static string SaveCustom(TimetableTemplate template, string? directory = null)
    {
        if (string.IsNullOrWhiteSpace(template.Id))
            throw new ClassBlock.Core.ClassBlockException("自定义模板缺少编号，无法保存。");

        var dir = directory ?? GetCustomDirectory();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{template.Id}.json");
        File.WriteAllText(path, ToJson(template));
        return path;
    }

    public static void DeleteCustom(string id, string? directory = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        var path = Path.Combine(directory ?? GetCustomDirectory(), $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
    }
}

/// <summary>每个年级最近一次使用的模板编号，随窗口关闭前后的选择保存。</summary>
public sealed class ClassBlockPrefs
{
    public Dictionary<string, string> LastTemplateByGrade { get; set; } = new();
}

public static class ClassBlockPrefsStore
{
    private static string GetFilePath(string? directory) => Path.Combine(
        directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TeachCraft", "ClassBlock"),
        "settings.json");

    public static ClassBlockPrefs Load(string? directory = null)
    {
        var path = GetFilePath(directory);
        if (!File.Exists(path)) return new ClassBlockPrefs();

        try
        {
            return JsonSerializer.Deserialize<ClassBlockPrefs>(File.ReadAllText(path)) ?? new ClassBlockPrefs();
        }
        catch
        {
            return new ClassBlockPrefs();
        }
    }

    public static void Save(ClassBlockPrefs prefs, string? directory = null)
    {
        var path = GetFilePath(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(prefs, JsonOptions));
    }
}
```

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release --filter ClassBlockTemplateTests`
Expected: 2 个用例 PASS

- [ ] **Step 6: Commit** `feat(classblock-core): 作息模板数据模型与 JSON 序列化`

---

### Task 3: 三套内置预设 + 年级匹配

**Files:**
- Create: `src/ClassBlock.Core/Timetable/Templates/builtin-grade-9.json`
- Create: `src/ClassBlock.Core/Timetable/Templates/builtin-grade-7.json`
- Create: `src/ClassBlock.Core/Timetable/Templates/builtin-grade-8.json`
- Modify: `src/ClassBlock.Core/ClassBlock.Core.csproj`
- Test: `tests/TeachCraft.Tests/ClassBlockTemplateTests.cs`

- [ ] **Step 1: 写失败测试（追加到 ClassBlockTemplateTests.cs）**

```csharp
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
```

- [ ] **Step 2: 运行确认失败**（Builtins 抛 ClassBlockException：内置作息模板缺失）

Run: `dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release --filter "DefaultForGrade|Builtins_Contain"`
Expected: FAIL

- [ ] **Step 3: 写 builtin-grade-9.json（历史硬编码输出的逐行快照）**

```json
{
  "id": "builtin-grade-9",
  "name": "九年级作息（内置）",
  "isBuiltin": true,
  "gradeTag": "九",
  "rows": [
    { "times": ["7:20—7:50"], "label": "早读", "kind": "Normal", "slot": "EarlyReading" },
    { "times": ["8:00—8:40"], "label": "1", "kind": "Normal", "slot": "Period1" },
    { "times": ["8:50—9:30"], "label": "2", "kind": "Normal", "slot": "Period2" },
    { "times": ["9:30—10:00"], "label": "", "kind": "Break", "slot": "None", "staticText": "大课间" },
    { "times": ["10:00—10:40"], "label": "3", "kind": "Normal", "slot": "Period3" },
    { "times": ["10:50—11:30"], "label": "4", "kind": "Normal", "slot": "Period4" },
    { "times": [], "label": "", "kind": "Lunch", "slot": "None", "staticText": "11:30—12:00　午餐" },
    { "times": ["12:00—12:30"], "label": "午学", "kind": "Normal", "slot": "LunchStudy" },
    { "times": ["14:20—15:00"], "label": "5", "kind": "Normal", "slot": "Period5" },
    { "times": ["15:10—15:50"], "label": "6", "kind": "Normal", "slot": "Period6" },
    { "times": ["16:00—16:40"], "label": "7", "kind": "Normal", "slot": "Period7" },
    { "times": ["16:50—17:20"], "label": "课服1", "kind": "Normal", "slot": "AfterSchool1" },
    { "times": ["17:20—17:50"], "label": "课服2", "kind": "Normal", "slot": "AfterSchool2" },
    { "times": [], "label": "", "kind": "Dinner", "slot": "None", "staticText": "17:50—18:50　晚餐" },
    { "times": ["19:00—19:20"], "label": "", "kind": "News", "slot": "None", "staticText": "新闻联播" },
    { "times": ["19:20—20:20", "20:30—21:30"], "label": "晚修", "kind": "Evening", "slot": "Evening" }
  ]
}
```

> 注意：午餐/晚餐 `staticText` 中「12:00　午餐」之间是全角空格，与历史输出一致。

- [ ] **Step 4: 写 builtin-grade-7.json（第 3 节后插朗读行，第 4 节顺延——第 4 节与午餐时间为占位，上线后可在编辑器里改）**

复制 grade-9 全文后仅改：`id`→`builtin-grade-7`、`name`→`七年级作息（内置）`、`gradeTag`→`七`、`Period3` 行之后插入：

```json
    { "times": ["10:50—11:00"], "label": "朗读", "kind": "Normal", "slot": "None", "staticText": "朗读", "mergeDays": true },
```

并把 `Period4` 行改为：

```json
    { "times": ["11:00—11:40"], "label": "4", "kind": "Normal", "slot": "Period4" },
```

- [ ] **Step 5: 写 builtin-grade-8.json**

同 grade-7 结构：`id`→`builtin-grade-8`、`name`→`八年级作息（内置）`、`gradeTag`→`八`，朗读行：

```json
    { "times": ["10:50—11:10"], "label": "朗读", "kind": "Normal", "slot": "None", "staticText": "朗读", "mergeDays": true },
```

`Period4` 行改为：

```json
    { "times": ["11:10—11:50"], "label": "4", "kind": "Normal", "slot": "Period4" },
```

- [ ] **Step 6: csproj 挂内嵌资源（ClassBlock.Core.csproj 在 QuestPDF ItemGroup 前插入）**

```xml
  <ItemGroup>
    <EmbeddedResource Include="Timetable\Templates\*.json" />
  </ItemGroup>
```

- [ ] **Step 7: 运行测试确认通过**

Run: `dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release --filter ClassBlockTemplateTests`
Expected: 4 个用例 PASS

- [ ] **Step 8: Commit** `feat(classblock-core): 内置七/八/九年级作息预设与年级匹配`

---

### Task 4: Builder 模板驱动化

**Files:**
- Modify: `src/ClassBlock.Core/Timetable/TimetableBuilder.cs`（整文件替换）
- Test: `tests/TeachCraft.Tests/ClassBlockTemplateTests.cs`

- [ ] **Step 1: 写失败测试（追加）**

```csharp
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
    var index = timetable.Rows.IndexOf(reading);

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
    Assert.Equal(new[] { "11:10—11:50" }, timetable.Rows[timetable.Rows.IndexOf(reading) + 1].Times);
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
```

> 文件顶部需要 `using System.Linq;`（ImplicitUsings 已含）与既有 `using ClassBlock.Core.Import;`。

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release --filter "BuiltinGrade|ShowToggles"`
Expected: FAIL（无带 template 参数的 Build 重载）

- [ ] **Step 3: 整文件替换 TimetableBuilder.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ClassBlock.Core.Models;

namespace ClassBlock.Core.Timetable;

/// <summary>
/// 按「作息模板」把总表里的一个班级组装成课程表：
/// 模板决定行序与时间，Slot 决定每行从总表绑定什么数据。
/// </summary>
public static class TimetableBuilder
{
    private static readonly string[] DayHeaders = { "星期一", "星期二", "星期三", "星期四", "星期五" };

    public static ClassTimetable Build(MasterSchedule master, string classKeyOrName, TimetableSettings? settings = null)
        => Build(master, classKeyOrName, TimetableTemplates.DefaultForGrade(master.Info.Grade), settings);

    public static ClassTimetable Build(
        MasterSchedule master,
        string classKeyOrName,
        TimetableTemplate template,
        TimetableSettings? settings = null)
    {
        settings ??= new TimetableSettings();

        var cls = master.FindClass(classKeyOrName)
                  ?? throw new ClassBlockException("总表里没有这个班级，请重新选择。");

        var headTeacher = master.HeadTeachers.TryGetValue(cls.Key, out var found) ? found : string.Empty;
        var rows = new List<TimetableRow>();

        foreach (var spec in template.Rows)
        {
            if (!IsEnabled(settings, spec.Slot)) continue;

            var row = BuildRow(master, cls, spec, settings);
            if (row is not null) rows.Add(row);
        }

        var title = string.IsNullOrWhiteSpace(settings.Title) ? $"{cls.Name}课程表" : settings.Title.Trim();

        return new ClassTimetable
        {
            Title = title,
            Subtitle = BuildSubtitle(master, headTeacher),
            ClassName = cls.Name,
            HeadTeacher = headTeacher,
            ScheduleSheet = master.Info.ScheduleSheet,
            DayHeaders = DayHeaders,
            Rows = rows,
            Footnotes = BuildFootnotes(master, settings),
        };
    }

    /// <summary>显示开关只过滤绑定行；静态行与结构行（大课间/午餐/晚餐/新闻联播）始终保留。</summary>
    private static bool IsEnabled(TimetableSettings settings, TemplateSlot slot) => slot switch
    {
        TemplateSlot.EarlyReading => settings.ShowEarlyReading,
        TemplateSlot.LunchStudy => settings.ShowLunchStudy,
        TemplateSlot.AfterSchool1 or TemplateSlot.AfterSchool2 => settings.ShowAfterSchool,
        TemplateSlot.Evening => settings.ShowEveningStudy,
        _ => true,
    };

    private static TimetableRow? BuildRow(MasterSchedule master, ClassSchedule cls, TemplateRowSpec spec, TimetableSettings settings)
    {
        return spec.Slot switch
        {
            TemplateSlot.Period1 => PeriodRow(cls, spec, 0, settings.ShowTeachers),
            TemplateSlot.Period2 => PeriodRow(cls, spec, 1, settings.ShowTeachers),
            TemplateSlot.Period3 => PeriodRow(cls, spec, 2, settings.ShowTeachers),
            TemplateSlot.Period4 => PeriodRow(cls, spec, 3, settings.ShowTeachers),
            TemplateSlot.Period5 => PeriodRow(cls, spec, 4, settings.ShowTeachers),
            TemplateSlot.Period6 => PeriodRow(cls, spec, 5, settings.ShowTeachers),
            TemplateSlot.Period7 => PeriodRow(cls, spec, 6, settings.ShowTeachers),
            TemplateSlot.EarlyReading => EarlyReadingRow(master, spec),
            TemplateSlot.LunchStudy => LunchStudyRow(master, cls, spec),
            TemplateSlot.AfterSchool1 => ServiceRow(master, cls, spec, first: true, settings.ShowTeachers),
            TemplateSlot.AfterSchool2 => ServiceRow(master, cls, spec, first: false, settings.ShowTeachers),
            TemplateSlot.Evening => EveningRow(cls.Days, spec),
            _ => StaticRow(spec),
        };
    }

    private static TimetableRow PeriodRow(ClassSchedule cls, TemplateRowSpec spec, int periodIndex, bool showTeachers) => new()
    {
        Times = spec.Times,
        Label = spec.Label,
        Kind = TimetableRowKind.Normal,
        Cells = Enumerable.Range(0, 5)
            .Select(day => CourseText(cls.Days, day, periodIndex, showTeachers))
            .ToArray(),
    };

    private static TimetableRow? EarlyReadingRow(MasterSchedule master, TemplateRowSpec spec)
    {
        var cells = Enumerable.Range(0, 5)
            .Select(i => i < master.EarlyReading.Count ? Normalize(master.EarlyReading[i]) : string.Empty)
            .ToArray();

        if (cells.All(string.IsNullOrWhiteSpace)) return null;

        return new TimetableRow { Times = spec.Times, Label = spec.Label, Kind = spec.Kind, Cells = cells };
    }

    private static TimetableRow? LunchStudyRow(MasterSchedule master, ClassSchedule cls, TemplateRowSpec spec)
    {
        if (!master.LunchStudy.TryGetValue(cls.Key, out var lunch) || lunch.Count == 0) return null;

        var cells = Enumerable.Range(0, 5)
            .Select(i => i < lunch.Count ? FormatTeacher(lunch[i]) : string.Empty)
            .ToArray();

        if (cells.All(string.IsNullOrWhiteSpace)) return null;

        return new TimetableRow { Times = spec.Times, Label = spec.Label, Kind = spec.Kind, Cells = cells };
    }

    private static TimetableRow? ServiceRow(MasterSchedule master, ClassSchedule cls, TemplateRowSpec spec, bool first, bool showTeachers)
    {
        var cells = Enumerable.Range(0, 5)
            .Select(day => ServiceText(master, cls, day, first, showTeachers))
            .ToArray();

        if (cells.All(t => t.Length == 0)) return null;

        return new TimetableRow
        {
            Times = spec.Times,
            Label = spec.Label,
            Kind = spec.Kind,
            Cells = cells,
            MergeDays = Mergeable(cells),
        };
    }

    private static TimetableRow EveningRow(IReadOnlyList<DaySchedule> days, TemplateRowSpec spec) => new()
    {
        Times = spec.Times,
        Label = spec.Label,
        Kind = TimetableRowKind.Evening,
        Cells = Enumerable.Range(0, 5)
            .Select(day => day < days.Count ? FormatTeacher(days[day].EveningStudy) : string.Empty)
            .ToArray(),
    };

    private static TimetableRow StaticRow(TemplateRowSpec spec)
    {
        if (spec.Kind is TimetableRowKind.Break or TimetableRowKind.News or TimetableRowKind.Lunch or TimetableRowKind.Dinner)
        {
            return new TimetableRow
            {
                Times = spec.Times,
                Label = spec.Label,
                Kind = spec.Kind,
                MergedText = spec.StaticText,
            };
        }

        return new TimetableRow
        {
            Times = spec.Times,
            Label = spec.Label,
            Kind = spec.Kind,
            Cells = Enumerable.Repeat(spec.StaticText, 5).ToArray(),
            MergeDays = spec.MergeDays,
        };
    }

    private static string BuildSubtitle(MasterSchedule master, string headTeacher)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(master.Info.Term)) parts.Add(master.Info.Term);
        if (!string.IsNullOrWhiteSpace(master.Info.Month)) parts.Add(master.Info.Month);
        if (!string.IsNullOrWhiteSpace(headTeacher)) parts.Add($"班主任 {headTeacher}");
        return string.Join(" · ", parts);
    }

    private static string CourseText(IReadOnlyList<DaySchedule> days, int day, int period, bool showTeachers)
    {
        if (day >= days.Count || period >= days[day].Periods.Count) return string.Empty;

        var cell = days[day].Periods[period];
        if (cell.IsEmpty) return string.Empty;

        if (!showTeachers || string.IsNullOrWhiteSpace(cell.Teacher)) return cell.Subject;
        return $"{cell.Subject}\n{cell.Teacher}";
    }

    private static string ServiceText(MasterSchedule master, ClassSchedule cls, int day, bool first, bool showTeachers)
    {
        if (day >= cls.Days.Count) return string.Empty;

        var cell = first ? cls.Days[day].AfterSchool1 : cls.Days[day].AfterSchool2;
        if (cell.IsEmpty) return string.Empty;

        var teacher = cell.Teacher;
        if (string.IsNullOrWhiteSpace(teacher) &&
            master.AfterSchool.TryGetValue(cls.Key, out var service) &&
            day < service.Count)
        {
            teacher = first ? service[day].Service1 : service[day].Service2;
        }

        if (!showTeachers || string.IsNullOrWhiteSpace(teacher)) return cell.Subject;
        return $"{cell.Subject}\n{teacher}";
    }

    /// <summary>午学、晚修的教师姓名补上「老师」，并保留「详见附件」这类说明。</summary>
    private static string FormatTeacher(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;

        var name = Regex.Replace(text, @"\s+", string.Empty);
        if (text.EndsWith("老师", StringComparison.Ordinal) ||
            name.Contains("详见") ||
            name.Contains("附件") ||
            name.Contains('（') ||
            name.Contains('('))
        {
            return text;
        }

        return IsChineseName(name) ? $"{name}老师" : text;
    }

    private static bool IsChineseName(string value) =>
        value.Length is >= 2 and <= 4 && value.All(c => c >= '\u4e00' && c <= '\u9fff');

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? string.Empty).Trim(), @"\s*/\s*", "/");

    private static bool Mergeable(IReadOnlyList<string> cells)
    {
        if (cells.Count == 0 || cells.Any(string.IsNullOrWhiteSpace)) return false;
        return cells.All(c => string.Equals(c, cells[0], StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> BuildFootnotes(MasterSchedule master, TimetableSettings settings)
    {
        var notes = new List<string>();
        if (settings.ShowEarlyReading && !string.IsNullOrWhiteSpace(master.Info.EarlyReadingRule))
            notes.Add($"早读：{Normalize(master.Info.EarlyReadingRule)}");

        return notes;
    }
}
```

- [ ] **Step 4: 运行模板测试确认通过**

Run: `dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release --filter ClassBlockTemplateTests`
Expected: 8 个用例 PASS

- [ ] **Step 5: 运行既有 ClassBlock 测试确认无回归**

Run: `dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release --filter "ClassBlockTests|ToolboxTests"`
Expected: 全部 PASS（旧 `Build(master, key, settings)` 重载走九年级预设，行为不变）

- [ ] **Step 6: Commit** `feat(classblock-core): 课程表生成改为作息模板驱动`

---

### Task 5: 自定义模板读写 + 偏好记忆测试

**Files:**
- Test: `tests/TeachCraft.Tests/ClassBlockTemplateTests.cs`（实现已在 Task 2 完成，此处补测试）

- [ ] **Step 1: 追加测试**

```csharp
[Fact]
public void CustomTemplates_SaveLoadDeleteRoundTrip()
{
    var dir = Path.Combine(_root, "templates");
    var template = SampleTemplate() with { };
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

    TimetableTemplateIO.SaveCustom(new TimetableTemplate { Id = "ok", Name = "好的" }, dir);

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
```

> `SampleTemplate() with { }` 用于复制（class 无 with 语义则改为 `new TimetableTemplate { Id = "custom-1", Name = "测试作息", GradeTag = "七", Rows = { ... } }`，直接构造即可——执行时按编译器实际行为取最简写法：本仓库 TimetableTemplate 是普通 class，`with` 不可用，直接 `SampleTemplate(); template.Id = "custom-1";` 两行赋值）。

- [ ] **Step 2: 运行确认通过**

Run: `dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release --filter ClassBlockTemplateTests`
Expected: 11 个用例 PASS

- [ ] **Step 3: Commit** `test(classblock): 自定义模板读写与年级偏好记忆回归`

---

### Task 6: ViewModel 集成（模板选择 + 偏好 + Build 传参）

**Files:**
- Modify: `src/ClassBlock.App/ClassBlockViewModel.cs`

- [ ] **Step 1: 添加模板集合与选中项（放在「课表显示设置」字段区之后）**

```csharp
    // ----------------------------- 作息模板 -----------------------------

    public ObservableCollection<TimetableTemplate> Templates { get; } = new();
    [ObservableProperty] private TimetableTemplate? _selectedTemplate;
```

- [ ] **Step 2: 构造函数加载模板列表（加在 `public bool HasMaster` 等计算属性之前，字段声明区之后）**

```csharp
    public ClassBlockViewModel()
    {
        LoadTemplates();
    }
```

- [ ] **Step 3: 添加模板管理方法（放在 `LoadMaster` 方法之前）**

```csharp
    public void LoadTemplates()
    {
        var previousId = SelectedTemplate?.Id;
        var (customs, warnings) = TimetableTemplateIO.LoadCustomTemplates();

        Templates.Clear();
        foreach (var template in TimetableTemplates.Builtins) Templates.Add(template);
        foreach (var template in customs) Templates.Add(template);

        SelectedTemplate = TimetableTemplates.Find(Templates, previousId) ?? PrefsOrDefault();

        if (warnings.Count > 0) SetStatus(warnings[0], true);
    }

    public void AdoptSavedTemplate(string templateId)
    {
        LoadTemplates();
        SelectedTemplate = TimetableTemplates.Find(Templates, templateId) ?? SelectedTemplate;
    }

    private TimetableTemplate PrefsOrDefault()
    {
        if (_master is null) return TimetableTemplates.DefaultForGrade(null);

        var grade = string.IsNullOrWhiteSpace(_master.Info.Grade) ? "未识别" : _master.Info.Grade;
        var prefs = ClassBlockPrefsStore.Load();
        if (prefs.LastTemplateByGrade.TryGetValue(grade, out var id))
        {
            var found = TimetableTemplates.Find(Templates, id);
            if (found is not null) return found;
        }

        return TimetableTemplates.DefaultForGrade(_master.Info.Grade);
    }

    partial void OnSelectedTemplateChanged(TimetableTemplate? value)
    {
        if (value is not null && _master is not null)
        {
            var grade = string.IsNullOrWhiteSpace(_master.Info.Grade) ? "未识别" : _master.Info.Grade;
            var prefs = ClassBlockPrefsStore.Load();
            prefs.LastTemplateByGrade[grade] = value.Id;
            ClassBlockPrefsStore.Save(prefs);
        }

        _ = RefreshPreviewAsync();
    }
```

- [ ] **Step 4: `LoadMaster` 在 `SearchText = string.Empty;` 之前插入模板套用**

```csharp
        SelectedTemplate = PrefsOrDefault();
```

- [ ] **Step 5: `BuildDocument` 与 `RefreshPreviewAsync` 传入模板**

`BuildDocument` 改为：

```csharp
    public ClassTimetable BuildDocument()
    {
        if (_master is null || SelectedClass is null)
            throw new ClassBlockException("请先导入总表并选择一个班级。");

        var template = SelectedTemplate ?? TimetableTemplates.DefaultForGrade(_master.Info.Grade);
        return TimetableBuilder.Build(_master, SelectedClass.Key, template, ToSettings());
    }
```

`RefreshPreviewAsync` 中 `var settings = ToSettings();` 之后加一行：

```csharp
        var template = SelectedTemplate ?? TimetableTemplates.DefaultForGrade(master.Info.Grade);
```

Task.Run 内改为：

```csharp
                var doc = TimetableBuilder.Build(master, item.Key, template, settings);
```

- [ ] **Step 6: 构建确认**

Run: `dotnet build src/ClassBlock.App/ClassBlock.App.csproj -c Release`
Expected: 0 警告 0 错误

- [ ] **Step 7: Commit** `feat(classblock-app): 模板选择、年级偏好记忆与生成联动`

---

### Task 7: 设置面板「作息模板」卡

**Files:**
- Modify: `src/ClassBlock.App/ClassBlockWindow.axaml`

- [ ] **Step 1: Window 根标签加命名空间（第 4 行 `xmlns:app` 之后）**

```xml
        xmlns:ct="using:ClassBlock.Core.Timetable"
```

- [ ] **Step 2: 设置列（`<!-- 设置 -->` Border 内 StackPanel 的第一个子元素前，即「课程表标题」settings-card 之前）插入**

```xml
                        <Border Classes="settings-card">
                            <StackPanel Spacing="8">
                                <TextBlock Classes="section-title" Text="作息模板" />
                                <ComboBox ItemsSource="{Binding Templates}"
                                          SelectedItem="{Binding SelectedTemplate, Mode=TwoWay}"
                                          HorizontalAlignment="Stretch"
                                          AutomationProperties.Name="作息模板">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="ct:TimetableTemplate">
                                            <TextBlock Text="{Binding Name}" FontSize="11.5" />
                                        </DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                                <TextBlock Text="模板决定课表的行序与时间，导入总表后按年级自动套用。"
                                           Classes="helper" />
                                <StackPanel Orientation="Horizontal" Spacing="6">
                                    <Button Classes="ghost" Content="编辑模板" Click="OnEditTemplate" FontSize="11"
                                            AutomationProperties.Name="编辑作息模板" />
                                    <Button Classes="ghost" Content="新建模板" Click="OnNewTemplate" FontSize="11"
                                            AutomationProperties.Name="新建作息模板" />
                                </StackPanel>
                            </StackPanel>
                        </Border>
```

- [ ] **Step 3: ClassBlockWindow.axaml.cs 增加两个处理器（放在 `OnImportMaster` 之前）**

```csharp
    // ----------------------------- 作息模板 -----------------------------

    private void OnEditTemplate(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTemplate is null) return;

        var editor = new TimetableTemplateEditorWindow(ViewModel.SelectedTemplate);
        editor.ShowDialog(this);

        if (editor.SavedTemplateId is not null)
            ViewModel.AdoptSavedTemplate(editor.SavedTemplateId);
        else
            ViewModel.LoadTemplates();
    }

    private void OnNewTemplate(object? sender, RoutedEventArgs e)
    {
        var source = ViewModel.SelectedTemplate ?? TimetableTemplates.DefaultForGrade(null);
        var editor = new TimetableTemplateEditorWindow(source, startAsCopy: true);
        editor.ShowDialog(this);

        if (editor.SavedTemplateId is not null)
            ViewModel.AdoptSavedTemplate(editor.SavedTemplateId);
    }
```

`using ClassBlock.Core.Timetable;` 加入文件头（第 11 行 `using ClassBlock.Core.Rendering;` 之后）。

- [ ] **Step 4: 构建**

Run: `dotnet build src/ClassBlock.App/ClassBlock.App.csproj -c Release`
Expected: 0 警告 0 错误（此时 `TimetableTemplateEditorWindow` 尚不存在，本步会报 CS0103——与 Task 8 合并构建亦可；若想独立过编译，先跳过本步构建，放到 Task 8 一起验）

- [ ] **Step 5: Commit（与 Task 8 合并提交亦可）** `feat(classblock-app): 设置面板作息模板选择与编辑器入口`

---

### Task 8: 模板编辑器窗口

**Files:**
- Create: `src/ClassBlock.App/TimetableTemplateEditorViewModel.cs`
- Create: `src/ClassBlock.App/TimetableTemplateEditorWindow.axaml`
- Create: `src/ClassBlock.App/TimetableTemplateEditorWindow.axaml.cs`

- [ ] **Step 1: 编辑器 VM**

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using ClassBlock.Core.Models;
using ClassBlock.Core.Timetable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassBlock.App;

public sealed class KindOption
{
    public KindOption(TimetableRowKind kind, string display)
    {
        Kind = kind;
        Display = display;
    }

    public TimetableRowKind Kind { get; }
    public string Display { get; }
}

public sealed class SlotOption
{
    public SlotOption(TemplateSlot slot, string display)
    {
        Slot = slot;
        Display = display;
    }

    public TemplateSlot Slot { get; }
    public string Display { get; }
}

public partial class TemplateRowEditViewModel : ObservableObject
{
    [ObservableProperty] private string _time1 = string.Empty;
    [ObservableProperty] private string _time2 = string.Empty;
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private KindOption _kind;
    [ObservableProperty] private SlotOption _slot;
    [ObservableProperty] private string _staticText = string.Empty;
    [ObservableProperty] private bool _mergeDays;

    public TemplateRowEditViewModel(KindOption kind, SlotOption slot)
    {
        _kind = kind;
        _slot = slot;
    }

    public static TemplateRowEditViewModel FromSpec(
        TemplateRowSpec spec,
        IReadOnlyList<KindOption> kinds,
        IReadOnlyList<SlotOption> slots)
    {
        return new TemplateRowEditViewModel(
            kinds.First(k => k.Kind == spec.Kind),
            slots.First(s => s.Slot == spec.Slot))
        {
            Time1 = spec.Times.Count > 0 ? spec.Times[0] : string.Empty,
            Time2 = spec.Times.Count > 1 ? spec.Times[1] : string.Empty,
            Label = spec.Label,
            StaticText = spec.StaticText,
            MergeDays = spec.MergeDays,
        };
    }

    public TemplateRowSpec ToSpec()
    {
        var times = new List<string>();
        if (Time1.Trim().Length > 0) times.Add(Time1.Trim());
        if (Time2.Trim().Length > 0) times.Add(Time2.Trim());

        return new TemplateRowSpec
        {
            Times = times,
            Label = Label.Trim(),
            Kind = Kind.Kind,
            Slot = Slot.Slot,
            StaticText = StaticText.Trim(),
            MergeDays = MergeDays,
        };
    }
}

public partial class TimetableTemplateEditorViewModel : ObservableObject
{
    public IReadOnlyList<KindOption> KindOptions { get; } = new[]
    {
        new KindOption(TimetableRowKind.Normal, "普通行"),
        new KindOption(TimetableRowKind.Break, "大课间（合并行）"),
        new KindOption(TimetableRowKind.Lunch, "午餐（整行红字）"),
        new KindOption(TimetableRowKind.Dinner, "晚餐（整行红字）"),
        new KindOption(TimetableRowKind.News, "新闻联播（合并行）"),
        new KindOption(TimetableRowKind.Evening, "晚修（双时间高行）"),
    };

    public IReadOnlyList<SlotOption> SlotOptions { get; } = new[]
    {
        new SlotOption(TemplateSlot.None, "静态内容"),
        new SlotOption(TemplateSlot.Period1, "第 1 节"),
        new SlotOption(TemplateSlot.Period2, "第 2 节"),
        new SlotOption(TemplateSlot.Period3, "第 3 节"),
        new SlotOption(TemplateSlot.Period4, "第 4 节"),
        new SlotOption(TemplateSlot.Period5, "第 5 节"),
        new SlotOption(TemplateSlot.Period6, "第 6 节"),
        new SlotOption(TemplateSlot.Period7, "第 7 节"),
        new SlotOption(TemplateSlot.EarlyReading, "早读"),
        new SlotOption(TemplateSlot.LunchStudy, "午学"),
        new SlotOption(TemplateSlot.AfterSchool1, "课服1"),
        new SlotOption(TemplateSlot.AfterSchool2, "课服2"),
        new SlotOption(TemplateSlot.Evening, "晚修教师"),
    };

    public ObservableCollection<TemplateRowEditViewModel> Rows { get; } = new();

    [ObservableProperty] private string _templateName = string.Empty;
    [ObservableProperty] private bool _isBuiltin;
    [ObservableProperty] private TemplateRowEditViewModel? _selectedRow;

    public string TemplateId { get; private set; } = string.Empty;
    public string GradeTag { get; }
    public bool IsPersisted { get; }
    public bool CanEdit => !IsBuiltin;
    public bool CanDelete => !IsBuiltin && IsPersisted;
    public bool CanSave => CanEdit && TemplateName.Trim().Length > 0 && Rows.Count > 0;

    public TimetableTemplateEditorViewModel(TimetableTemplate source, bool startAsCopy)
    {
        TemplateId = source.Id;
        GradeTag = source.GradeTag;
        TemplateName = source.Name;
        IsBuiltin = source.IsBuiltin;
        IsPersisted = !startAsCopy && !source.IsBuiltin;

        foreach (var spec in source.Rows)
            Rows.Add(TemplateRowEditViewModel.FromSpec(spec, KindOptions, SlotOptions));

        SelectedRow = Rows.Count > 0 ? Rows[0] : null;
    }

    [RelayCommand]
    private void CopyAsCustom()
    {
        TemplateId = Guid.NewGuid().ToString("N");
        TemplateName = TemplateName.Trim().Length == 0 ? "自定义作息" : $"{TemplateName.Trim()} · 自定义";
        IsBuiltin = false;
    }

    [RelayCommand]
    private void AddRow()
    {
        var row = new TemplateRowEditViewModel(KindOptions[0], SlotOptions[0]);
        var index = SelectedRow is null ? Rows.Count : Rows.IndexOf(SelectedRow) + 1;
        Rows.Insert(index, row);
        SelectedRow = row;
    }

    [RelayCommand]
    private void RemoveRow()
    {
        if (SelectedRow is null) return;

        var index = Rows.IndexOf(SelectedRow);
        Rows.RemoveAt(index);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedRow is null) return;

        var index = Rows.IndexOf(SelectedRow);
        if (index > 0) Rows.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedRow is null) return;

        var index = Rows.IndexOf(SelectedRow);
        if (index < Rows.Count - 1) Rows.Move(index, index + 1);
    }

    public TimetableTemplate BuildTemplate() => new()
    {
        Id = TemplateId,
        Name = TemplateName.Trim(),
        IsBuiltin = false,
        GradeTag = GradeTag,
        Rows = Rows.Select(r => r.ToSpec()).ToList(),
    };

    partial void OnTemplateNameChanged(string value) => OnPropertyChanged(nameof(CanSave));

    partial void OnIsBuiltinChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanSave));
    }
}
```

> `TemplateRowEditViewModel.FromSpec` 里 `List<string> Times` 用集合初始化器 `new() { ... }` 填充。

- [ ] **Step 2: 编辑器窗口 AXAML**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:app="using:ClassBlock.App"
        x:Class="ClassBlock.App.TimetableTemplateEditorWindow"
        x:DataType="app:TimetableTemplateEditorViewModel"
        Title="作息模板编辑器"
        Width="780" Height="760"
        MinWidth="680" MinHeight="560"
        WindowStartupLocation="CenterOwner"
        Background="{StaticResource PaperBrush}">
    <Design.DataContext>
        <app:TimetableTemplateEditorViewModel>
            <!-- 设计时数据由代码构造，留空即可 -->
        </app:TimetableTemplateEditorViewModel>
    </Design.DataContext>

    <Window.Styles>
        <Style Selector="Border.row-card">
            <Setter Property="Background" Value="{StaticResource CardBrush}" />
            <Setter Property="BorderBrush" Value="{StaticResource LineBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="8" />
            <Setter Property="Padding" Value="10" />
        </Style>
        <Style Selector="TextBox.field">
            <Setter Property="Background" Value="#FCFBF7" />
            <Setter Property="BorderBrush" Value="{StaticResource LineBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="5" />
            <Setter Property="Padding" Value="8,6" />
            <Setter Property="FontSize" Value="11.5" />
            <Setter Property="MinHeight" Value="30" />
        </Style>
        <Style Selector="TextBlock.helper">
            <Setter Property="FontSize" Value="9.5" />
            <Setter Property="LineHeight" Value="16" />
            <Setter Property="Foreground" Value="#9C9585" />
            <Setter Property="TextWrapping" Value="Wrap" />
        </Style>
        <Style Selector="TextBlock.hint">
            <Setter Property="FontSize" Value="10" />
            <Setter Property="Foreground" Value="{StaticResource AccentDeepBrush}" />
            <Setter Property="TextWrapping" Value="Wrap" />
        </Style>
    </Window.Styles>

    <Grid RowDefinitions="Auto,Auto,*,Auto" Margin="14" RowSpacing="10">
        <!-- 名称与只读提示 -->
        <StackPanel Grid.Row="0" Spacing="7">
            <Grid ColumnDefinitions="56,*" ColumnSpacing="8" VerticalAlignment="Center">
                <TextBlock Text="模板名" FontSize="11.5" VerticalAlignment="Center" />
                <TextBox Grid.Column="1" Classes="field" Text="{Binding TemplateName, Mode=TwoWay}"
                         IsEnabled="{Binding CanEdit}" Watermark="例如：八年级作息（自定义）" />
            </Grid>
            <TextBlock Classes="hint" IsVisible="{Binding IsBuiltin}"
                       Text="内置预设不可修改。点「复制为自定义」后即可编辑并另存。" />
        </StackPanel>

        <!-- 行编辑区 -->
        <ScrollViewer Grid.Row="2">
            <ItemsControl ItemsSource="{Binding Rows}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="app:TemplateRowEditViewModel">
                        <Border Classes="row-card" Margin="0,0,0,8"
                                IsEnabled="{Binding $parent[Window].DataContext.CanEdit}">
                            <StackPanel Spacing="6">
                                <Grid ColumnDefinitions="120,110,90,1*" ColumnSpacing="8">
                                    <TextBox Classes="field" Text="{Binding Time1, Mode=TwoWay}"
                                             Watermark="时间，如 10:50—11:00" />
                                    <TextBox Grid.Column="1" Text="{Binding Time2, Mode=TwoWay}"
                                             Watermark="时间段2（晚修）" />
                                    <TextBox Grid.Column="2" Text="{Binding Label, Mode=TwoWay}"
                                             Watermark="标签" />
                                    <ComboBox Grid.Column="3"
                                              ItemsSource="{Binding $parent[Window].DataContext.SlotOptions}"
                                              SelectedItem="{Binding Slot, Mode=TwoWay}"
                                              HorizontalAlignment="Stretch" />
                                </Grid>
                                <Grid ColumnDefinitions="170,1*,Auto,Auto" ColumnSpacing="8">
                                    <ComboBox
                                        ItemsSource="{Binding $parent[Window].DataContext.KindOptions}"
                                        SelectedItem="{Binding Kind, Mode=TwoWay}"
                                        HorizontalAlignment="Stretch" />
                                    <TextBox Grid.Column="1" Text="{Binding StaticText, Mode=TwoWay}"
                                             Watermark="静态内容 / 整行文字" />
                                    <CheckBox Grid.Column="2" IsChecked="{Binding MergeDays, Mode=TwoWay}"
                                              Content="五天合并" VerticalAlignment="Center" FontSize="11" />
                                </Grid>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>

        <!-- 操作栏 -->
        <StackPanel Grid.Row="3" Spacing="8">
            <TextBlock x:Name="EditorStatus" Classes="hint" Text="" />
            <StackPanel Orientation="Horizontal" Spacing="8">
                <Button Classes="ghost" Content="添加一行" Command="{Binding AddRowCommand}" />
                <Button Classes="ghost" Content="上移" Command="{Binding MoveUpCommand}" />
                <Button Classes="ghost" Content="下移" Command="{Binding MoveDownCommand}" />
                <Button Classes="ghost" Content="删除行" Command="{Binding RemoveRowCommand}" />
                <Border Width="1" Height="22" Background="{StaticResource LineBrush}" Margin="4,0" />
                <Button Classes="primary" Content="复制为自定义" Command="{Binding CopyAsCustomCommand}"
                        IsVisible="{Binding IsBuiltin}" />
                <Button Classes="primary" Content="保存模板" Click="OnSave"
                        IsVisible="{Binding !IsBuiltin}" IsEnabled="{Binding CanSave}" />
                <Button Classes="ghost" Content="删除模板" x:Name="DeleteButton" Click="OnDelete"
                        IsVisible="{Binding CanDelete}" />
                <Button Classes="ghost" Content="关闭" Click="OnClose" />
            </StackPanel>
            <TextBlock Classes="helper"
                       Text="「绑定」为空的行使用静态内容；绑定第 N 节 / 早读 / 午学 / 课服 / 晚修的行会自动填入总表数据。显示开关（早读、午学等）只对绑定行生效。" />
        </StackPanel>
    </Grid>
</Window>
```

> `Design.DataContext` 若导致设计器报错（VM 无无参构造），直接删除该节点即可，不影响运行。

- [ ] **Step 3: 编辑器 code-behind**

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ClassBlock.Core;
using ClassBlock.Core.Timetable;

namespace ClassBlock.App;

public partial class TimetableTemplateEditorWindow : Window
{
    /// <summary>保存成功时带回模板编号；关闭/删除时为 null。</summary>
    public string? SavedTemplateId { get; private set; }

    private bool _confirmDelete;

    public TimetableTemplateEditorWindow(TimetableTemplate source, bool startAsCopy = false)
    {
        InitializeComponent();
        DataContext = new TimetableTemplateEditorViewModel(source, startAsCopy);
    }

    private TimetableTemplateEditorViewModel ViewModel => (TimetableTemplateEditorViewModel)DataContext!;

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanSave)
        {
            SetEditorStatus("请先填写模板名称并至少保留一行。");
            return;
        }

        try
        {
            var template = ViewModel.BuildTemplate();
            TimetableTemplateIO.SaveCustom(template);
            SavedTemplateId = template.Id;
            Close();
        }
        catch (ClassBlockException ex)
        {
            SetEditorStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetEditorStatus($"保存模板失败：{ex.Message}");
        }
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            DeleteButton.Content = "确认删除";
            return;
        }

        try
        {
            TimetableTemplateIO.DeleteCustom(ViewModel.TemplateId);
            Close();
        }
        catch (Exception ex)
        {
            SetEditorStatus($"删除模板失败：{ex.Message}");
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void SetEditorStatus(string message)
    {
        EditorStatus.Text = message;
        EditorStatus.Foreground = new SolidColorBrush(Color.Parse("#C6613F"));
    }
}
```

- [ ] **Step 4: 构建 + 全量测试**

Run: `dotnet build TeachCraft.slnx -c Release && dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release && dotnet test tests/GradeStrip.Tests/GradeStrip.Tests.csproj -c Release`
Expected: 0 警告 0 错误；TeachCraft.Tests 30 项（19+11）、GradeStrip.Tests 102 项全 PASS

- [ ] **Step 5: Commit** `feat(classblock-app): 作息模板编辑器窗口（复制内置、增删行、另存/删除自定义）`

---

### Task 9: 视觉走查与真实文件验证

- [ ] **Step 1: 用真实总表（本机已有、不入库）走一遍三套预设**

```powershell
dotnet publish src/TeachCraft.App/TeachCraft.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/TeachCraft-win-x64-sc

dist/TeachCraft-win-x64-sc/TeachCraft.exe --classblock-shot dist\shots\grade9.png <真实总表.xlsx> 九6
```

人工核对：
- 默认（九年级）输出与改造前一致：行序、时间、朗读行不存在。
- 在设置面板切换到「七年级作息（内置）」：第 3 节后出现「朗读」合并格，第 4 节 11:00—11:40（占位）。
- 「编辑模板」→「复制为自定义」→ 改第 4 节时间 → 保存 → 预览即时刷新；重开窗口后该年级自动套用上次模板。
- 删除自定义模板后回退内置预设。

- [ ] **Step 2: PNG/PDF 导出回归（UI 内点打印与导出图片各一次）**

Expected: 均正常，朗读行出现在输出中。

---

### Task 10: 文档同步

**Files:**
- Modify: `AGENTS.md`（第七节）
- Modify: `.memory/10-时序课程表.md`

- [ ] **Step 1: AGENTS.md 第七节替换行序契约条目**

旧：

```
- 课程表行序是稳定契约：早读 → 1 → 2 → 大课间 → 3 → 4 → 午餐 → 午学 → 5 → 6 → 7 → 课服1 → 课服2 → 晚餐 → 新闻联播 → 晚修。午餐、晚餐为整行红字；大课间、新闻联播合并星期列；晚修为两段时间的高行。
```

新：

```
- 课程表行序与时间由作息模板驱动：模板行通过 Slot（第 N 节 / 早读 / 午学 / 课服1/2 / 晚修 / 静态）绑定总表数据。内置七 / 八 / 九三套预设；九年级预设 = 原固定行序快照（早读 → 1 → 2 → 大课间 → 3 → 4 → 午餐 → 午学 → 5 → 6 → 7 → 课服1 → 课服2 → 晚餐 → 新闻联播 → 晚修），七 / 八年级在第三节后插入朗读行。午餐、晚餐为整行红字；大课间、新闻联播合并星期列；晚修为两段时间的高行。
- 作息模板可在界面编辑器中复制内置、增删行、改时间后另存为自定义模板，存于 %APPDATA%\TeachCraft\ClassBlock\templates；导入总表后按识别年级自动套用该年级上次使用的模板，无记录时用内置预设。新增模板行类型或 Slot 槽位才需要改 Core 并升 Minor，学校改作息只需改模板。
```

- [ ] **Step 2: .memory/10-时序课程表.md 三处更新**

数据管线图（第 15 行附近）：

```
TimetableBuilder.Build(master, "九6", template, settings)
        │  (template = 内置七/八/九预设或自定义模板；Slot 决定每行绑定的数据)
```

「课程表行序（稳定契约）」章节标题与开头改为：

```
## 课程表行序（由作息模板驱动）

下表是九年级内置预设（= 历史固定行序快照）；七 / 八年级预设在其第 3 节后插入「朗读」静态合并行（10:50—11:00 / 10:50—11:10），第 4 节时间顺延（占位值，可在编辑器修改）。自定义模板在窗口编辑器中管理，存于 %APPDATA%\TeachCraft\ClassBlock\templates，`settings.json` 记录每个年级上次使用的模板。
```

「边界」章节倒数第二条：

```
- 作息由模板驱动：学校改作息只需在界面复制内置模板修改，不需要改代码；新增模板行类型或 Slot 槽位才需要改 Core 并升 Minor。
```

- [ ] **Step 3: Commit** `docs: 同步时序作息模板的约定与项目记忆`

---

## Self-Review 记录

- **Spec 覆盖**：设计文档 §3 模型 → Task 2；§4 Builder → Task 4；§5 内置预设 → Task 3；§6 存储与记忆 → Task 2/5/6；§7 UI → Task 6/7/8；§8 错误处理 → Task 2（FromJson 包装）/ Task 5（损坏跳过）/ Task 8（保存失败提示）；§9 测试 → Task 1–5 全覆盖（7 类用例）；§10 版本与文档 → 已核实 1.3.0 未占用 + Task 10。无缺口。
- **占位扫描**：无 TBD/TODO；Task 3 的七/八年级第 4 节与午餐占位时间为设计文档明确记录的决策，非计划缺口。
- **类型一致性**：`TimetableTemplate`/`TemplateRowSpec`/`TemplateSlot`/`TimetableTemplateIO`/`ClassBlockPrefs(Store)`/`TimetableTemplates.DefaultForGrade`/`Find` 在 Task 2 定义，Task 4/6/8 引用一致；`Build(master, key, template, settings)` 重载签名在 Task 4 与 Task 6 一致。
