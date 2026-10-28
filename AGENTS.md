# AGENTS.md

> 本文件供所有参与维护「知微 · TeachCraft」的 AI 智能体与开发者共同遵守，定义版本号规范、CI/CD 契约、仓库结构与编码准则。

---

## 一、项目定位

「知微」是面向教师的本地工具箱：10 件图片 / PDF 小工具，加上「分晓 · GradeStrip」成绩条生成器、「归位 · SeatGrid」座位表生成器、「时序 · ClassBlock」课程表生成器与「织序 · GradeWeave」全年级课表编排器，统一使用分晓的纸墨设计语言与本地优先原则。技术栈为 **C# / .NET 10 + Avalonia 12**，所有文件处理在本机完成，不上传、不留存。

---

## 二、版本号管理规范（与分晓同构）

采用 **语义化版本 (SemVer 2.0.0) + 构建流水线递增号**：

$$\text{v}\{Major\}.\{Minor\}.\{Patch\}[-\text{preview}.\{RunNumber\}]$$

### 1. 唯一版本源

基准版本只写在仓库根目录的 `Directory.Build.props`：

```xml
<Version>1.0.0</Version>
```

**任何发布脚本与工作流都不得重复硬编码版本号。** 升级版本时只改这一处。

### 2. 触发规则

| 触发 | 版本号 | 发布形态 |
| :--- | :--- | :--- |
| 推送 git tag（如 `v1.1.0`） | `v1.1.0` | 正式 Release，`prerelease: false` |
| push `main` | `v{Version}-preview.{github.run_number}` | 公开 Pre-release |
| `workflow_dispatch` | 同上 | 公开 Pre-release |

- 预览版标签由 `softprops/action-gh-release` 自动创建。
- 预览发布完成后，更早的 `*-preview.*` 版本会被自动转为 Draft，保证 Releases 页面只保留最新预览；正式版不会被降级。
- 版本脚本：`scripts/version.ps1`，读取 `GITHUB_REF_TYPE` / `GITHUB_REF_NAME` / `GITHUB_RUN_NUMBER`。

### 3. 晋级规则

| 版本段 | 触发场景 |
| :--- | :--- |
| **Major** | 数据格式、工具 ID、发布结构发生不兼容变更 |
| **Minor** | 新增工具、新增输出格式、新增平台支持、界面大改版 |
| **Patch** | Bug 修复、文案/间距/性能微调 |

