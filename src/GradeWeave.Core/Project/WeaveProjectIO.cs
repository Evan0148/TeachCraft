using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GradeWeave.Core.Models;
using GradeWeave.Core.Timetable;

namespace GradeWeave.Core.Project;

/// <summary>工程文件只保存本机数据，扩展名 .weave.json，便于老师备份与换机继续。</summary>
public static class WeaveProjectIO
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>把工程序列化为可读 JSON；自动保存先在 UI 线程取快照，再由后台写入。</summary>
    public static string Serialize(WeaveProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return JsonSerializer.Serialize(project, Options);
    }

    public static WeaveProject Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new GradeWeaveException("工程文件内容为空，无法读取。");

        try
        {
            var project = JsonSerializer.Deserialize<WeaveProject>(json, Options)
                          ?? throw new GradeWeaveException("工程文件内容为空，无法读取。");

            // 旧工程没有作息时间表：按现有节次还原一份，老师确认后才允许重新生成。
            TimetableService.Ensure(project);
            return project;
        }
        catch (JsonException ex)
        {
            throw new GradeWeaveException("工程文件不是有效的织序工程，可能已被其他程序改写。", ex);
        }
    }

    public static void Save(WeaveProject project, string path) => SaveJson(Serialize(project), path);

    /// <summary>原子写入：先写同目录临时文件，再替换目标，避免中途断电留下半截工程。</summary>
    public static void SaveJson(string json, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new GradeWeaveException("未指定保存路径。");

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    public static WeaveProject Load(string path)
    {
        if (!File.Exists(path))
            throw new GradeWeaveException("找不到这个织序工程文件。");

        return Deserialize(File.ReadAllText(path));
    }
}
