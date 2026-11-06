# AGENTS.md

Guidance for AI agents working in this repo. Human-readable conventions and gotchas that aren't
obvious from the code alone.

## What this project is

The internal file storage service for the Laraue.* ecosystem (BRD-226, BRD-339). A Laraue app
(`Laraue Boards` first; feedback screenshots, BRD-227, later) sends the files its users upload
here and keeps only the returned file ids. Its API is internal on purpose: users never call it, the app they're in does, over gRPC. File bytes
never go out through gRPC: the app keeps its own links (Boards' `/api/files/{id}`, so permissions stay
the app's) and answers them with a redirect to the file's public URL on this service's public host,
`storage.laraue.com`, behind Cloudflare. See "Downloads".

**Stage 1 (BRD-339)**: internal gRPC only (`Protos/storage.proto`, `StorageService`).
- `UploadFile(owner, user_id?, file_name, content_type, content, quota_bytes?)`, where `owner` is an
  `Owner` message (a oneof of `organization_id` or `user_id`) -> the stored file,
  its preview for an image, a new pending `reference_id`, the owner's usage after the upload, and
  whether it was a duplicate - or `quota_exceeded` (usage, quota, required bytes) with nothing stored.
- `ConfirmReference(reference_id)`, `DeleteReference(reference_id)` - see "References".
- `GetFile(file_id)` (name, type, size and `url` - see "Downloads"), `GetUsage(owner)`. Every
  `StoredFile` in a reply carries its `url`.
- Every call needs the `x-laraue-service-id` header, and a service only sees its own files: another
  service's file id is `NOT_FOUND`.

**Not built yet - future stages, don't add speculatively**:
- Recalculating an owner's usage from its files (the counter is updated on every upload/delete in the
  same transaction, so it shouldn't drift; add a repair job only if it does).
- Cleaning up content left behind when a write or delete failed half-way (logged and counted in
  `storage_bucket_failures_total`).
- Uploads straight from the client to the bucket (presigned upload links). An upload passes through the
  calling app and one gRPC message, since this service needs the bytes for the hash and the preview.
- Type checks beyond what previews need - see "What this service checks".

## Domain model

- `Owner` (table `owners`) - whoever files are stored for, with `UsageBytes`: the total size of its
  files, originals and previews. Created on its first upload. Abstract, one table (EF table-per-
  hierarchy, discriminator `Kind`, a check constraint keeps exactly one id column set):
  - `OrganizationOwner` - `OrganizationId`, the calling service's own organization id (Boards:
    the organization a file is uploaded in). Unique per `(ServiceId, OrganizationId)`.
  - `UserOwner` - `UserId`, the global Laraue user id from Identity (e.g. feedback screenshots,
    BRD-227). Unique per `(ServiceId, UserId)`.
  Owners are separate per calling service: the same user in two apps has two usages. Typed columns
  rather than one string key, so ids are validated and nobody parses a separator. Both unique indexes
  are filtered (`... is not null`), and `FilesService.LockOwner`'s `on conflict` repeats those
  filters - change them together.
- `StoredFile` (table `stored_files`) - one file, id a UUIDv7:
  - `Kind` `Original` or `Preview`. Both are stored under their own id as the key (32 hex digits, no
    prefix or extension - the type is in the object's headers), and both live in
    the one bucket; nothing is kept on the server's disk.
  - `ContentHash` - SHA-256 of an original, to find the same upload for the same owner.
  - `References` - its `FileReference` rows, one per upload (see "References"). The file and its
    preview are deleted with the last one.
  - `SourceFileId` - a preview's original (one preview per original, deleted with it).
  - `UploadedByUserId` - the global Laraue user id (Identity) of whoever uploaded it first, for audit
    only. This service doesn't call Identity.
- `FileReference` (table `file_references`) - one upload's use of an original, id a UUIDv7,
  `Pending` or `Confirmed`.
- `Service` - a consuming app (`LaraueBoards`), seeded via `HasData` from `Data/ServicesData.cs`,
  same as Feedback. Adding a caller means a new enum value, a `ServicesData` entry, a migration and a
  matching value in the proto's `ServiceId`.

## References

Uploading and saving the caller's own link (Boards' attachment) can't be one transaction across two
services. So every upload returns a reference of its own, and the caller owns its life:
1. `UploadFile` adds a **pending** reference (a duplicate too - each upload gets its own).
2. The caller saves its link, then calls `ConfirmReference`. Confirming twice is a no-op.
3. `DeleteReference` deletes exactly that reference; deleting it again, or an unknown one, is a no-op,
   so the caller can retry after a timeout without dropping someone else's reference to a shared file.
   The file and its preview go with the last reference, and the owner's usage goes down.
