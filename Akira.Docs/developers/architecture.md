# Architecture

## Rules

These rules hold for every part of Akira:

- **API first.** Every feature is in the REST API under `/api/v1`. No endpoint is for one client only.
- **The server decides, the clients play.** The server picks the version, the tracks and the delivery, and returns them as a playback plan.
- **Radarr and Sonarr own the library.** Akira does not scan folders or fetch metadata.
- **Stable ids.** A track id stays the same when the file is probed again. An ffprobe index is only a hint in a plan.
- **Watch state belongs to the movie or episode**, by its TMDB or TVDB id, not to a file. An upgraded file keeps it.
- **Track rules are attributes**, such as language and flags, not track ids. So they apply to every file and every episode.

## Server

| Project | Holds |
|---------|-------|
| `Akira.Server` | The host: controllers, authentication, background workers |
| `Akira.Application` | Services the API and the plugins use, request and response records |
| `Akira.Core` | The domain and pure logic: track resolver, playback decisions, markers. No I/O |
| `Akira.Infrastructure` | The database, Radarr and Sonarr clients, ffprobe and ffmpeg |
| `Akira.Migrations` | FluentMigrator migrations for SQLite and MariaDB |
| `Akira.Contracts` | The plugin contract |

## More

The design and the reasons behind it are in `docs`, next to the repositories, one file per topic. The server's rules are in `AGENTS.md`; each other repository has a `CLAUDE.md` with its own.
