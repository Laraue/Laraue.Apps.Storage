using System.Diagnostics.Metrics;

namespace Laraue.Apps.Storage.Services;

/// <summary>
/// Meter <c>Laraue.Apps.Storage</c>, exported on <c>/_metrics</c>:
/// <c>storage_uploads_total{result}</c> (stored, deduplicated, quota_exceeded) and
/// <c>storage_bucket_failures_total{operation}</c> (write, read, delete) - what the bucket alert watches. Every
/// label value starts at zero so <c>increase()</c> sees the first event.
/// </summary>
public sealed class StorageMetrics
{
    public const string MeterName = "Laraue.Apps.Storage";

    private readonly Counter<long> _uploads;
    private readonly Counter<long> _bucketFailures;

    public StorageMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _uploads = meter.CreateCounter<long>("storage.uploads", description: "Uploads by result.");
        _bucketFailures = meter.CreateCounter<long>(
            "storage.bucket.failures",
            description: "Failed reads, writes and deletes of stored files.");

        foreach (var result in new[] { UploadResults.Stored, UploadResults.Deduplicated, UploadResults.QuotaExceeded })
            _uploads.Add(0, new KeyValuePair<string, object?>("result", result));

        foreach (var operation in new[] { BucketOperations.Write, BucketOperations.Read, BucketOperations.Delete })
            _bucketFailures.Add(0, new KeyValuePair<string, object?>("operation", operation));
    }

    public void RecordUpload(string result)
    {
        _uploads.Add(1, new KeyValuePair<string, object?>("result", result));
    }

    public void RecordBucketFailure(string operation)
    {
        _bucketFailures.Add(1, new KeyValuePair<string, object?>("operation", operation));
    }

    public static class UploadResults
    {
        public const string Stored = "stored";
        public const string Deduplicated = "deduplicated";
        public const string QuotaExceeded = "quota_exceeded";
    }

    public static class BucketOperations
    {
        public const string Write = "write";
        public const string Read = "read";
        public const string Delete = "delete";
    }
}
