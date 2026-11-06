namespace Laraue.Apps.Storage.Services.Buckets;

public static class FileBucketRanges
{
    /// <summary>
    /// The inclusive byte positions <paramref name="range"/> covers in an object of <paramref name="totalLength"/>
    /// bytes, clamped to its end like HTTP does. Throws <see cref="FileBucketRangeNotSatisfiableException"/> when
    /// nothing of the object is in the range.
    /// </summary>
    public static (long From, long To) Resolve(FileBucketRange range, long totalLength)
    {
        if (range.SuffixLength is { } suffix)
        {
            if (suffix <= 0 || totalLength == 0)
                throw new FileBucketRangeNotSatisfiableException(totalLength);

            return (Math.Max(totalLength - suffix, 0), totalLength - 1);
        }

        var from = range.From ?? 0;
        if (from >= totalLength)
            throw new FileBucketRangeNotSatisfiableException(totalLength);

        var to = range.To is { } end && end < totalLength ? end : totalLength - 1;
        return (from, to);
    }
}
