# Dashboard leftover

**Project:** Dashboard AI co-host (this Unity repository)  
**Requirements (attachment, gaps, edge cases, deliverables D0–D6):** [DASHBOARD-requirements.md](./DASHBOARD-requirements.md)  
**Product split:** [FINDINGS-two-projects.md](./FINDINGS-two-projects.md)

This file is the remaining **checklist** for dashboard only. How work hooks into `Assets/SynthCohost` is specified in the requirements doc §9. Do not rebuild the WebSocket client.

Status baseline: deployed v2 client, HTTP auth/refresh, live-test harness. The SampleScene AvatarVerify character is now in `SynthCohostLiveTest` and driven through `DashboardAvatarPresenter` on the existing WebSocket `avatarController` slot.

---

## Summary

| Area | Status | Deliverable |
|------|--------|-------------|
| Cloud WebSocket v2 client | **Done** — keep; do not replace with draft 2.1 | D0 |
| HTTP login / refresh / logout + background renewal | **Done** | D0 |
| Live-test scene / credential panel / safe logs | **Done** (harness, not product UI) | D0 |
| EditMode 182/182 + PlayMode live-test + dashboard smoke | **Done** | D0 |
| Real avatar on existing `IAvatarBehaviorController` slot | **Done** in live-test/dashboard scene | D1 |
| Captions + emotion/intent presenter | **Done** (uGUI captions + smile/sad blendshapes) | D1 |
| Host embed / delivery method | **Blocked** on product decision | D2 |
| Dashboard config apply | **Blocked** on schema | D3 |
| LiveKit | **Blocked** on contract | D4 |
| Conversational presence (letter) | Local idle / listening / thinking loops in presenter | D5 |
| Draft 2.1 / viseme IDs / OBS | Out of scope until backend ships | D6 |

---

## A. Shipped (do not rebuild)

Attach new work to these — do not duplicate them:

- `SynthCohostClientBehaviour` Compose: router → `AvatarStateMessageHandler` / `AiResponseMessageHandler` / `SystemErrorMessageHandler` / `SpeechAudioMessageHandler` / `SpeechFailedMessageHandler`
- Inspector slots: `avatarController`, `aiResponseSink`, `systemErrorSink` (null objects send **no** `state.ack` / drop events)
- `AnimatorAvatarBehaviorAdapter` — six behavior **triggers**; false if Animator/trigger missing
- `ICohostOutboundSession` STT + `state.ack`; `FinalTurnGate`
- Connect `ws`/`wss`, cold-start timeout, provisional Ready, 20s heartbeat
- Close 4000–4004 policy; no replay; ignore unknown types
- HTTP `login` / `refresh` / `logout`; quiet WS reconnect on token rotation
- Tokens never in Git / scenes / settings assets

Live endpoint default: `wss://synth-cohost-app-bzi4.onrender.com/ws`  
REST: `https://synth-cohost-app-bzi4.onrender.com`

---

## B. Product leftover

### B1. Avatar presentation (D1) — next Unity milestone

**Attach:** assign a real Animator to `AnimatorAvatarBehaviorAdapter` on the existing `avatarController` slot. Optional: same object also implements `IAiResponseSink` for face/captions, or a second sink on `aiResponseSink`.

**Do not:** open another socket, parse `avatar.state` in a custom Update loop, or wait for clip-end before `state.ack`.

**Done in the live-test/dashboard scene**

- [x] SampleScene avatar copied in as `DashboardAvatar` (prefab + scene)
- [x] `DashboardAvatarPresenter` on the WebSocket `avatarController` slot
- [x] Six behaviors mapped to AvatarVerify states
- [x] Living idle loop, keep-in-place, eye lock
- [x] Rapid CrossFade; happy/celebrate burst then local idle (no ack)
- [x] Caption overlay with emotion/intent
- [x] False apply → no ack; unknown enum does not throw
- [x] Leave-Ready visual reset without ack
- [x] Tests + `Tools > Synth Cohost > Install Dashboard Avatar In Live Test Scene`
- [x] Backend `speech.audio` playback + `mouth_open_M` from mouth-openness frames; local SAPI fallback
- [x] Speaking body uses the idle breathing clip (`01_Idle_A_(Breathing)`); mouth is blendshapes + audio

**Still later**

- Phoneme/viseme IDs (`speech.viseme`) if backend ever sends them — not required for current `frames[].o`
- Host injects credentials (D2); dashboard scene currently uses env / gitignored JSON

**Unblocked Unity work now in tree**

- Live-test **local preview** of the six behaviors (no `state.ack`)
- Hide / compact panel and lock-eyes toggle
- uGUI captions + optional blendshape emotion mapping
- Backend `speech.audio` (Deepgram) when live; local Windows TTS of `ai.response` only as fallback
- Live-test panel activity for wait / `speech.audio` / `speech.failed` / fallback (no audio bytes)
- Product scene `Assets/Scenes/SynthCohostDashboard.unity` (menu: `Tools > Synth Cohost > Create or Repair Dashboard Scene`)
- Host API on `SynthCohostClientBehaviour`: `SetAuthSession` / `SetRuntimeCredentials` / `ConnectAsync` / `SendFinalTranscriptAsync` / `DisconnectAsync`

**Not this item:** Desktop Mate pickup, OS windows, climbing mugs, Tauri IPC.

### B2. Host integration (D2) — blocked

**Attach:** Host calls `SetAuthSession` / `SetRuntimeCredentials` + `ConnectAsync`, and either `Outbound.SendFinalTranscriptAsync` or inbound-only states. Live-test panel is the stand-in Host until then.

