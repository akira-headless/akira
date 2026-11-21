# Configuration

Akira reads its settings from environment variables. A double underscore separates the levels, for example `Arr__Instances__0__Url`. The server stops at start when a needed setting is missing, and names the setting in the log.

## Radarr and Sonarr

Give one block per instance, numbered from `0`.

| Variable | Description |
|----------|-------------|
| `Arr__Instances__0__Key` | A stable key: lowercase letters, digits and dashes, at most 32. For example `radarr`. Do not use `tmdb` or `tvdb`. |
| `Arr__Instances__0__Name` | The name users see, for example `Radarr 4K`. |
| `Arr__Instances__0__Kind` | `Radarr` or `Sonarr`. |
| `Arr__Instances__0__Url` | The address Akira uses for the instance. |
| `Arr__Instances__0__ApiKey` | The API key from **Settings > General** of the instance. |
| `Arr__Instances__0__PathMappings__0__From` | A path as the instance sees it. |
| `Arr__Instances__0__PathMappings__0__To` | The same path in the Akira container, for example `/movies`. |
| `Arr__Instances__0__WebhookSecret` | Optional. The password of the webhook connection, at least 16 characters. Without it Akira refuses webhooks of this instance. |
| `Library__SyncInterval` | Time between full syncs. Default `00:30:00`. A client starts one at once with `POST /api/v1/library/sync`. |

## Media folders

Mount the media folders in the container, for example at `/movies` and `/tv`, and map the paths of Radarr and Sonarr to them.

