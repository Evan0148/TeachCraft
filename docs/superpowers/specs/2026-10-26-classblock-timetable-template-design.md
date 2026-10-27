# 时序 · ClassBlock 作息模板设计（多年级支持）

日期：2026-10-26
状态：待用户评审
关联：`.memory/10-时序课程表.md`、AGENTS.md 第七节

## 1. 背景与目标

时序目前把作息骨架硬编码在 `TimetableBuilder`（`PeriodSlots` 常量与各 `AddXxx` 方法），等于只有一套「九年级作息」。实际各年级作息不同：

- 七年级：朗读课 10:50—11:00
- 八年级：朗读课 10:50—11:10
- 九年级：无朗读课，直接上第四节

已确认的事实：

1. 年级差异范围 = 朗读课的有无/时间 + 个别行的时间微调，整体骨架相同。
2. 朗读课显示固定内容（五天一致，如「朗读」），不来自总表。
3. 模板形态 = 内置七/八/九三套预设 + **在 UI 中可视化编辑、另存、删除自定义模板**（不提供 JSON 手改入口）。

目标：把「作息 = 配置」建模为模板数据，Builder 数据驱动；所有模板操作在界面内完成。

## 2. 非目标

- 不做多工作簿多年级混合导入（仍是一个工作簿一个年级）。
- 不做模板导入/导出文件（自定义模板只在 AppData 内管理）。
- 不改渲染层 `TimetableRenderer` 与 `TimetableRow` 模型（现有字段已够用）。

## 3. 数据模型（ClassBlock.Core/Timetable）

```csharp
public enum TemplateSlot
{
    None,             // 纯静态行：朗读、大课间、午餐、晚餐、新闻联播
    Period1..Period7, // 绑定课程网格第 N 节（enum 逐个列出）
    EarlyReading, LunchStudy, AfterSchool1, AfterSchool2, Evening,
}

public sealed class TemplateRowSpec
{
    public IReadOnlyList<string> Times { get; init; } // 晚修两条时间
    public string Label { get; init; }                // "朗读" / "1" / "课服1" …
    public TimetableRowKind Kind { get; init; }       // 复用现有枚举
    public TemplateSlot Slot { get; init; }
    public string StaticText { get; init; }           // Slot=None 时的内容
    public bool MergeDays { get; init; }              // 静态行五天合并为一格
}

public sealed class TimetableTemplate
{
    public string Id { get; init; }       // "builtin-grade-7" / GUID
    public string Name { get; init; }     // 「七年级作息」/「自定义 · 2026春」
    public bool IsBuiltin { get; init; }
    public string GradeTag { get; init; } // 七/八/九，空 = 不自动匹配
    public IReadOnlyList<TemplateRowSpec> Rows { get; init; }
}
```

`TemplateRowSpec` → `TimetableRow` 是纯映射，渲染所需的 `IsFullWidth` / `MergesLabelAndDays` 仍由 `Kind` 推导，与现状一致。

## 4. Builder 改造

- 新签名：`Build(MasterSchedule, classKeyOrName, TimetableTemplate template, TimetableSettings? settings)`。
- 保留旧签名重载：不传模板时按 `master.Info.Grade` 自动选内置预设（无匹配用九年级），行为向后兼容，现有测试不破。
- 遍历 `template.Rows`，按 `Slot` 绑定数据：
  - `PeriodN` → `CourseText(days, day, N-1, showTeachers)`（复用现有逻辑）。
  - `EarlyReading` / `LunchStudy` / `AfterSchool1` / `AfterSchool2` / `Evening` → 现有辅助表读取与 `FormatTeacher` 规则不变。
  - `None` → `StaticText`（`MergeDays=true` 时五天合并为一格）。
- 四个显示开关变为按 Slot 过滤：`ShowEarlyReading→EarlyReading`、`ShowLunchStudy→LunchStudy`、`ShowAfterSchool→AfterSchool1/2`、`ShowEveningStudy→Evening`；`None` 行与 `Break/Lunch/Dinner/News` 结构行始终显示。
- 标题、副标题、脚注（早读规则）逻辑不变。

## 5. 内置预设

三套 JSON 内嵌资源（`builtin-grade-7/8/9.json`），与自定义模板共用同一套序列化代码。

