using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.UI.Logic;
using TradeCopilot.UI.Logic.Services;
using TradeCopilot.UI.Logic.ViewModels;

namespace TradeCopilot.UI;

/// <summary>
/// Application entry point. The app never exits on window close: closing hides the
/// overlay and the process lives until the tray "Quit" command (Stage 6b).
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>Builds the DI container from appsettings.json and shows the overlay.</summary>
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
        services.AddTransient<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<IPipeClient>(),
            NullLogger<MainViewModel>.Instance));
        _services = services.BuildServiceProvider();

        var window = new MainWindow(_services.GetRequiredService<MainViewModel>());
        MainWindow = window;
        window.Show();
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

            _services.Dispose();
            _services = null;
        }

        base.OnExit(e);
    }

    /// <summary>Shuts the application down. Called by the tray "Quit" command (Stage 6b).</summary>
    public new void Shutdown() => base.Shutdown();
}