4. `PendingReferencesCleaner` (background, every 10 minutes, `InternalApiHost`) deletes references
   still pending after `Storage:PendingReferenceLifetime` (24 h): uploads whose caller failed before
   saving its link. A pending upload counts toward the quota until then, so not confirming isn't a way
   around the limit.

The caller should record "confirm X" / "delete X" in its own transaction (an outbox) and retry them
until they succeed: a link saved but never confirmed would lose its file after 24 h. Deletes and the
cleaner lock the owner row and re-read the reference under the lock, so a reference confirmed while
the cleaner runs is kept.

## Quota

The service knows usage, not limits: the caller passes its owner's limit as `quota_bytes` on each
upload (Boards: 100 MB on Free, 1 GB plus add-ons on paid tariffs, BRD-340/341). The check runs
under a row lock on the owner (`select ... for update` in `FilesService.LockOwner`), so two uploads
at the same moment can't both pass it. What counts is the original plus its preview. A duplicate
never exceeds the quota, since it adds nothing. Leaving `quota_bytes` unset means no limit.

## What this service checks

- Size: at most `Storage:MaxFileSizeBytes` (20 MB), not empty. Callers enforce their own,
  smaller per-type limits first (Boards: images 3 MB, documents 10 MB) and their own allowed types -
  Boards' `AttachmentFileTypes` stays in Boards, since it also judges files from Telegram messages
  that never come here.
- Owner, content type (a `type/subtype`), file name (directories and control characters dropped, cut
  to 255 characters with the extension kept).
- Previews (`PreviewMaker`, SkiaSharp): made from the content, not the declared type - anything Skia
  reads (JPEG, PNG, GIF, WebP, BMP...) gets a WebP at most 640 px on its long side: the first frame of
  an animation, turned upright by its EXIF orientation (phone photos), never scaled up. Memory is
  bounded on purpose (the server has little to spare): images over 40 megapixels are skipped, and a
  JPEG is decoded straight at a reduced scale (1/2, 1/4, 1/8). A file Skia can't read is stored without
  a preview. SkiaSharp (MIT) rather than ImageSharp: every ImageSharp release without known
  vulnerabilities needs a Six Labors license key to build in Release. The Docker image uses
  `SkiaSharp.NativeAssets.Linux.NoDependencies`, so no system libraries are needed.

## Project layout

Solution: `Laraue.Apps.Storage.sln`. Structured the same way as `Laraue.Apps.Feedback` and
`Laraue.Apps.Identity` - `Host -> Host{Services} -> Services`, a host never referencing `Services`
directly, only through its own `Host{Services}` project.

- `src/Laraue.Apps.Storage.DataAccess` - EF Core `DatabaseContext`, entities, migrations.
- `src/Laraue.Apps.Storage.Internal.Contracts` - the `.proto` contract plus generated stubs
  (`GrpcServices="Both"`), `GrpcHeaders` and `ServiceIdInterceptor` (unary and server-streaming
  calls). `IsPackable=true` - published to NuGet.org as
  `Laraue.Apps.Storage.Internal.Contracts` via `.github/workflows/nuget-publish.yml` (manual trigger;
  `main` publishes the base `<Version>` from `Directory.Build.props`, any other branch appends
  `-alpha.<run number>`).
- `src/Laraue.Apps.Storage.Services` - core logic, not tied to gRPC: `FilesService` (upload with
  dedup and quota, public URLs, references, usage), `Buckets` (`IFileBucket`: `S3FileBucket` for B2,
  `LocalFileBucket` for tests), `PreviewMaker`,
  `StorageMetrics`. Invalid input is a `StorageValidationException` with an `Errors.resx` message.
- `src/Laraue.Apps.Storage.InternalApiServices` - `StorageGrpcService` (wire <-> domain types,
  exceptions -> status codes) and the DI composition (`AddInternalApiServices()`).
- `src/Laraue.Apps.Storage.WebApiServices` - `PublicFilesService` (which keys are files, reading them)
  and its DI (`AddWebApiServices()`); `src/Laraue.Apps.Storage.WebApiHost` - `Program.cs` and a thin
  `FilesController`, one HTTP/1.1 port (`Urls`, `http://localhost:5565` locally). See "Downloads".
  The generated
  proto types share names with the domain ones (`ServiceId`, `StoredFile`, `QuotaExceeded`), so the
  file aliases them as `Contracts.`/`Domain.`.
