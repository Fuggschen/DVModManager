using System.Collections.Concurrent;
using DVModManager.Models;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class ModDiscoveryServiceTests
{
    private readonly ModDiscoveryService _service;

    public ModDiscoveryServiceTests()
    {
        _service = new ModDiscoveryService(NullLogger<ModDiscoveryService>.Instance);
    }

    [Fact]
    public async Task ScanAllModsAsync_DiscoversActiveAndInactiveMods()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());
        var activeModsDir = Path.Combine(tempGamePath, "Mods");
        var inactiveModsDir = Path.Combine(tempGamePath, "Mods.inactive");

        Directory.CreateDirectory(Path.Combine(activeModsDir, "ModA"));
        File.WriteAllText(Path.Combine(activeModsDir, "ModA", "Info.json"),
            """{"Id": "ModA", "DisplayName": "Mod A", "Version": "1.0.0"}""");

        Directory.CreateDirectory(Path.Combine(inactiveModsDir, "ModB"));
        File.WriteAllText(Path.Combine(inactiveModsDir, "ModB", "Info.json"),
            """{"Id": "ModB", "DisplayName": "Mod B", "Version": "2.0.0"}""");

        // Staging folder that should be ignored
        Directory.CreateDirectory(Path.Combine(activeModsDir, ".staging"));

        try
        {
            var mods = await _service.ScanAllModsAsync(tempGamePath);

            Assert.Equal(2, mods.Count);

            var modA = Assert.Single(mods, m => m.Id == "ModA");
            Assert.True(modA.IsActive);
            Assert.Equal(ModState.Active, modA.State);
            Assert.True(modA.HasMetadata);

            var modB = Assert.Single(mods, m => m.Id == "ModB");
            Assert.False(modB.IsActive);
            Assert.Equal(ModState.Inactive, modB.State);
            Assert.True(modB.HasMetadata);
        }
        finally
        {
            if (Directory.Exists(tempGamePath)) Directory.Delete(tempGamePath, true);
        }
    }

    [Fact]
    public async Task ParseModInfoAsync_WhenInfoJsonMissing_SetsNoMetadata()
    {
        var tempModDir = Path.Combine(Path.GetTempPath(), "OrphanMod_" + Guid.NewGuid());
        Directory.CreateDirectory(tempModDir);

        try
        {
            var modInfo = await _service.ParseModInfoAsync(tempModDir, isActive: true);

            Assert.NotNull(modInfo);
            Assert.False(modInfo.HasMetadata);
            Assert.Equal(ModState.NoMetadata, modInfo.State);
            Assert.Equal(Path.GetFileName(tempModDir), modInfo.Id);
        }
        finally
        {
            if (Directory.Exists(tempModDir)) Directory.Delete(tempModDir, true);
        }
    }

    [Fact]
    public async Task ParseModInfoAsync_WhenInfoJsonCorrupt_FallsBackGracefully()
    {
        var tempModDir = Path.Combine(Path.GetTempPath(), "CorruptMod_" + Guid.NewGuid());
        Directory.CreateDirectory(tempModDir);
        File.WriteAllText(Path.Combine(tempModDir, "Info.json"), "{ invalid json ");

        try
        {
            var modInfo = await _service.ParseModInfoAsync(tempModDir, isActive: false);

            Assert.NotNull(modInfo);
            Assert.False(modInfo.HasMetadata);
            Assert.Equal(ModState.NoMetadata, modInfo.State);
        }
        finally
        {
            if (Directory.Exists(tempModDir)) Directory.Delete(tempModDir, true);
        }
    }

    [Fact]
    public async Task OnFileSystemChange_RapidChanges_DebouncesToSingleModsChanged()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Debounce_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var fires = 0;
        _service.ModsChanged += (_, _) => Interlocked.Increment(ref fires);

        try
        {
            for (var i = 0; i < 20; i++)
            {
                _service.OnFileSystemChange(this,
                    new FileSystemEventArgs(WatcherChangeTypes.Created, tempDir, $"mod{i}"));
                await Task.Delay(10);
            }

            // Debounce window is 500ms after the last change; wait with slack.
            await Task.Delay(800);

            Assert.Equal(1, Volatile.Read(ref fires));
        }
        finally
        {
            _service.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task OnFileSystemChange_ConcurrentCallbacks_NeverThrow()
    {
        // Regression for the fatal ObjectDisposedException: FileSystemWatcher
        // callbacks run on thread-pool threads and used to dispose the previous
        // debounce CTS while a concurrent callback was still reading its Token.
        var tempDir = Path.Combine(Path.GetTempPath(), "Race_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var errors = new ConcurrentQueue<Exception>();

        try
        {
            var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    try
                    {
                        _service.OnFileSystemChange(this,
                            new FileSystemEventArgs(WatcherChangeTypes.Created, tempDir, "mod" + i));
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(workers);
            Assert.Empty(errors);
        }
        finally
        {
            _service.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task OnFileSystemChange_RacingDispose_NeverThrows()
    {
        // Shutdown is the other path that used to dispose a CTS another thread
        // was still using (ObjectDisposedException on the watcher's IO thread).
        var tempDir = Path.Combine(Path.GetTempPath(), "DisposeRace_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var errors = new ConcurrentQueue<Exception>();

        try
        {
            var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 2000; i++)
                {
                    try
                    {
                        _service.OnFileSystemChange(this,
                            new FileSystemEventArgs(WatcherChangeTypes.Changed, tempDir, "f" + i));
                    }
                    catch (Exception ex)
                    {
                        errors.Enqueue(ex);
                    }
                }
            })).ToArray();

            var disposeTask = Task.Run(() =>
            {
                try { _service.Dispose(); }
                catch (Exception ex) { errors.Enqueue(ex); }
            });

            await Task.WhenAll(workers.Append(disposeTask));
            Assert.Empty(errors);
        }
        finally
        {
            _service.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Dispose_CancelsPendingDebounce()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PendingDebounce_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var fires = 0;
        _service.ModsChanged += (_, _) => Interlocked.Increment(ref fires);

        try
        {
            _service.OnFileSystemChange(this,
                new FileSystemEventArgs(WatcherChangeTypes.Created, tempDir, "mod"));
            await Task.Delay(50); // well within the 500ms debounce window

            _service.Dispose();
            await Task.Delay(800);

            Assert.Equal(0, Volatile.Read(ref fires));
        }
        finally
        {
            _service.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task StartWatching_ExternalFolderChange_RaisesModsChanged()
    {
        var tempGamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());

        var fires = 0;
        _service.ModsChanged += (_, _) => Interlocked.Increment(ref fires);

        try
        {
            _service.StartWatching(tempGamePath);
            Directory.CreateDirectory(Path.Combine(tempGamePath, "Mods", "NewMod"));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (Volatile.Read(ref fires) == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(50);

            Assert.True(Volatile.Read(ref fires) >= 1,
                "ModsChanged was not raised for an external folder change");
        }
        finally
        {
            _service.Dispose();
            if (Directory.Exists(tempGamePath)) Directory.Delete(tempGamePath, true);
        }
    }
}
