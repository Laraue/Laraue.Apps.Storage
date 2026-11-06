# AGENTS.md

Guidance for AI agents working in this repo. Human-readable conventions and gotchas that aren't
obvious from the code alone.

## What this project is

The internal file storage service for the Laraue.* ecosystem (BRD-226, BRD-339). A Laraue app
(`Laraue Boards` first; feedback screenshots, BRD-227, later) sends the files its users upload
here and keeps only the returned file ids. Its API is internal on purpose: users never call it, the app they're in does, over gRPC. File bytes
never go out through gRPC: the app keeps its own links (Boards' `/api/files/{id}`, so permissions stay
the app's) and answers them with a redirect to a signed link - to the bucket, or to this service's
small public web host for files on the server's disk. See "Downloads".

**Stage 1 (BRD-339)**: internal gRPC only (`Protos/storage.proto`, `StorageService`).
- `UploadFile(owner, user_id?, file_name, content_type, content, quota_bytes?)`, where `owner` is an
  `Owner` message (a oneof of `organization_id` or `user_id`) -> the stored file,
  its preview for an image, the owner's usage after the upload, and whether it was a duplicate - or
  `quota_exceeded` (usage, quota, required bytes) with nothing stored.
- `GetFile(file_id)`, `GetDownloadUrl(file_id, attachment, download_file_name?)` (a signed link to
  redirect to - see "Downloads"),
  `DeleteFile(file_id)`, `GetUsage(owner)`.
- Every call needs the `x-laraue-service-id` header, and a service only sees its own files: another
  service's file id is `NOT_FOUND`.

**Not built yet - future stages, don't add speculatively**:
- Recalculating an owner's usage from its files (the counter is updated on every upload/delete in the
  same transaction, so it shouldn't drift; add a repair job only if it does).
- Cleaning up content left behind when a write or delete failed half-way (logged and counted in
  `storage_bucket_failures_total`).
- Uploads straight from the client to the bucket (signed upload links). An upload passes through the
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
  - `Kind` `Original` (in the bucket, `files/<id>`) or `Preview` (on local disk under
    `Storage:PreviewsDirectory`, sharded by the id's last characters, `<id>.webp`).
  - `ContentHash` - SHA-256 of an original, to find the same upload for the same owner.
  - `References` - how many uploads returned this original and haven't been deleted. A duplicate
    upload adds one and stores nothing; `DeleteFile` drops one and deletes the file and its preview
    only at zero. So a caller deletes once per upload it made, without tracking duplicates itself.
  - `SourceFileId` - a preview's original (one preview per original, deleted with it).
  - `UploadedByUserId` - the global Laraue user id (Identity) of whoever uploaded it first, for audit
    only. This service doesn't call Identity.
- `Service` - a consuming app (`LaraueBoards`), seeded via `HasData` from `Data/ServicesData.cs`,
  same as Feedback. Adding a caller means a new enum value, a `ServicesData` entry, a migration and a
  matching value in the proto's `ServiceId`.

## Quota

The service knows usage, not limits: the caller passes its owner's limit as `quota_bytes` on each
upload (Boards: 100 MB on Free, 1 GB plus add-ons on paid tariffs, BRD-340/341). The check runs
under a row lock on the owner (`select ... for update` in `FilesService.LockOwner`), so two uploads
at the same moment can't both pass it. What counts is the original plus its preview. A duplicate
never exceeds the quota, since it adds nothing. Leaving `quota_bytes` unset means no limit.

## What this service checks

- Size: at most `Storage:MaxFileSizeBytes` (20 MB by default), not empty. Callers enforce their own,
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
  dedup and quota, download locations, delete, usage), `Buckets` (`IFileBucket`: `S3FileBucket` for B2,
  `LocalFileBucket` for previews and for originals in local runs/tests), `PreviewMaker`,
  `StorageMetrics`. Invalid input is a `StorageValidationException` with an `Errors.resx` message.
- `src/Laraue.Apps.Storage.InternalApiServices` - `StorageGrpcService` (wire <-> domain types,
  exceptions -> status codes) and the DI composition (`AddInternalApiServices()`).
- `src/Laraue.Apps.Storage.WebApiServices` - `FilesEndpoint` (the public route) and its DI
  (`AddWebApiServices()`); `src/Laraue.Apps.Storage.WebApiHost` - its `Program.cs`, one HTTP/1.1 port
  (`Urls`, `http://localhost:5565` locally). See "Hosts: internal and public". The generated
  proto types share names with the domain ones (`ServiceId`, `StoredFile`, `QuotaExceeded`), so the
  file aliases them as `Contracts.`/`Domain.`.
- `src/Laraue.Apps.Storage.InternalApiHost` - the gRPC host: `Program.cs` only (Kestrel h2c, DI,
  OpenTelemetry, migrations on startup). Two separate ports (`Kestrel:GrpcPort`/`:HealthPort`,
  5563/5564 by default - Feedback uses 5463/5464, Identity 5363/5364, Billing 5263/5264): one
  HTTP/2-only for gRPC, one HTTP/1.1-only for `/_health`/`/_metrics`. Kestrel can't serve HTTP/1.1
  and cleartext HTTP/2 on one endpoint without TLS; don't "simplify" it back to one port.
  `MaxReceiveMessageSize` is 32 MB, since an upload is one message with the whole file.
- `tests/Laraue.Apps.Storage.IntegrationTests` - a `WebApplicationFactory<Program>` host that hands
  back the real generated gRPC client over the in-memory `TestServer`, and `host.PublicHost`
  (`WebApiTestHost`), the public host over the same folders with the same signing key, which tests
  download links through. Originals and previews go to
  temp folders (`Bucket:LocalDirectory`, `Storage:PreviewsDirectory`), so no test reaches B2.
  `using var testScope = host.CreateTestScope();` cleans the database; test classes are
  `[Collection("IntegrationTest")]`.

## Downloads

`GetDownloadUrl` returns a signed link the caller redirects its client to (302), with its expiry:
- **Originals** (the bucket): a presigned S3 GET link (`S3FileBucket.GetDownloadUrl`, signed locally,
  no request to B2), valid `Storage:DownloadUrlLifetime` (30 min by default - long enough for a video's
  range requests on the same link). `response-content-type` and `response-content-disposition` make B2
  answer with the stored type and `inline` or `attachment; filename=...` (the caller's name for the
  download). B2 serves byte ranges itself.
- **Previews** (this server's disk): a link to the public web host (`WebApiHost`,
  `{Storage:PublicBaseUrl}/files/previews/<key>?ct=..&cd=..&exp=..&sig=..`), signed by
  `FileLinkSigner` (HMAC-SHA256 over area, key, type, disposition and expiry, key `FileLinks:SigningKey`
  shared by both hosts). It expires at the end of tomorrow (UTC), so the link is the same all day and
  browsers cache the preview (`Cache-Control: private, max-age` up to the expiry) while boards re-render.
- In local runs and tests, originals with `Bucket:LocalDirectory` get the same kind of link
  (`/files/originals/...`) instead of a B2 one.

Files are always served from a domain of their own (B2's, or the public host's, e.g.
`files.laraue.com`), never an app's, so an uploaded file can't run as a page on the app's origin. The
public host also sends `nosniff` and a sandbox CSP.

## Hosts: internal and public

- `InternalApiHost` - gRPC (HTTP/2 port) plus `/_health`/`/_metrics` (HTTP/1.1 port), on the docker
  network only. Owns the database and the bucket, and writes the previews volume. Nothing public.
- `WebApiHost` - the public side, behind nginx: one route, `GET /files/{area}/{key}`, serving
  `previews` from its own read-only mount of the previews volume (and `originals` from
  `Bucket:LocalDirectory` in local runs/tests). It has no database, no bucket credentials and no
  gRPC: a valid signature and expiry are the only permission, and a wrong or expired link, an unknown
  area, a path outside the folder and a missing file are all 404, so it says nothing about which files
  exist. `/_health` and `/_metrics` are for the docker network; nginx proxies `/files/` only.

## The bucket

- A Backblaze B2 bucket of its own, separate from the restic backup bucket, reached through the
  S3-compatible API (`AWSSDK.S3`) with an application key limited to this bucket. The key needs
  read, write, **list and delete** (signing download links needs no extra permission, but the links
only work with read): B2 keeps every version of a file by default, and a plain
  `DeleteObject` only hides it (still stored and billed), so `S3FileBucket.Delete` lists and deletes
  each version.
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
- `Storage:PreviewsDirectory` - a volume on the server's disk. Committed as `storage-data/previews`
  for local runs. `Storage:MaxFileSizeBytes` - optional, 20 MB by default.
- `Bucket:ServiceUrl`, `Bucket:Region`, `Bucket:BucketName`, `Bucket:KeyId`, `Bucket:ApplicationKey`
  - not committed; Production validates them at startup.
- `Storage:DownloadUrlLifetime` - optional, 30 minutes by default.
- `Storage:PublicBaseUrl` - where clients reach `WebApiHost` (`https://files.laraue.com`). Committed
  as `http://localhost:5565` for local runs.
- `FileLinks:SigningKey` - at least 32 characters, the same in both hosts. Not committed; put a
  local one in both hosts' `appsettings.Development.json` (git-ignored).
- `Bucket:LocalDirectory` - local runs and tests only: originals go to that folder instead of B2, and
  the `Bucket:*` credentials aren't needed. Set it in both hosts' `appsettings.Development.json`, so the
  public host serves those originals too.

Settings `WebApiHost` needs: `Storage:PreviewsDirectory` (the same volume, mounted read-only),
`FileLinks:SigningKey`, and `Bucket:LocalDirectory` only in local runs. Its committed
`appsettings.json` points at the internal host's local previews folder and listens on 5565.

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
  watches the latter.
- Gauges (`StorageStateMetrics`, read from the database every 60 s by a background service, a scrape
  only reads the last snapshot): `storage_stored_bytes{location}` and `storage_stored_files{location}`
  (`bucket` - B2 originals, what B2 bills; `local_disk` - previews on the server's disk, next to
  BRD-347's disk alerts), `storage_owners{kind}` (organizations and users with files) and
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
`/home/laraue/storage-web-api-host`, compose service `storage-web-api-host`). Set up once by a human,
not in this repo: both compose services (`Laraue.Apps.Infra`, `services/storage-*/`), the database
`laraue_storage` (created by the migrations on first start), the previews volume (read-write in the
internal host, read-only in the web host), the `Bucket__*` and `FileLinks__SigningKey` values in the
server's `.env`, the `files.laraue.com` DNS record, certificate and nginx site (proxying `/files/` only),
and this repo's GitHub environments/secrets (`SSH_*`, `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID` for deploy
notifications, `NUGET_USER` for publishing).

## Testing

Naming: `{Handler}_Should{ExpectedBehavior}_When{Condition}`. One `[Fact]`/`[Theory]` per case.
Assert on observable behavior (the gRPC response, stored rows, files in the temp folders), not on logs.

## Workflow for new features

Move step by step from the data model up (entities/migration, services, gRPC, tests), stopping after
each step for the user's review instead of one big diff.

## Task flow

Branch per task: `feature/<task-number>-<description>`, e.g. `feature/BRD-339-storage-service`.