- `src/Laraue.Apps.Storage.InternalApiHost` - the gRPC host: `Program.cs` only (Kestrel h2c, DI,
  OpenTelemetry, migrations on startup). Two separate ports (`Kestrel:GrpcPort`/`:HealthPort`,
  5563/5564 by default - Feedback uses 5463/5464, Identity 5363/5364, Billing 5263/5264): one
  HTTP/2-only for gRPC, one HTTP/1.1-only for `/_health`/`/_metrics`. Kestrel can't serve HTTP/1.1
  and cleartext HTTP/2 on one endpoint without TLS; don't "simplify" it back to one port.
  `MaxReceiveMessageSize` is 32 MB, since an upload is one message with the whole file.
- `tests/Laraue.Apps.Storage.IntegrationTests` - a `WebApplicationFactory<Program>` host that hands
  back the real generated gRPC client over the in-memory `TestServer`. Files go to a temp folder
  instead of B2 (`RecordingFileBucket`, which also remembers each object's headers), `Storage:PublicBaseUrl`
  is that folder's `file://` URL so files' URLs point into it, and `host.PublicHost` (`WebApiTestHost`)
  is the public host over the same folder.
  `using var testScope = host.CreateTestScope();` cleans the database and the folder; test classes are
  `[Collection("IntegrationTest")]`.

## Downloads

Every file has one public, permanent URL: `{Storage:PublicBaseUrl}/{key}`, i.e.
`https://storage.laraue.com/<id>`, for an original and a preview alike.
- **The public host** (`WebApiHost`) answers it: it streams the object from the **private** bucket with
  a read-only key and the headers stored at upload. It has no database and no gRPC, and serves only keys
  shaped like a file id (`PublicFilesService`); anything else, a deleted file included, is 404.
  It always sends the whole file.
- **Cloudflare** proxies `storage.laraue.com` and caches every response for a day (a cache rule makes the
  extension-less originals cacheable too), so only cache misses reach the host and B2. Byte ranges
  (video seeking) are served by Cloudflare from its cached copy. `storage_public_reads_total{result}`
  counts what reached the host.
- **Why not a public bucket:** Backblaze only allows public buckets on accounts with a payment method.
  The private bucket plus this host needs none, and the bucket key never leaves the server.
- **Access** is knowing the URL: its key holds the file's UUIDv7, which isn't guessable and is only
  learned from data a user could read - the same model as Boards' own `/api/files/{id}`, which checks no
  permission either.
- **Headers are fixed at upload** (`FileObjectHeaders`, stored with the object): the content type;
  `inline` for images and videos (not SVG), otherwise `attachment; filename=...` with the stored name -
  so a deduplicated file keeps its first upload's name; and `public, max-age=86400`. The host adds
  `nosniff` and `Content-Security-Policy: sandbox; default-src 'none'`.
- **Deleting** removes the object from B2 but purges nothing: Cloudflare and browsers may serve a
  deleted file for up to a day (the `max-age`), to whoever already has its URL. That window is accepted
  (2026-11-06) in exchange for no Cloudflare API token and no purge code.

## The bucket

- A Backblaze B2 bucket of its own, separate from the restic backup bucket, reached through the
  S3-compatible API (`AWSSDK.S3`). The bucket is **private**. Two application keys, both limited
  to this bucket: the internal host's needs write, **list and delete** (B2 keeps every version of a file
  by default, and a plain
  `DeleteObject` only hides it (still stored and billed), so `S3FileBucket.Delete` lists and deletes
  each version); the public host's only reads.
- The SDK's default CRC checksum headers are turned off (`WHEN_REQUIRED`) and payloads are signed in
  one piece (`UseChunkEncoding = false`), for S3-compatible APIs that don't take them.
- Writes happen before the database commit, deletes after it: a failure leaves unused bytes behind
  (logged, counted), never a row pointing at missing content.

## Options and configuration

Same convention as Boards and Feedback: every options class through `AddValidatedOptions`
(validation and `ValidateOnStart()` in Production only), required values `[Required]`. Secrets never
go into a committed `appsettings.json` - production values come from the deploy server's environment.

Settings `InternalApiHost` needs:
- `ConnectionStrings:Postgre`.
- `Storage:MaxFileSizeBytes` (20 MB) and `Storage:PendingReferenceLifetime` (24 hours) - committed
  in `appsettings.json`, not as defaults in the options class; production doesn't override them.
