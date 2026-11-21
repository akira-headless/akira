# Initial setup

Do these steps once, after [Installation](./installation). At the end, Akira has your library, your users and a way in for your frontend.

## 1. Check the start

Read the log:

```bash
docker logs akira
```

A missing or wrong setting stops the server at start, and the log names it. Fix it in the compose file and run `docker compose up -d` again.

## 2. Create the admin

At the first start Akira writes a setup code to the log.

1. Find the code: `docker logs akira 2>&1 | grep "setup code"`.
2. Open the admin page: `http://<host>:8484`.
3. Enter the setup code and your name. A PIN is optional.

You are now the admin. Akira shows an API key for you once. Keep it in a password manager: it is your key for the API and for your first frontend tests.

## 3. Check the server

On the admin page, **Overview** shows the server, the database and ffprobe. All three must be healthy. ffprobe reads the tracks of every file.

## 4. Check the library

Akira reads Radarr and Sonarr at start, and then every 30 minutes. Ask the API for the first titles, with the API key from step 2:

```bash
curl -H "X-Api-Key: <your API key>" "http://<host>:8484/api/v1/items?limit=5"
```

An empty `items` list means Akira does not see the files. Check the URL, the API key and the path mappings of each instance. See [Configuration](./configuration#radarr-and-sonarr).

## 5. Connect the webhooks

Without webhooks, Akira finds new downloads at the next sync. With webhooks, it finds them at once.

1. Set `Arr__Instances__N__WebhookSecret` for each instance, at least 16 characters, and run `docker compose up -d`.
2. In Radarr or Sonarr, go to **Settings > Connect > + > Webhook**.
3. Set the URL to `http://<host>:8484/api/v1/webhooks/<key>`, where `<key>` is the instance key, for example `radarr`.
4. Set the method to `POST`, the username to anything, and the password to the webhook secret.
5. Select **On Import**, **On Upgrade**, **On Rename** and **On Delete**. Select **Test**, then **Save**.

## 6. Add users

1. On the admin page, go to **Users**.
2. Enter a name, a role and optionally a PIN.

Each user has their own watch state, track rules and Trakt link. An admin manages the server; a user only watches.

A PIN has 4 to 8 digits. With a PIN, a user can sign in at home with name and PIN, without a pairing code, and protect their profile on a shared device. After 5 wrong PINs the user is locked out for 5 minutes. Each lockout after that takes twice as long, up to one day, until the correct PIN is given.

## 7. Set the defaults

These are optional. Each one is a few settings in the compose file:

- **Languages**: the default audio and subtitle languages for all users. See [Configuration](./configuration#languages).
- **Hardware acceleration**: convert on the GPU. See [Playback](../features/playback#hardware-acceleration).
- **Subtitles**: downloads from OpenSubtitles.com and translations by Lingarr. See [Subtitles](../features/subtitles).
- **Trakt**: send what users watch to their Trakt account. See [Configuration](./configuration#trakt).

## 8. Connect a frontend

Akira is ready. It has no player, so connect a frontend now. See [Build your own frontend](../frontend/), or [with an AI assistant](../frontend/ai-assistant).

Each frontend signs in as a device, in one of three ways:

| Way | When |
|-----|------|
| A pairing code | The frontend shows a code. Approve it on the admin page under **Devices**, as **personal** (one user) or **shared** (each user picks their profile). Works from anywhere. |
| Name and PIN | At home only, for users with a PIN. |
| An API key | For scripts, tests and your own tools. Make one under **API keys**. A key acts as the user who made it. |

Revoke a device under **Devices**. It is signed out within a minute.
