# 交接任务书：知微 TeachCraft UI 手感与一致性（第二阶段）

> 用途：把「需要真实桌面交互验收」的剩余 UI 项交给一个能启动应用、能看界面、能录屏的执行 Agent。
> 生成时间：2026-10-27 · 基线版本 1.11.2 · 上游报告 `outputs/ui-audit-report-2026-10-27.html`

---

## 一、你的任务

在真实桌面环境中完成 4 组 UI 改造并**提供可观察的验收证据**。这些项上一轮已诊断清楚、但因为没有交互环境而主动延后，**不需要重新调研**。

- 仓库：`D:\Documents\TeachCraft`
- 技术栈：C# / .NET 10 + Avalonia 12（Windows / macOS / Linux 桌面）
- 产品：面向教师的本地工具箱，5 个应用——壳 `TeachCraft.App`、归位 `SeatGrid.App`、时序 `ClassBlock.App`、织序 `GradeWeave.App`、分晓 `GradeStrip.App`
- 设计语言：纸墨（Paper `#FAF9F5`、Ink `#141413`、Accent `#D97757`、AccentDeep `#A34A36`、Muted `#6F6A5E`、1px 细边、**无阴影**、圆角 6–12）
- 共享 UI 组件：`src/Shared/`（`WindowFit` 窗口钳制、`ErrorText` 异常文案、`ConfirmDialog` 确认弹窗），由 `Directory.Build.props` 链接编入 5 个 App

### 基线（必须保持不变）

```powershell
dotnet build TeachCraft.slnx -c Release      # 期望 0 警告 0 错误
dotnet test tests/GradeStrip.Tests/GradeStrip.Tests.csproj -c Release   # 102
dotnet test tests/TeachCraft.Tests/TeachCraft.Tests.csproj -c Release   # 30
dotnet test tests/GradeWeave.Tests/GradeWeave.Tests.csproj -c Release   # 103
# 合计 235，必须全绿
```

### 启动与取证通道（已存在，直接复用）

- `--shot <png>`：把窗口渲染成 PNG 后退出（壳、分晓均支持，见 `TeachCraft.App/App.axaml.cs:515`、`GradeStrip.App/App.axaml.cs:55`）
- `--selftest`：端到端自测（`TeachCraft.App/App.axaml.cs:486`）
- `--seatgrid-demo`：归位演示名单（`TeachCraft.App/App.axaml.cs:366`）
- 其它截图参数：`--busy-preview`、`--exam-manager`、`--scroll-bottom`、`--include-absent` 等

---

## 二、已经做完，不要重做

1. 窗口适配：`src/Shared/WindowFit.cs`（按 `Screen.Scaling` 做 DIP↔像素换算），7 个窗口 Opened 接入；三课堂窗最小 1120×680；NewProject 放开缩放
2. 织序三个课表网格已包 ScrollViewer
3. 破坏性确认：归位 5 处、分晓替换考试、织序 4 个切换工程入口，统一走 `ConfirmDialog`
4. 织序分工 Apply 校验前置 + `HasProjectReferences(ignoreClassIds)` 预演
5. 异常文案网关 `ErrorText`（4 处接入）
6. 色板统一（Muted `#6F6A5E`、错误色 `#A34A36`）、12 处散值清理、禁用文字 3.54:1
7. 分晓图标 avares 路径；4 处 `ShowOverwritePrompt`
8. 工具箱运行重入守卫；两处拖拽已记录 `grabOffset` + 源卡真实尺寸、阈值 10px
9. **抽屉已改单一进度模型**（`MainWindow.axaml.cs`，16ms + 指数趋近 response 0.09s，可打断）
10. 无障碍：4 搜索框 AutomationProperties、KPI 卡键盘、归位 Delete/Esc、搜索 Enter、EditorCloseGuard Alt+A
11. 图标统一：Emoji → 几何图形、`↗` → Path、删除 `Border:pressed` 死样式、mini 24×24、MonoFont 回退
12. 排版方向：52px 负字距、19/21/30/32px 收紧、正文行高 20
13. 织序关窗 IsBusy 拦截、删班级确认

---

## 三、待办（按优先级，逐项独立提交）

### P1-A 归位拖拽：速度投影 + 幽灵飞向落点

