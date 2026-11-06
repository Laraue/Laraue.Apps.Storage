using System.Diagnostics.Metrics;
using Laraue.Apps.Storage.DataAccess;
using Laraue.Apps.Storage.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Storage.Services;

/// <summary>
/// Gauges of what is stored now, read from the database (exact, and surviving a restart, unlike counting in the
/// buckets): <c>storage_stored_bytes{kind}</c> and <c>storage_stored_files{kind}</c> (original, preview - all in
/// the B2 bucket, so their sum is what B2 bills), <c>storage_owners{kind}</c> (organizations and users
/// that have files) and <c>storage_usage_bytes</c> (the owners' usage counters summed - it should equal the total
/// stored bytes; a difference means a counter drifted). Registered by <c>InternalApiHost</c> only; refreshed in the
/// background every <see cref="RefreshInterval"/>, a scrape only reads the last snapshot.
/// </summary>
public sealed class StorageStateMetrics : BackgroundService
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StorageStateMetrics> _logger;

    private volatile Snapshot _snapshot = Snapshot.Empty;

    public StorageStateMetrics(
        IMeterFactory meterFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<StorageStateMetrics> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var meter = meterFactory.Create(StorageMetrics.MeterName);

        meter.CreateObservableGauge(
            "storage.stored.bytes",
            () => Measurements(_snapshot.StoredBytes, "kind"),
            description: "Bytes stored in the bucket now, by kind: original or preview.");

        meter.CreateObservableGauge(
            "storage.stored.files",
            () => Measurements(_snapshot.StoredFiles, "kind"),
            description: "Files stored in the bucket now, by kind: original or preview.");

        meter.CreateObservableGauge(
            "storage.owners",
            () => Measurements(_snapshot.Owners, "kind"),
            description: "Owners that have files now, by kind: organization or user.");

        meter.CreateObservableGauge(
            "storage.usage.bytes",
            () => _snapshot.UsageBytes,
            description: "The owners' usage counters summed. Equals storage_stored_bytes summed unless a counter drifted.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The gauges keep their last values; the next tick tries again.
                _logger.LogWarning(ex, "Refreshing the storage state metrics failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var files = await context.StoredFiles
            .GroupBy(x => x.Kind)
            .Select(x => new { Kind = x.Key, Count = x.LongCount(), Bytes = x.Sum(f => f.Size) })
            .ToListAsync(cancellationToken);

        var owners = await context.Owners
            .Where(x => x.UsageBytes > 0)
            .GroupBy(x => x.Kind)
            .Select(x => new { Kind = x.Key, Count = x.LongCount() })
            .ToListAsync(cancellationToken);

        var usageBytes = await context.Owners.SumAsync(x => x.UsageBytes, cancellationToken);

        _snapshot = new Snapshot(
            StoredBytes: ByLabel(files.ToDictionary(x => x.Kind, x => x.Bytes), FileKindLabel),
            StoredFiles: ByLabel(files.ToDictionary(x => x.Kind, x => x.Count), FileKindLabel),
            Owners: ByLabel(owners.ToDictionary(x => x.Kind, x => x.Count), KindLabel),
            UsageBytes: usageBytes);
    }

    /// <summary>
    /// Every known label value, zero when there's no row for it, so a series never disappears.
    /// </summary>
    private static Dictionary<string, long> ByLabel<TKey>(Dictionary<TKey, long> values, Func<TKey, string> label)
        where TKey : struct, Enum
    {
        return Enum.GetValues<TKey>().ToDictionary(label, x => values.GetValueOrDefault(x));
    }

    private static string FileKindLabel(StoredFileKind kind) => kind switch
    {
        StoredFileKind.Original => "original",
        StoredFileKind.Preview => "preview",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string KindLabel(OwnerKind kind) => kind switch
    {
        OwnerKind.Organization => "organization",
        OwnerKind.User => "user",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static IEnumerable<Measurement<long>> Measurements(IReadOnlyDictionary<string, long> values, string label)
        => values.Select(x => new Measurement<long>(x.Value, new KeyValuePair<string, object?>(label, x.Key)));

    private sealed record Snapshot(
        IReadOnlyDictionary<string, long> StoredBytes,
        IReadOnlyDictionary<string, long> StoredFiles,
        IReadOnlyDictionary<string, long> Owners,
        long UsageBytes)
    {
        public static readonly Snapshot Empty = new(
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            new Dictionary<string, long>(),
            0);
    }
}
