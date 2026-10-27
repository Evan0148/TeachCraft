using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SeatGrid.App;

public partial class GroupViewModel : ObservableObject
{
    public GroupViewModel(int number, string label, IReadOnlyList<RowViewModel> rows,
        IReadOnlyList<ColumnHandleViewModel> columnHandles)
    {
        Number = number;
        Label = label;
        Rows = rows;
        ColumnHandles = columnHandles;
    }

    public int Number { get; }
    public string Label { get; }
    public IReadOnlyList<RowViewModel> Rows { get; }
    public IReadOnlyList<ColumnHandleViewModel> ColumnHandles { get; }

    /// <summary>列把手区左侧留白：仅第一组需要为行首把手列让位。</summary>
    [ObservableProperty] private bool _showRowSpacer;

    [ObservableProperty] private IBrush _labelBrush = Brushes.Gray;
    [ObservableProperty] private bool _showHints = true;
    [ObservableProperty] private Thickness _labelMargin = new(0, 0, 0, 5);
}

public partial class RowViewModel : ObservableObject
{
    public RowViewModel(int number, IReadOnlyList<SeatCellViewModel> seats)
    {
        Number = number;
        Seats = seats;
    }

    public int Number { get; }
    public IReadOnlyList<SeatCellViewModel> Seats { get; }

    /// <summary>行首整排拖动把手：只在第一组显示，避免每组重复。</summary>
    [ObservableProperty] private bool _showHandle;

    public string AutomationName => $"第{Number}排 整排调整把手";
}

public partial class ColumnHandleViewModel : ObservableObject
{
    public ColumnHandleViewModel(int group, int column)
    {
        Group = group;
        Column = column;
    }

    /// <summary>0 基物理组号。</summary>
    public int Group { get; }

    /// <summary>0 基组内列号。</summary>
    public int Column { get; }

    [ObservableProperty] private double _width = 68;

    public string Label => $"第{Group + 1}组 · 第{Column + 1}列";
    public string AutomationName => $"{Label} 整列调整把手";
}
