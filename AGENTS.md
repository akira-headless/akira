# AGENTS.md

Instructions for coding agents working in this repository.

**Akira**: a headless personal media server for one household, API first. The design and its reasons are in `../docs/`, one file per topic (`../docs/README.md`); read the files of an area before working in it. `docs` sits next to the repositories in `D:\Projects\akira` and is part of none of them.

## What this project is
- A **.NET 10 backend** that owns the library, track selection, watch state and the Radarr/Sonarr integration.
- **Clients are thin**, and more may follow, so the backend never assumes a client.
- Radarr and Sonarr are the **source of truth** for what exists. We do not scrape metadata.

## Core rules (do not break these)
1. **API first.** Every feature is in the versioned REST API (`/api/v1`) and the OpenAPI document. No client-specific endpoints.
2. **The server decides, clients execute.** Track and playback decisions are made on the server and returned as a *playback plan*.
3. **Stable ids everywhere.** Never expose or store ffprobe stream indexes as ids. The index is only a hint inside a plan.
4. **Watch state belongs to the logical item** (movie or episode, keyed by TMDB/TVDB ids), never to a file. A file upgrade keeps progress, watched state and track preferences.
5. **Track preferences are stored as attributes** (language, codec class, flags), not stream ids, so they survive upgrades and apply across a series.
6. **Real time over plain WebSocket + JSON**, with a catch-up endpoint for missed events.
7. **Destructive actions** (delete, unmonitor) go through action endpoints, need explicit parameters and are logged.

## Stack
- .NET 10, ASP.NET Core MVC controllers (as in Lingarr), `Microsoft.AspNetCore.OpenApi`, Scalar in Development.
- **SQLite** (default) or **MariaDB** (`Database:Provider`). EF Core at runtime (MariaDB through `Microting.EntityFrameworkCore.MySql`, the maintained Pomelo fork), **FluentMigrator** for the schema: one migration set for both.
- ffprobe and ffmpeg as separate processes.
- xUnit v3 + Moq, `WebApplicationFactory` for endpoints, Testcontainers for MariaDB.
- Central package management: versions only in `Directory.Packages.props`.
- Docker; Radarr and Sonarr are external.

## Repo layout
Every project sits at the repository root, as in Lingarr. Namespaces follow the project (`Akira.Core.Playback`);
`Akira.Application` shares the host's root namespace, `Akira.Server`.
```
Akira.Server/               the host: Controllers/ (under /api/v1, base ApiController, and BridgeControllerConvention),
                            Endpoints/ (ApiEnum, ServiceResults, MediaResults, BridgeEndpoints), Extensions/ (all DI and pipeline wiring),
                            Authentication/ (handler, result handler, rate limits, setup gate), Workers/ (hosted services)
Akira.Application/          what the API and plugins call: Interfaces/Services/, Services/, Models/{Area}/ (records that
                            cross the API), Events/, Webhooks/, Authentication/, Bridges/ (the plugin facades), Plugins/,
                            Endpoints/ItemRoutes.cs and Playback/StreamRoutes.cs (the urls services hand out)
Akira.Core/                 entities, enums and pure logic (library, track resolver, playback planner), no I/O
Akira.Infrastructure/       EF Core, upstream clients (Integrations/), sync, ffprobe and ffmpeg runners
Akira.Migrations/           FluentMigrator migrations (Migrations/M{NNNN}_{Name}.cs)
Akira.Contracts/            the plugin contract, the only assembly a plugin references
Akira.*.Tests/              Akira.Core.Tests, Akira.Infrastructure.Tests, Akira.Migrations.Tests, Akira.Server.Tests
                            (endpoints, OpenAPI, startup)
Akira.Testing/              shared test helpers, not a test project
Akira.Server.Testing/       the server in a test host (AkiraApiFactory, seeds)
Akira.Client/               the web admin UI (Vue 3, Vite, Tailwind, Pinia), built into Akira.Server/wwwroot
Akira.Docs/                 the user guide (VitePress): the server only, headless, no apps; public/ holds the mark and icon
README.md                   for users, after Lingarr's: what Akira does, install, main settings; then building it yourself and Portainer
docker-compose*.yml         docker-compose.yml (build and run), .mariadb.yml (with MariaDB), .portainer.yml (the stack)
```
Compose files carry no comments: what a variable means goes in README.md or the guide.
The built-in subtitle plugins (OpenSubtitles, Lingarr) stay in `Akira.Infrastructure`.

