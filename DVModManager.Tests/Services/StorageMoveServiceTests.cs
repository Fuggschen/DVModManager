using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class StorageMoveServiceTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "StorageMove_" + Guid.NewGuid().ToString("N"));
    private readonly string _configDir;
    private readonly string _oldStorage;
    private readonly StorageMoveService _service;

    public StorageMoveServiceTests()
    {
        _configDir = Path.Combine(_workDir, "config");
        _oldStorage = Path.Combine(_workDir, "old");
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(_oldStorage);
        _service = new StorageMoveService(NullLogger<StorageMoveService>.Instance, _configDir);
    }

    public void Dispose() => TestData.DeleteDirectory(_workDir);

    private void CreateStorageData(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "versions", "TestMod"));
        File.WriteAllText(Path.Combine(root, "versions", "TestMod", "1.0.0.zip"), "zip");
        File.WriteAllText(Path.Combine(root, "versions", "TestMod", "versions.json"), "[]");
        Directory.CreateDirectory(Path.Combine(root, "downloads", "umm"));
        File.WriteAllText(Path.Combine(root, "downloads", "umm", "umm-0.33.2.zip"), "umm");
        Directory.CreateDirectory(Path.Combine(root, "profiles"));
        File.WriteAllText(Path.Combine(root, "profiles", "Default.json"), "{}");
    }

    [Fact]
    public void Check_NewFolderThatDoesNotExist_IsOk()
    {
        Assert.Equal(StorageMoveCheck.Ok, _service.Check(_oldStorage, Path.Combine(_workDir, "new")));
    }

    [Fact]
    public void Check_EmptyExistingFolder_IsOk()
    {
        var target = Path.Combine(_workDir, "new");
        Directory.CreateDirectory(target);

        Assert.Equal(StorageMoveCheck.Ok, _service.Check(_oldStorage, target));
    }

    [Fact]
    public void Check_FolderWithFiles_IsNotEmpty()
    {
        var target = Path.Combine(_workDir, "new");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "something.txt"), "x");

        Assert.Equal(StorageMoveCheck.NotEmpty, _service.Check(_oldStorage, target));
    }

    [Fact]
    public void Check_SamePathWithTrailingSeparator_IsUnchanged()
    {
        Assert.Equal(StorageMoveCheck.Unchanged,
            _service.Check(_oldStorage, _oldStorage + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Check_RelativePath_IsInvalid()
    {
        Assert.Equal(StorageMoveCheck.InvalidPath, _service.Check(_oldStorage, "relative/storage"));
    }

    [Fact]
    public void Check_FolderInsideMovedData_IsRejected()
    {
        Assert.Equal(StorageMoveCheck.InsideMovedData,
            _service.Check(_oldStorage, Path.Combine(_oldStorage, "versions", "sub")));
    }

    [Fact]
    public void Check_ConfigDirectoryWithOnlySettingsAndLogs_IsOk()
    {
        // Moving back to the default location, which always holds settings.json and logs
        File.WriteAllText(Path.Combine(_configDir, "settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_configDir, "logs"));

        Assert.Equal(StorageMoveCheck.Ok, _service.Check(_oldStorage, _configDir));
    }

    [Fact]
    public void Check_ConfigDirectoryWithStorageData_IsNotEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_configDir, "versions"));

        Assert.Equal(StorageMoveCheck.NotEmpty, _service.Check(_oldStorage, _configDir));
    }

    [Fact]
    public async Task MoveAsync_MovesAllStorageFoldersAndRemovesThemFromOldPath()
    {
        CreateStorageData(_oldStorage);
        File.WriteAllText(Path.Combine(_oldStorage, "settings.json"), "{}");
        var target = Path.Combine(_workDir, "new");

        var result = await _service.MoveAsync(_oldStorage, target);

        Assert.Equal(StorageMoveResult.Moved, result);
        Assert.Equal("zip", File.ReadAllText(Path.Combine(target, "versions", "TestMod", "1.0.0.zip")));
        Assert.Equal("umm", File.ReadAllText(Path.Combine(target, "downloads", "umm", "umm-0.33.2.zip")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "profiles", "Default.json")));
        Assert.False(Directory.Exists(Path.Combine(_oldStorage, "versions")));
        Assert.False(Directory.Exists(Path.Combine(_oldStorage, "downloads")));
        Assert.False(Directory.Exists(Path.Combine(_oldStorage, "profiles")));
        // Files that aren't storage data stay where they are
        Assert.True(File.Exists(Path.Combine(_oldStorage, "settings.json")));
    }

    [Fact]
    public async Task MoveAsync_ReportsProgressUpToComplete()
    {
        CreateStorageData(_oldStorage);
        var reports = new List<double>();

        await _service.MoveAsync(_oldStorage, Path.Combine(_workDir, "new"), new SyncProgress(reports.Add));

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1]);
    }

    [Fact]
    public async Task MoveAsync_CopyFails_KeepsOldDataAndRemovesPartialCopy()
    {
        // Exclusive file locks are only enforced on Windows
        if (!OperatingSystem.IsWindows()) return;

        CreateStorageData(_oldStorage);
        var target = Path.Combine(_workDir, "new");
        var locked = Path.Combine(_oldStorage, "profiles", "Default.json");

        StorageMoveResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            result = await _service.MoveAsync(_oldStorage, target);

        Assert.Equal(StorageMoveResult.Failed, result);
        Assert.True(File.Exists(Path.Combine(_oldStorage, "versions", "TestMod", "1.0.0.zip")));
        Assert.True(File.Exists(locked));
        Assert.False(Directory.Exists(Path.Combine(target, "versions")));
        Assert.False(Directory.Exists(Path.Combine(target, "profiles")));
    }

    [Fact]
    public async Task MoveAsync_NoStorageData_Succeeds()
    {
        var result = await _service.MoveAsync(_oldStorage, Path.Combine(_workDir, "new"));

        Assert.Equal(StorageMoveResult.Moved, result);
    }

    /// <summary>Progress&lt;T&gt; posts to the sync context; this reports inline so the test sees every value.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
