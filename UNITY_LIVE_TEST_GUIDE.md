# Unity Live Test Guide

## What is already configured

- Test scene: `Assets/Scenes/SynthCohostLiveTest.unity`
- Reusable prefab: `Assets/SynthCohost/Prefabs/SynthCohostLiveTest.prefab`
- Connection settings: `Assets/SynthCohost/Configuration/SynthCohostLiveConnectionSettings.asset`
- Live WebSocket default: `wss://synth-cohost-app.onrender.com/ws`
- Auto Connect is disabled so credentials can be entered safely after Play Mode starts.
- Access tokens and avatar IDs are not serialized into the scene, prefab, settings, or source.

`SampleScene` remains the clean starter scene. Use the dedicated live-test scene for backend testing.

## Required credentials

The live test requires both values from the same live backend account:

1. A valid short-lived access token obtained through the backend's supported login/refresh flow.
2. A tenant-owned avatar UUID created or returned by the backend/avatar dashboard.

The exact REST register/login/refresh/`POST /avatars` request and response schemas are not present in this Unity repository. Request either a test token plus matching avatar UUID from the backend developer, or the exact REST API contract so Unity can implement the flow.

Do not use the development token or placeholder avatar from `test_ws.sh` against the live backend.

## Run the live test

1. Let Unity finish compiling. Confirm there are no Console errors.
2. In the Project window, open `Assets/Scenes/SynthCohostLiveTest.unity`.
3. Press Play and select the Game view.
4. Paste the temporary access token into **Access token**. It is masked and clears from the panel after it is transferred to the runtime-only credential provider.
5. Paste the matching avatar UUID into **Avatar UUID**.
6. Click **Connect** once.
7. Watch **State**. A sleeping Render service can remain `Connecting (Waking server...)` for about 50 seconds. Do not click Connect again.
8. Continue only when the state becomes `Ready`.
9. Enter text under **Transcript**, then click **Send Final**.
10. Expect a simulated avatar state such as `thinking`, followed by an AI response containing text, emotion, and intent. Any `system.error` is displayed in the panel.
11. Leave the connection open for more than 60 seconds to confirm 20-second heartbeats keep it alive.
12. Click **Disconnect**, then exit Play Mode.

The avatar adapter in this test scene records behavior and sends `state.ack`; it does not animate a final avatar model.

## Change live/local endpoint

1. Exit Play Mode.
2. Select `Assets/SynthCohost/Configuration/SynthCohostLiveConnectionSettings.asset`.
3. Change only **Endpoint URL**:
   - Live: `wss://synth-cohost-app.onrender.com/ws`
   - Local: `ws://127.0.0.1:8080/ws`
4. Enter Play Mode again and use credentials valid for that environment.

Never put a token into the settings asset, a scene, a prefab, source code, `PlayerPrefs`, or Git.

## Repair the generated setup

If an asset/reference is accidentally removed, exit Play Mode and run:

`Tools > Synth Cohost > Create or Repair Live Test Scene`

This recreates the settings asset, prefab references, scene, and Build Settings entry. It does not create or save credentials.

## Useful failure meanings

- `AuthRequired`, close `4000`, or close `4001`: obtain a fresh token and confirm the avatar belongs to that account.
- Close `4002`: the session/envelope does not match the deployed v2 contract.
- Close `4003`: heartbeat timeout; capture the Console and panel status.
- Close `4004`: rate limit; wait for the displayed retry period and reduce partial-message frequency.
- `TurnAlreadyInFlight`: wait for the current `ai.response`, terminal error, disconnect, or turn timeout.