当前基线：`1.15.0`（2026-10-28 织序导出改为统一设置弹窗：可选 Excel / PNG / PDF、Excel 双样式（软件自带 / 参考校）、按分页勾选总表 / 分工表 / 教师职位表 / 班级课表 / 教师课表 / 冲突页；新增教师职位安排表与横向「参考校样式」课程总表，按 Minor 晋级）。`1.14.1`（2026-10-27 竞名 UI 地毯式审查修复，Patch：报名表在 1120 最小窗口下不再横向溢出（列宽改 `30,106,78,1*,1.1*,28` 比例列、去掉 `MinWidth=860`），表头移出滚动区常驻；导入替换报名表、替换赛程、导出覆盖同名文件、删除有内容的行、关窗共 5 处补 `ConfirmDialog` 后果说明；三处 `catch` 改走 `ErrorText`；报名体检面板常驻并区分「结构完整/N 项需处理」且不再丢弃提醒类问题；性别切换自动摘除新性别不存在的项目并在状态栏说明，导入/校验同步补「女子没有 1000 米」规则；`Button.ghost` 补 `:pressed/:disabled`、`section-tab/day-card/filter-chip` 补按下态，tab 选中加底部 Accent 指示条；正文字号下限抬到 10、装饰英文小标 9，`#FCFAF4/#F7F4EB/#FBF3EC/#FBE4D7/#E7E3D8` 收敛为 `WashBrush/PinnedBrush/WashHoverBrush/AccentWashBrush/LineBrush` 令牌（新增于 `App.axaml`）；赛程「趣味」并入「团体」筛选与配色、未高亮条目透明度 0.38→0.7、`6 人`改`6 人次`、右侧概览补空态、日期列表可滚动；状态栏加状态圆点与 tooltip、成功/中性/错误三色；导出按钮按是否有原始报名表启用；性别下拉与项目标签在行内垂直居中（补回 `ComboBox.cell` 基样式、标签容器 `VerticalAlignment=Center`），新增窄窗形态（窗口 <1240 收起右侧概览，1120 下三枚团体标签单行、行高回到 41px），新增顶栏「保存」（写回原始报名表，先备份 `.bak`，Ctrl+S）与「另存为…」、`IsDirty` 未保存标记；报名体检改为实时校验单源，导入时的历史提示独立成「导入时的 N 条提示 + 知道了」并在首次编辑时自动收起（修复「解决冲突后提示不消失」）；新增 `--meetroster-scale` / `--meetroster-metrics` / `--meetroster-remove` 走查参数，测试 40 → 41 项）。`1.14.0`（2026-10-27 竞名报名表项目格改为「只显示已报项目」：团体项目与比赛项目都按已报数量渲染标签——没报只留一个「选择项目」占位框、报一个只显示一个、最多三个；团体三格（T1/T2/T3）合并为一列多选标签，点任一标签打开多选弹窗（上限 3 项、实时显示「本班已有 N 人报此项目」），写回按顺序填补空位；`20*50米男女混合接力` 与 `一分钟跳绳` 恢复为可选团体项目（导入不再忽略、回写不再丢弃、校验不再提示「现场出人」，改为不在模板列表才提示），并补团体项目重复填写校验；标签文字统一垂直居中（`Button.event-cell` 加 `VerticalContentAlignment` 与 `TextBlock` 居中）；合成示例数据补 1/2/3 项差异，新增 `--meetroster-dialog-slot` 走查参数，按 Minor 晋级）。`1.13.0`（2026-10-27 归位自动排座新增同桌性别弹窗：点「自动排座」先在弹窗里选「异性同桌 · 男女交替」或「同性同桌 · 男男/女女成对」，Core 新增 `ArrangeMode.GenderPair` 与下拉「同性同桌」项（男男/女女两两成对、未知排末尾、一侧用尽另一侧连排），选名单顺序/随机/按身高/按视力时弹窗补「按当前方式」直达；按 Minor 晋级）。`1.12.1`（2026-10-27 UI 手感第二轮，补记版本：改动落在 1.12.0 之后的提交里、最终随 1.13.0 发布，Patch：归位拖拽按 Apple 指数衰减投影（`project(v, 0.998)`，`src/Shared/DragPhysics.cs`）预测落点、幽灵 120–200ms 临界阻尼飞向目标格中心、出界 0.55 橡皮筋并保留「拖出即取消」；织序拖拽浮层移出裁剪层到窗口根并用同套物理，修复 `OnGridPointerMoved` 只放行 Candidate 导致幽灵不跟手的既有缺陷；归位滑块经 60 帧压测（130 人名单 / 96 座 / 96 人入座，尺寸刷新 p95 3.1–3.4ms、BuildGrid p95 3.1ms，均低于 8ms 阈值）改回拖动中合并重建（`Dispatcher.Post(Background)` + `_rebuildQueued`），新增 `--slider-bench` 取证通道；新增 `src/Shared/MotionPreference.cs`（Windows SPI_GETCLIENTAREAANIMATION / macOS NSWorkspace，Linux 暂 false），reduced 时抽屉纯 120ms 淡入、忙碌旋转改不确定进度条、入场缩放取消。`1.12.0`（2026-10-27 新增「竞名 · MeetRoster」：第五件常备应用，落地报名界面、模板字段（参赛单位、编码、领队与体育老师、姓名 / 性别 / 团体 1–3 / 比赛 1–2）、多班报名表导入与按班回写、往届“竞赛日程 + 项目分组表”导入与班级高亮；赛程自动生成规则留待后续）。`1.11.2`（2026-10-27 UI 一致性第一批修复：新增 `src/Shared/` 共享 UI 工具——`WindowFit` 按 `Screen.Scaling` 把窗口钳进工作区、`ErrorText` 异常文案网关、`ConfirmDialog` 统一确认弹窗；三个课堂应用最小尺寸降到 1120×680 并全窗口接入钳制，NewProject 放开缩放；织序研究/走班/编排三个课表网格补双向滚动；归位删学生/清座位/重导入名单与配置/关窗、分晓替换考试、织序切换工程补后果说明式确认；织序分工保存改为校验前置（不再留下半成品工程）；Muted 统一 `#6F6A5E`、错误色改 `#A34A36` 并清理 `#9C9585/#A29C8C/#8E8776/#B0AA9C` 散值；四处导出补 `ShowOverwritePrompt`、工具箱运行按钮补重入守卫、两处拖拽尊重抓取偏移与真实尺寸，按 Patch 晋级）。`1.11.1`（2026-10-27 内存与预览管线微调：归位预览改按 1600 宽解码并在关窗删除临时图（最大布局从 84MB 峰值降到约 8MB）、临时目录兜底清理一天前的残留、时序预览从 2.0 倍降到 1.25 倍（常驻 21MB → 约 8MB）、新增 `runtimeconfig.template.json` 写入 `System.GC.ConserveMemory=9`；实测空载工作集 270MB → 247MB，按 Patch 晋级）。`1.11.0`（2026-10-27 织序「教师分工」改为先建教师名单再分工：名单视角维护姓名 + 学科并支持批量粘贴；班级 / 教师 / 学科视角从名单下拉选人，按学科过滤；分工体检汇总待补与教师负载。五种视角共享同一份草稿，按 Minor 晋级）。`1.10.2`（2026-10-27 测试资产隐私硬化：两份智学网 `.xls` fixture 在字节层擦除全部残留真实姓名 / 考号 / 校名，覆盖 SST 奇对齐孤儿条目与「姓名_成绩单」属主标题两种此前不可见形态；dry 复扫零命中、NPOI 金丝雀与全量测试结果不变，按 Patch 晋级）。`1.10.1`（2026-10-27 全链路性能修复：织序并发生成门禁与自动保存版本守卫、PDF 转图片批量加载与流式编码、织序课表索引与格子复用、时序预览防抖/取消/后台解码、分晓导入单次物化与 O(N) 排名、归位滑块松手提交与渲染缓存、R2R 实测自包含 +115MB / 框架依赖 +62MB 体积代价过高未启用，按 Patch 晋级）。`1.10.0` 为织序新增「冲突聚合台」与锁课：冲突按类型聚合、可按班级 / 教师 / 关键词筛选并一键定位；课程格可锁定，重新生成时保留，拖拽移动自动锁定，按 Minor 晋级。`1.9.1` 为织序走班组教师下拉只显示当前学科任课教师，并按分工带入任课班教室。`1.9.0` 为织序新增自动保存：每 2 秒防抖保存、关闭前落盘、当前工程原子写回、无路径时写本机恢复文件并可恢复。`1.8.0` 为织序「作息时间表」步骤与确认门槛；`1.7.0` 为织序顶栏「教师分工」入口、新走班组默认自动安排与手动放课；`1.6.0` 为织序「全新制作」、走班自动安排与 `.xls` 导入；`1.5.0` 为织序「临时活动」与 PDF 依赖修复；`1.4.0` 为「织序 · GradeWeave」首版；`1.3.0` 为时序作息模板与演示数据合成化，`1.2.1` 修复归位空名单时的蒙层残影（Patch），`1.2.0` 新增「时序 · ClassBlock」，`1.1.0` 是归位上线版本，`1.0.0` 是首个完整工具箱版本。

