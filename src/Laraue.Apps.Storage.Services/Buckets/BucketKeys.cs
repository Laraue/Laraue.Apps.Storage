namespace Laraue.Apps.Storage.Services.Buckets;

/// <summary>
/// The keys the two <see cref="IFileBucket"/>s are registered under.
/// </summary>
public static class BucketKeys
{
    /// <summary>
    /// Uploaded originals: the B2 bucket (a local folder in local runs and tests).
    /// </summary>
    public const string Originals = "originals";

    /// <summary>
    /// Previews on the server's disk.
    /// </summary>
    public const string Previews = "previews";
}
