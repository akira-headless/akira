<p align="center"><img src="Akira.Docs/public/akira-icon.png" width="128" alt="Akira"></p>

# Akira

[![Version](https://img.shields.io/github/v/release/akira-headless/akira)](https://github.com/akira-headless/akira/releases)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg)](LICENSE)
[![Guide](https://img.shields.io/badge/guide-akira--headless.github.io-blue)](https://akira-headless.github.io/akira/)

Akira is a headless media server, so it has no player of its own. It builds the library directly from Radarr and Sonarr, picks the audio and subtitle tracks, remembers what you watched and streams it to the app you use.

Follow the [installation guide](https://akira-headless.github.io/akira/getting-started/installation) to install Akira, and the [app guide](https://akira-headless.github.io/akira/frontend/) to build your own app for it.

### What Akira does

- **The library** is built directly from Radarr and Sonarr, a new download shows up right away through a webhook.
- **Versions and tracks**: the app tells Akira what the device can play, Akira picks the version, the audio and subtitle tracks by the user's rules, and direct play or HLS.
- **Transcoding** to HLS when the device can't play the file, on the CPU or an Intel, AMD or NVIDIA GPU, with HDR tone mapping.
- **Watch state** per user that stays when Radarr or Sonarr upgrades the file, and [Trakt](https://trakt.tv) sync.
- **Subtitles** from [OpenSubtitles.com](https://www.opensubtitles.com), translated with [Lingarr](https://github.com/lingarr-translate/lingarr).
- **Intros, credits and seek thumbnails**, so an app can skip and preview.
- **Bridges** for Jellyfin apps, Stremio and DLNA TVs.

## Usage

### Docker image tags

| Tag | Description | Architectures |
|-----|-------------|---------------|
| `latest` | Latest stable release | `amd64` `arm64` |
| `1.2.3` | Specific version | `amd64` `arm64` |

The image is `ghcr.io/akira-headless/akira` and has the GPU drivers for Intel, AMD and NVIDIA, the Intel ones on `amd64` only.

## Setting up Akira

Akira uses SQLite by default, MariaDB is supported as well. The host network is optional, you only need it when DLNA TVs have to find Akira.

```yaml
services:
  akira:
    image: ghcr.io/akira-headless/akira:latest
    container_name: akira
    restart: unless-stopped
    ports:
      - "8484:8484"
    # network_mode: host # optional, in place of ports, so DLNA TVs can find Akira
    environment:
      - ASPNETCORE_HTTP_PORTS=8484
      - Arr__Instances__0__Key=radarr
      - Arr__Instances__0__Name=Radarr
      - Arr__Instances__0__Kind=Radarr
      - Arr__Instances__0__Url=http://192.168.1.10:7878
      - Arr__Instances__0__ApiKey=your-radarr-api-key
      - Arr__Instances__0__PathMappings__0__From=/movies # the root folder as Radarr sees it
      - Arr__Instances__0__PathMappings__0__To=/movies
      - Arr__Instances__1__Key=sonarr
      - Arr__Instances__1__Name=Sonarr
      - Arr__Instances__1__Kind=Sonarr
      - Arr__Instances__1__Url=http://192.168.1.10:8989
      - Arr__Instances__1__ApiKey=your-sonarr-api-key
      - Arr__Instances__1__PathMappings__0__From=/tv
      - Arr__Instances__1__PathMappings__0__To=/tv
      - Transcoding__TempPath=/app/transcodes
    group_add:
      - "1000" # the group that owns the media folders, so Akira can save subtitles
    volumes:
      - akira_config:/app/config
      - akira_transcodes:/app/transcodes
      - /path/to/media/movies:/movies
      - /path/to/media/tv:/tv

volumes:
  akira_config:
  akira_transcodes:
```

Sign-in with a name and PIN only works at home, Akira checks that on the address of the device. Docker Desktop and rootless Docker hide that address, so every device looks like it is at home, don't forward port 8484 to the internet there.

Open `http://<host>:8484` and do the [initial setup](https://akira-headless.github.io/akira/getting-started/setup).

### Setting up Akira using Docker CLI

```bash
docker run -d \
  --name akira \
  -p 8484:8484 \
  --restart unless-stopped \
  --group-add 1000 \
  -e ASPNETCORE_HTTP_PORTS=8484 \
  -e Arr__Instances__0__Key=radarr \
  -e Arr__Instances__0__Name=Radarr \
  -e Arr__Instances__0__Kind=Radarr \
  -e Arr__Instances__0__Url=http://192.168.1.10:7878 \
  -e Arr__Instances__0__ApiKey=your-radarr-api-key \
  -v akira_config:/app/config \
  -v /path/to/media/movies:/movies \
  ghcr.io/akira-headless/akira:latest
```

### Akira environment variables

| Environment variable | Description |
|----------------------|-------------|
| `ASPNETCORE_HTTP_PORTS=8484` | The port Akira listens on. |
| `Arr__Instances__0__Kind=Radarr` | `Radarr` or `Sonarr`, one block per instance numbered from 0. |
| `Arr__Instances__0__ApiKey=...` | The API key from **Settings > General** of the instance. |
| `Arr__Instances__0__PathMappings__0__From=/movies` | The root folder as the instance sees it, `__To` is the same folder in the Akira container. |
| `Playback__Languages__Subtitles=nl,en` | The default subtitle languages for all users, in order. `__Audio` does the same for audio. |
| `Transcoding__HardwareAcceleration=Vaapi` | `None`, `Vaapi`, `Qsv`, `Nvenc` or `Amf`, see [hardware acceleration](https://akira-headless.github.io/akira/features/playback#hardware-acceleration). |
| `Subtitles__OpenSubtitles__ApiKey=...` | Your OpenSubtitles.com API key, for subtitle requests. |
| `Subtitles__Lingarr__Url=http://lingarr:9876` | Your Lingarr, for translated subtitles. |
| `Trakt__ClientId=...` | Your app on trakt.tv, with `Trakt__ClientSecret`. |
| `Database__Provider=MariaDb` | `Sqlite` (default) or `MariaDb`, see [database](https://akira-headless.github.io/akira/getting-started/configuration#database). |

All other [settings](https://akira-headless.github.io/akira/getting-started/configuration) can be found in the guide.

## Building your own app

Akira has a REST API with an OpenAPI document of every endpoint, which any app can use. The [app guide](https://akira-headless.github.io/akira/frontend/) shows how to build one, by hand or with an AI coding assistant, and the [API documentation](https://akira-headless.github.io/akira/developers/api) covers the rest. For an app that has its own API, you can write a bridge as a [plugin](https://akira-headless.github.io/akira/developers/plugins).

## Development

Build and test with `dotnet build Akira.slnx` and `dotnet test Akira.slnx`, the admin page is in `Akira.Client` and the guide in `Akira.Docs`. See [Building](Akira.Docs/developers/building.md).

### Building the image yourself

`docker-compose.yml` builds Akira from this repository, start it with `docker compose up --build`. Put your secrets in a `.env` file next to it, add `-f docker-compose.mariadb.yml` to run it with a MariaDB container. The file publishes port 8484, so DLNA TVs won't find it, use `network_mode: host` with `ASPNETCORE_HTTP_PORTS=8484` for that.

The file reads these from `.env`:

| Variable | Default | Description |
|---|---|---|
| `GPU_DRIVERS` | | The GPU drivers to build into the image, comma separated: `intel`, `amd`, `nvidia`. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Development` adds Scalar at `/scalar`. |
| `SUBTITLE_LANGUAGES`, `AUDIO_LANGUAGES` | | The household's languages, comma separated, for example `nl,en`. |
| `SUBTITLE_MODE` | | When subtitles show: `Off`, `ForcedOnly`, `WhenAudioForeign` or `Always`. |
| `HARDWARE_ACCELERATION` | `None` | `None`, `Nvenc`, `Qsv`, `Vaapi` or `Amf` (Windows only). |
| `TONE_MAPPING` | `Cpu` | `Cpu`, `OpenCl` (with `Nvenc` or `Qsv`) or `Vaapi` (with `Vaapi`). |
| `TRAKT_CLIENT_ID`, `TRAKT_CLIENT_SECRET` | | Your app on trakt.tv. |
| `OPENSUBTITLES_API_KEY`, `OPENSUBTITLES_USERNAME`, `OPENSUBTITLES_PASSWORD` | | Your OpenSubtitles.com account. |

### Portainer

1. Build the images with `..\tools\build-images.ps1` (add `-GpuDrivers intel` for GPU transcoding), it saves them in `../out/akira-images.tar`.
2. In Portainer, import the tar under **Images > Import**.
3. Under **Stacks > Add stack > Web editor**, paste [`docker-compose.portainer.yml`](docker-compose.portainer.yml).
4. Fill in the variables below and deploy. A variable without a default is required, Akira won't start without it and tells you which one is missing.

Akira answers on `http://<host>:8484`, the web player on `http://<host>:8090` and the guide on `http://<host>:8091`. `akira_config` holds the database, keep it. `akira_transcodes` holds the video Akira converts while someone watches, a few GB per stream, and is emptied when Akira starts.

| Variable | Default | Description |
|---|---|---|
| `RADARR_URL` | | The address of Radarr, as Akira reaches it over the network. |
| `RADARR_API_KEY` | | The API key from **Settings > General** in Radarr. |
| `RADARR_ROOT` | `/movies` | The root folder of Radarr, as Radarr sees it. Akira maps it to `/movies` in its container. |
| `SONARR_URL` | | The address of Sonarr. |
| `SONARR_API_KEY` | | The API key from **Settings > General** in Sonarr. |
| `SONARR_ROOT` | `/tv` | The root folder of Sonarr, as Sonarr sees it. Akira maps it to `/tv`. |
| `MOVIES_PATH` | | The movies folder on the host. It must be writable, because Akira saves subtitles next to the videos. |
| `TV_PATH` | | The TV folder on the host, writable for the same reason. |
| `MEDIA_GID` | `1000` | The group that owns the media folders (`ls -ln` shows it). Radarr and Sonarr let its members write. Akira runs as uid 1654 and joins this group. Do not set `user` instead: the config volume belongs to uid 1654. |
| `SUBTITLE_LANGUAGES` | | The household's subtitle languages, comma separated, for example `nl,en`. Users can set their own. |
| `AUDIO_LANGUAGES` | | The household's audio languages, in the same form. |
| `OPENSUBTITLES_API_KEY`, `OPENSUBTITLES_USERNAME`, `OPENSUBTITLES_PASSWORD` | | Optional. Subtitle requests from OpenSubtitles.com. Give all three or none. |
| `TRAKT_CLIENT_ID`, `TRAKT_CLIENT_SECRET` | | Optional. The app you registered on trakt.tv. Each user links their own account. |
| `HARDWARE_ACCELERATION` | `None` | `Vaapi` to convert on an Intel GPU (the images built with `-GpuDrivers intel`), `None` for the CPU only. |
| `TONE_MAPPING` | `Cpu` | HDR tone mapping: `Vaapi` together with `Vaapi` above, `Cpu` otherwise. |
| `RENDER_GID` | `993` | The host's `render` group, through which Akira reads the GPU in `/dev/dri`. Find it with `getent group render \| cut -d: -f3`. |
| `TRUSTED_PROXY` | `172.16.0.0/12` | The Docker networks. Akira believes the client address the web player's nginx forwards from here, so a browser at home can sign in with a name and PIN through the web player. |
| `WEB_PORT` | `8090` | The port of the web player. It reaches Akira through the host (`host.docker.internal:8484`). |
| `GUIDE_PORT` | `8091` | The port of the guide. |
