# AGENTS.md

> 本文件供所有参与维护「知微 · TeachCraft」的 AI 智能体与开发者共同遵守，定义版本号规范、CI/CD 契约、仓库结构与编码准则。

---

## 一、项目定位

「知微」是面向教师的本地工具箱：10 件图片 / PDF 小工具，加上「分晓 · GradeStrip」成绩条生成器与「归位 · SeatGrid」座位表生成器，统一使用分晓的纸墨设计语言与本地优先原则。技术栈为 **C# / .NET 10 + Avalonia 12**，所有文件处理在本机完成，不上传、不留存。

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

当前基线：`1.1.0`（2026-10-23 新增「归位 · SeatGrid」，按 Minor 晋级）。`1.0.0` 是首个完整工具箱版本。

---

## 三、CI/CD 契约

工作流：`.github/workflows/release-draft.yml`

1. **测试**：`windows-latest` 上运行 `dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release`，失败则阻止发布。
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
src/TeachCraft.App           # Avalonia 桌面端（工具箱壳 + 分晓 / 归位入口）
src/TeachCraft.Core          # 图片 / PDF 处理引擎
src/SeatGrid.App             # 归位座位表窗口
src/SeatGrid.Core            # 名单识别、排座、PNG/PDF/Excel 导出
vendor/gradestrip            # 分晓 App + Core 的只读快照
tests/TeachCraft.Tests       # xUnit 测试（16 项）
installer                    # Inno Setup 脚本与中文语言文件
bundle/macos                 # Info.plist / Entitlements / DMG 脚本
scripts/version.ps1          # 版本号计算
.github/workflows            # 跨平台构建与发布
.memory                      # 项目记忆（决策、踩坑、架构）
design-preview               # 已评审的 HTML 视觉稿（只读参考）
```

---

## 五、分晓（GradeStrip）集成约定

- 分晓以 **项目引用 + 程序集内嵌** 的方式进入工具箱；点击「打开应用」在 **同一进程** 里创建 `GradeStrip.App.Views.MainWindow`，不要改成启动外部 exe。
- `vendor/gradestrip` 是 **只读快照**，来源：`Evan0148/GradeStrip`，提交 `db0baeb`（2026-10-22）。唯一的结构性改动是把 `GradeStrip.App` 的 `OutputType` 改为 `Library`：它在本项目里只作为窗口模块使用；若保留 `WinExe`，framework-dependent 发布会报 `NETSDK1151`（自包含可执行文件不能被非自包含可执行文件引用）。
- 同步方式：从上游复制 `src/GradeStrip.App`、`src/GradeStrip.Core`（排除 `bin/obj` 与 `*.user`），保持相对目录结构；复制后把 `OutputType` 改为 `Library`，并删除 `ApplicationIcon` / `ApplicationManifest` 属性。若上游新增 App 级 `StaticResource`（笔刷、字体、样式），必须同步进 `src/TeachCraft.App/App.axaml`，否则 XAML 会在运行时找不到资源。
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

## 七、编码与设计准则

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

## 八、验证要求

- 提交前必须通过：`dotnet build TeachCraft.slnx -c Release` 与 16 项 xUnit 测试。
- 涉及图片 / PDF 引擎的改动，必须额外用真实文件跑一遍：格式转换、PDF 合并、Word→PDF、PDF 转图片。
- 涉及归位的改动，必须额外验证：带表头 / 无表头名单识别、配置 xlsx 往返、PNG / PDF / Excel 三种导出。
- 发布前用 `--shot` / `--panel <tool-id>` / `--gradestrip-shot` / `--seatgrid-shot` 做一次视觉走查（详见 `.memory/07-验证与测试资产.md`）。
