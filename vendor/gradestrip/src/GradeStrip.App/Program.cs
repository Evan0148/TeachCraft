using Avalonia;
using Avalonia.Media;
using System;

namespace GradeStrip.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 确保跨平台（macOS / Linux）完整支持 GBK/CP936 等旧版 Excel (.xls) 编码
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
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
                new FontFallback { FontFamily = new FontFamily(".AppleSystemUIFont") },
                new FontFallback { FontFamily = new FontFamily("Helvetica Neue") },
                new FontFallback { FontFamily = new FontFamily("Songti SC") },
                new FontFallback { FontFamily = new FontFamily("STSong") },
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
