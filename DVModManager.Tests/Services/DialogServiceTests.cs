using Avalonia.Controls;
using Avalonia.Interactivity;
using DVModManager.Services;
using Xunit;

namespace DVModManager.Tests.Services;

public class DialogServiceTests
{
    private class TestLocalization : ILocalizationService
    {
        public string CurrentLanguage => "en";
        public IReadOnlyList<string> SupportedLanguages => ["en"];
        public string this[string key] => key;
        public string GetString(string key) => key;
        public string GetString(string key, params object?[] args) => key;
        public void SetLanguage(string languageCode) { }
#pragma warning disable CS0067 // event never used — stub
        public event EventHandler? LanguageChanged;
#pragma warning restore CS0067
    }

    [Fact]
    public void MessageContent_OkButton_IsWiredToClose()
    {
        // Regression: the OK button used to be wired to a null callback, so the
        // popup after installing/uninstalling the UMM loader could never close.
        var closed = false;
        var content = new DialogService(new TestLocalization())
            .BuildMessageContent("message", () => closed = true);

        var okButton = ((StackPanel)content).Children.OfType<Button>().Single();
        Assert.Equal("dialog.button.ok", okButton.Content);

        okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(closed);
    }
}
