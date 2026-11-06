using Laraue.Apps.Storage.Services.Resources;

namespace Laraue.Apps.Storage.Services;

/// <summary>
/// A request value this service doesn't accept - <c>INVALID_ARGUMENT</c>.
/// </summary>
public sealed class StorageValidationException(string message) : Exception(message);

/// <summary>
/// No such file for the calling service - <c>NOT_FOUND</c>.
/// </summary>
public sealed class StoredFileNotFoundException(Guid fileId) : Exception(string.Format(Errors.FileNotFound, fileId));

/// <summary>
/// No such file reference for the calling service (never was, or deleted) - <c>NOT_FOUND</c>.
/// </summary>
public sealed class FileReferenceNotFoundException(Guid referenceId)
    : Exception(string.Format(Errors.FileReferenceNotFound, referenceId));
