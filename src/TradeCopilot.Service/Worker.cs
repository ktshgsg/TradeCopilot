namespace TradeCopilot.Service;

/// <summary>
/// Background worker that executes the TradeCopilot service loop.
/// </summary>
public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Worker"/> class.
    /// </summary>
    /// <param name="logger">The logger used to write worker status messages.</param>
    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes the worker loop until cancellation is requested.
    /// </summary>
    /// <param name="stoppingToken">Token used to signal that the host is shutting down.</param>
    /// <returns>A task that completes when the worker stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {Time}", DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
