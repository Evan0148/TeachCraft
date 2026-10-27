using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GradeWeave.App;

/// <summary>
/// 编辑窗口关闭前的放弃确认：草稿有改动时先弹中文确认；
/// 「继续编辑」保持窗口（回车 / Esc 都回到编辑），「放弃关闭」才真正关闭。
/// </summary>
public static class EditorCloseGuard
{
    /// <summary>弹出放弃确认；返回 true 表示老师选择放弃改动并关闭。</summary>
    public static async Task<bool> ShowAsync(Window owner, string message)
    {
        var discard = false;

        var keepButton = new Button
        {
            Content = "继续编辑",
            Padding = new Avalonia.Thickness(16, 7),
            FontSize = 11.5,
            IsDefault = true,
            IsCancel = true,
        };
        var discardButton = new Button
        {
            Content = "放弃关闭",
            Padding = new Avalonia.Thickness(14, 7),
            FontSize = 11.5,
        };

        var window = new Window
        {
            Title = "放弃改动？",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.Parse("#FAF9F5")),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24, 20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 12.5,
                        LineHeight = 20,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#3D3B36")),
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { keepButton, discardButton },
                    },
                },
            },
        };

        keepButton.Click += (_, _) => window.Close();
        discardButton.Click += (_, _) =>
        {
            discard = true;
            window.Close();
        };

        await window.ShowDialog<bool?>(owner);
        return discard;
    }
}
