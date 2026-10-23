using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SeatGrid.App;

public partial class GroupViewModel : ObservableObject
{
    public GroupViewModel(int number, string label, IReadOnlyList<RowViewModel> rows)
    {
        Number = number;
        Label = label;
        Rows = rows;
    }

    public int Number { get; }
    public string Label { get; }
    public IReadOnlyList<RowViewModel> Rows { get; }

    [ObservableProperty] private IBrush _labelBrush = Brushes.Gray;
    [ObservableProperty] private bool _showHints = true;
    [ObservableProperty] private Thickness _labelMargin = new(0, 0, 0, 5);
}

public partial class RowViewModel : ObservableObject
{
    public RowViewModel(int number, string label, IReadOnlyList<SeatCellViewModel> seats)
    {
        Number = number;
        Label = label;
        Seats = seats;
    }

    public int Number { get; }
    public string Label { get; }
    public IReadOnlyList<SeatCellViewModel> Seats { get; }

    [ObservableProperty] private IBrush _labelBrush = Brushes.Gray;
    [ObservableProperty] private bool _showHints = true;
    [ObservableProperty] private bool _showRowLabel = true;
}
