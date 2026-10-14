using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace DVModManager.Services;

public class UmmParamsService : IUmmParamsService
{
    private const string ParamsFileName = "Params.xml";

    private readonly ILogger<UmmParamsService> _logger;

    public UmmParamsService(ILogger<UmmParamsService> logger)
    {
        _logger = logger;
    }

    public string GetParamsPath(string gamePath) =>
        Path.Combine(UmmLoaderService.GetManagerPath(gamePath), ParamsFileName);

    public IReadOnlyDictionary<string, bool> ReadEnabledStates(string gamePath)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var path = GetParamsPath(gamePath);
        if (!File.Exists(path)) return result;

        try
        {
            var doc = XDocument.Load(path);
            var mods = doc.Root?.Element("ModParams")?.Elements("Mod") ?? [];
            foreach (var mod in mods)
            {
                var id = (string?)mod.Attribute("Id");
                if (string.IsNullOrEmpty(id)) continue;
                // UMM's XmlSerializer defaults Enabled to true when the attribute is absent.
                var enabled = !bool.TryParse((string?)mod.Attribute("Enabled"), out var e) || e;
                result.TryAdd(id, enabled);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            _logger.LogWarning(ex, "Could not read UMM params from {Path}", path);
        }

        return result;
    }

    public async Task SetEnabledAsync(string gamePath, string modId, bool enabled)
    {
        var path = GetParamsPath(gamePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var doc = File.Exists(path)
            ? XDocument.Load(path)
            : new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("Param"));

        var root = doc.Root ?? throw new XmlException($"{path} has no root element");
        var modParams = root.Element("ModParams");
        if (modParams == null)
        {
            modParams = new XElement("ModParams");
            root.Add(modParams);
        }

        var entries = modParams.Elements("Mod").ToList();
        var entry = entries.FirstOrDefault(m => string.Equals((string?)m.Attribute("Id"), modId, StringComparison.Ordinal))
                    ?? entries.FirstOrDefault(m => string.Equals((string?)m.Attribute("Id"), modId, StringComparison.OrdinalIgnoreCase));

        var value = enabled ? "true" : "false";
        if (entry != null)
        {
            entry.SetAttributeValue("Enabled", value);
        }
        else
        {
            modParams.Add(new XElement("Mod",
                new XAttribute("Id", modId),
                new XAttribute("Enabled", value),
                new XElement("Hotkey",
                    new XElement("keyCode", "None"),
                    new XElement("modifiers", "0"))));
        }

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            Async = true
        };

        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        await using (var writer = XmlWriter.Create(stream, settings))
        {
            await doc.SaveAsync(writer, CancellationToken.None);
        }
        File.Move(tempPath, path, overwrite: true);

        _logger.LogInformation("Set UMM Enabled={Enabled} for {ModId} in {Path}", enabled, modId, path);
    }
}