- `Bucket:ServiceUrl`, `Bucket:Region`, `Bucket:BucketName`, `Bucket:KeyId`, `Bucket:ApplicationKey`
  - not committed; Production validates them at startup.
- `Storage:PublicBaseUrl` - where the public host is reached (`https://storage.laraue.com`). Production
  validates it; for local runs set it to the local public host, `http://localhost:5565`, in
  `appsettings.Development.json`.
- `Bucket:LocalDirectory` - tests and local runs without a bucket: files go to that folder instead of
  B2 and the `Bucket:*` credentials aren't needed. Set the same folder for both hosts locally.

Settings `WebApiHost` needs: `Bucket:*` with the **read-only** key (validated in production), or
`Bucket:LocalDirectory` locally; `Urls` (committed as `http://localhost:5565`).

## EF Core

- Snake_case naming convention (`.UseSnakeCaseNamingConvention()`), same as Identity/Billing/Feedback.
- Timestamps are UTC `DateTime` from `IDateTimeProvider`, not `DateTime.UtcNow`.
- Project straight to the needed shape with `.Select(...)`; load entities only to change them.

## User-facing text

No string literals in `throw new SomeException("...")`. Use `Resources/Errors.resx` (+ the
hand-maintained `Errors.Designer.cs` - `dotnet build` doesn't regenerate it; add the matching property
by hand, mirroring an existing entry).

## Logging

Always `ILogger<T>`, never the bare non-generic `ILogger`.

## Metrics

Prometheus on `InternalApiHost`'s `/_metrics` (health port), meter `Laraue.Apps.Storage`. Same
conventions as Feedback/Identity/Billing: labels are low-cardinality - never an owner or file id.
- Counters (`StorageMetrics`, events, every label value starts at zero):
  `storage_uploads_total{result}` (stored, deduplicated, quota_exceeded) and
  `storage_bucket_failures_total{operation}` (write, delete) - the Infra alert on bucket errors
  watches the latter. The public host (its own `/_metrics`) counts `storage_public_reads_total{result}`
  (served - a Cloudflare cache miss - and not_found).
- Gauges (`StorageStateMetrics`, read from the database every 60 s by a background service, a scrape
  only reads the last snapshot): `storage_stored_bytes{kind}` and `storage_stored_files{kind}`
  (`original`, `preview` - all in the bucket, so the sum is what B2 bills), `storage_owners{kind}` (organizations and users with files) and
  `storage_usage_bytes` (the owners' usage counters summed - equal to the stored bytes summed unless a
  counter drifted). From the database, not counted in the buckets: exact, and they survive restarts.

## Database safety

Migrations run automatically on startup (`InternalApiHost` and the integration test host). **Never
run `dotnet ef database drop`** - `--startup-project` decides which connection string a command
targets (dev `storage` vs. test `storage_tests`). Confirm the target of any destructive command first.

## Build-lock protocol

`MSB3026`/`MSB3027` file-lock errors mean `InternalApiHost` is running locally and holding the DLLs.
Don't kill it - ask the user to stop it, then retry.

## Deploy

Same shape as Billing's `.github/workflows/dotnet.yml`: the `deploy` job (push to `main` only) deploys
two hosts, each behind its own GitHub environment approved by hand - `InternalApiHost` (environment
`internalapi-production`, to `/home/laraue/storage-internal-api-host`, compose service
`storage-internal-api-host`) and `WebApiHost` (environment `webapi-production`, to
`/home/laraue/storage-web-api-host`, compose service `storage-web-api-host`). Set up once by a human, not
in this repo: both compose services and the nginx site for `storage.laraue.com` (`Laraue.Apps.Infra`), the
database `laraue_storage` (created by the migrations on first start), the `STORAGE_*` values in the
server's `.env`, the private B2 bucket and its two keys, the Cloudflare side of `storage.laraue.com` (a
proxied DNS record to the server, a cache rule caching everything on it), and this repo's GitHub
environments/secrets (`SSH_*`, `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID` for deploy notifications,
`NUGET_USER` for publishing).

## Testing

Naming: `{Handler}_Should{ExpectedBehavior}_When{Condition}`. One `[Fact]`/`[Theory]` per case.
Assert on observable behavior (the gRPC response, stored rows, files in the temp folders), not on logs.

## Workflow for new features

Move step by step from the data model up (entities/migration, services, gRPC, tests), stopping after
each step for the user's review instead of one big diff.

## Task flow

Branch per task: `feature/<task-number>-<description>`, e.g. `feature/BRD-339-storage-service`.
