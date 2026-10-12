using DVModManager.Models;
using Xunit;

namespace DVModManager.Tests.Models;

public class UmmGameInfoTests
{
    [Fact]
    public void ParseFromManagerConfig_FindsDerailValleyEntry()
    {
        var info = UmmGameInfo.ParseFromManagerConfig(
            """
            <Config>
              <GameInfo Name="Other Game"><EntryPoint>[A.dll]B.C.D:After</EntryPoint></GameInfo>
              <GameInfo Name="Derail Valley">
                <EntryPoint>[UnityEngine.CoreModule.dll]UnityEngine.Display.cctor:After</EntryPoint>
                <MinimalManagerVersion>0.27.0</MinimalManagerVersion>
              </GameInfo>
            </Config>
            """,
            UmmGameInfo.DerailValleyName);

        Assert.NotNull(info);
        Assert.Equal("[UnityEngine.CoreModule.dll]UnityEngine.Display.cctor:After", info!.EntryPoint);
        Assert.Equal("0.27.0", info.MinimalManagerVersion);
    }

    [Fact]
    public void ParseFromManagerConfig_MissingGame_ReturnsNull()
    {
        Assert.Null(UmmGameInfo.ParseFromManagerConfig("<Config />", "Derail Valley"));
        Assert.Null(UmmGameInfo.ParseFromManagerConfig("not xml", "Derail Valley"));
    }

    [Fact]
    public void GameConfigXml_RoundTrips()
    {
        var original = UmmGameInfo.DerailValleyFallback();

        var xml = original.ToGameConfigXml();
        var parsed = UmmGameInfo.ParseFromGameConfig(xml);

        Assert.NotNull(parsed);
        Assert.Equal(original.Name, parsed!.Name);
        Assert.Equal(original.EntryPoint, parsed.EntryPoint);
        Assert.Equal(original.StartingPoint, parsed.StartingPoint);
        Assert.Equal(original.GameExe, parsed.GameExe);
        Assert.Equal(original.MinimalManagerVersion, parsed.MinimalManagerVersion);
    }

    [Fact]
    public void ParseFromGameConfig_RejectsWrongShape()
    {
        // The package's UnityModManagerConfig.xml is a different schema.
        Assert.Null(UmmGameInfo.ParseFromGameConfig(
            "<Config><GameInfo Name=\"Derail Valley\"><EntryPoint>X</EntryPoint></GameInfo></Config>"));
    }
}
