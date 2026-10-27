using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using SeatGrid.Core.Models;
using SeatGrid.Core.Roster;

namespace SeatGrid.App;

/// <summary>
/// 仅供滑块手感取证（--slider-bench）：在真实 Avalonia 运行时里模拟连续拖动
/// 布局滑块，统计每帧实时刷新的耗时分布（p50/p95），用来决定「格子大小」
/// 等滑块是拖动中实时重建，还是保持松手后一次刷新。不参与真实业务。
/// </summary>
public static class SeatGridDiagnostics
{
    private static readonly string[] Surnames = { "陈", "李", "王", "张", "刘", "赵", "周", "吴", "徐", "孙" };
    private static readonly string[] GivenNames = { "思远", "雨桐", "浩然", "欣怡", "子涵", "梓萱", "俊杰", "梦琪", "一鸣", "悦然", "晨曦", "嘉怡", "博文" };

    private const int Frames = 60;
    private const int WarmupFrames = 5;

    public static async Task<string> RunLayoutBenchAsync()
    {
        var vm = new SeatGridViewModel();
        vm.LoadRoster(CreateBenchRoster(Surnames.Length * GivenNames.Length));
        vm.GroupCount = 8;
        vm.RowCount = 6;
        vm.ColumnCount = 2;
        vm.AutoArrange();

        // 路径 A：格子大小滑块。松手才刷新的现状绕过了这条路径，
        // 所以不进入 BeginInteractiveLayoutChange，直接连续改值，
        // 量到的就是「拖动中实时跟随」每帧要付出的代价。
        for (var i = 0; i < WarmupFrames; i++) vm.CellScale = 85 + i;
        var scaleSamples = new double[Frames];
        for (var frame = 0; frame < Frames; frame++)
        {
            var sw = Stopwatch.StartNew();
            vm.CellScale = 50 + frame * 2.5;
            scaleSamples[frame] = sw.Elapsed.TotalMilliseconds;
        }

        // 路径 B：组 / 排 / 列滑块，属性变更触发 BuildGrid 全量重建。
        for (var i = 0; i < WarmupFrames; i++) vm.RowCount = 6 + i % 5;
        var buildSamples = new double[Frames];
        for (var frame = 0; frame < Frames; frame++)
        {
            var sw = Stopwatch.StartNew();
            vm.RowCount = 6 + frame % 5;
            buildSamples[frame] = sw.Elapsed.TotalMilliseconds;
        }

        // 行为验证：交互会话中改 CellScale，经一次后台合并刷新后格子尺寸立即跟随。
        vm.CellScale = 100; // 先回到已知基准（宽度 = 基准格 × 1.0）
        var followBefore = vm.Seats.First().Width;
        vm.BeginInteractiveLayoutChange();
        vm.CellScale = 160;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var followDuring = vm.Seats.First().Width;
        vm.EndInteractiveLayoutChange();

        return FormatReport(scaleSamples, buildSamples, followBefore, followDuring);
    }

    /// <summary>合成名单：全部虚构姓名，仅供压测，不含任何真实信息。</summary>
    private static RosterImportResult CreateBenchRoster(int count)
    {
        var students = new List<Student>(count);
        for (var i = 0; i < count; i++)
        {
            students.Add(new Student
            {
                Name = Surnames[i % Surnames.Length] + GivenNames[i / Surnames.Length % GivenNames.Length],
                Gender = i % 2 == 0 ? Gender.Male : Gender.Female,
                Number = (2026001 + i).ToString(),
                Height = 148 + (i % 15) * 2.0,
                Vision = Math.Round(4.4 + (i % 6) * 0.1, 1),
                Score = 480 + (i * 11) % 90,
                OriginalGroup = $"第{i % 8 + 1}组",
                Notes = string.Empty,
            });
        }

        return new RosterImportResult("滑块压测名单.xlsx", "滑块压测名单.xlsx", students, Array.Empty<string>());
    }

    private static string FormatReport(double[] scaleSamples, double[] buildSamples, double followBefore, double followDuring)
    {
        var report = new StringBuilder();
        report.AppendLine("# 归位滑块实时重建压测（P1-C）");
        report.AppendLine();
        report.AppendLine($"- 日期：{DateTime.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine($"- 数据：{Surnames.Length * GivenNames.Length} 人合成名单 / 8 组 × 6 排 × 2 列 = 96 座（自动排座后 96 人入座）");
        report.AppendLine("- 场景：不进入交互挂起，直接连续改值——量得的就是「拖动中实时跟随」的单帧代价");
        report.AppendLine($"- 采样：{Frames} 帧（另有 {WarmupFrames} 帧预热不计入）");
        report.AppendLine();
        report.AppendLine("| 路径 | 触发 | p50 (ms) | p95 (ms) | min (ms) | max (ms) | 60 帧总计 (ms) | 16ms 帧预算占用 p95 |");
        report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        AppendRow(report, "格子大小滑块", "CellScale 50→197 连续变化（RefreshSeatVisuals + UpdatePodium）", scaleSamples);
        AppendRow(report, "组/排/列滑块", "RowCount 6→10 往复（BuildGrid 全量重建）", buildSamples);
        report.AppendLine();
        report.AppendLine("## 行为验证：拖动中实时跟随");
        report.AppendLine();
        report.AppendLine("| 检查 | 结果 |");
        report.AppendLine("| --- | --- |");
        report.AppendLine($"| 交互会话中改 CellScale=160，一次后台合并刷新后格子宽度 {followBefore:F0}px → {followDuring:F0}px | {(followDuring > followBefore ? "通过（实时跟随）" : "失败（未跟随）")} |");
        report.AppendLine();
        report.AppendLine("结论阈值：p95 < 8ms（16ms 帧预算的一半）→ 可改 33ms 合并重建；否则保持松手后刷新。");
        return report.ToString();
    }

    private static void AppendRow(StringBuilder report, string name, string trigger, double[] samples)
    {
        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        var p50 = Percentile(sorted, 0.50);
        var p95 = Percentile(sorted, 0.95);
        var min = sorted[0];
        var max = sorted[^1];
        var total = 0.0;
        foreach (var sample in samples) total += sample;
        report.AppendLine(
            $"| {name} | {trigger} | {p50:F3} | {p95:F3} | {min:F3} | {max:F3} | {total:F1} | {p95 / 16.0 * 100:F0}% |");
    }

    private static double Percentile(double[] sorted, double fraction)
    {
        var index = (int)Math.Ceiling(fraction * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
