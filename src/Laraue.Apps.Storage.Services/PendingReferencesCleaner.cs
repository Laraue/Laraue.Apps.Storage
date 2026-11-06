using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Storage.Services;

/// <summary>
/// Deletes references that stayed pending longer than <see cref="StorageOptions.PendingReferenceLifetime"/>: uploads
/// whose caller never saved its link (it failed after <c>UploadFile</c>), and their files when nothing else uses
/// them, so they stop counting toward the owner's quota. Runs every <see cref="Interval"/> in <c>InternalApiHost</c>,
/// in batches of <see cref="BatchSize"/>.
/// </summary>
public sealed class PendingReferencesCleaner(
    IServiceScopeFactory scopeFactory,
    ILogger<PendingReferencesCleaner> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    public const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The next tick tries again.
                logger.LogWarning(ex, "Deleting expired pending references failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Deletes every expired pending reference, a batch at a time, and returns how many.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;
        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var filesService = scope.ServiceProvider.GetRequiredService<IFilesService>();

            deleted = await filesService.DeleteExpiredPendingReferencesAsync(BatchSize, cancellationToken);
            total += deleted;
        }
        while (deleted == BatchSize);

        if (total > 0)
            logger.LogInformation("Deleted {Count} references left pending", total);

        return total;
    }
}