---

## 三、CI/CD 契约

工作流：`.github/workflows/release-draft.yml`

1. **测试**：`windows-latest` 上依次运行 `dotnet test tests/GradeStrip.Tests/GradeStrip.Tests.csproj -c Release`（102 项，分晓导入 / 报表 / 导出回归）、`dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release`（78 项，工具箱、时序模板、竞名报名/赛程/名单导入导出端到端）与 `dotnet test tests/GradeWeave.Tests/GradeWeave.Tests.csproj -c Release`（115 项，织序自动保存 / 作息时间表 / 导入 / 编排 / 活动 / 走班 / 手动放课 / 锁课 / 冲突聚合台 / 教师名单与分工五视角 / 导出设置与双样式 / xls 导入 / 导出 / 工程往返 / 岗位匹配回归），失败则阻止发布。
2. **构建矩阵**：7 个 RID × 2 种部署模式 = 14 个包。

| RID | 运行器 | 产物 |
| :--- | :--- | :--- |
| `win-x86` / `win-x64` / `win-arm64` | windows-latest | `.zip` + `*-Setup.exe`（Inno Setup） |
| `osx-x64` / `osx-arm64` | macos-latest | `.app` + `.dmg` + `.zip`（ad-hoc 签名） |
| `linux-x64` / `linux-arm64` | windows-latest（交叉发布） | `.zip` |

- **`linux-arm` 不参与矩阵**：QuestPDF 无该架构原生库。若未来要支持，必须先替换 PDF 排版引擎或做功能降级，不能在缺少原生库时宣称支持。
- 部署模式：`self-contained`（`sc`，内嵌运行时）与 `framework-dependent`（`fdd`）。
- 单文件发布在 Windows / Linux 使用 `PublishSingleFile=true` + `IncludeNativeLibrariesForSelfExtract=true`；macOS 按 Avalonia 官方建议只使用 `PublishSingleFile=true`，不启用 native 自解压。
- 发布前删除 `*.pdb`，避免 80MB+ 的 Skia PDB 进入压缩包与签名流程。
- 发布目标：softprops/action-gh-release；资产名格式 `TeachCraft-{VERSION_TAG}-{rid}-{sc|fdd}.{zip|dmg}`、`TeachCraft-{VERSION_TAG}-{rid}-{sc|fdd}-Setup.exe`。

### 本地等价命令

```powershell
dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release

dotnet publish src/TeachCraft.App/TeachCraft.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o dist/TeachCraft-win-x64-sc

./build-installer.ps1 -Rid win-x64 -Version v1.0.0 -Deployment self-contained
```

macOS：

```bash
chmod +x bundle/macos/build-dmg.sh
./bundle/macos/build-dmg.sh osx-arm64 v1.0.0 self-contained
```

---

## 四、仓库结构

