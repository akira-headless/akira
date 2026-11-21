# What is Akira

Akira serves the movies and shows from Radarr and Sonarr through an API. It is headless: it has no player and no apps of its own. You build the frontend, by hand or with an AI coding assistant.

## Headless

Most media servers come with their own apps. Akira does not. It does the work of a media server, and leaves the screen to a frontend:

| Akira does | The frontend does |
|------------|-------------------|
| Reads the library from Radarr and Sonarr | Shows the library |
| Picks the version, the tracks and direct play or conversion | Plays the source Akira gives |
| Keeps users, watch state and track rules | Reports progress and track changes |
| Converts, finds subtitles, sends events | Reacts to events |

So a frontend stays small, and you choose what it looks like and where it runs. Write one by hand or with an AI coding assistant. See [Build your own frontend](../frontend/). For another app that has its own API, write a bridge as a plugin. See [Plugin API](../developers/plugins).

## Get started

1. [Install](./installation) the server with Docker.
2. Do the [initial setup](./setup): the admin, the library check, webhooks and users.
3. [Build your own frontend](../frontend/), or [with an AI assistant](../frontend/ai-assistant).

## What Akira does

- **The library**, from Radarr and Sonarr, with titles, artwork and ratings. A new download shows at once through a webhook.
- **A playback plan.** The frontend sends what the device can play. Akira picks the version, the audio and subtitle tracks by the user's rules, and direct play or HLS.
- **Transcoding** to HLS when the device cannot play the file, on the CPU or the GPU.
- **Users and watch state.** Progress, watched and the tracks a user picked stay with the movie or episode, also when Radarr or Sonarr upgrades the file.
- **Intros, credits and seek previews**, so a frontend can skip and show thumbnails.
- **Subtitles** from OpenSubtitles.com, or translated by Lingarr.
- **Sign-in** for devices: pairing codes, PINs and API keys.
- **Trakt** scrobbling, and **webhooks** with every event.
- **Delete and unmonitor** through Radarr and Sonarr.

## What Akira does not do

- It does not play media itself. That is the frontend's job.
- It does not scan folders or fetch metadata. Radarr and Sonarr own the library.
- It does not download. Radarr and Sonarr do that.
- It has no music, photos or live TV.

## Parts

| Part | What |
|------|------|
| The server | One Docker container. It serves the API on port `8484`. |
| The admin page | In the same container, at `http://<host>:8484`. For administration only: users, devices, API keys, subtitles, webhooks, Trakt, settings and what plays now. |
| Plugins | Optional DLLs you write or add, for more subtitle sources and bridges. |
| The database | SQLite in the `config` folder, or MariaDB. |

Next: [Installation](./installation).
