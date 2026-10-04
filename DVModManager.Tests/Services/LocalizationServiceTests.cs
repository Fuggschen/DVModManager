using DVModManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Services;

public class LocalizationServiceTests
{
    private readonly LocalizationService _loc;

    public LocalizationServiceTests()
    {
        _loc = new LocalizationService();
    }

    [Fact]
    public void DefaultLanguage_IsEnglish()
    {
        Assert.Equal("en", _loc.CurrentLanguage);
    }

    [Fact]
    public void SupportedLanguages_ContainsExpectedLanguages()
    {
        Assert.Contains("en", _loc.SupportedLanguages);
        Assert.Contains("de", _loc.SupportedLanguages);
        Assert.Contains("fr", _loc.SupportedLanguages);
        Assert.Contains("zh", _loc.SupportedLanguages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetString_WhenKeyIsNullOrEmpty_ReturnsEmptyString(string? key)
    {
        var result = _loc.GetString(key!);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetString_WhenKeyExists_ReturnsTranslatedString()
    {
        _loc.SetLanguage("en");
        var activeEn = _loc.GetString("modstate.active");
        Assert.Equal("Active", activeEn);

        _loc.SetLanguage("de");
        var activeDe = _loc.GetString("modstate.active");
        Assert.Equal("Aktiv", activeDe);
    }

    [Fact]
    public void GetString_WhenKeyMissing_ReturnsKey()
    {
        var nonExistentKey = "non.existent.key.12345";
        var result = _loc.GetString(nonExistentKey);
        Assert.Equal(nonExistentKey, result);
    }

    [Fact]
    public void GetString_WithArguments_FormatsCorrectly()
    {
        _loc.SetLanguage("en");
        var formatted = _loc.GetString("modstate.update_available", "2.0.0");
        Assert.Contains("2.0.0", formatted);
    }

    [Fact]
    public void Indexer_ReturnsSameAsGetString()
    {
        _loc.SetLanguage("en");
        Assert.Equal(_loc.GetString("modstate.active"), _loc["modstate.active"]);
    }

    [Fact]
    public void SetLanguage_WhenUnsupported_FallsBackToEnglish()
    {
        _loc.SetLanguage("unsupported_lang_code");
        Assert.Equal("en", _loc.CurrentLanguage);
    }

    [Fact]
    public void SetLanguage_RaisesLanguageChangedAndPropertyChangedEvents()
    {
        bool languageChangedFired = false;
        List<string?> propertyChangedNames = new();

        _loc.LanguageChanged += (_, _) => languageChangedFired = true;
        _loc.PropertyChanged += (_, e) => propertyChangedNames.Add(e.PropertyName);

        _loc.SetLanguage("de");

        Assert.True(languageChangedFired);
        Assert.Contains(nameof(LocalizationService.CurrentLanguage), propertyChangedNames);
        Assert.Contains("Item[]", propertyChangedNames);
    }
}