```
TeachCraft.slnx
Directory.Build.props        # 版本号唯一来源
src/TeachCraft.App           # Avalonia 桌面端（工具箱壳 + 五只常备应用入口）
src/TeachCraft.Core          # 图片 / PDF 处理引擎
src/SeatGrid.App             # 归位座位表窗口
src/SeatGrid.Core            # 名单识别、排座、PNG/PDF/Excel 导出
src/ClassBlock.App           # 时序课程表窗口
src/ClassBlock.Core          # 总表识别、班级课程提取、PNG/PDF 渲染
src/MeetRoster.App           # 竞名运动会报名与赛程窗口
src/MeetRoster.Core          # 报名模型、项目目录、示例排程与合成演示数据
src/GradeWeave.App           # 织序课表编排窗口
src/GradeWeave.Core          # 工作簿导入、自动编排、冲突分析、Excel/PNG/PDF 导出
src/GradeStrip.App           # 分晓成绩条窗口（Library，同进程内嵌）
src/GradeStrip.Core          # 成绩导入、分析、xlsx 导出
tests/GradeStrip.Tests       # xUnit 测试（102 项，分晓导入 / 报表 / 导出回归 + 合成 fixtures）
tests/GradeWeave.Tests       # xUnit 测试（115 项，织序自动保存 / 作息时间表 / 导入 / 编排 / 活动 / 走班 / 手动放课 / 锁课 / 冲突聚合台 / 教师名单与分工五视角 / 导出设置与双样式 / xls 导入 / 导出 / 工程往返 / 岗位匹配回归 + 合成 fixtures）
tests/TeachCraft.Tests       # xUnit 测试（78 项，工具箱、时序模板与竞名报名/赛程/名单导入导出端到端）
installer                    # Inno Setup 脚本与中文语言文件
bundle/macos                 # Info.plist / Entitlements / DMG 脚本
scripts/version.ps1          # 版本号计算
.github/workflows            # 跨平台构建与发布
.memory                      # 项目记忆（决策、踩坑、架构）
design-preview               # 已评审的 HTML 视觉稿（只读参考）
```

---

## 五、分晓（GradeStrip）集成约定

- 分晓以 **一等项目 + 程序集内嵌** 的方式进入工具箱：`src/GradeStrip.App`、`src/GradeStrip.Core` 与归位、时序并列，直接在本仓库开发。点击「打开应用」在 **同一进程** 里创建 `GradeStrip.App.Views.MainWindow`，不要改成启动外部 exe。
- `GradeStrip.App` 的 `OutputType` 必须是 `Library`：它只作为窗口模块使用；若改回 `WinExe`，framework-dependent 发布会报 `NETSDK1151`（自包含可执行文件不能被非自包含可执行文件引用）。
- 命名空间保持 `GradeStrip.App` / `GradeStrip.Core` 不变（与 `SeatGrid.*` 同构），不要为了统一品牌做全局重命名。
- `AvaloniaUI.DiagnosticsSupport` 包引用必须 **无条件引入**（只加 `PrivateAssets=All`），不能按 Configuration 条件排除——否则 Release 还原后 `Program.cs` 的 `WithDeveloperTools()` 会报 CS1061。
- 历史渊源：代码源自 `Evan0148/GradeStrip`（上游 2026-10-24 main，含概览报表管线与成绩导入兼容修复）。2026-10-24 起并入本仓库直接开发，上游仓库转为独立应用、不再同步；若上游未来有新特性，手动 cherry-pick。
- 若引入 App 级 `StaticResource`（笔刷、字体、样式），必须同步进 `src/TeachCraft.App/App.axaml`，否则 XAML 会在运行时找不到资源。
- 发布目录里的 `GradeStrip.App.exe` / `GradeStrip.App.runtimeconfig.json` 由 `RemoveGradeStripAppHost` target 兜底删除，只保留 DLL。

---

## 六、归位（SeatGrid）集成约定

- 归位以独立 Avalonia 窗口运行，但**由工具箱同进程打开**（`SeatGridWindow`），单实例复用；不要改成外部 exe。
- 数据管线固定为：`RosterImporter` → `SeatingEngine` → `SeatChartComposer` → `SeatChartRenderer` / `SeatChartExcelExporter` / `SeatGridConfigIO`。界面、PNG、PDF、Excel 共用同一个 `SeatChartDocument`，不得各自写一套布局。
- 名单识别必须同时支持 **带表头** 与 **无表头** 的 xlsx / xls / csv；不得要求用户套用固定模板。识别字段：姓名、性别、学号、身高、视力、成绩、原小组、备注。
- 座位坐标统一用 `SeatPosition(Group, Row, Column)`；`Row=0` 永远表示靠近讲台的第一排。教师视角 / 学生视角只改变展示顺序，不改变物理座位。
- 打印 PDF 采用「高分辨率 PNG 嵌入 A4」的方案（QuestPDF），不要改回 SkiaSharp 矢量 PDF：后者会把整个中文字体内嵌，单张座位表可达 23MB。
- 导出路径不得覆盖已导入的名单文件；UI 层必须做 `IsSamePath` 检查。
- 新增第 11 件小工具或归位的新导出格式时，按 Minor 版本晋级，并同步 `.memory/09-归位座位表.md`。
- 归位的默认格子缩放为 85%（68px），保证 1440 宽窗口能完整显示 4 组 × 2 列；用户可在设置里改回 100%（80px）。

---

## 七、时序（ClassBlock）集成约定

