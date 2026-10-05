using Azure.Core;
using Azure.Identity;
using AzureCost.Core.DependencyInjection;
using AzureCost_WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace AzureCost_WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>The main application window.</summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>The UI thread dispatcher.</summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>The native window handle (HWND), for pickers and interop.</summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// The application's dependency-injection container. The exact same
    /// <c>AddAzureCost()</c> call used by the console app powers this app — only the
    /// credential is swapped for an interactive one so users get a sign-in popup.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();

        // Last-resort safety net: log anything that slips through so the app reports the
        // problem instead of vanishing. Normal errors are handled in the view model.
        UnhandledException += (_, e) =>
        {
            LogCrash(e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash(e.Exception);
            e.SetObserved();
        };
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AzureCostWinUI");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                $"[{DateTimeOffset.Now:u}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Never let logging throw.
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddAzureCost(options =>
        {
            // Try silent sources first (az login, Visual Studio), then fall back to an
            // interactive browser sign-in so the app works even on a machine that has
            // never run 'az login'.
            options.Credential = new ChainedTokenCredential(
                new AzureCliCredential(),
                new VisualStudioCredential(),
                new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
                {
                    TokenCachePersistenceOptions = new TokenCachePersistenceOptions
                    {
                        Name = "AzureCostWinUI",
                    },
                }));
        });

        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window.Activate();
    }
}
