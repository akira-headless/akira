# With an AI assistant

An AI coding assistant can write a frontend for Akira, if it knows how Akira works. The prompts on this page tell it. Use them in order: each one builds on the one before. Read [Build your own frontend](./) first, so you can check what the assistant makes.

## Before you start

1. Make an empty project folder and open your assistant in it.
2. Save the OpenAPI document in the folder, so the assistant reads the real routes and fields:

    ```bash
    curl -o openapi.json http://<host>:8484/openapi/v1.json
    ```

3. Keep your API key ready for the first tests, or approve the pairing code the frontend shows.
4. For a browser frontend, plan the reverse proxy now. See [Native app or browser](./#native-app-or-browser).

Replace the parts in `<angle brackets>` in each prompt. Test each step before the next one.

## 1. Project and sign-in

```text
Build a frontend for Akira, a headless media server. The API is described in
openapi.json in this project; read it and never guess a route or a field.
Use <your framework> for <TV / phone / desktop / browser>.

Akira facts:
- Base url is configurable, for example http://192.168.1.10:8484. Every route is under /api/v1.
- JSON is camelCase, enums are camelCase strings, nulls are always present.
- Errors are RFC 9457 problem details: show their "title" and "detail".
- Lists are paged: { items, total, offset, limit }, limit at most 200.

Sign-in:
- Pairing: POST /api/v1/auth/pairings with deviceName, client, clientVersion and
  platform. Show "code" large on screen. Poll POST /api/v1/auth/pairings/{pairingId}/token
  with { pollSecret } every pollIntervalSeconds: 202 means waiting, 200 gives the device
  token once. Stop at expiresAt.
- PIN at home: GET /api/v1/auth/pin/users lists users with a PIN; POST
  /api/v1/auth/pin/sign-in with the user and PIN gives a device token.
- Send the token as "Authorization: Bearer <token>" on every request, also for
  images and streams. Never put it in a url. Store it securely.
- In a browser, exchange the token once with POST /api/v1/auth/browser-session.
  Akira then sets an HttpOnly cookie that also covers images, streams and the
  WebSocket. Send the header "X-Akira-Request: 1" on every request that changes
  something. The page must be served from the same address as the API.
- On a 401, forget the token and show sign-in again.
- After sign-in, GET /api/v1/users/me shows the user and the device.

Build the sign-in screen and an API client module with these rules. Nothing else yet.
```

## 2. Browse the library

```text
Add browsing to the Akira frontend. Read openapi.json for the exact shapes.

- Home: GET /api/v1/library/continue-watching, GET /api/v1/library/next-up, and
  GET /api/v1/items?sort=added&order=desc for recently added. One row each.
- Movies and Shows: GET /api/v1/items?type=movie or type=series, paged with offset
  and limit. Search with the "search" parameter.
- A title: GET /api/v1/items/{id}. For a series, GET /api/v1/items/{id}/children
  lists the seasons, and the same call on a season lists its episodes.
- Images: GET /api/v1/items/{id}/images/{kind}. In a native app, send the
  Authorization header. In a browser, the session cookie covers <img src>.
  Keep the ETag of an image and send it in If-None-Match: a 304 means it is the same.
- Watched: POST /api/v1/items/{id}/actions/mark-played.

Back always goes up the library: episode to its season, season or movie or show to Home.
```

## 3. Play

```text
Add playback to the Akira frontend. Akira decides what to play; the frontend
only describes the device and follows the plan.

1. POST /api/v1/items/{id}/playback-plan with "capabilities": the video codecs,
   containers, audio codecs it decodes (audioDecode) and passes through
   (audioPassthrough), subtitle formats, HDR formats, maxHeight, maxBitrateKbps,
   and accessModes ["http", "hls"]. Use the device's real specs. Akira converts
   what is not listed; a list left out means "plays everything", so nothing is converted.
2. The plan has sessionId, sources, the chosen audio and subtitle tracks, all
   tracks, chapters, markers, resumePositionMs and trickplayUrl.
3. Play the first source. Mode "http" is the file with range requests; mode
   "hls" is a playlist Akira transcodes. Send the Authorization header on every
   media request.
4. Start at resumePositionMs. Select the plan's audio and subtitle track.
5. Every 10 seconds, and on pause and seek: POST /api/v1/sessions/{sessionId}/progress
   with positionMs and isPaused.
6. When the user changes audio or subtitles: POST /api/v1/sessions/{sessionId}/tracks.
   Akira remembers the choice for the movie or the whole show.
7. On exit: POST /api/v1/sessions/{sessionId}/stop with positionMs.
8. If the "http" source fails to play, ask a new plan with accessModes ["hls"].

Text subtitles: GET /api/v1/files/{fileId}/subtitles/{streamId}/vtt gives WebVTT.
Markers: show "Skip intro" and "Skip credits" buttons at their times.
Trickplay: GET the trickplayUrl for seek thumbnails; 404 means none yet.
```

## 4. Live updates

```text
Add live updates to the Akira frontend.

- Open the WebSocket /api/v1/events/ws?since=<last sequence>, with the
  Authorization header in a native app or the session cookie in a browser. It replays what was missed, sends "stream.ready", then
  every event as it happens. Store the last "sequence" you handled.
- Reload what an event touches: library events reload the lists, watch state
  events update progress and watched marks.
- On a lost connection, reconnect with a backoff. Offline catch-up:
  GET /api/v1/events?since=<sequence>.
- Keep the socket open only while the app is in view.
```

## 5. Subtitles and track rules

```text
Add subtitle requests and track rules to the Akira frontend.

- GET /api/v1/items/{id}/subtitle-options?language=<ISO 639-2 code> lists the tracks the
  file has, downloads, and tracks to translate from.
- POST /api/v1/items/{id}/subtitle-requests asks for one. Progress arrives as
  "subtitle.request.updated" events, or poll GET /api/v1/subtitle-requests/{id}.
  When it is done, ask a new playback plan to get the new track.
- The user's languages: GET and PUT /api/v1/users/me/track-preferences.
- Rules for one movie or show: GET, PUT and DELETE /api/v1/items/{id}/track-preferences.
```

## 6. TV and remote

```text
Make the Akira frontend work with a TV remote or game controller only.

- Every element is reachable with the arrow keys. Show a clear focus ring.
- Each screen starts its focus where a remote expects it: the first row on Home,
  Play on a title page.
- Back returns focus to the element that opened the screen.
- In the player: left and right seek 10 seconds, OK shows the controls, Back hides
  them and then leaves.
```

## Tips

- Ask the assistant to check each call against `openapi.json` when a request fails with 400.
- Send the device's real capabilities in the playback plan. Akira converts only what the device does not list. A list left out means the device plays everything, so Akira converts nothing and an unsupported file fails.
- Make the frontend controllable from another device: connect the WebSocket with `remote=true`, and handle `remote.command` events. See [API](../developers/api#live-events).
