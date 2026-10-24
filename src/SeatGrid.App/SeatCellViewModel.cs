using System;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SeatGrid.Core.Models;

namespace SeatGrid.App;

public partial class SeatCellViewModel : ObservableObject
{
    public SeatCellViewModel(SeatPosition position)
    {
        Position = position;
    }

    public SeatPosition Position { get; }

    public string PositionLabel => $"{Position.Group + 1}组 · {Position.Row + 1}排 · {Position.Column + 1}列";

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private bool _isFemale;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _showEmptyFrame;
    [ObservableProperty] private double _width = 80;
    [ObservableProperty] private double _height = 80;
    [ObservableProperty] private double _fontSize = 14;
    [ObservableProperty] private FontWeight _fontWeight = FontWeight.Normal;
    [ObservableProperty] private IBrush _fill = Brushes.White;
    [ObservableProperty] private IBrush _stroke = Brushes.Gray;
    [ObservableProperty] private IBrush _nameBrush = Brushes.Black;
    [ObservableProperty] private Thickness _borderThickness = new(1);
    [ObservableProperty] private string _automationName = string.Empty;

    private bool _emptyFrameWhenIdle;
    private bool _isDropTarget;
    private IBrush _strokeBeforeDrop = Brushes.Gray;
    private Thickness _borderBeforeDrop = new(1);

    private static readonly IBrush DropStrokeBrush = new SolidColorBrush(Color.Parse("#D97757"));

    public void Update(
        string displayName,
        bool isEmpty,
        bool isFemale,
        bool isSelected,
        double size,
        double fontSize,
        bool bold,
        IBrush seatBackground,
        IBrush emptyBackground,
        IBrush nameColor,
        IBrush femaleColor,
        IBrush border,
        bool hideEmptySeats)
    {
        DisplayName = displayName;
        IsEmpty = isEmpty;
        IsFemale = isFemale;
        IsSelected = isSelected;
        Width = size;
        Height = size;
        FontSize = fontSize;
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal;
        Fill = isEmpty ? emptyBackground : seatBackground;
        Stroke = border;
        NameBrush = isFemale ? femaleColor : nameColor;
        BorderThickness = isEmpty ? new Thickness(0) : new Thickness(1);
        _emptyFrameWhenIdle = isEmpty && !hideEmptySeats;
        ShowEmptyFrame = _emptyFrameWhenIdle && !isSelected;
        _isDropTarget = false;
        AutomationName = isEmpty
            ? $"空座位 {PositionLabel}"
            : $"{PositionLabel} {displayName}";
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        ShowEmptyFrame = _emptyFrameWhenIdle && !selected;
    }

    /// <summary>拖拽悬停时临时换上强调色边框，松手或移走后由下一次 Update 还原。</summary>
    public void SetDropTarget(bool on)
    {
        if (on == _isDropTarget) return;
        _isDropTarget = on;
        if (on)
        {
            _strokeBeforeDrop = Stroke;
            _borderBeforeDrop = BorderThickness;
            Stroke = DropStrokeBrush;
            BorderThickness = new Thickness(2.2);
        }
        else
        {
            Stroke = _strokeBeforeDrop;
            BorderThickness = _borderBeforeDrop;
        }
    }
}