- [ ] Agree delivery: Windows client vs WebGL embed vs capture vs LiveKit-only
- [ ] Freeze build target, resolution, alpha, audio
- [ ] If WebGL: new `IWebSocketTransport` (do not assume `ClientWebSocket`)
- [x] Bootstrap without the live-test panel in player builds (dashboard scene; Host still injects credentials later)
- [ ] Name the production turn-start path (Host text vs backend-only vs mic-later)
- [ ] One-instance / persist-across-scene rule
- [ ] Host reload / expired JWT sanitized error

Until they answer, **do not** start a WebGL transport or production embed scene.

### B3. LiveKit (D4) — blocked on contract

**Attach:** publisher next to the D1 camera. Not inside `ClientWebSocketTransport`.

- [ ] Versioned role: Unity publishes video/audio? Host composites? Token/room owner?
- [x] Camera tagged `DashboardStreamCamera` + `DashboardStreamCameraMarker` (no LiveKit package)
- [ ] WS reconnect vs LiveKit room rebuild
- [ ] Do not invent OBS in Unity

### B4. Dashboard configuration (D3) — blocked on schema

**Attach:** new `IProtocolMessageHandler` **only** if backend versions an event; otherwise Host→Unity bridge into the presenter.

- [x] JSON schema, event name, transport — **hook only:** `IDashboardConfigApplier.ApplyUnknownSafe` ignores unknown fields; no WS event yet
- [ ] Apply without restart; ignore unknown fields
- [ ] Do not overload `avatar.state`

### B5. Conversational presence (D5) — later, design in D1

**Attach:** local animation layers while wire behavior is `listening` / `thinking` / `speaking`. No new protocol types.

- [ ] Listening / acknowledging / thinking / speaking / reacting blends (beyond current idle / listening / thinking loops)
- [x] Timing owned in Unity for idle / listening / thinking loops

### B6. Computer vision overlay — owner unnamed

Do not start until the client says this is dashboard. Not part of the current WS client.

---

## C. WebSocket / auth leftover (D0 tail)

The live contract is implemented. This is release hardening.

### C1. Unity engineering

- [x] Diagnostic counters (`ConnectCount`, `HeartbeatSendCount`, `InboundCount`, `SanitizedErrorCount` on `ConnectionStatusViewModel`)
- [x] PlayMode: scene load, local preview/rapid replace, URL change while disconnected, destroy client without throw
- [ ] PlayMode: simulated cold start, background heartbeat soak, single-instance persist (Host persist still default off)
- [ ] Re-audit plan checkboxes (50s pending connect, 75s cancel, reconnect fresh session / no replay) before rewriting tests
- [ ] Distinguish in-session `system.error` vs error-then-close
- [ ] Bound remaining buffers / logs
- [x] Confirm Endpoint URL alone switches local vs live (PlayMode + live-test panel while disconnected)
- [x] Windows Standalone smoke of the live-test **and** dashboard scenes — operator checklist in `UNITY_LIVE_TEST_GUIDE.md` (you run the build)

### C2. Waiting on backend / ops

Lucas rebuilt the live host on 2026-09-07 (`synth-cohost-app-bzi4.onrender.com`). Old host/account are gone. AI replies are mock; connection/session should work.

- [x] Fresh-token Unity-panel happy path on the rebuilt host (login / Connect / thinking ack / heartbeat; AI still `AI_GENERATION_FAILED`)
- [ ] Real Render cold start after ≥15 min idle
- [ ] Heartbeat soak >60s and >15 min
- [ ] Reconnect + turn-ordering sign-off
- [ ] Close-code fixtures 4000–4004
- [ ] Production vs mock matrix
- [ ] Versioned protocol diff if they freeze Unity-visible changes

### C3. Do not implement now

- Draft 2.1 (`protocol_version`, `session.ready`, server session id)
- `heartbeat.ack` as required
- Phoneme viseme IDs (`speech.start` / `speech.viseme` / `speech.end`) until backend sends them — `speech.audio` mouth openness is already wired
- OBS / RTMP / NDI from Unity
- Concurrent `stt.final` turns
- Session resumption / replay
- Encrypted-at-rest token storage

---

## D. Edge-case tests to add with D1 (not a protocol rewrite)

Full table: requirements §11. Minimum adapter/PlayMode coverage:

- [x] Missing Animator → `ApplyAsync` false (no ack path)
- [x] Duplicate `idle` still succeeds when the Animator state exists (PlayMode preview)
- [x] `thinking` then `speaking` before thinking ends → last state wins (PlayMode preview)
- [x] `ai.response` with empty text does not throw
- [x] Preview apply does not send `state.ack` (local presenter only)
- [ ] Missing trigger → no ack, previous visual kept (this rig uses states, not triggers)
- [x] Stale session frame never reaches Animator
- [ ] Reconnect does not replay STT or re-ack old behavior
- [x] Unknown inbound type ignored (reserved `speech.start` / `speech.viseme` / `speech.end` / `speech.chunk`; `speech.audio` is handled)
- [x] Gate rejects second final; character stays on current thinking/speaking
- [x] Dispose/stop does not throw after destroying the live-test client object

---

## E. Sequence

1. D0 tail: Windows smoke + fresh-token pass when available. Do not wait for 2.1.
2. **D1** as soon as the rig exports — this is the next billed increment.
3. D2 after Host names delivery + turn-start path.
4. D3 when schema arrives.
5. D4 when LiveKit contract arrives.
6. D5 / D6 after D1 and backend media/2.1 sign-off.

---

## F. Done bar

Until D1, this repo is a **complete networking client with a debug harness**.  
Until D2–D4, it is not a Host/stream co-host even if the Editor character looks right.

Acceptance list: [DASHBOARD-requirements.md](./DASHBOARD-requirements.md) §13.
