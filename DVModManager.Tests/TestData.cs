using System.IO.Compression;
using System.Net;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DVModManager.Tests;

/// <summary>Stub HttpMessageHandler returning fixed payloads per URL.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new();

    public void Map(string url, string content) =>
        _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8)
        };

    public void MapBytes(string url, byte[] content) =>
        _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        };

    public void MapNotFound(string url) =>
        _routes[url] = () => new HttpResponseMessage(HttpStatusCode.NotFound);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri?.ToString() ?? "";
        return Task.FromResult(_routes.TryGetValue(url, out var factory)
            ? factory()
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

/// <summary>
/// Builds realistic test artifacts: valid .NET assemblies with chosen names
/// (via dnlib re-naming of a real assembly), UMM-style package zips and a fake
/// Derail Valley game folder.
/// </summary>
internal static class TestData
{
    /// <summary>Valid .NET assembly bytes named <paramref name="assemblyName"/>.</summary>
    public static byte[] BuildAssembly(string assemblyName, Version version)
    {
        // Renaming a real compiled assembly keeps the PE/metadata valid.
        var source = typeof(TestData).Assembly.Location;
        using var module = ModuleDefMD.Load(source);
        module.Name = assemblyName + ".dll";
        module.Assembly.Name = assemblyName;
        module.Assembly.Version = version;
        using var ms = new MemoryStream();
        module.Write(ms);
        return ms.ToArray();
    }

    /// <summary>Minimal valid UMM package zip. Returns the zip path.</summary>
    public static string CreateUmmZip(string directory, string version = "0.33.2",
        string? rootPrefix = null, bool includeGameConfig = true, bool includeDoorstop = true,
        bool includeLoaderAssembly = true)
    {
        Directory.CreateDirectory(directory);
        var zipPath = Path.Combine(directory, $"umm-{Guid.NewGuid():N}.zip");

        string P(string rel) => rootPrefix == null ? rel : rootPrefix + "/" + rel;

        using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        void Add(string rel, byte[] bytes)
        {
            var entry = zip.CreateEntry(P(rel));
            using var es = entry.Open();
            es.Write(bytes);
        }

        var loaderDll = includeLoaderAssembly
            ? BuildAssembly("UnityModManager", Version.Parse(version + ".0"))
            : BuildAssembly("NotUmm", new Version(1, 0, 0, 0));

        Add("UnityModManager.dll", loaderDll);
        Add("UnityModManager.xml", "<doc />"u8.ToArray());
        Add("0Harmony.dll", "harmony-root"u8.ToArray());
        Add("dnlib.dll", "dnlib"u8.ToArray());
        Add("System.Xml.dll", "xml"u8.ToArray());
        Add("Harmony/2.2/0Harmony.dll", "harmony-2.2"u8.ToArray());
        Add("Harmony/1.2/0Harmony12.dll", "harmony-12"u8.ToArray());
        Add("Harmony/1.2/0Harmony-1.2.dll", "harmony-1.2"u8.ToArray());

        if (includeDoorstop)
        {
            Add("winhttp_x64.dll", "winhttp64"u8.ToArray());
            Add("winhttp_x86.dll", "winhttp32"u8.ToArray());
        }
        if (includeGameConfig)
            Add("UnityModManagerConfig.xml", Encoding.UTF8.GetBytes(ManagerConfigXml()));

        return zipPath;
    }

    public static string ManagerConfigXml() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Config>
          <Repository>https://example.invalid/feed.json</Repository>
          <GameInfo Name="Derail Valley">
            <Folder>Derail Valley</Folder>
            <ModsDirectory>Mods</ModsDirectory>
            <ModInfo>Info.json</ModInfo>
            <GameExe>DerailValley.exe</GameExe>
            <EntryPoint>[UnityEngine.CoreModule.dll]UnityEngine.Display.cctor:After</EntryPoint>
            <StartingPoint>[Assembly-CSharp.dll]DV.Globals.AssignSelfToHelpers:After</StartingPoint>
            <MinimalManagerVersion>0.27.0</MinimalManagerVersion>
          </GameInfo>
        </Config>
        """;

    /// <summary>Fake Derail Valley game folder with the Managed layout.</summary>
    public static string CreateGameDirectory(string? parent = null)
    {
        var gamePath = Path.Combine(parent ?? Path.GetTempPath(), "Game_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(gamePath, "DerailValley_Data", "Managed"));
        return gamePath;
    }

    /// <summary>Dummy entry assembly with static methods on one type (defaults: Dummy.Entry.Run/.cctor).</summary>
    public static string CreateDummyEntryAssembly(string managedDir,
        string fileName = "Dummy.dll", string typeNamespace = "Dummy", string typeName = "Entry",
        params string[] methods)
    {
        if (methods.Length == 0) methods = ["Run", ".cctor"];

        using var module = ModuleDefMD.Load(typeof(TestData).Assembly.Location);
        module.Name = fileName;
        module.Assembly.Name = Path.GetFileNameWithoutExtension(fileName);

        var type = new TypeDefUser(typeNamespace, typeName, module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = dnlib.DotNet.TypeAttributes.Public | dnlib.DotNet.TypeAttributes.BeforeFieldInit
        };
        foreach (var name in methods)
        {
            var method = new MethodDefUser(name, MethodSig.CreateStatic(module.CorLibTypes.Void),
                dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
                dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static
                | dnlib.DotNet.MethodAttributes.HideBySig);
            method.Body = new CilBody();
            method.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
            type.Methods.Add(method);
        }
        module.Types.Add(type);

        var path = Path.Combine(managedDir, fileName);
        module.Write(path);
        return path;
    }

    /// <summary>
    /// A real SettingsService whose settings file lives in <paramref name="directory"/>.
    /// Never construct SettingsService without a path in tests: the default is the
    /// user's real settings file, and any SaveAsync would overwrite it.
    /// </summary>
    public static SettingsService CreateSettingsService(string directory) =>
        new(NullLogger<SettingsService>.Instance, Path.Combine(directory, "settings.json"));

    public static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch { /* best-effort cleanup */ }
    }
}
