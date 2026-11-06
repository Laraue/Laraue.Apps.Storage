# Laraue.Apps.Storage

Internal file storage service for the Laraue.* apps. Every consuming app (`Laraue Boards` first) keeps
its users' uploaded files here over internal gRPC, in a Backblaze B2 bucket (S3 API): originals and the
image previews made from them. The service counts each owner's usage and checks it against the limit
the caller passes; the limits themselves stay in the calling app. See [AGENTS.md](AGENTS.md) for the
design and what's deliberately *not* built yet.

**Stage 1 (BRD-339)**: internal gRPC `UploadFile`, `GetFile`, `GetDownloadUrl` (a presigned bucket link
to redirect to - file bytes never go out through gRPC), `ConfirmReference`, `DeleteReference`,
`GetUsage`.

## License

Licensed under the [GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0-only), including the
`Laraue.Apps.Storage.Internal.Contracts` NuGet package. You're free to use, modify and self-host it,
for personal or any other use, as long as you follow the AGPL - in particular, if you run a modified
version as a network service, you must make your modified source available to its users.

A commercial license without the AGPL obligations is available on request - contact the author via
[GitHub](https://github.com/win7user10).
