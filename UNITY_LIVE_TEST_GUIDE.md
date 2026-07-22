# Unity Live Test Guide

Product context (end goal, Host/Viewer/Unity/LiveKit split): see root **[README.md](README.md)**. This guide is only for exercising the Unity WebSocket / auth client against the live backend.

## What is already configured

- Test scene: `Assets/Scenes/SynthCohostLiveTest.unity`
- Reusable prefab: `Assets/SynthCohost/Prefabs/SynthCohostLiveTest.prefab`
- Connection settings: `Assets/SynthCohost/Configuration/SynthCohostLiveConnectionSettings.asset`
- Live WebSocket default: `wss://synth-cohost-app.onrender.com/ws`
- Auto Connect is disabled so credentials can be entered safely after Play Mode starts.
- Access tokens and avatar IDs are not serialized into the scene, prefab, settings, or source.
- Endpoint, access token, and avatar UUID are editable in the Game-view test panel before **Connect**.

`SampleScene` remains the clean starter scene. Use the dedicated live-test scene for backend testing.

## Required credentials

The live test requires both values from the same live backend account:

1. A valid short-lived access token obtained through the backend's supported login/refresh flow.
2. A tenant-owned avatar UUID created or returned by the backend/avatar dashboard.

The live REST base is `https://synth-cohost-app.onrender.com`. The verified temporary workflow uses `POST /auth/login` with email/password and `POST /auth/refresh` when a fresh access token is needed; avatars are created through `POST /avatars`. REST credentials remain outside Unity, and the live-test panel accepts only the resulting access token and matching avatar UUID at runtime.

Do not use the development token or placeholder avatar from `test_ws.sh` against the live backend.

## Run the live test

1. Let Unity finish compiling. Confirm there are no Console errors.
2. In the Project window, open `Assets/Scenes/SynthCohostLiveTest.unity`.
3. Press Play and select the Game view.
4. Review or change **WebSocket endpoint** while the client is disconnected.
5. Paste the temporary access token into **Access token**. It is masked, shows a safe expiry status, and clears after transfer to the runtime-only credential provider.
6. Paste the matching avatar UUID into **Avatar UUID**.
7. Click **Connect** once.
8. Watch **State**. A sleeping Render service can remain `Connecting (Waking server...)` for about 50 seconds. Do not click Connect again.
9. Continue only when the state becomes `Ready`. In deployed v2 this initially means the socket opened and the auth frame was sent; there is no positive `session.ready` response. The panel changes to **backend activity received** after the first valid inbound event.
10. Enter text under **Transcript**, then click **Send Final**.
11. Expect a simulated avatar state such as `thinking`, followed by an AI response containing text, emotion, and intent. Any `system.error` is displayed in the panel.
12. Leave the connection open for more than 60 seconds to confirm 20-second heartbeats keep it alive.
13. Click **Disconnect**, then exit Play Mode.

The avatar adapter in this test scene records behavior and sends `state.ack`; it does not animate a final avatar model.

## Connection and failure logs

The same safe activity is now shown in two places:

- **Activity log (also written to Console)** at the bottom of the Game-view panel.
- **Window > General > Console**, using the `[SynthCohost/LiveTest]`, `[SynthCohost/Bootstrap]`, `[SynthCohost/Transport]`, and `[SynthCohost/Session]` prefixes.

Enable the **Log**, **Warning**, and **Error** buttons in the Console toolbar. The expected startup sequence includes `Panel initialized`, `Client composed`, and `Auto-connect is disabled`. After clicking **Connect**, expect either:

- A clear red preflight error explaining which input blocked the socket; or
- `Connect requested`, `Opening WebSocket`, `Runtime endpoint accepted`, `Runtime credentials staged`, transport/socket progress, auth sent, and provisional `Ready`.

During a cold start, additional entries appear after 5, 30, and 60 seconds. If the state becomes `Reconnecting`, automatic retry is active and **Disconnect** cancels it.

The previously supplied 15-minute placeholder token is expired. With that token, **Connect is intentionally blocked before opening a socket** and both log views report that a fresh token is required.

Logs never include the token, avatar UUID, transcript or AI text, session ID, raw backend message, or exception message.

## Change live/local endpoint

