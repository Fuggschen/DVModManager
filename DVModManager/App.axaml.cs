using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DVModManager", "logs");
        services.AddLogging(b =>
        {
            b.AddConsole();
            b.AddProvider(new FileLoggerProvider(logDir, LogLevel.Debug));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        // HTTP
        services.AddHttpClient();

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