**文件**：`src/SeatGrid.App/SeatGridWindow.axaml.cs`

**现状**：`BeginDrag()` 已用 `_dragSourceSize` 生成幽灵、`UpdateDrag()` 已用 `position - _grabOffset`；`OnSeatPointerReleased()` 仍在 `EndDragVisuals()` 后直接 `MoveSeatStudent / SwapRows`，卡片是瞬间消失。

**目标**：
1. 在 `OnSeatPointerMoved` 维护最近 5 帧 `(Point, Stopwatch.GetTimestamp())` 环形缓冲
2. 松手时算速度（px/s），用 Apple 的指数衰减投影：`project(v, d=0.998) = v / 1000 * d / (1 - d)`
3. 用「当前点 + 投影距离」作为落点，取最近的**可放置**座位；若为 null 回退到 `_dropTarget`
4. 幽灵用 120–200ms 临界阻尼飞到目标格中心（快甩取 120ms，慢放取 200ms），动画结束再提交数据；提交前后不要出现一帧空白
5. 拖出网格时对幽灵位置施加橡皮筋：`rubberband(o, dim, 0.55) = o * dim * 0.55 / (dim + 0.55 * |o|)`

**验收**：录屏对比「慢放到相邻格」与「快速甩到远端格」——落点不同且符合直觉；卡片有明确的落位动作，不再凭空消失。

---

### P1-B 织序拖拽：幽灵移出裁剪层 + 同样加物理

**文件**：`src/GradeWeave.App/GradeWeaveWindow.axaml.cs`（`BeginDrag` ~1510、`UpdateDrag` ~1563）、`src/GradeWeave.App/GradeWeaveWindow.axaml`（~1220–1246）

**现状**：`ScheduleDragLayer` 位于 `ClipToBounds="True"` 的卡片 Border 内，拖到网格卡片外幽灵被裁掉；松手同样瞬间改数据。

**目标**：
1. 把 `Canvas x:Name="ScheduleDragLayer"` 移到窗口根 Grid（`Grid.ColumnSpan` 覆盖内容列，`ZIndex=99`，`IsHitTestVisible=False`），仍用 `this.TranslatePoint(windowPosition, ScheduleDragLayer)` 换算坐标
2. 边界橡皮筋同上（P1-A 第 5 点可抽成共享方法放 `src/Shared/`）
3. 落点投影与幽灵飞行同上

**验收**：拖到课表卡片右侧栏外时幽灵仍可见；松手落点与指针意图一致；跨滚动位置（ScheduleGridHost 已有 ScrollViewer）时落点仍正确。

---

### P1-C 归位滑块：先压测，再决定是否限频

**文件**：`src/SeatGrid.App/SeatGridViewModel.cs`（`BeginInteractiveLayoutChange` ~189、`OnCellScaleChanged` ~270）

**背景**：1.10.1 曾把滑块改为「松手才提交」以省重建；但拖动期间座位表完全不动，违反直接操纵。需要实测数据再决定。

**目标**：
1. 造 100 人 / 8 组 / 6 排的数据，连续拖动 `CellScale` 60 帧，用 `Stopwatch` 记录每次 `BuildGrid()` 耗时（p50 / p95）
2. 若 p95 < 8ms：改为 33ms 合并重建（`DispatcherTimer` 或 `Dispatcher.UIThread.Post(..., DispatcherPriority.Background)` + `_rebuildQueued` 标志），拖动时实时更新
3. 若 p95 ≥ 8ms：**保持现状**，把实测数据写进交接说明，并给出建议（例如只对 `CellScale` 做轻量路径：只改 `SeatCellViewModel.Width/Height`，不重建整表）
4. 无论哪种结论，都要在 `.memory/06-决策与踩坑记录.md` 补一条带数据的决策

**验收**：拖动格子大小滑块时，座位表要么实时跟随（若改），要么有明确的实测依据说明为何不改。

---

### P2-A reduced-motion（减弱动效）

**文件**：`src/Shared/`（新增 `MotionPreference.cs`）、`src/TeachCraft.App/App.axaml.cs`、抽屉与 busy 动画处

