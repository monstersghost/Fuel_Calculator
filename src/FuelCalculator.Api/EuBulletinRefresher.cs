using FuelCalculator.Core.Fuel;

namespace FuelCalculator.Api;

/// <summary>
/// Keeps the live EU Weekly Oil Bulletin prices fresh: checks every hour and re-reads the bulletin when the
/// copy in hand is more than 12 hours old. Estimates never wait for it; until the first read succeeds the
/// seed prices are used.
/// </summary>
public sealed class EuBulletinRefresher(EuOilBulletinPriceProvider provider, ILogger<EuBulletinRefresher> logger) : BackgroundService
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (provider.NeedsRefresh(MaxAge))
            {
                try
                {
                    await provider.RefreshAsync(stoppingToken);
                    logger.LogInformation("EU Weekly Oil Bulletin prices refreshed.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not refresh EU Weekly Oil Bulletin prices; using the last copy or the seed.");
                }
            }

            await Task.Delay(CheckEvery, stoppingToken);
        }
    }
}