## Conventions
- One type per file, named after the type, as in Lingarr. Only `Program.cs` and `AssemblyInfo.cs` hold none.
- Dependencies like Lingarr: `private readonly _camelCase` fields and an explicit constructor (one parameter per line when there are several), never a primary constructor. Records keep theirs.
- `Akira.Core` stays free of I/O.
- Every new endpoint: request and response records, OpenAPI metadata and at least one test.
- Tests are selective: one or two per change, for what can break (pure logic, a real ffmpeg or upstream shape, a security rule, a bug that happened). Prefer extending a flow test over a new one.
- `CancellationToken` throughout, no blocking calls.
- Settings: each section binds to an options class with a `SectionName` constant, a validator and `ValidateOnStart`, so a half-configured setting stops the server with the key named. Never read a raw key string. Environment overrides use `__` (`Database__SqlitePath`). The options classes hold the keys and defaults; `Akira.Docs/getting-started/configuration.md` lists what users set. Passwords come from the environment, never a committed file. `DatabaseConnection.DisplayName` is the only form of a connection that may be logged.

### API
- **Controllers, like Lingarr.** One per area, derived from `ApiController`, one named method per action. Actions return `TypedResults` unions (`Results<Ok<T>, ProblemHttpResult>`), so every status is in the OpenAPI document without `[ProducesResponseType]`. Metadata is attributes: `[EndpointName]`, `[EndpointSummary]`, `[Tags]`, `[Authorize(Policy = ...)]`, `[AllowAnonymous]`, `[EnableRateLimiting]`. Action templates are relative. Bodies are `application/json` only, and a missing value reaches the service (`SuppressImplicitRequiredAttributeForNonNullableReferenceTypes`). Diff `/openapi/v1.json` before and after a change to catch accidents.
- Actions stay thin: bind, call a service, shape the response. Logic is in `Akira.Application`, behind an interface, registered in `ServiceCollectionExtensions`. Application never references the host.
- **Never return an EF entity.** Every response is a record in `Models/`; a list and a detail are two records.
- Serializer (verified by `OpenApiDocumentTests`): camelCase properties, enums as camelCase strings, nulls written, strict numbers (`"24"` for an int is a 400), no cycle handling.
- Errors are RFC 9457 problem details. `AddProblemDetails` goes before `AddControllers`, or validation problems lose their `errors`. A binding failure is a 400 in every environment.
- `/openapi/v1.json` in every environment, Scalar at `/scalar` in Development only.
- **Enum route and query parameters use `ApiEnum<T>`**, never the enum: the default binding is case-sensitive and takes any number. An operation transformer puts the values in the OpenAPI document.
- Lists are `{ items, total, offset, limit }`, `limit` 1 to 200. Urls handed to clients come from `ItemRoutes`, the constants the routes use.

### Integrations (Radarr, Sonarr, artwork hosts)
- Every upstream call goes through `UpstreamHttp`, which turns each failure into an `UpstreamException` with a distinct `UpstreamFailure`. A 200 with HTML (a login page or proxy) is malformed, never an empty list.
- Every upstream `HttpClient` has a timeout and follows no redirects (`UpstreamHttp.CreateHandler`).
- The API key travels in `X-Api-Key`, never in a url, log line or exception message.
- Radarr and Sonarr share the HTTP layer (same Servarr codebase); their resources and clients stay separate.
- Upstream images: only `image/jpeg|png|webp|gif`, at most 20 MB, content type decided by the server. Remote artwork urls pass `Artwork.ValidateRemoteUrl` (https, allow-listed host) when stored and when fetched.
- Tests read real Radarr 6 / Sonarr 4 responses from `Akira.Infrastructure.Tests/Fixtures/`. A surprising shape gets its real response added there.