- 时序以独立 Avalonia 窗口运行，由工具箱同进程打开（`ClassBlockWindow`），单实例复用；不要改成外部 exe。
- 数据管线固定为：`MasterScheduleImporter` → `TimetableBuilder` → `TimetableRenderer`。界面预览、导出 PNG、打印 PDF 共用同一个 `ClassTimetable`，不得各自写一套版面。
- 总表识别只接受 `.xlsx`；必须同时找到「星期一…星期五」「1–7 节」和班级行，才认定是课程表。`.xls` 提示用户另存为 xlsx，不静默失败。
- 每天按 10 列分组：7 节课 + 课后服务1 + 课后服务2 + 晚修。读取单元格必须先解析合并区域，否则合并的「培优辅差 / 阳光体育」会只对第一行生效。
- 辅助信息从同一份工作簿读取：班主任（分工表）、午学（含 12:00 的 5 天表头）、课服/晚修（班级行上方含课服1/2/晚修的子表头）、早读（含「早读」与语文/英语的备注）。缺表时写入警告，不阻断生成。
- 课程表行序与时间由作息模板驱动：模板行通过 Slot（第 N 节 / 早读 / 午学 / 课服1/2 / 晚修 / 静态）绑定总表数据。内置七 / 八 / 九三套预设；九年级预设 = 原固定行序快照（早读 → 1 → 2 → 大课间 → 3 → 4 → 午餐 → 午学 → 5 → 6 → 7 → 课服1 → 课服2 → 晚餐 → 新闻联播 → 晚修），七 / 八年级在第三节后插入朗读行。午餐、晚餐为整行红字；大课间、新闻联播合并星期列；晚修为两段时间的高行。
- 作息模板可在界面编辑器中复制内置、增删行、改时间后另存为自定义模板，存于 `%APPDATA%\TeachCraft\ClassBlock\templates`；导入总表后按识别年级自动套用该年级上次使用的模板，无记录时用内置预设。学校改作息只需改模板；新增模板行类型或 Slot 槽位才需要改 Core 并升 Minor，并同步 `.memory/10-时序课程表.md`。
- 主课默认只显示学科；开启「显示任课教师」后学科加粗、教师排第二行。午学、晚修始终显示教师姓名并补「老师」，含「详见附件」的说明原样保留。
- 五天课服内容一致才合并为一格；内容不同必须逐日显示，不能为了好看强行合并。
- 打印 PDF 继续用「高分辨率 PNG 嵌入 A4」（QuestPDF），不要改成矢量 PDF。
- 导出路径不得覆盖已导入的总表；UI 层必须做 `IsSamePath` 检查。
- 新增总表格式或课程表行时，按 Minor 版本晋级，并同步 `.memory/10-时序课程表.md`。

---

## 八、编码与设计准则

1. **语言**：界面中文，工具 ID 用等宽拉丁（`image-convert` 等）；异常消息面向老师，必须是可读中文。
2. **设计语言**：沿用分晓的 Anthropic 纸墨色板（`#FAF9F5` / `#141413` / `#D97757`）、Anthropic Serif + Mono 字体、1px 细边白卡、无阴影、0.12–0.32s 克制动效。
3. **本地优先**：不得加入遥测、上传、账号体系；状态栏始终提示「本机运行 · 文件不上传」。
4. **工具定义**：工具 ID 与顺序是稳定契约，变更需升 Minor 以上并同步 `.memory/03-功能与工具面板.md`。
5. **异步**：所有文件处理走 `Task.Run`，UI 线程只做选择与展示；`Progress<int>` 仅在长任务（PDF 转图片）上报进度。
6. **错误处理**：预期内失败抛 `ToolboxException`（中文、可操作）；未知异常统一包装为「处理失败：…」并写入状态栏。
7. **多文件顺序**：合并 / 图片合成必须允许用户上移、下移、移除；不要依赖选择器返回顺序。
8. **输出保护**：任何可能覆盖输入文件的路径必须在 Core 层拒绝。
9. **提交规范**：Conventional Commits（`feat:` / `fix:` / `chore:` / `docs:`），正文说明「为什么」。

---

## 九、验证要求

- 提交前必须通过：`dotnet build TeachCraft.slnx -c Release` 与全部 xUnit 测试（GradeStrip.Tests 102 项 + TeachCraft.Tests 78 项 + GradeWeave.Tests 115 项）。
- 涉及图片 / PDF 引擎的改动，必须额外用真实文件跑一遍：格式转换、PDF 合并、Word→PDF、PDF 转图片。
- 涉及归位的改动，必须额外验证：带表头 / 无表头名单识别、配置 xlsx 往返、PNG / PDF / Excel 三种导出。
- 涉及时序的改动，必须额外验证：真实总表的年级/学期/月份/班级数识别、目标班级 7 节课与课服/午学/晚修提取、PNG 与 PDF 输出。
- 涉及织序的改动，必须额外验证：真实结构工作簿的导入、生成后无教师 / 班级冲突、教研与会议时段不被占用、分层走班同步、Excel / PNG / PDF 三种导出。
- 涉及教师分工的改动，必须额外验证：先建教师名单再分工的默认顺序、名单批量添加与未任课教师保存、班级 / 教师 / 学科 / 体检视角切换不丢草稿、下拉按学科过滤、删除教师前的任课 / 班主任 / 课表引用拦截。
- 涉及竞名的改动，必须额外验证：多班报名表导入、每项男女各 2 人 / 每人 2 个单项的超限提示仅在违规时显示、20*50 与一分钟跳绳的忽略且不进入体检、比赛项目列 0/1/2 个框的动态显示、按班回写的文件名与字段、往届赛程“竞赛日程 + 项目分组表”导入、班级高亮与 1120×680 布局。
- 发布前用 `--shot` / `--panel <tool-id>` / `--gradestrip-shot` / `--seatgrid-shot` / `--classblock-shot` / `--gradeweave-shot` / `--meetroster-shot` 做一次视觉走查（详见 `.memory/07-验证与测试资产.md`）。

