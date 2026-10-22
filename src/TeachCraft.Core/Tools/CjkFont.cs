using QuestPDF.Drawing;

namespace TeachCraft.Core.Tools;

/// <summary>
/// Word 转 PDF 需要一款带中日韩字形的字体。优先注册系统里的中文字体，
/// 避免 QuestPDF 2026 默认不加载系统字体导致中文变成方框。
/// </summary>
public static class CjkFont
{
    private static readonly Lazy<string> LazyFamily = new(Discover, isThreadSafe: true);

    public static string Family => LazyFamily.Value;

    private static string Discover()
    {
        foreach (var (path, family) in Candidates())
        {
            try
            {
                if (!File.Exists(path)) continue;
                FontManager.RegisterFontFromFile(path);
                return family;
            }
            catch
            {
                // 字体文件不可用（例如 TTC 不被支持）时继续尝试下一款。
            }
        }

        return "Lato";
    }

    private static IEnumerable<(string Path, string Family)> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(fonts)) fonts = @"C:\Windows";
            fonts = Path.Combine(fonts, "Fonts");

            yield return (Path.Combine(fonts, "Deng.ttf"), "DengXian");
            yield return (Path.Combine(fonts, "simhei.ttf"), "SimHei");
            yield return (Path.Combine(fonts, "msyh.ttc"), "Microsoft YaHei");
            yield return (Path.Combine(fonts, "simsun.ttc"), "SimSun");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return ("/System/Library/Fonts/PingFang.ttc", "PingFang SC");
            yield return ("/System/Library/Fonts/Hiragino Sans GB.ttc", "Hiragino Sans GB");
            yield return ("/System/Library/Fonts/Supplemental/Songti.ttc", "Songti SC");
        }
        else
        {
            yield return ("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", "Noto Sans CJK SC");
            yield return ("/usr/share/fonts/truetype/wqy/wqy-microhei.ttc", "WenQuanYi Micro Hei");
            yield return ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "DejaVu Sans");
        }
    }
}