### Database and migrations
One migration set runs on SQLite and MariaDB and CI only proves what a test exercises, so think every construct through on both.
- The schema changes only through `Akira.Migrations/Migrations/M{NNNN}_{Name}.cs` with a matching `[Migration(N)]` (list the folder for the next number; `MigrationConventionTests` checks). `M0001_Baseline` is empty on purpose.
- Never edit a shipped migration. `Down()` really reverses `Up()`. Guard re-runs and upgrades with `Schema.Table("x").Column("y").Exists()`.
- **Never `.AsString(n)`**: on MariaDB it is utf8mb3 `NVARCHAR`. Unbounded text is `.AsCustom("TEXT")` or `LONGTEXT`, an indexed string `.AsCustom("VARCHAR(n)")`.
- **Column types match EF on MariaDB**: `.AsDateTime()` is `DATETIME` where EF maps `datetime(6)`, and EF maps an unbounded string to `longtext`. Pick the matching type or set `HasColumnType`/`HasMaxLength`. `SchemaTests` checks every table, column, type and utf8mb4 on both engines, so a new entity lands with its migration.
- Provider blocks use `IfDatabase(MigrationDatabase.Sqlite)` or `.MariaDb`, never a raw string (`MigrationDatabaseTests`). Raw `Execute.Sql` only inside one. SQLite needs a table rebuild to drop, rename or retype a column.
- MariaDB compares text case-insensitively, SQLite case-sensitively; decide which a unique index means.
- Names are snake_case (`UseSnakeCaseNamingConvention()`). Entity configuration is an `IEntityTypeConfiguration<T>` in `Akira.Infrastructure`.
- Timestamps are `DateTimeOffset`, stored as UTC (`UseUtcTimestamps()`). The server has no time zone setting.
- Entities derive from `BaseEntity` (a `Guid.CreateVersion7()` id set up front, `CreatedAt`/`UpdatedAt` stamped on save). So **add every new row explicitly**; one only reachable through a navigation is taken for an existing row and fails with a concurrency exception.
- SQLite runs with WAL, `busy_timeout` and foreign keys (`SqlitePragmaConnectionInterceptor`). `DatabasePreflight` creates the SQLite folder and refuses a MariaDB database that is not utf8mb4.
- Endpoint and flow tests run on SQLite. A MariaDB subclass (`[Trait("Category", "Integration")]`, `MariaDbContainerFixture`) only where the engines differ; a new one needs that reason.

### Library sync
- A path from Radarr/Sonarr goes through `LibraryPaths`: under a path mapping when there are any, no `.` or `..` segment, never in the server's own folders (`ServerFolders`) or `/proc`, `/sys`, `/dev`, `/etc`, `/run`. A refused file is reported in `SyncResult.Problems`. An upstream never decides which file of the server is served.
- An item is identified by `IdentityKey` (built with `IdentityKeys` only), never a Radarr/Sonarr id. The same title in several instances is one item.
- Per instance: an `ArrLink` per item, at most one `MediaFile` per item, its own `MediaImage` rows. A sync only touches its own instance's rows. `LibrarySyncService` then deletes items no instance links and drops rows of removed instances; with no instances configured it deletes nothing.
- A failed list call unlinks nothing; a failed episode list leaves that series alone.
- An upgraded or renamed file is a new `MediaFile`; the item stays.
- Per user state is keyed by `IdentityKey` with no foreign key to items, so deleting an item never deletes history.

### Playback
- Track and version decisions are pure, in `Akira.Core/Playback` (`TrackResolver`, `VersionSelector`, `WatchProgress`). Services load, call and store. A new rule gets a resolver test with a real track layout first.
- ffprobe codec names go through `Formats` before they are compared with client capabilities.
- A remembered track is `TrackSignatures.For(stream)`, never a stream id. Global preferences use `TrackPreference.GlobalKey`.
- Never serve a path from a request: streaming looks it up from the file or stream id.
- `PlaybackSeed` builds a movie with an SDR and an HDR version and a three episode series.

### Events and webhooks
- A change clients should see publishes an `EventDraft` through `IEventPublisher` after it is saved. Types are `EventTypes` constants, payloads records in `Models/Events` listed in `EventPayloadTypes`. Per user events carry the `UserId`.
- `EventPublisher` is the only writer of `events` and holds one lock from insert to broadcast, so sequence order is commit order.
- Library events come from the sync results through `LibraryEventService.FromReport`; Infrastructure publishes no events.
- Incoming webhooks only trigger `ILibrarySyncService.RefreshAsync` for the id they name. Every sync and refresh holds `SyncLock`. Real payloads are in `Akira.Server.Tests/Fixtures/`; Radarr and Sonarr send the password as basic authentication.
- In tests, publish through the real `IEventPublisher` and read back with `GET /api/v1/events?since=0`. `FakeArrClientFactory` replaces Radarr and Sonarr through `AkiraApiFactory.TestServices`.