---

## 十、数据隐私红线（最高优先级）

- **绝不能泄露参考数据**：真实学生姓名、考号、学校名、评语等可识别信息不得进入代码、测试、文档、演示数据、截图与提交信息；仓库公开托管，一经提交即永久公开。
- **测试必须使用生成的假数据**：fixture 与演示数据一律使用合成克隆（真名→合成名、考号→合成考号、校名→合成校名，成绩/名次/结构保留）或内存构造数据；新增 fixture 必须按同一做法合成，禁止把用户提供过的真实文件内容直接入库。
- **演示内容与截图同规则**：`StripPreview` 演示表、`Assets/Shots` 截图、`design-preview` 资产一律基于合成数据生成；数据更新后用上游分晓 `--shot` 重截。
- **身份替换对照表不进仓库**：真名→假名映射表仅存本机 `temp/`（已 gitignore）。
- **提交前自检**：diff 中出现真实姓名/考号/校名（含作为更长字符串的子串出现）即违规，立即停止提交并清理；合成名须选不含任何真实 token 的写法。

---

## 十一、织序（GradeWeave）集成约定

- 织序以独立 Avalonia 窗口运行，由工具箱同进程打开（`GradeWeave.App.GradeWeaveWindow`），单实例复用；不要改成外部 exe。
- 数据入口有三个：导入工作簿（`.xlsx` / `.xls`，`.xls` 经 NPOI 转内存工作簿后与 `.xlsx` 走同一解析管线）、打开 `.weave.json` 工程、「全新制作」空白工程（`NewProjectWindow` 配置学校 / 年级 / 班级数 / 学科课时，`AssignmentEditorWindow` 先建教师名单，再填班主任与任课教师）。空状态与顶栏都有「全新制作」入口；「教师分工」不进顶栏，作为编排流程的一环放在 05 步「编排对象」卡内，配任课 / 教师摘要并在任课为 0 时提示先补分工，「全新制作」完成后直接接上分工窗口。
- 教师分工编辑器（`AssignmentEditorWindow` / `AssignmentEditorViewModel`）固定为五种视角共享同一份草稿，并把**教师名单**设为默认第 1 步：名单视角维护教师姓名 + 学科，支持批量粘贴（每行「姓名,学科」），新增但未任课的教师也会在保存时写入 `Teacher` 档案；**班级视角**按班填班主任与各学科教师，教师格是名单下拉；**教师视角**选中一位教师后用「班级 × 学科」勾选矩阵指派 / 取消任课；**学科视角**按学科逐班选择教师，支持「填入空班 / 全部替换」；**分工体检**汇总待补与教师负载。班级 / 学科下拉按学科过滤名单，旧工程的跨学科任课保留为当前项；删除名单教师前必须解除任课、班主任与课表 / 走班 / 会议 / 活动引用。切换视角只能刷新派生视图，不得丢失或提前写回草稿；只有点「保存分工」才同步教师档案、按姓名重建任课关系、重算周课时并写回工程。新增视角或改变草稿 / 保存规则时按 Minor 升级并同步 `.memory/12-织序课表编排.md`。
- 数据管线固定为：`WeaveWorkbookImporter` → `TimetableService`（确认作息并编译节次）→ `WeaveScheduler` → `ConflictAnalyzer` → `WeaveExcelExporter` / `WeaveImageRenderer` / `WeavePdfExporter`（冲突报告走 `WeaveConflictReportExporter`）。界面预览与三种导出共用同一个 `WeaveProject` 与 `LessonPlacement`，不得各自写一套排课结果。
- 五步流程固定为：**确认作息时间表** → 导入年级工作簿（分工表 / 确认表 / 课程表 / 会议活动备注）并核对班级、学科、教师与课时 → 设定教研、走班、会议约束 → 检查冲突后导出。作息时间表未确认时 `WeaveScheduler.Generate` 必须抛出可读中文异常，顶栏「生成课表」与 05 步「重新生成」必须禁用；手动放课、拖拽与清除也必须拒绝。
- **作息时间表（`WeaveTimetable` / `TimetableRow` / `TimetableService`）**：一天的有序时段列表，行类型固定为 `Lesson / MorningReading / Break / NoonStudy / Lunch / Rest / EyeExercise / AfterSchool / EveningStudy / Routine`；只有 `Lesson` 编译为正课节次，`AfterSchool / EveningStudy` 编译为 `IsExtension=true` 的扩展节次，其余只作作息说明。`TimetableService.Validate` 必须拒绝空表 / 无正课 / 非法时间 / 起止倒置 / 时段重叠，并按开始时间给出排序提示；`Confirm` 把节次写入 `WeaveProject.Periods`、写入内容签名 `ConfirmedSignature`，逐行改动后签名自动失配，必须重新确认。确认后节次减少时，超出新正课范围的 `LessonPlacement` 要移除并如实报告；教研 / 会议 / 走班 / 活动引用越界只提示，不静默改动。内置七 / 八 / 九年级作息与「仅正课七节」预设只使用通用年级名，不得写入真实学校作息；编辑窗口为 `TimetableEditorWindow`，沿用 ClassIsland 的时段列表 + 详情 + 时间轴编排方式，确认才写回工程。新增行类型、内置预设或改变节次编译规则时按 Minor 晋级，并同步 `.memory/12-织序课表编排.md`。
- 分层走班分两种时段模式：**自动安排**（新建走班组默认；`IsFlexible`，不占固定时段，`PermutationScheduler.ResolveFlexibleGroup` 在生成时挑选所有参与班与各层教师都空闲、不撞教研会议且同科不超限的 `WeeklyHours` 个槽位，落位写入 `ResolvedSlots`）与**固定时段**（学校已排定时才选自选星期与节次）。自动安排一个槽位都找不到时整组退化为普通课并在消息里说明，不静默丢安排；部分落位时余量按普通课编排。固定组优先落位，自动组在固定组之后挑选。生成后状态栏必须报告各组实际时段与「N 个班同节走班」，班级格与总表用底色与「走班 · N 个班同节」标记并在悬浮提示里列出 A/B 层教师；`ConstraintRules.TryGetLayeredGroupAt` 必须同时识别固定区间与 `ResolvedSlots`。走班组教师下拉只列出当前学科的任课教师（`WeaveProject.TeachersOfSubject`），选中教师后优先把教室带成该教师在本学科任课的参与班，带入教室后也按分工表反查该班该学科教师，不得把全年级教师都列进下拉。
- 手动放课：班级视角 / 总表视角点击课程格打开 `LessonEditorWindow`，按「学科 · 任课教师」列出本班候选并显示应排 / 已排课时与教师本节冲突预判；点一行即放入或替换。`LessonPlacementService` 写入 `IsFixed=true` 的课，重新生成时保留；活动占位与走班时段不得手动放课；教师冲突、教研 / 会议占用、超课时只写警告交给 `ConflictAnalyzer`，不得静默改课。
- 锁课与冲突聚合台：05 步右侧冲突面板底部「打开冲突聚合台」打开非模态 `ConflictCenterWindow`，按 `ConflictKind` 分组并支持类型 / 班级 / 教师 / 关键词筛选；有星期与节次的冲突定位到课程格，无节次时定位到相关班级或教师，定位只读、不改课。课程格右键菜单、选中后按 `L`、表头「锁定本班 / 解锁本班」可切换 `IsFixed`；锁定课在重新生成时保留，拖拽移动与交换自动锁定两格。活动占位与走班时段不得锁定，仍由活动列表与 03 步走班组管理。
- 编排策略：每个班的教学时段视为排列，用「分散式初始分配 + 最小冲突局部搜索」消除教师冲突、教研/会议占用与同科每日超限；超过时限（`ScheduleOptions.TimeLimit`）降级返回已排结果并列出未排课程（`UnplacedLesson`），不得谎报“全部排完”。同一班级同一学科每天不超过 2 节、下午不超过 1 节；主课优先上午。20 班 / 62 教师 / 694 节的真实规模回归必须保持可排满。
- 临时活动（`ActivityBlock`）分两种：`Overlay` 只在格子与导出里叠加「活动名 + 时间」，不改课程、课时与冲突；`Replace` 移除重叠节次的原课，写入 `LessonPlacement(IsActivity=true, IsFixed=true)` 固定占位，重排时必须保留该占位，被移除课时形成的缺口（课时不符 / 未排课）必须如实报告，不得静默补齐。时间通过 `ActivityResolver` 匹配 `PeriodSlot.TimeText` 的重叠节次（端点相接不算重叠）；作息时间不标准时可用 `Period` 手动指定。
- 活动占位格不可拖拽、不可右键清除；界面与导出必须显示活动名称与时间，活动不计入教师「已排 / 应排」周课时。
- 冲突类型固定为：教师冲突、班级冲突、教师不可用（教研 / 会议）、走班冲突、缺任课教师、课时不符、未排课程；每条冲突应尽量带星期与节次，导入数据缺节次时聚合台显示「位置未定位」并定位到相关班级或教师。
- 导出入口统一为 04 步顶栏「导出…」→ `ExportOptionsWindow`：格式 Excel / PNG / PDF；Excel 再选「软件自带样式」或「参考校样式」并勾选总表、分工表、教师职位表、班级课表、教师课表、冲突页。Excel 结构必须包含总表、分工表、教师职位表、班级课表、教师课表与冲突页；「参考校样式」按参考工作簿生成 `1-分工表`、`2-课程表`（横向 5 天 × 7 节 + 课后服务 2 列 + 晚修 1 列）与 `3-教师职位表`，宋体 / 楷体、黑色细框。打印 PDF 继续用「高分辨率 PNG 嵌入 A4」（QuestPDF），不要改成内嵌整套中文字体的矢量 PDF。
- 导出路径不得覆盖已导入的工作簿；UI 层必须做 `IsSamePath` 检查。首版只接受 `.xlsx`，`.xls` 提示另存为，不静默失败。
- **自动保存**：窗口每 2 秒检查一次工程快照，内容有变化才落盘；关闭窗口前同步补写一次，不丢最后几秒的改动。当前工程来自 / 已手动保存为 `.weave.json` 时，用 `WeaveProjectIO.SaveJson` 原子写回该文件；没有工程路径或写回失败时，写 `%LOCALAPPDATA%\TeachCraft\GradeWeave\autosave.weave.json` 与 `autosave.meta.json`，空状态显示「恢复上次自动保存」。自动保存绝不能写导入的 `.xlsx` / `.xls`；不得在仓库或上传服务中写快照。UI 状态栏必须显示「自动保存 · HH:mm:ss」或失败提示，不得静默失败。
- 本地优先：不得加入遥测、上传、账号体系；示例、fixture 与截图一律使用合成学校 / 教师 / 班级（示例中学、沈青禾、陆明川等虚构名），禁止引用真实课表。
- 新增导入表结构、约束类型或导出格式时，按 Minor 版本晋级，并同步 `.memory/12-织序课表编排.md`。

