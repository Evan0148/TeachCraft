using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassBlock.Core.Models;

namespace ClassBlock.Core.Timetable;

public static class TimetableTemplateIO
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(TimetableTemplate template) =>
        JsonSerializer.Serialize(template, JsonOptions);

    public static TimetableTemplate FromJson(string json)
    {
        try
        {
            var template = JsonSerializer.Deserialize<TimetableTemplate>(json, JsonOptions)
                ?? throw new ClassBlock.Core.ClassBlockException("模板内容为空，无法读取。");

            if (template.Rows.Count == 0)
                throw new ClassBlock.Core.ClassBlockException("模板里没有任何行，无法使用。");

            return template;
        }
        catch (ClassBlock.Core.ClassBlockException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ClassBlock.Core.ClassBlockException($"模板文件格式不正确：{ex.Message}", ex);
        }
    }

    // ----------------------------- 自定义模板目录 -----------------------------

    public static string GetCustomDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TeachCraft", "ClassBlock", "templates");

    public static (IReadOnlyList<TimetableTemplate> Templates, IReadOnlyList<string> Warnings) LoadCustomTemplates(
        string? directory = null)
    {
        var dir = directory ?? GetCustomDirectory();
        var templates = new List<TimetableTemplate>();
        var warnings = new List<string>();

        if (!Directory.Exists(dir)) return (templates, warnings);

        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                templates.Add(FromJson(File.ReadAllText(file)));
            }
            catch (ClassBlock.Core.ClassBlockException)
            {
                warnings.Add($"自定义模板 {Path.GetFileNameWithoutExtension(file)} 无法读取，已忽略。");
            }
        }

        return (templates, warnings);
    }

    public static string SaveCustom(TimetableTemplate template, string? directory = null)
    {
        if (string.IsNullOrWhiteSpace(template.Id))
            throw new ClassBlock.Core.ClassBlockException("自定义模板缺少编号，无法保存。");

        var dir = directory ?? GetCustomDirectory();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{template.Id}.json");
        File.WriteAllText(path, ToJson(template));
        return path;
    }

    public static void DeleteCustom(string id, string? directory = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        var path = Path.Combine(directory ?? GetCustomDirectory(), $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
    }
}

/// <summary>每个年级最近一次使用的模板编号，随窗口关闭前后的选择保存。</summary>
public sealed class ClassBlockPrefs
{
    public Dictionary<string, string> LastTemplateByGrade { get; set; } = new();
}

public static class ClassBlockPrefsStore
{
    private static string GetFilePath(string? directory) => Path.Combine(
        directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TeachCraft", "ClassBlock"),
        "settings.json");

    public static ClassBlockPrefs Load(string? directory = null)
    {
        var path = GetFilePath(directory);
        if (!File.Exists(path)) return new ClassBlockPrefs();

        try
        {
            return JsonSerializer.Deserialize<ClassBlockPrefs>(File.ReadAllText(path)) ?? new ClassBlockPrefs();
        }
        catch
        {
            return new ClassBlockPrefs();
        }
    }

    public static void Save(ClassBlockPrefs prefs, string? directory = null)
    {
        var path = GetFilePath(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(prefs));
    }
}
