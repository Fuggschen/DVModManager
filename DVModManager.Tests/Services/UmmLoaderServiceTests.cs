using DVModManager.Helpers;
using DVModManager.Models;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class UmmLoaderServiceTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "Loader_" + Guid.NewGuid().ToString("N"));
    private readonly UmmPayloadInfo _payload;
    private readonly UmmLoaderService _service;
    private readonly SettingsService _settings;
    private readonly AssemblyPatcher _patcher = new(NullLogger<AssemblyPatcher>.Instance);

    public UmmLoaderServiceTests()
    {
        Directory.CreateDirectory(_workDir);

        // Real payload via the real validation/extraction pipeline (offline).
        var zip = TestData.CreateUmmZip(_workDir);
        _settings = TestData.CreateSettingsService(_workDir);
        _settings.Settings.StoragePath = Path.Combine(_workDir, "storage");
        var payloads = new UmmPayloadService(
            new UmmReleaseFeedService(new StubHttpClientFactory(new StubHttpMessageHandler()),
                NullLogger<UmmReleaseFeedService>.Instance),
            _settings,
            new StubHttpClientFactory(new StubHttpMessageHandler()),
            NullLogger<UmmPayloadService>.Instance);
        _payload = payloads.ImportFromZip(zip)!;
        Assert.NotNull(_payload);

        _service = new UmmLoaderService(
            new StubPayloadService(_payload), _settings, _patcher, NullLogger<UmmLoaderService>.Instance);
    }

    public void Dispose() => TestData.DeleteDirectory(_workDir);

    private sealed class StubPayloadService : IUmmPayloadService
    {
        private readonly UmmPayloadInfo _payload;
        public StubPayloadService(UmmPayloadInfo payload) => _payload = payload;
        public Task<UmmPayloadInfo?> GetPayloadAsync(IProgress<double>? progress = null,
            CancellationToken ct = default) => Task.FromResult<UmmPayloadInfo?>(_payload);
        public UmmPayloadInfo? ImportFromZip(string zipPath) => _payload;
    }

    private string CreateGameWithEntryAssembly()
    {
        var gamePath = TestData.CreateGameDirectory(_workDir);
        // Derail Valley's UMM entry point lives in UnityEngine.CoreModule.dll.
        TestData.CreateDummyEntryAssembly(Path.Combine(gamePath, "DerailValley_Data", "Managed"),
            "UnityEngine.CoreModule.dll", "UnityEngine", "Display", ".cctor");
        return gamePath;
    }

    [Fact]
    public async Task InstallAsync_Doorstop_WritesProxyConfigAndLoaderFiles()
    {
        var gamePath = CreateGameWithEntryAssembly();

        SetMethod("Doorstop");
        Assert.True(await _service.InstallAsync(gamePath));

        Assert.True(DoorstopInstaller.IsInstalled(gamePath));
        var managerPath = UmmLoaderService.GetManagerPath(gamePath);
        Assert.True(File.Exists(Path.Combine(managerPath, "UnityModManager.dll")));
        Assert.True(File.Exists(Path.Combine(managerPath, "Harmony", "2.2", "0Harmony.dll")));
        Assert.True(File.Exists(Path.Combine(managerPath, "Config.xml")));

        var status = _service.GetStatus(gamePath);
        Assert.True(status.IsInstalled);
        Assert.Equal(UmmLoaderMethod.Doorstop, status.ActiveMethod);
        Assert.Equal("0.33.2", status.InstalledVersion);
    }

    [Fact]
    public async Task InstallAsync_Assembly_PatchesEntryAssembly()
    {
        var gamePath = CreateGameWithEntryAssembly();

        SetMethod("Assembly");
        Assert.True(await _service.InstallAsync(gamePath));

        var entryAsm = Path.Combine(gamePath, "DerailValley_Data", "Managed", "UnityEngine.CoreModule.dll");
        Assert.True(_patcher.IsPatched(entryAsm));
        Assert.False(DoorstopInstaller.IsInstalled(gamePath));

        var status = _service.GetStatus(gamePath);
        Assert.Equal(UmmLoaderMethod.Assembly, status.ActiveMethod);
    }

    [Fact]
    public async Task InstallAsync_WritesUmmGameConfig()
    {
        var gamePath = CreateGameWithEntryAssembly();

        SetMethod("Doorstop");
        Assert.True(await _service.InstallAsync(gamePath));

        var config = File.ReadAllText(Path.Combine(UmmLoaderService.GetManagerPath(gamePath), "Config.xml"));
        Assert.Contains("<Config Name=\"Derail Valley\">", config);
        Assert.Contains("UnityEngine.Display.cctor", config);
    }

    [Fact]
    public async Task InstallAsync_SwitchesMethod_AndLeavesNoStrayArtifacts()
    {
        var gamePath = CreateGameWithEntryAssembly();

        SetMethod("Doorstop");
        Assert.True(await _service.InstallAsync(gamePath));
        SetMethod("Assembly");
        Assert.True(await _service.InstallAsync(gamePath));

        // Doorstop artifacts must be gone after switching to the Assembly method.
        Assert.False(DoorstopInstaller.IsInstalled(gamePath));
        var status = _service.GetStatus(gamePath);
        Assert.Equal(UmmLoaderMethod.Assembly, status.ActiveMethod);
    }

    [Fact]
    public async Task UninstallAsync_RemovesEverything()
    {
        var gamePath = CreateGameWithEntryAssembly();
        SetMethod("Assembly");
        Assert.True(await _service.InstallAsync(gamePath));

        Assert.True(await _service.UninstallAsync(gamePath));

        var entryAsm = Path.Combine(gamePath, "DerailValley_Data", "Managed", "UnityEngine.CoreModule.dll");
        Assert.False(_patcher.IsPatched(entryAsm));
        Assert.False(File.Exists(entryAsm + AssemblyPatcher.OriginalSuffix));
        Assert.False(Directory.Exists(UmmLoaderService.GetManagerPath(gamePath)));

        var status = _service.GetStatus(gamePath);
        Assert.False(status.IsInstalled);
    }

    [Fact]
    public void GetStatus_NotInstalled_IsEmpty()
    {
        var gamePath = CreateGameWithEntryAssembly();

        var status = _service.GetStatus(gamePath);

        Assert.False(status.IsInstalled);
        Assert.Null(status.InstalledVersion);
        Assert.Null(status.ActiveMethod);
    }

    [Fact]
    public async Task InstallFromZipAsync_UsesImportedPackage()
    {
        var gamePath = CreateGameWithEntryAssembly();
        var zip = TestData.CreateUmmZip(_workDir);

        SetMethod("Doorstop");
        Assert.True(await _service.InstallFromZipAsync(zip, gamePath));

        Assert.True(DoorstopInstaller.IsInstalled(gamePath));
    }

    [Fact]
    public async Task InstallAsync_UsesConfiguredMethodFromSettings()
    {
        var gamePath = CreateGameWithEntryAssembly();
        SetMethod("Assembly");

        Assert.True(await _service.InstallAsync(gamePath));

        var entryAsm = Path.Combine(gamePath, "DerailValley_Data", "Managed", "UnityEngine.CoreModule.dll");
        Assert.True(_patcher.IsPatched(entryAsm));
        Assert.False(DoorstopInstaller.IsInstalled(gamePath));
    }

    [Fact]
    public async Task InstallAsync_DefaultSetting_UsesPerBuildDefault()
    {
        // Settings untouched → the per-build default applies (Doorstop on the
        // Windows build, Assembly on the Linux build).
        var gamePath = CreateGameWithEntryAssembly();
        var expected = Enum.Parse<UmmLoaderMethod>(AppSettings.DefaultUmmLoaderMethod);

        Assert.True(await _service.InstallAsync(gamePath));

        Assert.Equal(expected, _service.GetStatus(gamePath).ActiveMethod);
    }

    [Fact]
    public async Task InstallAsync_StaleAssemblyPatch_DoesNotOverrideConfiguredMethod()
    {
        // Regression: a leftover UMM Assembly patch must NOT override the
        // configured method (previously forced Assembly on Windows).
        var gamePath = CreateGameWithEntryAssembly();
        var entryAsm = Path.Combine(gamePath, "DerailValley_Data", "Managed", "UnityEngine.CoreModule.dll");
        Assert.True(_patcher.Inject(entryAsm, "[UnityEngine.CoreModule.dll]UnityEngine.Display.cctor:After"));
        SetMethod("Doorstop");

        Assert.True(await _service.InstallAsync(gamePath));

        Assert.True(DoorstopInstaller.IsInstalled(gamePath));
        Assert.False(_patcher.IsPatched(entryAsm)); // stale patch normalized away
    }

    private void SetMethod(string method) => _settings.Settings.UmmLoaderMethod = method;
}