### Outgoing webhooks and Trakt
- Both read `events` after their own stored sequence, woken by `EventSignal`. A new reader of events does the same; what it needs goes into the event.
- A secret the server reads back (signing secrets, Trakt tokens) is stored with `IDataProtectionProvider`. Credentials it only checks are `Secrets.Hash`.
- Deliveries are signed with `WebhookSignature` and go in sequence order; a failure holds back the events behind it.
- Trakt only through `ITraktClient`, faked in tests. Watch state goes to Trakt history only with reason `marked`; plays are scrobbles.

### Actions
- Delete and unmonitor go through `IActionService` only: it checks `MayRemoveAsync`, plans per instance, runs unmonitor before delete before search, writes an `AuditEntry` and refreshes what it touched. A new destructive action does the same and gets an `ActionKind`.
- Never act on more than the request names, and keep `dryRun` working.
- Upstream resources without a partial update (the Sonarr series) are read and written back as `JsonObject`, never a typed record.
- Audit entries have no foreign keys and carry the user name.
- Live checks against a real Radarr/Sonarr only unmonitor and restore. Never delete or search there.

### Admin UI
- `Akira.Client/`: Vue 3.5 `<script setup lang="ts">`, Vite, Tailwind 4, Pinia, vue-router, as Lingarr's client. Shared classes (`panel`, `button`, `input`, `table`) in `style.css`. No test framework yet; `npm run build` type checks first.
- The API only through `src/services/`. `api.ts` sends `X-Akira-Request` and turns a problem into an `ApiError`; a 401 goes to sign in.
- API types are generated: `src/types/openapi.ts` comes from the committed copy of the OpenAPI document `src/types/openapi.json` (`npm run api-types`), and `src/types/api.ts` only names them. After an API change, run `../tools/api-types.ps1` against a running server and commit both files; `npm run build` fails while `openapi.ts` does not match `openapi.json`.
- A browser signs in with `POST /api/v1/auth/browser-session` and then holds only an HttpOnly, SameSite=Strict `akira_session` cookie. The handler reads it only without a header credential, refuses it from another `Origin`, and refuses a change without `X-Akira-Request`.
- The server serves `wwwroot` and answers every path outside `/api`, `/openapi` and `/scalar` without an extension with `index.html` (`WebUiRoute`), so an unknown API path stays a 404.
- Admin-only pages carry `meta.admin`; the server checks the policy anyway.

### Plugins
- Design in `../docs/plugins.md`. `Akira.Contracts` is a stable API with its own records (never `Models/` or entities). After the first release a breaking change raises `AkiraPluginApi.Major`, an addition `Minor`; until then it stays 1.0.
- `PluginLoader` registers the built-ins and the DLLs in `Plugins:Path`. A DLL that does not load is skipped and listed in `GET /api/v1/plugins`. Setting values are never logged or returned, only whether one is set.
- A bridge's routes are controllers derived from `BridgeController<TBridge>`, without `[ApiController]`. `BridgeControllerConvention` locks them in: only an enabled bridge's own controllers, under `bridges/{key}`, anonymous to the host, out of the OpenAPI document; any other plugin controller is removed. State lives in singletons, never a controller. A bridge reads its own bodies.
- Bridges decide who calls through `IAccountAccess` and reach the library through the facades in `Akira.Application/Bridges`. A new need is a new contract method, never a reference to Akira's internals.
- A link (`DeviceKind.Link`, `pdl_` token) belongs to one user and one bridge and is refused on the API. Media urls for bridges are signed (`IMediaAccess`, `/api/v1/signed/...`) and die with their device. Never put a credential in a url Akira makes.
- Stream indexes appear only inside a bridge's responses and are mapped back to stream ids there.
- The lock-in is tested in `PluginTests`. A bridge's background service is only registered when it is enabled.
- A bridge without sign in acts as `IAccountAccess.FixedUserAsync`, answers the local network only (`ClientAddresses.IsLocal`, no forwarding header), and so do its signed urls (`SignedTarget.LocalOnly`).

