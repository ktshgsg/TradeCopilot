using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Setup;
using TradeCopilot.Core.Speech;
using TradeCopilot.UI.Infrastructure;
using TradeCopilot.UI.Logic;
using TradeCopilot.UI.Logic.Services;
using TradeCopilot.UI.Logic.Speech;
using TradeCopilot.UI.Logic.ViewModels;
using TradeCopilot.UI.Views;

namespace TradeCopilot.UI;

/// <summary>
/// Application entry point. The app never exits on window close: closing hides the
/// overlay and the process lives until the tray "Quit" command. First run shows the
/// consent dialog before the overlay.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>Builds the DI container, gates on first-run consent, and shows the overlay.</summary>
    /// <param name="e">Startup arguments.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var opts = new UiOptions();
        config.GetSection("Ui").Bind(opts);

        var services = new ServiceCollection();
        services.AddSingleton(opts);
        services.AddSingleton<IPipeClient>(new PipeClient(opts, NullLogger<PipeClient>.Instance));

        // Push-to-talk: a second WaveInEvent in this process, behind an abstraction the
        // tests can substitute. AudioCapture.Start logs and no-ops when the service holds
        // the mic, so contention surfaces in the status bar instead of an exception.
        services.AddSingleton(_ => new AudioCapture(NullLogger<AudioCapture>.Instance));
        services.AddSingleton<IAudioRecorder>(sp => new CoreAudioRecorder(sp.GetRequiredService<AudioCapture>()));

        services.AddSingleton(new SettingsStore());
        services.AddSingleton(sp => sp.GetRequiredService<SettingsStore>().Load());

        // First-run wizard: a dedicated HttpClient (the downloader configures the
        // timeout and user agent at construction) plus the manifest-driven downloader.
        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton(sp => new ModelDownloader(
            sp.GetRequiredService<HttpClient>(),
            NullLogger<ModelDownloader>.Instance));
        services.AddTransient(sp => new FirstRunViewModel(
            sp.GetRequiredService<ModelDownloader>(),
            NullLogger<FirstRunViewModel>.Instance));
        services.AddTransient(_ => new ConsentWindow(ConsentGate.DefaultFlagPath));
        // Singleton: the overlay and the Settings dialog resolve the SAME instance so
        // the push-to-talk toggle and the chat state stay in sync. A second resolve must
        // not create a second MainViewModel (that would double-subscribe AudioCapture).
        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<IPipeClient>(),
            NullLogger<MainViewModel>.Instance,
            sp.GetRequiredService<IAudioRecorder>(),
            ShutdownApp));
        services.AddTransient(sp => new SettingsWindow(
            sp.GetRequiredService<UiSettings>(),
            sp.GetRequiredService<SettingsStore>(),
            sp.GetRequiredService<IPipeClient>(),
            sp.GetRequiredService<MainViewModel>()));
        _services = services.BuildServiceProvider();

        // First-run consent: block the modal until the choice is made. Quit (or the X
        // button) leaves Accepted false and the process exits without the flag file.
        if (ConsentGate.ShouldShow(ConsentGate.DefaultFlagPath))
        {
            var consent = _services.GetRequiredService<ConsentWindow>();
            consent.ShowDialog();
            if (!consent.Accepted)
            {
                Shutdown();
                return;
            }
        }

        // First-run models: show the downloader wizard once (flag file missing and
        // models absent). Afterwards a missing model set puts the overlay in degraded
        // mode with a persistent banner.
        var firstRun = _services.GetRequiredService<FirstRunViewModel>();
        bool modelsPresent = ModelManifest.AllPresent(firstRun.BaseDirectory);
        if (FirstRunGate.ShouldShow(FirstRunGate.DefaultFlagPath, modelsPresent))
        {
            var wizard = new FirstRunWindow(firstRun);
            wizard.ShowDialog();
            modelsPresent = ModelManifest.AllPresent(firstRun.BaseDirectory);
        }

        var viewModel = _services.GetRequiredService<MainViewModel>();
        if (!modelsPresent)
        {
            NullLogger<App>.Instance.LogWarning("Running in degraded mode: required AI models are missing; rules and cache only.");
            viewModel.DegradedMode = true;
        }

        var window = new MainWindow(viewModel);
        MainWindow = window;
        viewModel.OpenSettingsRequested += () => OpenSettings(window);
        viewModel.OpenAuditLogRequested += OpenAuditLogFolder;
        window.Show();
    }

    /// <summary>Shows the modal Settings window owned by the overlay.</summary>
    /// <param name="owner">Overlay window.</param>
    private void OpenSettings(Window owner)
    {
        if (_services is null)
        {
            return;
        }

        var settings = _services.GetRequiredService<SettingsWindow>();
        settings.Owner = owner;
        settings.ShowDialog();
    }

    /// <summary>
    /// Opens the folder holding the audit log. v1 keeps data/audit.log beside the service
    /// executable; this opens the same relative folder under the UI's base directory
    /// (both share an install dir in packaged deployments).
    /// </summary>
    private static void OpenAuditLogFolder()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    /// <summary>
    /// Runs after <c>MainViewModel.QuitCommand</c> has disposed the pipe client and the
    /// recorder: lets the overlay close for real, then shuts the process down.
    /// </summary>
    private void ShutdownApp()
    {
        if (MainWindow is MainWindow overlay)
        {
            overlay.AllowClose();
        }

        Shutdown();
    }

    /// <summary>Disposes the DI container.</summary>
    /// <param name="e">Exit arguments.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            if (_services.GetService(typeof(IPipeClient)) is IAsyncDisposable pipe)
            {
                pipe.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            if (_services.GetService(typeof(HttpClient)) is HttpClient http)
            {
                http.Dispose();
            }

            _services.Dispose();
            _services = null;
        }

        base.OnExit(e);
    }

    /// <summary>Shuts the application down. Called by the consent Quit button and QuitCommand.</summary>
    public new void Shutdown() => base.Shutdown();
}
