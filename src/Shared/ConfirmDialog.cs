using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TeachCraft.Shared;

/// <summary>
/// 统一确认弹窗：纸墨配色、主操作 IsDefault、取消 IsCancel（Esc 可退）。
/// 用于不可撤销的操作（覆盖、替换、删除已成形的数据），文案必须写清后果与数量。
/// </summary>
internal static class ConfirmDialog
{
    private static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#FAF9F5"));
    private static readonly IBrush Card = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#E7E3D8"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#141413"));
    private static readonly IBrush InkSoft = new SolidColorBrush(Color.Parse("#3D3B36"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6F6A5E"));
    private static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#A34A36"));

    public static async Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        string confirmText,
        string cancelText = "取消",
        bool danger = false)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 404,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Paper,
        };

        var confirm = new Button
        {
            Content = confirmText,
            IsDefault = true,
            Padding = new Thickness(16, 7),
            Background = danger ? Danger : Ink,
            Foreground = Paper,
            BorderThickness = new Thickness(0),
        };
        var cancel = new Button
        {
            Content = cancelText,
            IsCancel = true,
            Padding = new Thickness(16, 7),
            Background = Card,
            Foreground = InkSoft,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
        };

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = Ink,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12.5,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);

        return await dialog.ShowDialog<bool>(owner);
    }
}