### Subtitle requests
- Sources and translators are `ISubtitleSource` and `ISubtitleTranslator`, registered by a plugin and found by `Key`. Nothing in the request flow knows a provider. Providers throw `ProviderException`, the built-ins `UpstreamException`.
- A placed subtitle goes next to the video with `SubtitlePlacement.FreePath` and is read back with `IProbeService.RescanSubtitlesAsync`, never added by hand.
- A file written only for a translator is the request's `TemporaryPath`, removed however the request ends. Translator paths go through its own mappings both ways (`LingarrOptions.ToLingarr`, `FromLingarr`).
- Embedded subtitles leave the video only through `ISubtitleExtractor`.
- Text subtitles reach clients as WebVTT Akira writes itself, never ffmpeg's encoder: `SubtitleConverter` extracts ASS or SRT, `SubtitleDecoder` decodes, `SrtReader`/`AssReader` read. A line at the top gets `line:0`. Burning a text subtitle uses `SubtitleConverter.BurnableAsync`, an embedded one too: never point the subtitles filter at the video. Raise `SubtitleConversion.Version` when the output changes.

### Authentication
- The fallback policy requires a signed in user. Open an endpoint only on purpose and add it to `EndpointSecurityTests.Anonymous`. Policies: `AkiraPolicies.Admin`, `Device` (a shared device without a profile passes), `Client` (any valid credential).
- Who calls comes from the claims (`GetUserId()`, `GetDeviceId()`, `IsAdmin()`), never a parameter.
- Secrets come from `Secrets`, stored as `Secrets.Hash`, shown in exactly one response and never logged. PINs through `Pins` and `PinAttemptLimiter`; compare with `Secrets.FixedTimeEquals`. A shared device never opens an admin without a PIN.
- A profile session makes nothing that outlives it (`PairingService.ProfileSessionRefusal`).
- Rate limits partition by `ClientAddresses.PartitionKey` (IPv6 by /64). `PairingStartLimiter` caps pairing starts per address for the API and every bridge.
- Expiring times come from the injected `TimeProvider`; tests use `FakeTimeProvider`.
- Services that can fail return `ServiceResult<T>`; `ServiceResults.Problem` maps each `ServiceError` to its status.
- Tests: `AkiraApiFactory.CreateClient()` acts as a seeded admin (`TestAdmin`); also `CreateAnonymousClient()`, `CreateClient(token)`, `SeedAdmin = false` and `Time`.

