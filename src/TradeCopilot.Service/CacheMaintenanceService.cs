using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Privacy;

namespace TradeCopilot.Service;

/// <summary>
/// Hourly cache retention: purges entries older than the configured
/// auto-purge age. Registered after <c>SqliteSemanticCache</c> in DI.
/// </summary>
public sealed class CacheMaintenanceService : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromHours(1);

    private readonly ISemanticCache _cache;
    private readonly PrivacyOptions _privacy;
    private readonly ILogger<CacheMaintenanceService> _log;

    /// <summary>Creates the maintenance service.</summary>
    /// <param name="cache">Semantic cache.</param>
    /// <param name="privacy">Privacy options carrying the purge age.</param>
    /// <param name="log">Logger.</param>
    public CacheMaintenanceService(
        ISemanticCache cache,
        PrivacyOptions privacy,
        ILogger<CacheMaintenanceService> log)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(privacy);
        ArgumentNullException.ThrowIfNull(log);
        _cache = cache;
        _privacy = privacy;
        _log = log;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                int before = (await _cache.GetRecentAsync(int.MaxValue, stoppingToken).ConfigureAwait(false)).Count;
                await _cache.PurgeOlderThanAsync(TimeSpan.FromHours(_privacy.AutoPurgeHours), stoppingToken).ConfigureAwait(false);
                int after = (await _cache.GetRecentAsync(int.MaxValue, stoppingToken).ConfigureAwait(false)).Count;
                _log.LogInformation("Cache maintenance purged {Purged} entries.", Math.Max(0, before - after));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.LogWarning(ex, "Cache maintenance failed; retrying next hour.");
            }
        }
    }
}
