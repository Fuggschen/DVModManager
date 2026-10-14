using System.IO.Compression;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class ManagerUpdateServiceTests : IDisposable
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public StubHttpMessageHandler(byte[] payload) => _payload = payload;

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "ManagerUpdate_" + Guid.NewGuid().ToString("N"));
    private readonly string _installDir;
    private readonly string _exePath;

    public ManagerUpdateServiceTests()
    {
        _installDir = Path.Combine(_workDir, "install");
        Directory.CreateDirectory(_installDir);
        _exePath = Path.Combine(_installDir, ManagerUpdateService.ExecutableName);
        File.WriteAllText(_exePath, "old");
    }

    public void Dispose() => TestData.DeleteDirectory(_workDir);

    private static byte[] CreateZip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }
        return stream.ToArray();
    }

    private ManagerUpdateService CreateService(byte[]? payload = null, Action<string>? startProcess = null,
        StubHttpMessageHandler? handler = null)
    {
        handler ??= new StubHttpMessageHandler(payload ?? []);
        return new ManagerUpdateService(new StubHttpClientFactory(handler),
            NullLogger<ManagerUpdateService>.Instance, _exePath, startProcess ?? (_ => { }));
    }

    private string Stage(params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(_workDir, "update", "staged");
        Directory.CreateDirectory(dir);
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        return dir;
    }

    [Fact]
    public void BuildDownloadUrl_UsesTagFromReleaseUrl()
    {
        var url = ManagerUpdateService.BuildDownloadUrl("0.0.5",
            "https://github.com/Fuggschen/DVModManager/releases/tag/dvmodmanager-v0.0.5", "win-x64");

        Assert.Equal("https://github.com/Fuggschen/DVModManager/releases/download/dvmodmanager-v0.0.5/DVModManager-win-x64.zip", url);
    }

    [Fact]
    public void BuildDownloadUrl_WithoutReleaseUrl_BuildsFromVersion()
    {
        var url = ManagerUpdateService.BuildDownloadUrl("v1.2.0", null, "linux-x64");

        Assert.Equal("https://github.com/Fuggschen/DVModManager/releases/download/dvmodmanager-v1.2.0/DVModManager-linux-x64.zip", url);
    }

    [Fact]
    public void CanSelfUpdate_PublishedExeInWritableFolder_IsTrue()
    {
        Assert.True(CreateService().CanSelfUpdate);
    }

    [Fact]
    public void CanSelfUpdate_DotnetHost_IsFalse()
    {
        var host = Path.Combine(_installDir, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        File.WriteAllText(host, "");
        var service = new ManagerUpdateService(new StubHttpClientFactory(new StubHttpMessageHandler([])),
            NullLogger<ManagerUpdateService>.Instance, host, _ => { });

        Assert.False(service.CanSelfUpdate);
    }

    [Fact]
    public async Task DownloadAndStage_ExtractsOnlyExecutable()
    {
        var handler = new StubHttpMessageHandler(CreateZip(
            (ManagerUpdateService.ExecutableName, "new"), ("DVModManager.pdb", "pdb")));
        var service = CreateService(handler: handler);

        var staged = await service.DownloadAndStageAsync("0.0.5", null);
        try
        {
            Assert.Equal([Path.Combine(staged, ManagerUpdateService.ExecutableName)], Directory.GetFiles(staged));
            Assert.Equal("new", File.ReadAllText(Path.Combine(staged, ManagerUpdateService.ExecutableName)));
            Assert.EndsWith($"/dvmodmanager-v0.0.5/DVModManager-{ManagerUpdateService.RuntimeId}.zip",
                handler.LastRequestUri!.AbsoluteUri);
        }
        finally
        {
            TestData.DeleteDirectory(Path.GetDirectoryName(staged)!);
        }
    }

    [Fact]
    public async Task DownloadAndStage_PackageWithoutExecutable_Throws()
    {
        var service = CreateService(CreateZip(("readme.txt", "x")));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync("0.0.5", null));
    }

    [Fact]
    public void ApplyUpdate_ReplacesExecutableAndKeepsBackup()
    {
        var staged = Stage((ManagerUpdateService.ExecutableName, "new"));

        CreateService().ApplyUpdate(staged);

        Assert.Equal("new", File.ReadAllText(_exePath));
        Assert.Equal("old", File.ReadAllText(_exePath + ManagerUpdateService.OldFileSuffix));
        Assert.False(Directory.Exists(Path.GetDirectoryName(staged)));
    }

    [Fact]
    public void ApplyUpdate_FailureAfterRename_RestoresOriginalExecutable()
    {
        // Staging folder without the executable: the old exe is renamed, then moving the new one in fails
        var staged = Stage();

        Assert.ThrowsAny<IOException>(() => CreateService().ApplyUpdate(staged));

        Assert.Equal("old", File.ReadAllText(_exePath));
        Assert.False(File.Exists(_exePath + ManagerUpdateService.OldFileSuffix));
    }

    [Fact]
    public void StartNewInstance_StartsCurrentExecutablePath()
    {
        string? started = null;
        CreateService(startProcess: p => started = p).StartNewInstance();

        Assert.Equal(_exePath, started);
    }

    [Fact]
    public async Task CleanupOldFiles_DeletesBackups()
    {
        var backup = _exePath + ManagerUpdateService.OldFileSuffix;
        File.WriteAllText(backup, "old");
        var unrelated = Path.Combine(_installDir, "settings.old");
        File.WriteAllText(unrelated, "keep");

        await CreateService().CleanupOldFilesAsync();

        Assert.False(File.Exists(backup));
        Assert.True(File.Exists(unrelated));
    }
}
