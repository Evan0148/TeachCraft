using System;
using Avalonia;
using Avalonia.Controls;

namespace TeachCraft.Shared;

/// <summary>
/// 窗口工作区适配：把窗口尺寸与位置钳进当前屏幕工作区。
/// 说明：<see cref="Window.Width"/> 等是 DIP，<see cref="Screen.WorkingArea"/> 是物理像素，
/// 必须按 <see cref="Screen.Scaling"/> 换算，否则 125% / 150% 缩放下会误判。
/// 本文件由 Directory.Build.props 以链接方式编入各 App 项目，internal 避免跨程序集类型冲突。
/// </summary>
internal static class WindowFit
{
    public static void Clamp(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null) return;

        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var area = screen.WorkingArea;
        var areaWidth = area.Width / scale;
        var areaHeight = area.Height / scale;
        if (areaWidth <= 0 || areaHeight <= 0) return;

        // 最小尺寸要先让路：否则下面设的 Height 会被 MinHeight 顶回去，窗口仍然出屏。
        if (window.MinWidth > areaWidth) window.MinWidth = Math.Max(720, areaWidth - 8);
        if (window.MinHeight > areaHeight) window.MinHeight = Math.Max(520, areaHeight - 8);

        if (window.Width > areaWidth) window.Width = Math.Max(window.MinWidth, areaWidth - 8);
        if (window.Height > areaHeight) window.Height = Math.Max(window.MinHeight, areaHeight - 8);

        var pixelWidth = (int)Math.Ceiling(window.Width * scale);
        var pixelHeight = (int)Math.Ceiling(window.Height * scale);
        var maxX = Math.Max(area.X, area.X + area.Width - pixelWidth);
        var maxY = Math.Max(area.Y, area.Y + area.Height - pixelHeight);
        window.Position = new PixelPoint(
            Math.Clamp(window.Position.X, area.X, maxX),
            Math.Clamp(window.Position.Y, area.Y, maxY));
    }
}
