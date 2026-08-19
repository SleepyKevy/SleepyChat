# Shared Kick Backend Contract

SleepyChat 1.0.0 uses the hosted shared API:

`https://sleepysource-api.sleepyservices.workers.dev`

The backend deployment source is intentionally **not** bundled in the SleepyChat source package. It is shared infrastructure rather than SleepyChat desktop source.

## Desktop routes used

- `POST /oauth/kick/start`
- `POST /oauth/kick/status`
- `POST /kick/connection/status`
- `POST /kick/events/sync`
- `POST /kick/chat/send`
- realtime `/realtime/connect`
- polling/ack fallback under `/kick/events/delivery/*`

The desktop sends only its opaque `connection_id` and `connection_token` to authenticated routes. The hosted service owns Kick access-token refresh, webhook verification, event subscription management, and Kick chat API calls.

## Permissions

Kick sending requires `chat:write`. The desktop reads the granted scope list from the hosted connection response and only enables sending when `chat:write` is present.

## Important maintenance rule

Do not create or deploy a separate SleepyChat Cloudflare Worker from this source tree. Changes to the shared hosted backend should be made and deployed from the canonical shared backend project/package.
