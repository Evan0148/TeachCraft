using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SeatGrid.App.Controls;

/// <summary>一个会弹出纸色色板的颜色块，比完整 ColorPicker 更贴合工具箱的视觉。</summary>
public sealed class ColorSwatchButton : Button
{
    private static readonly string[] Palette =
    {
        "#FFFFFF", "#FAF9F5", "#F4F2EC", "#F1EEE5",
        "#E7E3D8", "#C9C1AE", "#85806F", "#3D3B36",
        "#141413", "#000000", "#D97757", "#C6613F",
        "#70AD47", "#C6E0B4", "#DDEBF7", "#FFF2CC",
    };

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorSwatchButton, Color>(nameof(Color), Colors.White);

    private static readonly IBrush HoverRingBrush = new SolidColorBrush(Color.Parse("#D97757"));

    private readonly Border _swatch;
    private readonly Border _ring;

    public ColorSwatchButton()
    {
        Height = 28;
        MinWidth = 0;
        MinHeight = 0;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Padding = new Thickness(0);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Cursor = new Cursor(StandardCursorType.Hand);

        _swatch = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(Color.Parse("#B7AF9C")),
            BorderThickness = new Thickness(1),
        };

        // 外侧 hover 描边环，避免白底色块在白卡上看不清边界
        _ring = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(2),
            Child = _swatch,
        };

        Content = _ring;
        UpdateSwatch();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == ColorProperty) UpdateSwatch();
        };
        Click += (_, _) => ShowPalette();
        PointerEntered += (_, _) => _ring.BorderBrush = HoverRingBrush;
        PointerExited += (_, _) => _ring.BorderBrush = Brushes.Transparent;
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private void UpdateSwatch()
    {
        _swatch.Background = new SolidColorBrush(Color);
        ToolTip.SetTip(this, $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}");
    }

    private void ShowPalette()
    {
        var panel = new WrapPanel
        {
            Width = 184,
            Orientation = Orientation.Horizontal,
        };

        var flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#FAF9F5")),
                BorderBrush = new SolidColorBrush(Color.Parse("#E7E3D8")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Child = panel,
            },
        };

        foreach (var hex in Palette)
        {
            var color = Color.Parse(hex);
            var button = new Button
            {
                Width = 34,
                Height = 30,
                Margin = new Thickness(3),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.Parse("#D6D1C4")),
                BorderThickness = new Thickness(1),
                Tag = hex,
            };
            ToolTip.SetTip(button, hex);
            button.Click += (_, _) =>
            {
                Color = color;
                flyout.Hide();
            };
            panel.Children.Add(button);
        }

        flyout.ShowAt(this);
    }
}
