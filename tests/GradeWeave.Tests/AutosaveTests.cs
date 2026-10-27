using GradeWeave.Core.Models;
using GradeWeave.Core.Project;

namespace GradeWeave.Tests;

/// <summary>自动保存回归：工程路径、恢复文件、元数据、清理与损坏文件。</summary>
public sealed class AutosaveTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "GradeWeave.Autosave", Guid.NewGuid().ToString("N"));

    public AutosaveTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void SaveWithoutProjectPath_WritesRecoveryAndMetadata()
    {
        var project = WeaveSamples.CreateDemo();
        var json = WeaveProjectIO.Serialize(project);
        var savedAt = new DateTime(2026, 10, 26, 14, 32, 10);
        var info = WeaveAutosaveInfo.FromProject(project, null, savedAt);
        var store = new WeaveAutosaveStore(_directory);

        var result = store.SaveJson(json, null, info);

        Assert.True(result.Success, result.Warning);
        Assert.Equal(WeaveAutosaveTarget.RecoveryFile, result.Target);
        Assert.True(File.Exists(store.RecoveryPath));
        Assert.True(File.Exists(store.InfoPath));

        var metadata = store.TryGetInfo();
        Assert.NotNull(metadata);
        Assert.Equal(savedAt, metadata!.SavedAt);
        Assert.Equal("九年级", metadata.GradeName);
        Assert.Equal(4, metadata.ClassCount);
        Assert.True(metadata.TimetableConfirmed);

        Assert.True(store.TryLoad(out var loaded, out var loadedInfo));
        Assert.NotNull(loaded);
        Assert.Equal(project.Classes.Count, loaded!.Classes.Count);
        Assert.Equal(project.Lessons.Count, loaded.Lessons.Count);
        Assert.Equal(savedAt, loadedInfo!.SavedAt);
    }

    [Fact]
    public void SaveWithProjectPath_WritesProjectFileOnly()
    {
        var project = WeaveSamples.CreateDemo();
        var json = WeaveProjectIO.Serialize(project);
        var projectPath = Path.Combine(_directory, "saved.weave.json");
        var store = new WeaveAutosaveStore(_directory);

        var result = store.SaveJson(
            json, projectPath, WeaveAutosaveInfo.FromProject(project, projectPath));

        Assert.True(result.Success, result.Warning);
        Assert.Equal(WeaveAutosaveTarget.ProjectFile, result.Target);
        Assert.True(File.Exists(projectPath));
        Assert.False(File.Exists(store.RecoveryPath));
        Assert.False(File.Exists(store.InfoPath));
        Assert.Equal(project.Classes.Count, WeaveProjectIO.Load(projectPath).Classes.Count);
    }

    [Fact]
    public void SaveWithUnwritableProjectPath_FallsBackToRecovery()
    {
        var project = WeaveSamples.CreateDemo();
        var json = WeaveProjectIO.Serialize(project);
        var directoryPath = Path.Combine(_directory, "not-a-file.weave.json");
        Directory.CreateDirectory(directoryPath);
        var store = new WeaveAutosaveStore(_directory);

        var result = store.SaveJson(
            json, directoryPath, WeaveAutosaveInfo.FromProject(project, directoryPath));

        Assert.True(result.Success, result.Warning);
        Assert.Equal(WeaveAutosaveTarget.RecoveryFile, result.Target);
        Assert.False(string.IsNullOrWhiteSpace(result.Warning));
        Assert.True(File.Exists(store.RecoveryPath));
        Assert.True(File.Exists(store.InfoPath));
    }

    [Fact]
    public void SaveTwice_ReplacesProjectFileAtomically()
    {
        var project = WeaveSamples.CreateDemo();
        project.SchoolName = "第一次";
        var path = Path.Combine(_directory, "atomic.weave.json");
        WeaveProjectIO.Save(project, path);

        project.SchoolName = "第二次";
        WeaveProjectIO.Save(project, path);

        Assert.Equal("第二次", WeaveProjectIO.Load(path).SchoolName);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(_directory),
            file => Path.GetFileName(file).Contains(".tmp-", StringComparison.Ordinal));
    }

    [Fact]
    public void Clear_RemovesRecoveryAndMetadata()
    {
        var project = WeaveSamples.CreateDemo();
        var store = new WeaveAutosaveStore(_directory);
        store.SaveJson(
            WeaveProjectIO.Serialize(project),
            null,
            WeaveAutosaveInfo.FromProject(project, null));

        store.Clear();

        Assert.False(File.Exists(store.RecoveryPath));
        Assert.False(File.Exists(store.InfoPath));
        Assert.False(store.TryLoad(out _, out _));
    }

    [Fact]
    public void CorruptRecovery_FailsWithoutThrowing()
    {
        var project = WeaveSamples.CreateDemo();
        var store = new WeaveAutosaveStore(_directory);
        store.SaveJson(
            WeaveProjectIO.Serialize(project),
            null,
            WeaveAutosaveInfo.FromProject(project, null));

        File.WriteAllText(store.RecoveryPath, "{ this is not a weave project");

        Assert.False(store.TryLoad(out var loaded, out var info));
        Assert.Null(loaded);
        Assert.Null(info);
    }
}