- **九年级预设 = 现在硬编码输出的逐行快照**，作为回归锚点。
- 七/八年级预设：在九年级时间轴上于第 3 节后插入朗读行（10:50—11:00 / 10:50—11:10），第 4 节及午餐时间用**占位值**（第 4 节顺延 11:00—11:40 / 11:10—11:50，午餐暂保持 11:30—12:00）。占位时间以 UI 编辑器修正为准，朗读行时间是已确认事实。
- 内置预设不可编辑、不可删除；提供「复制为自定义」。

## 6. 存储与自动匹配

- 自定义模板目录：`%APPDATA%\TeachCraft\ClassBlock\templates\*.json`（`Environment.SpecialFolder.ApplicationData`），遵循本地优先、无网络。
- `Id` 用 GUID，`Name` 可重复；文件损坏时跳过该文件并在警告区提示「自定义模板 xxx 无法读取，已忽略」，不阻断导入。
- 自动匹配：导入总表后选 `GradeTag == 识别年级` 的内置预设；用户可手动改选任意模板（含自定义）。
- 记忆选择：同目录 `settings.json` 记录每个年级最近一次使用的模板 Id；下次导入同年级自动套用上次的（含自定义模板）。

## 7. UI 设计（全部操作在 UI 内完成）

### 7.1 设置面板（ClassBlockWindow 内）

「作息模板」区，置于现有显示开关上方：

- ComboBox：内置 3 条 + 自定义若干（分组显示「内置 / 自定义」）。
- 「编辑模板」按钮：打开模板编辑器窗口。
- 「新建空白模板」按钮：从零开始建（默认行集 = 当前选中模板的副本）。

### 7.2 模板编辑器窗口（新 `TimetableTemplateEditorWindow`）

纸墨设计语言（1px 细边白卡、Anthropic 色板、无阴影、0.12–0.32s 克制动效）：

- 行列表（每行一张卡）：时间输入（晚修两框）、标签、类型下拉、数据绑定下拉（Slot）、静态文字（Slot=None 时显示）、五天合并勾选、上移/下移/删除。
- 内置模板打开时整体只读，头部提示「内置预设不可修改，修改将另存为自定义模板」；点「复制为自定义」解锁编辑。
- 底部操作：另存为（输入名称，写 AppData）、删除（仅自定义）、关闭。
- 任何改动即时反映：编辑器保存后 `ClassBlockViewModel` 用新模板重跑 `Build` 刷新预览。
- 删除当前正在使用的模板：先弹确认，删除后回退到年级匹配的内置预设。

### 7.3 预览联动

选中模板变化 / 编辑器保存 → 重跑 `TimetableBuilder.Build`，与现有显示开关联动一致。

## 8. 错误处理

- 模板 JSON 损坏 → 跳过 + 警告区中文提示，不阻断。
- 模板行缺时间/绑定缺失数据 → 沿用现有留空与警告机制（如无午学表）。
- 自定义模板被手动删文件 → 启动扫描时忽略缺失，选中的模板 Id 失效则回退内置预设。

## 9. 测试计划（TeachCraft.Tests，新增用例）

1. 九年级内置模板输出与旧硬编码结构逐行一致（行数、时间、标签、合并标记）。
2. 七年级模板含朗读行（StaticText=朗读，位于第 3 节与第 4 节之间），第 4 节时间顺延。
3. `TimetableTemplate` JSON 序列化往返。
4. 自定义模板保存/加载/删除（临时目录，不污染真实 AppData）。
5. Slot 过滤：`ShowEarlyReading=false` 时早读行不出现；`None` 行不受开关影响。
6. 自动匹配：GradeTag=七 选中七年级预设；未识别年级回退九年级。
7. 损坏 JSON 跳过并产生警告。

发布前用 `--classblock-shot` 视觉走查三套预设的输出。

## 10. 版本与文档

- Minor 晋级：`Directory.Build.props` → `1.3.0`。
- AGENTS.md 第七节：行序从「稳定契约」改为「由作息模板驱动，内置七/八/九预设；模板行经 Slot 绑定总表数据」。
- 同步 `.memory/10-时序课程表.md`。
