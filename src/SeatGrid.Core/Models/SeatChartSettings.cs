using System;
using System.Globalization;

namespace SeatGrid.Core.Models;

public enum ViewMode
{
    Teacher,
    Student,
}

public enum ArrangeMode
{
    RosterOrder,
    GenderAlternate,
    Random,
    Height,
    Vision,
}

/// <summary>座位表的所有可调参数。颜色统一以 #RRGGBB 保存，便于 Excel / PDF 共用。</summary>
public sealed class SeatChartSettings
{
    public const int BaseCellSize = 80;

    public string Title { get; set; } = "班级座位表";
    public bool BoldTitle { get; set; } = true;
    public string HeadTeacher { get; set; } = string.Empty;
    public string Monitor { get; set; } = string.Empty;

    public ViewMode View { get; set; } = ViewMode.Teacher;

    public int Groups { get; set; } = 4;
    public int Rows { get; set; } = 6;
    public int Columns { get; set; } = 2;

    /// <summary>格子缩放百分比，50–200，基准 80px。</summary>
    public int CellScalePercent { get; set; } = 100;

    public int PodiumGap { get; set; } = 16;
    public int NameFontSize { get; set; } = 14;

    public bool BoldNames { get; set; }
    public bool HideEmptySeats { get; set; }
    public bool MarkFemales { get; set; } = true;
    public bool SpaceTwoCharNames { get; set; } = true;
    public bool ShowGroupRowHints { get; set; } = true;

    public string SeatBackground { get; set; } = "#FFFFFF";
    public string EmptySeatBackground { get; set; } = "#FAF9F5";
    public string SeatText { get; set; } = "#141413";
    public string SeatBorder { get; set; } = "#E7E3D8";
    public string PodiumBackground { get; set; } = "#F4F2EC";
    public string PodiumText { get; set; } = "#85806F";
    public string GroupLabel { get; set; } = "#C6613F";
    public string RowLabel { get; set; } = "#85806F";
    public string FemaleMark { get; set; } = "#C6613F";

    public int CellSizePx =>
        Math.Max(40, (int)Math.Round(BaseCellSize * (CellScalePercent / 100.0)));

    public SeatChartLayout GetLayout() => new(Groups, Rows, Columns);

    public SeatChartSettings Clone() => (SeatChartSettings)MemberwiseClone();

    public void Normalize()
    {
        Groups = Math.Clamp(Groups, 1, 8);
        Rows = Math.Clamp(Rows, 1, 10);
        Columns = Math.Clamp(Columns, 1, 4);
        CellScalePercent = Math.Clamp(CellScalePercent, 50, 200);
        PodiumGap = Math.Clamp(PodiumGap, 0, 100);
        NameFontSize = Math.Clamp(NameFontSize, 10, 36);
        Title = string.IsNullOrWhiteSpace(Title) ? "座位表" : Title.Trim();

        SeatBackground = NormalizeColor(SeatBackground, "#FFFFFF");
        EmptySeatBackground = NormalizeColor(EmptySeatBackground, "#FAF9F5");
        SeatText = NormalizeColor(SeatText, "#141413");
        SeatBorder = NormalizeColor(SeatBorder, "#E7E3D8");
        PodiumBackground = NormalizeColor(PodiumBackground, "#F4F2EC");
        PodiumText = NormalizeColor(PodiumText, "#85806F");
        GroupLabel = NormalizeColor(GroupLabel, "#C6613F");
        RowLabel = NormalizeColor(RowLabel, "#85806F");
        FemaleMark = NormalizeColor(FemaleMark, "#C6613F");
    }

    public static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var text = value.Trim();
        if (!text.StartsWith('#')) text = "#" + text;

        // Avalonia 的 Color.ToString() 会输出 #AARRGGBB，这里统一压成 #RRGGBB
        if (text.Length == 9) text = "#" + text[3..];

        if (text.Length != 7) return fallback;
        return int.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? text.ToUpperInvariant()
            : fallback;
    }
}
