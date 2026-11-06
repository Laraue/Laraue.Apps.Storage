namespace Laraue.Apps.Storage.Internal.Contracts;

/// <summary>
/// Metadata header names used across this contract, shared between clients (attach via an
/// interceptor, not per-call) and <c>StorageGrpcService</c> (reads via
/// <c>ServerCallContext.RequestHeaders</c>). gRPC metadata keys must be lowercase.
/// </summary>
public static class GrpcHeaders
{
    /// <summary>
    /// Which calling service this request is from, as the numeric value of <see cref="ServiceId"/>
    /// (e.g. "1" for <see cref="ServiceId.LaraueBoards"/>) - identifies the caller once per
    /// connection/client rather than being repeated on every request message.
    /// </summary>
    public const string ServiceIdHeaderName = "x-laraue-service-id";
}