**目标**：
1. 启动时探测：Windows 用 `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)`；macOS 用 `NSWorkspace.accessibilityDisplayShouldReduceMotion`（ObjC 互操作）；Linux 暂返回 false
2. 结果写入全局 `bool ReduceMotion`（建议 `Application.Current.Resources["ReduceMotion"]` 或静态属性）
3. reduced 时：抽屉改为 120ms 纯 Opacity 淡入（`TranslateTransform.X` 不动）、`Canvas.busy-spin` 停转改不确定进度条、`ToolPanelView` 的 0.96→1 缩放取消
4. 三平台至少验证 Windows；macOS/Linux 若无法实测，代码要能优雅降级并在交接说明里标注「未实机验证」

**验收**：Windows 设置 → 辅助功能 → 视觉效果 → 关闭「动画效果」后重启应用，开抽屉无横移，仅淡入。

---

### P2-B 跨应用一致性收口（视觉结构，需逐屏走查）

**文件**：5 个 App 的 axaml + `src/Shared/`

**目标**（每项单独提交，附前后截图）：
1. 抽 `EmptyState` 组件：标题「还没有 X」+ 一句下一步 + 主 CTA；筛选无结果换「没有匹配的 X + 清除筛选」
2. 状态栏三段式：左状态点+文案 / 中统计 / 右版本号 `V1.x.x`，高度 32–36，padding 一致
3. 导出统一为「导出 ▾」聚合（Excel / PNG / PDF / 报告），打印作为其中一项
4. 列表单击语义统一：单击选中、双击或右侧显式按钮打开（织序课程格当前是单击直接弹窗，需改）
5. 弹层统一真 `Window` + `IsDefault/IsCancel`；分晓的考试管理/删除确认目前是窗口内 Grid 覆层（Esc 无效、焦点穿透），需改真窗

**验收**：5 个应用同位置截图对比，空态/状态栏/导出入口结构一致；分晓弹层 Esc 可退且 Tab 不会跑到背后。

---

## 四、硬约束

- **不改 VM 的公开契约**（235 项测试直接依赖）；必须改时先补测试再改
- 不引入新的 NuGet / npm 依赖
- 不破坏纸墨设计语言：不加阴影、不用 >12 圆角、不引入彩色（除既有 Tier 分档色）
- 所有确认弹窗走 `src/Shared/ConfirmDialog.cs`，所有面向用户的异常走 `src/Shared/ErrorText.cs`
- 每次提交前 `dotnet build -c Release` 必须 0 警告 0 错误，235 测试全绿
- 版本按 Patch 晋级：`Directory.Build.props` 1.11.2 → 1.11.3（多轮可继续递增），并同步 `AGENTS.md` 基线描述

---

## 五、交付物

每项一份，缺一不可：

1. **改动清单**：文件路径 + 关键行号 + 一句话说明
2. **行为证据**：录屏（拖拽/动效）或前后截图（布局），文件名写清 `before-*.png` / `after-*.png`
3. **数据证据**：P1-C 的 p50/p95 重建耗时表
4. **回写**：更新 `outputs/ui-audit-report-2026-10-27.html` 第六章状态（已修 / 延后原因），并在 `.workbuddy/memory/2026-10-27.md` 追加一段决策记录（含踩坑）

---

## 六、建议顺序

1. **P1-C 压测**（半天，纯数据，风险最低，结论会影响后面）
2. **P1-A 归位拖拽**（手感收益最直观，单文件改动）
3. **P1-B 织序拖拽**（复用 P1-A 的投影与橡皮筋工具函数）
4. **P2-A reduced-motion**（平台 API，单独验证）
5. **P2-B 一致性收口**（逐屏截图，最后做，避免返工）

---

## 七、不确定时怎么办

- 抽屉模型参考 `TeachCraft.App/Views/MainWindow.axaml.cs` 的 `StartPanelAnimation / OnPanelTick`，那是本轮已验证的可打断实现
- 手感参数用 Apple 的默认值：临界阻尼（不 overshoot）、response 0.3–0.4s；只有手势带速度时才允许 damping ≈ 0.8
- 任何「改了但看不出差别」的项，优先相信录屏而不是代码推断
- 如果某项实现后与既有 235 项测试冲突，**先停下来说明冲突点**，不要为了让测试通过而弱化交互