Akira saves subtitles next to the videos. Add the group that owns the media folders to the container with `group_add`, so Akira can write there. See [Subtitles](../features/subtitles#permissions).

## Languages

| Variable | Description |
|----------|-------------|
| `Playback__Languages__Subtitles` | The default subtitle languages for all users, in order, for example `nl,en`. |
| `Playback__Languages__Audio` | The default audio languages for all users, in order. |
| `Playback__Languages__SubtitleMode` | When subtitles show: `Off`, `ForcedOnly`, `WhenAudioForeign` or `Always`. |

Each user can set their own rules in an app. These are the defaults for users who did not.

## Network

| Variable | Description |
|----------|-------------|
| `ASPNETCORE_HTTP_PORTS` | The port of the server, for example `8484`. |
| `Proxy__TrustedProxies__0` | A proxy address or network whose forwarded headers Akira believes, for example `172.16.0.0/12` for the Docker networks. |

## Transcoding

Akira converts a video when a device cannot play the file. See [Playback](../features/playback).

| Variable | Description |
|----------|-------------|
| `Transcoding__HardwareAcceleration` | `None`, `Vaapi`, `Qsv`, `Nvenc` or `Amf`. |
| `Transcoding__ToneMapping` | HDR to SDR: `Cpu`, `Vaapi` (with Vaapi) or `OpenCl` (with Qsv, Nvenc or Amf). |
| `Transcoding__MaxConcurrent` | Conversions at the same time, default `2`. `0` turns conversion off. |
| `Transcoding__MaxVideoBitrateKbps` | The highest bitrate of a conversion, default `20000`. |
| `Transcoding__FfmpegPath` | The ffmpeg Akira uses for conversions, subtitles, thumbnails and intro detection, default `ffmpeg`. `Subtitles__FfmpegPath` is not used anymore: remove it, or Akira does not start. |
| `Transcoding__TempPath` | The folder for converted video. Default: a folder in the temporary folder of the system. |
| `Transcoding__MaxAhead` | How far a conversion runs ahead of the player before it stops and waits, default `00:03:00`. |
| `Transcoding__KeepBehind` | How long converted video stays behind the player, default `00:02:00`. |

Akira writes converted video to `Transcoding__TempPath` while someone watches. In a container the default folder is in the container itself, which is slow and fills the disk of Docker. Mount a volume or a fast disk and point the setting at it:

```yaml
    environment:
      - Transcoding__TempPath=/app/transcodes
    volumes:
      - akira_transcodes:/app/transcodes
```

The image has `/app/transcodes` for this, so a named volume there is writable. A folder of the host must be writable by uid 1654. Akira removes only its own folders there, at start and when a stream stops.

One stream keeps at most `MaxAhead` plus `KeepBehind` of video in the folder, about 2.5 GB for a 4K remux with the defaults. A jump back past `KeepBehind` converts that part again. Lower both values for a small disk or a folder in memory (`tmpfs`).

## Subtitle requests

| Variable | Description |
|----------|-------------|
| `Subtitles__OpenSubtitles__ApiKey` | The API key of OpenSubtitles.com. |
| `Subtitles__OpenSubtitles__Username`, `__Password` | Your OpenSubtitles.com account. Give both or none. |
| `Subtitles__Lingarr__Url` | The address of Lingarr, for translations. |
| `Subtitles__Lingarr__ApiKey` | Only when Lingarr has authentication on. |
| `Subtitles__Lingarr__PathMappings__0__From`, `__To` | Akira's paths to Lingarr's paths. |

## Trakt

| Variable | Description |
|----------|-------------|
| `Trakt__ClientId`, `Trakt__ClientSecret` | The app you register on trakt.tv. Give both or none. Each user links their own account on the admin page. |

## App updates

Akira can give apps their updates. Each platform has a source: the address of an `update.json` that you or the builder of the app publish next to each build. When an app starts, or when someone presses Check for updates in its settings, Akira reads the source, downloads a new build and keeps the newest one. The app then offers to install it.

| Variable | Description |
|----------|-------------|
| `ClientUpdates__Sources__android__ManifestUrl` | The `update.json` of the Android app. Use `linux` for the Linux app. Other names are possible, in lowercase letters, digits and dashes. |
| `ClientUpdates__Sources__android__Authorization` | Only when the source needs a login. Akira sends it as the `Authorization` header, for example `Basic <base64 of user:token>`. Akira does not send it to another host after a redirect. |
| `ClientUpdates__Path` | The folder for the builds, default `config/client-updates`. |

`update.json` looks like this:

```json
{ "build": 29512345, "version": "0.1.0 (2026-11-16 10:00)", "file": "akira.apk", "sha256": "<64 hex digits>" }
```

`build` is the number the app compares with its own. `file` is the address of the build, full or relative to `update.json`. Akira keeps a build only when its SHA-256 is the same as `sha256`.

## Database

SQLite is the default and needs no settings. For MariaDB:

| Variable | Description |
|----------|-------------|
| `Database__Provider` | `Sqlite` or `MariaDb`. |
| `Database__SqlitePath` | The SQLite file, default `config/akira.db`. |
| `Database__Host`, `__Port`, `__Name`, `__Username`, `__Password` | The MariaDB connection. The database must be `utf8mb4`. |

## Other settings

| Variable | Description |
|----------|-------------|
| `Playback__PlayedThreshold` | How far into a video it counts as watched, default `0.9`. |
| `Playback__MinimumResume` | A stop before this time makes no resume point and keeps the one there was, default `00:01:00`. |
| `Auth__PinMaxAttempts` | Wrong PINs before a lockout, default `5`. |
| `Auth__PinLockout` | How long the first lockout takes, default `00:05:00`. Each lockout in a row takes twice as long, up to one day. |
| `Auth__BrowserSessionLifetime` | How long a browser stays signed in when it is not used, default 30 days. Each use past half of this time starts it again. |
| `Trickplay__Enabled` | Seek thumbnails, default `true`. |
| `ImageCache__MaxMegabytes` | Disk space for posters and backdrops from Radarr, Sonarr and the artwork hosts, default `500`. When it is full, Akira removes the images used least recently. `0` turns the cache off. |
| `ImageCache__Path` | The folder of that cache, default `config/images`. |
| `Markers__Enabled` | Recap, intro and credits detection, default `true`. |
| `Markers__Recaps` | Recap detection from audio, default `true`. Akira reads each episode once from start to end for it. |
| `Events__Retention` | How long live events stay for catch-up, default 7 days. |
