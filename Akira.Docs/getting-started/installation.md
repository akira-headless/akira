# Installation

Akira runs as a Docker container. Radarr and Sonarr run on their own; Akira connects to them. This page covers the images and how to start Akira with Docker Compose or the Docker CLI.

## Requirements

- Docker on a Linux host.
- Radarr, Sonarr or both, with their API keys.
- The media folders of Radarr and Sonarr, readable from the Akira host.

## Docker images

The image is in the GitHub Container Registry:

| Image | Description |
|-------|-------------|
| `ghcr.io/akira-headless/akira-server` | The server, with the admin page on the same port |

| Tag | Description |
|-----|-------------|
| `latest` | The latest release |
| `1.2.3` | A specific release |

The image is for `amd64` and `arm64`. Docker pulls the one for your host. It has the GPU drivers for Intel, AMD and NVIDIA; the Intel drivers are on `amd64` only.

## Docker Compose

Akira answers on port `8484`. The host network is optional: use it only when DLNA TVs must find Akira. Then remove `ports` and use `network_mode: host`.

```yaml
services:
  akira:
    image: ghcr.io/akira-headless/akira-server:latest
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
      - akira_transcodes:/app/transcodes # converted video, see Configuration
      - /path/to/media/movies:/movies
      - /path/to/media/tv:/tv

volumes:
  akira_config:
  akira_transcodes:
```

Then start it:

```bash
docker compose up -d
```

Sign-in with name and PIN works at home only. Akira checks this on the address of the device. Docker Desktop and rootless Docker hide that address, so each device looks like it is at home. With those, do not forward port 8484 to the internet.

See [Configuration](./configuration) for all settings, and continue with the [initial setup](./setup).

The container checks itself every 30 seconds with `GET /api/v1/health`. `docker ps` shows `healthy` when the database answers, and `unhealthy` after three failed checks in a row.

## Docker CLI

```bash
docker volume create akira_config

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
  ghcr.io/akira-headless/akira-server:latest
```

## GPU transcoding

The image has the drivers for Intel, AMD and NVIDIA. Give the container the GPU and set two settings. See [Hardware acceleration](../features/playback#hardware-acceleration).

## Update

```bash
docker compose pull
docker compose up -d
```

The database is in the `akira_config` volume. An update keeps it, and Akira updates the database itself at start.

## MariaDB

SQLite is the default and needs no setup. For MariaDB, see [Configuration](./configuration#database). The database must use `utf8mb4`.