### Transcoding
- Whether a version needs HLS is `StreamDecision.Decide` (pure, tested with real layouts), and burn-in `StreamDecision.Burns`. The plan offers `hls` only to a client that lists it.
- ffmpeg for HLS only through `HlsTranscoder` with arguments from `FfmpegHlsArguments` (pure, tested): one process per session, killed on stop, seek, track change and idle. Failures are a `TranscodeException`. The progressive stream only through `ProgressiveTranscoder` with `BuildProgressive`, sharing `Transcoding:MaxConcurrent` through `IHlsTranscoder.TryReserve`/`Release`. A change to shared arguments keeps both test sets passing.
- A segment is the same whichever run writes it, or a seek plays gaps, repeats and audio out of step. Encodes: segment n starts on the first frame at or after n times the segment length (`-copyts`, `-start_at_zero`, forced keyframes moved back by the run's first frame offset, so 23.976 fps cuts the same from any start). Copied video: on the file's keyframes from the Matroska Cues or the MP4 sample table (`HlsSegments.FromKeyframes`, `KeyframeIndex`, `Mp4SampleTable`), cut by the segment muxer and renamed from `.part` when complete. The transcoder tests check both with ffprobe.
- Only folders this server made are removed from `Transcoding:TempPath`.
- Hardware acceleration changes decoding and the encoder only; filters stay on the CPU unless `ToneMapping` puts tone mapping on the GPU. A failing GPU run falls back to libx264 for the server's lifetime (`HlsTranscoder.SoftwareOnly`). `AKIRA_TEST_HWACCEL` runs the GPU test.
- A path inside a filter goes through `FfmpegHlsArguments.FilterPath`.

### Markers and trickplay
- Markers come from `Markers` (pure); a new title pattern gets a test with real chapter names first. Clients get them in the plan.
- Audio matching is `AudioMatch`, recaps `RecapMatch` (both pure), over `IAudioFingerprinter` (chromaprint). Those integration tests skip without chromaprint; `AKIRA_MARKER_SEASON` points one at real episodes. Raise `MarkerScanService.Version` when matching changes.
- Trickplay sheets only from `TrickplayGenerator`, written to a `.partial` folder and swapped in. Raise `TrickplayGenerator.Version` when they change. Endpoint tests fake `ITrickplayGenerator`.

### Probing
- Every ffmpeg and ffprobe input from the library is `-protocol_whitelist file -i file:<path>`.
- ffprobe only through `FfprobeRunner`: an argument list, a timeout that kills the process tree, bounded output. Failures are a `ProbeException`.
- Track ids come from `StreamIds` only. Raise `FfprobeParser.Version` when parsing changes.
- A probe never reads a whole video; only external subtitle files are counted packet by packet.
- Re-probing an unchanged file updates the same track rows (`ProbeService.ReplaceStreams`).
- Disk timestamps are compared in whole seconds.
- `ExternalSubtitleScanner.Find` throws when a folder cannot be listed: an empty list would remove the file's subtitles.
- Tests needing ffprobe are `Integration` and use `Akira.Testing.Ffprobe.Path`. `Fixtures/media/synthetic.mkv` is a 50 KB file with every track case; regenerate it with ffmpeg, never replace it with a real file.
- Git Bash rewrites path-like environment values (`/movies`); set path mappings from PowerShell or appsettings.

### Git
- Never run `git commit`, `git push`, `git merge`, `git rebase` or `git revert`. Rowan writes every commit message himself. Leave changes in the working tree.
- No `Co-authored-by:` trailer or other agent attribution.
- Branches follow Conventional Commits: `feat/...`, `fix/...`.
- The pre-commit hook runs the unit tests (`git config core.hooksPath .githooks`).

### Branding
- The mark (a rounded A with a play button cut out, `#3d8bf7` on `#13171d`) is geometry in `../tools/make_icons.py`, which writes it into `Akira.Docs/public` and `Akira.Client/public`. Change it there and run the script; never edit the generated images. New sizes are added to the script.

### Comments
Sparse on purpose. Load the `rowan-comment-style` skill before writing any comment. Every record gets a one-line `<summary>` of what it represents, every public type in `Akira.Contracts` gets one, no XML doc on properties, `<inheritdoc />` on implementations, and a comment only for a non-obvious why. No em dashes anywhere in the repository.

## Commands
- Build: `dotnet build Akira.slnx`. Test: `dotnet test Akira.slnx` (needs Docker and ffprobe), or with `--filter "Category!=Integration"` without them.
- One area while working: `dotnet test Akira.Server.Tests --filter "FullyQualifiedName~Akira.Server.Tests.Playback"` (or `.Auth`, `.Events`, `.Subtitles`, `.Trakt`, `.Webhooks`, `.Actions`, `.Endpoints`, `Akira.Infrastructure.Tests.Transcoding` and so on). The full suite before handing a step over.
- Run: `dotnet run --project Akira.Server` (http://localhost:8484, Scalar at `/scalar`, database in `Akira.Server/config/akira.db`). Radarr/Sonarr with `Arr__Instances__0__Key`, `__Kind`, `__Url`, `__ApiKey`.
- Plugins from outside: point `Plugins__Path` at a folder with their DLLs; the server image ships without them.
- Admin UI: `cd Akira.Client`, `npm install`, `npm run dev` (port 5173, proxies `/api` to 8484), `npm run build` into `Akira.Server/wwwroot`.
- Guide: `cd Akira.Docs`, `npm run dev` or `npm run build` (fails on a dead link). A new setting or user facing feature gets its line there, in short Simplified Technical English.
- Docker: `docker compose up --build`; add `-f docker-compose.mariadb.yml` with `DB_PASSWORD` for MariaDB. For a GPU: `GPU_DRIVERS` in `.env`, `HARDWARE_ACCELERATION` and `TONE_MAPPING`, and the device lines from `Akira.Docs/features/playback.md` in the compose file.
- Portainer: `../tools/build-images.ps1` into `../out/akira-images.tar`, import it, paste `docker-compose.portainer.yml`; steps, variables and ports in README.md.
- Watch state from Jellyfin: `python ../tools/import-jellyfin.py --jellyfin <url> --jellyfin-user <name> --akira <url> --dry-run` with `JELLYFIN_API_KEY` and `AKIRA_API_KEY` set.
- CI (`.github/workflows/ci.yml`, GitHub Actions): unit tests, MariaDB integration tests, the admin UI build, the guide build and a Docker image build.
- Release: a tag `vX.Y.Z` runs `release.yml`, which publishes `ghcr.io/akira-headless/akira-server` (amd64 and arm64, every GPU driver) and a GitHub release. `pages.yml` puts the guide on GitHub Pages on every push to `main` that changes `Akira.Docs`; the build reads its base path from `DOCS_BASE`.
