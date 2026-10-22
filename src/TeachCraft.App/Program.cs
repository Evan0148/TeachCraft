using Avalonia;
using Avalonia.Media;
using System;

namespace TeachCraft.App;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 读取旧版 .xls 需要的 GBK 编码。
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        string defaultFont;
        FontFallback[] fallbacks;

        if (OperatingSystem.IsMacOS())
        {
            defaultFont = "PingFang SC";
            fallbacks = new[]
            {
                new FontFallback { FontFamily = new FontFamily("Hiragino Sans GB") },
                new FontFallback { FontFamily = new FontFamily("Songti SC") },
                new FontFallback { FontFamily = new FontFamily("SimSun") },
            };
        }
        else if (OperatingSystem.IsLinux())
        {
            defaultFont = "Noto Sans CJK SC";
            fallbacks = new[]
            {
                new FontFallback { FontFamily = new FontFamily("WenQuanYi Micro Hei") },
                new FontFallback { FontFamily = new FontFamily("DejaVu Sans") },
            };
        }
        else
        {
            defaultFont = "Microsoft YaHei UI";
            fallbacks = new[]
            {
                new FontFallback { FontFamily = new FontFamily("Microsoft YaHei") },
                new FontFallback { FontFamily = new FontFamily("SimSun") },
            };
        }

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = defaultFont,
                FontFallbacks = fallbacks,
            })
            .LogToTrace();
    }
}
