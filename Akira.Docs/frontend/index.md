# Build your own frontend

Akira has no player. A frontend is any program that signs in, shows the library and plays a file: a TV app, a web page, a desktop player or a script. You write it yourself, by hand or with an AI coding assistant.

Do the [initial setup](../getting-started/setup) first. You need a working server and your API key.

## How a frontend works

Akira decides, the frontend follows. A frontend never picks tracks or computes watch state itself.

```text
1. Sign in      pairing code or PIN             -> a device token
2. Browse       items, seasons, episodes, Continue watching, Next up
3. Ask a plan   what the device plays           -> version, tracks, source, resume point
4. Play         the source url, with the token
5. Report       progress every 10 seconds, track changes, stop
6. Stay live    the event WebSocket             -> reload what changed
```

Every route, field and value is in the OpenAPI document: `http://<host>:8484/openapi/v1.json`. Generate a client from it, or give it to your AI assistant.

## Try the API

Use your API key in the `X-Api-Key` header.

List the first titles:

```bash
curl -H "X-Api-Key: <key>" "http://<host>:8484/api/v1/items?limit=5"
```

Ask a playback plan for one of them, as a device that plays H.264 with AAC:

```bash
curl -X POST "http://<host>:8484/api/v1/items/<id>/playback-plan" \
  -H "X-Api-Key: <key>" -H "Content-Type: application/json" \
  -d '{"capabilities": {"videoCodecs": ["h264"], "audioDecode": ["aac"], "accessModes": ["http", "hls"]}}'
```

The answer has the `sources` to play, the chosen `audio` and `subtitle` track, `resumePositionMs` and a `sessionId`. Every other call of a frontend follows from these.

## Native app or browser

| | Native app (TV, phone, desktop) | Browser |
|-|---------------------------------|---------|
| Credential | `Authorization: Bearer <token>` on every request, also images and streams | An HttpOnly cookie from `POST /api/v1/auth/browser-session` |
| Address | Any | The same address as the API, through a reverse proxy |
| Extra | Nothing | The header `X-Akira-Request: 1` on every request that changes something |

Akira does not send CORS headers. A browser frontend must be served from the same address as `/api`. With nginx:

```nginx
map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 8080;
    root /usr/share/nginx/html;

    location /api/ {
        proxy_pass http://<akira-host>:8484;
        proxy_http_version 1.1;
        proxy_set_header Host $http_host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_buffering off;
        proxy_read_timeout 1h;
    }

    location / {
        try_files $uri /index.html;
    }
}
```

Keep the browser's `Host`: Akira accepts the cookie only when the `Origin` matches it. For PIN sign-in through the proxy, add the proxy's address to `Proxy__TrustedProxies__0`.

## Device capabilities

The playback plan depends on what the device says it plays. Akira converts only what the device does not list. A list left out means the device plays everything, so Akira converts nothing and an unsupported file fails. Send the real values of the device:

| Field | Values |
|-------|--------|
| `videoCodecs` | `h264`, `hevc`, `av1`, `vp9`, `vp8`, `mpeg2video` |
| `audioDecode` | `aac`, `mp3`, `flac`, `opus`, `vorbis`, `pcm`, `alac`, `ac3`, `eac3`, `dts`, `dtshd`, `truehd` |
| `audioPassthrough` | The formats a receiver takes unchanged, from the same list |
| `subtitleFormats` | `srt`, `ass`, `vtt`, `pgs`, `vobsub`, `dvb`, `mov_text` |
| `containers` | `mkv`, `mp4`, `m4v`, `mov`, `webm`, `ts`, `m2ts` |
| `hdr` | `hdr10`, `dolbyvision`, `hlg` |
| `maxHeight`, `maxBitrateKbps` | The largest picture and bitrate the device takes |
| `accessModes` | `http` (the file) and `hls` (a stream Akira converts) |

A browser plays few formats: usually `h264`, `aac`, `vtt` and `mp4`, with `hls` through hls.js.

## Checklist

A frontend that does these works well with Akira:

- It sends the real capabilities of the device.
- It plays the first source of the plan, starts at `resumePositionMs` and selects the plan's tracks.
- It reports progress every 10 seconds and on pause and seek, reports track changes, and sends stop on exit. Without this, resume and watched do not work.
- It asks a new plan with only `hls` when the file does not play.
- It reloads what an event changes, not on a timer.
- It never puts the token in a url or a log.
- It shows the `title` and `detail` of an error.
- It signs in again on a 401.
- On a TV, it works with arrow keys only.

## Next

- [With an AI assistant](./ai-assistant): prompts that build a frontend step by step.
- [API](../developers/api): credentials, events and webhooks.
