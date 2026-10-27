using System.Text.Encodings.Web;
using System.Text.Json;
using GradeWeave.Core.Models;

namespace GradeWeave.Core.Project;

/// <summary>自动保存的恢复文件信息；恢复界面只展示时间、年级与班级数，不展示学校名。</summary>
public sealed class WeaveAutosaveInfo
{
    public DateTime SavedAt { get; set; }
    public string ProjectPath { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public string GradeName { get; set; } = string.Empty;
    public int ClassCount { get; set; }
    public int LessonCount { get; set; }
    public bool TimetableConfirmed { get; set; }

    public static WeaveAutosaveInfo FromProject(
        WeaveProject project, string? projectPath, DateTime? savedAt = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new WeaveAutosaveInfo
        {
            SavedAt = savedAt ?? DateTime.Now,
            ProjectPath = projectPath ?? string.Empty,
            SourceFile = project.SourceFile ?? string.Empty,
            GradeName = project.GradeName ?? string.Empty,
            ClassCount = project.Classes.Count,
            LessonCount = project.Lessons.Count,
            TimetableConfirmed = project.Timetable?.IsConfirmed == true,
        };
    }
}

public enum WeaveAutosaveTarget
{
    ProjectFile,
    RecoveryFile,
}

public sealed record WeaveAutosaveResult(
    bool Success,
    WeaveAutosaveTarget Target,
    string Path,
    string? Warning,
    bool Skipped = false);

/// <summary>
/// 自动保存落盘。有当前工程文件时原子写回该文件；没有路径或工程文件不可写时，
/// 写本机恢复文件与元数据，下次启动可恢复。所有文件都在本机，不上传。
/// </summary>
public sealed class WeaveAutosaveStore
{
    private static readonly JsonSerializerOptions MetadataOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private string _sessionId = string.Empty;
    private long _lastWriteVersion = -1;

    public WeaveAutosaveStore(string? rootDirectory = null)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : rootDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory)) baseDirectory = Path.GetTempPath();

        RootDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(baseDirectory, "TeachCraft", "GradeWeave")
                : baseDirectory);
        RecoveryPath = Path.Combine(RootDirectory, "autosave.weave.json");
        InfoPath = Path.Combine(RootDirectory, "autosave.meta.json");
    }

    public string RootDirectory { get; }
    public string RecoveryPath { get; }
    public string InfoPath { get; }

    /// <summary>手动保存也走同一把锁，避免与后台自动保存同时写同一个工程文件。</summary>
    public void SaveProjectFile(string json, string path)
    {
        lock (_gate)
        {
            WeaveProjectIO.SaveJson(json, path);
        }
    }

    /// <summary>开始一份自动保存会话；旧会话的在途写入会被拒绝，避免新工程被旧快照覆盖。</summary>
    public void BeginSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;

        lock (_gate)
        {
            _sessionId = sessionId;
            _lastWriteVersion = -1;
        }
    }

    /// <summary>写入自动保存快照；失败不抛异常，由调用方展示状态。</summary>
    public WeaveAutosaveResult SaveJson(string json, string? projectPath, WeaveAutosaveInfo info)
        => SaveJsonCore(json, projectPath, info, version: 0, sessionId: null);

    /// <summary>带版本守卫的写入：旧会话或旧版本快照直接跳过，不覆盖更新的内容。</summary>
    public WeaveAutosaveResult SaveJson(
        string json, string? projectPath, WeaveAutosaveInfo info, long version, string sessionId)
        => SaveJsonCore(json, projectPath, info, version, sessionId);

    private WeaveAutosaveResult SaveJsonCore(
        string json, string? projectPath, WeaveAutosaveInfo info, long version, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new WeaveAutosaveResult(false, WeaveAutosaveTarget.RecoveryFile, RecoveryPath, "自动保存内容为空。");

        lock (_gate)
        {
            if (sessionId is not null &&
                (!string.Equals(sessionId, _sessionId, StringComparison.Ordinal) || version < _lastWriteVersion))
            {
                return new WeaveAutosaveResult(
                    true, WeaveAutosaveTarget.RecoveryFile, RecoveryPath, null, Skipped: true);
            }

            string? warning = null;
            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                try
                {
                    WeaveProjectIO.SaveJson(json, projectPath);
                    RememberVersion(sessionId, version);
                    return new WeaveAutosaveResult(
                        true, WeaveAutosaveTarget.ProjectFile, Path.GetFullPath(projectPath), null);
                }
                catch (Exception ex) when (IsFileFailure(ex))
                {
                    warning = $"当前工程文件无法写入，已改存恢复文件：{ex.Message}";
                }
            }

            try
            {
                WeaveProjectIO.SaveJson(json, RecoveryPath);
                WeaveProjectIO.SaveJson(JsonSerializer.Serialize(info, MetadataOptions), InfoPath);
                RememberVersion(sessionId, version);
                return new WeaveAutosaveResult(
                    true, WeaveAutosaveTarget.RecoveryFile, RecoveryPath, warning);
            }
            catch (Exception ex) when (IsFileFailure(ex))
            {
                return new WeaveAutosaveResult(
                    false, WeaveAutosaveTarget.RecoveryFile, RecoveryPath, warning ?? ex.Message);
            }
        }
    }

    private void RememberVersion(string? sessionId, long version)
    {
        if (sessionId is not null && string.Equals(sessionId, _sessionId, StringComparison.Ordinal))
            _lastWriteVersion = Math.Max(_lastWriteVersion, version);
    }

    public WeaveAutosaveInfo? TryGetInfo()
    {
        lock (_gate)
        {
            return TryGetInfoUnlocked();
        }
    }

    public bool TryLoad(out WeaveProject? project, out WeaveAutosaveInfo? info)
    {
        lock (_gate)
        {
            project = null;
            info = TryGetInfoUnlocked();
            if (info is null) return false;

            try
            {
                project = WeaveProjectIO.Load(RecoveryPath);
                return true;
            }
            catch (Exception ex) when (IsFileFailure(ex))
            {
                project = null;
                info = null;
                return false;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var path in new[] { RecoveryPath, InfoPath })
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
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

    private WeaveAutosaveInfo? TryGetInfoUnlocked()
    {
        try
        {
            if (!File.Exists(RecoveryPath) || !File.Exists(InfoPath)) return null;

            var info = JsonSerializer.Deserialize<WeaveAutosaveInfo>(
                File.ReadAllText(InfoPath), MetadataOptions);
            return info is not null && info.SavedAt != default ? info : null;
        }
        catch (Exception ex) when (IsFileFailure(ex))
        {
            return null;
        }
    }

    private static bool IsFileFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or JsonException or GradeWeaveException;
}
