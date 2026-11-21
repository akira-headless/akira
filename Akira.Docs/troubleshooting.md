# Troubleshooting

## The server does not start

Read the log: `docker logs akira`. A missing or wrong setting stops the server at start, and the log names it.

## A new download does not show

Akira syncs with Radarr and Sonarr every 30 minutes. Connect the webhooks to see new downloads at once. See [Initial setup](./getting-started/setup#_5-connect-the-webhooks).

## A subtitle cannot be saved

The error is `Could not write ... Access to the path ... is denied`. The folder is not writable for the media group. See [Subtitles](./features/subtitles#permissions).

## Name and PIN do not show at sign-in

All of these must be true:

- The user has a PIN. An admin sets it under **Users**.
- The device is on the home network.
- Behind a reverse proxy, `Proxy__TrustedProxies__0` covers the proxy's address.

## Playback starts slowly

The device cannot play the file, so Akira converts it first. Turn on [hardware acceleration](./features/playback#hardware-acceleration). On the admin page, **Now playing** shows the encoder and the speed.

## The container runs an old image

After a pull, the container keeps its image until you recreate it. Compare the two IDs:

```bash
docker inspect akira --format '{{.Image}}'
docker images ghcr.io/akira-headless/akira
```

If they differ, run `docker compose up -d`.
