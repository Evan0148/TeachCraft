using System;
using System.IO;

namespace GradeWeave.App;

/// <summary>
/// 参考校样式的显示名称只存本机，不写进仓库、工程文件或导出内容。
/// 默认使用中性名称；老师可在导出弹窗里改成自己学校的样式名。
/// </summary>
internal static class ExportStylePreferences
{
    public const string DefaultName = "参考校样式";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TeachCraft", "GradeWeave", "export-style.txt");

    public static string LoadName()
    {
        try
        {
            if (!File.Exists(FilePath)) return DefaultName;
            var text = File.ReadAllText(FilePath).Trim();
            return text.Length == 0 ? DefaultName : text;
        }
        catch
        {
            return DefaultName;
        }
    }

    public static void SaveName(string? name)
    {
        try
        {
            var text = (name ?? string.Empty).Trim();
            if (text.Length == 0) text = DefaultName;
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, text);
        }
        catch
        {
            // 本机配置写不进去不影响导出，静默回退默认名称。
        }
    }
}
