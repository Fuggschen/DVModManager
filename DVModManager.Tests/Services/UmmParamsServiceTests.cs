using System.Xml.Linq;
using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class UmmParamsServiceTests : IDisposable
{
    // Trimmed copy of a real UMM 0.33.2 Params.xml.
    private const string SampleParams = """
        <?xml version="1.0" encoding="utf-8"?>
        <Param xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <Hotkey>
            <keyCode>F10</keyCode>
            <modifiers>0</modifiers>
          </Hotkey>
          <UIScale>1</UIScale>
          <LastUpdateCheck>2026-09-04T03:46:51.9113972+02:00</LastUpdateCheck>
          <ModParams>
            <Mod Id="Multiplayer" Enabled="true">
              <Hotkey>
                <keyCode>None</keyCode>
                <modifiers>0</modifiers>
              </Hotkey>
            </Mod>
            <Mod Id="ZCouplers" Enabled="false">
              <Hotkey>
                <keyCode>F5</keyCode>
                <modifiers>1</modifiers>
              </Hotkey>
            </Mod>
          </ModParams>
        </Param>
        """;

    private readonly UmmParamsService _service = new(NullLogger<UmmParamsService>.Instance);
    private readonly string _gamePath = Path.Combine(Path.GetTempPath(), "Game_" + Guid.NewGuid());

    private string ParamsPath => _service.GetParamsPath(_gamePath);

    private void WriteParams(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ParamsPath)!);
        File.WriteAllText(ParamsPath, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_gamePath))
            Directory.Delete(_gamePath, true);
    }

    [Fact]
    public void GetParamsPath_PointsIntoUmmFolder()
    {
        var expected = Path.Combine(_gamePath, "DerailValley_Data", "Managed", "UnityModManager", "Params.xml");
        Assert.Equal(expected, ParamsPath);
    }

    [Fact]
    public void ReadEnabledStates_ParsesEntries()
    {
        WriteParams(SampleParams);

        var states = _service.ReadEnabledStates(_gamePath);

        Assert.Equal(2, states.Count);
        Assert.True(states["Multiplayer"]);
        Assert.False(states["zcouplers"]);
    }

    [Fact]
    public void ReadEnabledStates_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(_service.ReadEnabledStates(_gamePath));
    }

    [Fact]
    public void ReadEnabledStates_CorruptFile_ReturnsEmpty()
    {
        WriteParams("<Param><ModParams><Mod Id=");

        Assert.Empty(_service.ReadEnabledStates(_gamePath));
    }

    [Fact]
    public void ReadEnabledStates_MissingEnabledAttribute_TreatedAsEnabled()
    {
        WriteParams("""<Param><ModParams><Mod Id="A" /></ModParams></Param>""");

        Assert.True(_service.ReadEnabledStates(_gamePath)["A"]);
    }

    [Fact]
    public async Task SetEnabledAsync_ExistingEntry_OnlyChangesEnabled()
    {
        WriteParams(SampleParams);

        await _service.SetEnabledAsync(_gamePath, "Multiplayer", false);
        await _service.SetEnabledAsync(_gamePath, "ZCouplers", true);

        var states = _service.ReadEnabledStates(_gamePath);
        Assert.False(states["Multiplayer"]);
        Assert.True(states["ZCouplers"]);

        var doc = XDocument.Load(ParamsPath);
        Assert.Equal("1", doc.Root!.Element("UIScale")!.Value);
        Assert.Equal("2026-09-04T03:46:51.9113972+02:00", doc.Root.Element("LastUpdateCheck")!.Value);
        Assert.Equal("F10", doc.Root.Element("Hotkey")!.Element("keyCode")!.Value);
        var zc = doc.Root.Element("ModParams")!.Elements("Mod").Single(m => (string?)m.Attribute("Id") == "ZCouplers");
        Assert.Equal("true", (string?)zc.Attribute("Enabled"));
        Assert.Equal("F5", zc.Element("Hotkey")!.Element("keyCode")!.Value);
        Assert.False(File.Exists(ParamsPath + ".tmp"));
    }

    [Fact]
    public async Task SetEnabledAsync_NewMod_AppendsEntryWithHotkey()
    {
        WriteParams(SampleParams);

        await _service.SetEnabledAsync(_gamePath, "NewMod", false);

        var doc = XDocument.Load(ParamsPath);
        var mods = doc.Root!.Element("ModParams")!.Elements("Mod").ToList();
        Assert.Equal(3, mods.Count);
        var added = mods.Single(m => (string?)m.Attribute("Id") == "NewMod");
        Assert.Equal("false", (string?)added.Attribute("Enabled"));
        Assert.Equal("None", added.Element("Hotkey")!.Element("keyCode")!.Value);
    }

    [Fact]
    public async Task SetEnabledAsync_MissingFile_CreatesFile()
    {
        await _service.SetEnabledAsync(_gamePath, "ModA", false);

        Assert.True(File.Exists(ParamsPath));
        Assert.False(_service.ReadEnabledStates(_gamePath)["ModA"]);
    }
}
