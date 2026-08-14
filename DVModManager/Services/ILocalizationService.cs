namespace DVModManager.Services;

/// <summary>
/// Provides localization/translation services with key-based lookup, live language switching,
/// and fallback to English for missing translations.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Gets the currently active language code (e.g., "en", "de", "fr").</summary>
    string CurrentLanguage { get; }

    /// <summary>Gets a list of supported language codes.</summary>
    IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>
    /// Gets the localized string for the given key in the current language,
    /// falling back to English if unavailable, or returning the key itself if not found anywhere.
    /// </summary>
    /// <param name="key">The translation key (e.g., "btn.save", "status.game_detected").</param>
    /// <returns>The translated string, English fallback, or the key name if missing entirely.</returns>
    string GetString(string key);

    /// <summary>
    /// Indexer shorthand for XAML binding paths like [toolbar.save_profile].
    /// </summary>
    string this[string key] { get; }

    /// <summary>
    /// Gets the localized string with format arguments applied (like string.Format).
    /// Falls back to English if unavailable, or returns the key if not found.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted translated string, English fallback, or key if missing.</returns>
    string GetString(string key, params object?[] args);

    /// <summary>
    /// Switches the active language and notifies all subscribers.
    /// Language must be in SupportedLanguages or falls back to "en".
    /// </summary>
    /// <param name="languageCode">The language code to switch to.</param>
    void SetLanguage(string languageCode);

    /// <summary>
    /// Raised when the active language changes, allowing UI elements to refresh.
    /// </summary>
    event EventHandler? LanguageChanged;
}