---

## 十二、竞名（MeetRoster）集成约定

- 竞名以独立 Avalonia 窗口运行，由工具箱同进程打开（`MeetRoster.App.MeetRosterWindow`），单实例复用；`MeetRoster.App` 保持 `Library`，不要改成外部 exe。
- 窗口默认必须是空工程：报名页显示「从导入报名表开始」，赛程页显示「尚未导入赛程」，不得自动载入合成示例数据。示例数据只允许在 `--meetroster-demo` 截图走查或测试中显式使用。
- 报名表模板固定为：`A1` 运动会名称、`B2` 参赛单位、`B3` 参赛单位编码、`B4` 领队（班主任）与电话、`B5` 任课体育老师、第 7 行表头、第 8 行示例、第 9 行起为运动员。字段为姓名、性别、团体项目 1–3、比赛项目 1–2；不得要求用户改模板。
- 报名表的团体项目保留 3 个框；比赛项目合并为一列：0–1 个单项显示一个框（未报显示「选择项目」），2 个单项显示两个框。点击比赛项目列打开多选弹窗（同织序视觉）：可搜索、同时选 / 取消最多 2 个单项、人数预判与满员 / 重复提醒、清空全部、完成后按数量显示；团体项目框仍为单选。所有项目框都不带三角形。
- 报名规则固定为：每个单项每班男女各最多 2 人；每人最多 2 个单项（不含集体项目）；`20*50米男女混合接力` 与 `一分钟跳绳` 不填、现场出人；`4x100米接力` 可填。导入时忽略前两项且不计入违规；导出时也只写 `4x100米接力`。
- 「报名体检」只在真正违反报名规则（单项男女超 2 人、同一人超 2 个单项或重复项目、性别 / 项目无法识别）时出现；被忽略的团体项目、文件名建议、重复姓名等非规则提醒不显示，合规班级不出现体检卡片。
- 报名导入支持一次选择多个 `.xlsx` 班级文件；文件名应为「**年级**班运动会报名表.xlsx」，文件名与表内 `B2` 不一致时提示但不阻断。按班回写必须复用导入原文件以保留隐藏工作表与下拉校验，目标文件名由程序生成，禁止覆盖原始报名表。
- 赛程导入固定读取往届工作簿的「竞赛日程」与「项目分组表」：日程提供日期 / 上午下午 / 径赛田赛趣味赛 / 组别 / 性别 / 项目 / 赛次 / 人数 / 组数 / 录取 / 时间；分组表提供组次、道次、号码、姓名与班级。`预决赛` 与 `预赛` 视为同一赛次；`跳绳` 归一为 `一分钟跳绳`，`20x50米接力` 归一为 `20*50米男女混合接力`。
- 项目分组表的班级只写「6班」，必须与组别拼成完整班级名（如「八年级6班」）后再匹配报名班级与高亮；`1000米` 保留为个人项目（图片清单可能省略，但模板与往届赛程均存在）。
- 班级高亮只改变展示，不得改赛程数据：选中班级后，日程中包含该班条目的项目卡与分组条目同时着色，其余条目淡化；清除高亮恢复中性。高亮班级下拉来自当前班级集合，不写死示例班级。
- 真实报名表与赛程工作簿只在本机读取，不上传、不入库；测试、演示数据与截图一律使用合成学校 / 学生 / 班级（示例中学、苏晚晴、陈屿等虚构名）。真实数据不得进入 `tests/fixtures`、`.memory`、`design-preview` 或提交信息。
- 新增报名表字段、规则、赛程表结构或导出格式时按 Minor 版本晋级，并同步 `.memory` 中的竞名说明与测试数量。
