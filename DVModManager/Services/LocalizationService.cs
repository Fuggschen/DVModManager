using System.Globalization;
using System.ComponentModel;

namespace DVModManager.Services;

/// <summary>
/// Loads translations from an embedded CSV resource (key,en,de,fr,... format)
/// and provides runtime key lookup with fallback to English.
/// Implements INotifyPropertyChanged to notify UI when language changes.
/// </summary>
public class LocalizationService : ILocalizationService, INotifyPropertyChanged
{
    private readonly Dictionary<string, Dictionary<string, string>> _translations = [];
    private readonly List<string> _languages = [];
    private string _currentLanguage = "en";

    public string CurrentLanguage
    {
        get => _currentLanguage;
        private set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnPropertyChanged(nameof(CurrentLanguage));
            }
        }
    }

    public IReadOnlyList<string> SupportedLanguages => _languages.AsReadOnly();

    public event EventHandler? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationService()
    {
        LoadTranslationsFromEmbeddedCsv();
    }

    /// <summary>
    /// Loads translations from the embedded CSV resource "DVModManager.Resources.strings.csv".
    /// Expected format: key,en,de,fr,...
    /// Handles BOM, empty cells, and missing language columns gracefully.
    /// </summary>
    private void LoadTranslationsFromEmbeddedCsv()
    {
        _translations.Clear();
        _languages.Clear();
        _languages.Add("en"); // English always supported as fallback

        try
        {
            var assembly = typeof(LocalizationService).Assembly;
            const string expectedResourceName = "DVModManager.Resources.strings.csv";
            var resourceName = expectedResourceName;

            // Resolve resource name defensively in case namespace/publish settings change.
            // This avoids silent failures where the exact expected name cannot be found.
            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("Resources.strings.csv", StringComparison.OrdinalIgnoreCase)
                                      || n.EndsWith("strings.csv", StringComparison.OrdinalIgnoreCase))
                    ?? expectedResourceName;
                stream = assembly.GetManifestResourceStream(resourceName);
            }

            using (stream)
            {
                if (stream == null)
                {
                    var names = string.Join(", ", assembly.GetManifestResourceNames());
                    Console.Error.WriteLine($"Warning: Embedded localization CSV not found. Expected '{expectedResourceName}'. Available resources: {names}");
                    return;
                }

                using (var reader = new StreamReader(stream))
                {
                    // Parse header row to identify language columns
                    var headerLine = reader.ReadLine();
                    if (headerLine == null) return;

                    // Be defensive about BOM and zero-width marks without dropping regular characters.
                    headerLine = headerLine.TrimStart('\uFEFF');

                    var headers = ParseCsvLine(headerLine).Select(h => h.Trim()).ToArray();
                    if (headers.Length == 0)
                    {
                        Console.Error.WriteLine("CSV header is empty. Localization unavailable.");
                        return;
                    }

                    var keyHeader = headers[0]
                        .Trim('\uFEFF', '\0', ' ', '\t', '\r', '\n');
                    if (!keyHeader.Equals("key", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"Warning: CSV header first column is '{headers[0]}' instead of 'key'. Proceeding anyway.");
                    }

                    // Extract language codes from header (skip "key" column)
                    var languages = headers.Skip(1).ToList();
                    _languages.Clear();
                    _languages.Add("en"); // Always available
                    foreach (var lang in languages)
                    {
                        if (!string.IsNullOrWhiteSpace(lang) && lang != "en" && !_languages.Contains(lang))
                            _languages.Add(lang);
                    }

                    // Parse data rows
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        // Be defensive about stray BOM on row starts.
                        line = line.TrimStart('\uFEFF');

                        var parts = ParseCsvLine(line);
                        if (parts.Length < 1) continue;

                        var key = parts[0].Trim();
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        // Initialize entry for this key
                        if (!_translations.ContainsKey(key))
                            _translations[key] = [];

                        // Populate each language column (index 0 is key, so language i is at parts[i+1])
                        for (int i = 0; i < languages.Count && i + 1 < parts.Length; i++)
                        {
                            var langCode = languages[i].Trim();
                            var value = parts[i + 1].Trim();
                            
                            if (!string.IsNullOrWhiteSpace(langCode) && !string.IsNullOrWhiteSpace(value))
                            {
                                _translations[key][langCode] = UnescapeCsvValue(value);
                            }
                        }
                    }

                    Console.Error.WriteLine($"Localization loaded from '{resourceName}'. Languages: {string.Join(", ", _languages)}. Keys: {_translations.Count}.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading embedded localization CSV: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses one CSV row and supports quoted cells with commas and escaped quotes.
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote inside a quoted field.
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        result.Add(current.ToString());
        return result.ToArray();
    }

    /// <summary>
    /// Unescapes CSV-quoted values (handles quoted newlines, commas, escaped quotes, etc.).
    /// For now, assumes values are not quoted unless we detect quotes during parsing.
    /// </summary>
    private static string UnescapeCsvValue(string value)
    {
        // Basic unescaping: if value was quoted, remove outer quotes and unescape inner quotes
        if (value.StartsWith("\"") && value.EndsWith("\""))
        {
            value = value[1..^1];
            value = value.Replace("\"\"", "\"");
        }
        return value;
    }

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";

        // Try current language
        if (_translations.TryGetValue(key, out var langDict))
        {
            if (langDict.TryGetValue(_currentLanguage, out var translation))
                return translation;

            // Fall back to English if current language not found
            if (langDict.TryGetValue("en", out var englishTranslation))
                return englishTranslation;
        }

        // Return key name as last resort
        return key;
    }

    /// <summary>
    /// Indexer for XAML binding: {Binding [key], Source={StaticResource Loc}}
    /// When language changes, "Item[]" PropertyChanged fires to refresh all indexer bindings.
    /// </summary>
    public string this[string key] => GetString(key);

    public string GetString(string key, params object?[] args)
    {
        var template = GetString(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch
        {
            // If format fails, return template as-is
            return template;
        }
    }

    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            languageCode = "en";

        // Normalize to supported language or fall back to English
        if (!_languages.Contains(languageCode))
            languageCode = "en";

        if (languageCode == _currentLanguage)
            return; // No change

        CurrentLanguage = languageCode; // Use property setter to trigger PropertyChanged
        // Notify all indexer bindings ([key]) that every key's value has changed
        // "Item[]" is the standard PropertyChanged name for indexers (defined in Binding.IndexerName in System.Windows.Data)
        OnPropertyChanged("Item[]");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
