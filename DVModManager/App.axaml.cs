using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DVModManager.Models;
using DVModManager.Services;
using DVModManager.ViewModels;
using DVModManager.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DVModManager;

public class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Initialize localization service with default language (English).
        // If settings are loaded later, the language will be updated via MainWindowViewModel.
        // For now, we just ensure the service is ready before any UI is created.
        var localizationService = Services.GetRequiredService<ILocalizationService>();
        // Expose as an Application-level resource so XAML can bind to it via {StaticResource Loc}
        Resources["Loc"] = localizationService;
        // Language will be applied after settings load in MainWindowViewModel.InitializeAsync()

        // Catch unhandled exceptions on any thread and surface them in the status bar / console
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DVModManager", "logs");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            var text = ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "Unknown error";
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var entry = $"[{stamp}] [FATAL] Unhandled exception (terminating={e.IsTerminating}):{Environment.NewLine}{text}{Environment.NewLine}";
            try
            {
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, $"dvmm-{DateTime.Now:yyyy-MM-dd}.log"), entry);
            }
            catch { }
            Console.Error.WriteLine(entry);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            var text = e.Exception.ToString();
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var entry = $"[{stamp}] [TASK] Unobserved task exception:{Environment.NewLine}{text}{Environment.NewLine}";
            try
            {
                Directory.CreateDirectory(logDir);
                File.AppendAllText(Path.Combine(logDir, $"dvmm-{DateTime.Now:yyyy-MM-dd}.log"), entry);
            }
            catch { }
            Console.Error.WriteLine(entry);
            e.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = vm };

            // Provide the window reference to the dialog service
            Services.GetRequiredService<IDialogService>().SetOwner(window);

            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                Services.GetRequiredService<IGameDetectionService>().Dispose();
                Services.GetRequiredService<IModDiscoveryService>().Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string GetAppDataDirectory()
    {
        if (OperatingSystem.IsLinux())
        {
            // Respect XDG_CONFIG_HOME if set to a valid absolute path, otherwise fall back to ~/.config
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var configBase = !string.IsNullOrEmpty(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(configBase, ManagerStorage.DirectoryName);
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ManagerStorage.DirectoryName);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        var logDir = Path.Combine(GetAppDataDirectory(), "logs");
        services.AddLogging(b =>
        {
            b.AddConsole();
            b.AddProvider(new FileLoggerProvider(logDir, LogLevel.Debug));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        // HTTP
        services.AddHttpClient();

        // Localization (must be registered early, before UI/ViewModels)
        services.AddSingleton<ILocalizationService, LocalizationService>();

        // Core infrastructure
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDialogService, DialogService>();

        // Game & mod services
        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IModDiscoveryService, ModDiscoveryService>();
        services.AddSingleton<IVersionCacheService, VersionCacheService>();
        services.AddSingleton<IModInstallService, ModInstallService>();
        services.AddSingleton<IProfileService, ProfileService>();

        // Update services
        services.AddSingleton<IGitHubModsService, GitHubModsService>();
        services.AddSingleton<INexusModsService, NexusModsService>();
        services.AddSingleton<IUpdateService, UpdateService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProfileViewModel>();
    }
}
