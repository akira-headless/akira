# API

Akira is headless: every feature is in the REST API under `/api/v1`, and a frontend needs nothing else. A frontend only shows the library and plays what the plan says. Track choice, watch state and transcoding stay in the server.

## Documentation

- The OpenAPI document: `http://<host>:8484/openapi/v1.json`.
- Scalar, to try calls: `http://localhost:8484/scalar`, in development only.

## Credentials

| Credential | Header |
|------------|--------|
| Device token | `Authorization: Bearer pdt_...` |
| API key | `X-Api-Key: pak_...` |

Get a device token with a pairing (`POST /api/v1/auth/pairings`) or a PIN sign-in at home (`POST /api/v1/auth/pin/sign-in`). Make an API key on the admin page under **API keys**. A key acts as the user who made it.

Never put a credential in a url.

## Playback

`POST /api/v1/items/{id}/playback-plan` with the device's capabilities returns the plan: the version, the tracks and the sources. The client then reports `progress`, `tracks` and `stop` on `/api/v1/sessions/{id}`.

## Live events

`/api/v1/events/ws` is a WebSocket with every event, after a replay from `since`. `GET /api/v1/events?since=` is the catch-up for a client that was offline.

A client that can be controlled connects with `remote=true`. It then gets `remote.command` messages, which are not stored. Send them with `POST /api/v1/remote/devices/{id}/commands`.

## Webhooks out

An admin adds webhooks under **Webhooks**. Akira posts events to them in order. A slow or failing webhook holds back only its own events. Each post has `X-Akira-Event`, `X-Akira-Delivery` and `X-Akira-Signature: t=...,v1=...`, an HMAC-SHA256 of `t.body` with the webhook secret.