For a one-run change, edit **WebSocket endpoint** directly in the Game-view panel while disconnected, then click **Connect**. This override remains in memory and does not dirty the settings asset.

Use a plain `ws://` or `wss://` endpoint with no embedded user info, query string, or fragment. Non-loopback targets require `wss://`.

For a persistent non-secret default, exit Play Mode, select `Assets/SynthCohost/Configuration/SynthCohostLiveConnectionSettings.asset`, and change **Endpoint URL**:

   - Live: `wss://synth-cohost-app.onrender.com/ws`
   - Local: `ws://127.0.0.1:8080/ws`

Then enter Play Mode again and use credentials valid for that environment.

## Optional local placeholders / PC build login

In the live-test panel you can enter:

1. Account email  
2. Account password  
3. Avatar UUID  
4. Click **Get access token**  
5. Click **Connect**

That flow works in the **Editor** and in **PC builds**. Credentials are never stored in scenes, prefabs, or Git.

### Automatic access-token renewal (bridge `docs/auth-token-flow.md`)

When Connect is used with both an access token and a refresh token (from **Get access token**), Unity:

- Stores both tokens in a runtime-only auth session
- Refreshes the access token in the background ~2 minutes before JWT expiry
- Always replaces **both** access and refresh tokens after rotation
- Quietly reconnects the WebSocket with the new access token (deployed v2 has no mid-session re-auth)
- On `AUTH_FAILED` / close `4001`, attempts **one** refresh + reconnect before requiring a full login again
- Clears tokens when refresh itself returns `401`

Access tokens still last ~15 minutes; refresh tokens last ~30 days. Logout (when used) calls `POST /auth/logout` then clears local session state.

The panel can also prefill values from process environment variables or a local JSON draft. Environment variables take precedence:

- `SYNTH_COHOST_ENDPOINT`
- `SYNTH_COHOST_ACCESS_TOKEN`
- `SYNTH_COHOST_AVATAR_ID`

Local-file shape (`SynthCohostLiveTest.local.json`):

```json
{
  "endpointUrl": "wss://synth-cohost-app.onrender.com/ws",
  "accessToken": "paste-a-fresh-short-lived-token",
  "avatarId": "00000000-0000-0000-0000-000000000000",
  "refreshToken": "optional-long-lived-refresh-token",
  "email": "optional-test-account@example.com",
  "password": "optional-test-password"
}
```

Where the file is read/written:

| Environment | Path |
|-------------|------|
| Unity Editor | `UserSettings/SynthCohostLiveTest.local.json` (gitignored) |
| PC build (auto-save after Get access token) | `Application.persistentDataPath/SynthCohostLiveTest.local.json` |
| PC build (optional drop-in) | Same folder as the `.exe`: `SynthCohostLiveTest.local.json` |

**Get access token** uses `/auth/refresh` when a refresh token is saved, otherwise `/auth/login` with email/password. Access tokens still expire in about 15 minutes; click **Get access token** again before Connect when needed.

Never put a token into the settings asset, a scene, a prefab, source code, `PlayerPrefs`, or Git.

## Repair the generated setup

If an asset/reference is accidentally removed, exit Play Mode and run:

`Tools > Synth Cohost > Create or Repair Live Test Scene`

This recreates the settings asset, prefab references, scene, and Build Settings entry. It does not create or save credentials.

## Useful failure meanings

- `AUTH_FAILED`, `AuthRequired`, close `4000`, or close `4001`: obtain a fresh token and confirm the avatar belongs to that account. Paste the replacement token and click **Connect**; **Reconnect** intentionally does not retry a rejected token.
- Close `4002`: the session/envelope does not match the deployed v2 contract.
- Close `4003`: heartbeat timeout; capture the Console and panel status.
- Close `4004`: rate limit; wait for the displayed retry period and reduce partial-message frequency.
- `TurnAlreadyInFlight`: wait for the current `ai.response`, terminal error, disconnect, or turn timeout.

The panel now shows the last inbound event, last outbound event, their UTC times, the sanitized close summary, and a safe client error. The live settings asset uses verbose diagnostics, so the Console also records state transitions, event names, reconnect timing, and close-policy decisions without logging tokens or transcript contents.
