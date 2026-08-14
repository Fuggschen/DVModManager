using DVModManager.Models;

namespace DVModManager.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task SaveAsync();
    Task LoadAsync();
}
