using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace GradeWeave.App;

/// <summary>编辑窗口打开后钳制到屏幕工作区，避免在 1366×768 屏上底部按钮被任务栏挡住。</summary>
public static class ScreenFit
{
    /// <summary>高度超出工作区时收缩窗口，并把窗口位置钳回工作区内。</summary>
    public static void Clamp(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null) return;

        var area = screen.WorkingArea;

        if (window.Height > area.Height) window.Height = area.Height;

        var width = (int)Math.Ceiling(window.Bounds.Width);
        var height = (int)Math.Ceiling(window.Bounds.Height);
        var x = Math.Clamp(window.Position.X, area.X, Math.Max(area.X, area.X + area.Width - width));
        var y = Math.Clamp(window.Position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - height));
        window.Position = new PixelPoint(x, y);
    }
}
