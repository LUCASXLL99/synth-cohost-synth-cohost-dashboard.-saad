# Dashboard AI co-host — Unity requirements

**Project:** Dashboard / Host AI co-host (this Unity repository)  
**Not this project:** Desktop companion (see [DESKTOP-COMPANION-plan.md](./DESKTOP-COMPANION-plan.md))  
**Leftover checklist:** [DASHBOARD-leftover.md](./DASHBOARD-leftover.md)  
**Split rationale:** [FINDINGS-two-projects.md](./FINDINGS-two-projects.md)

This document is the dashboard source of truth for **what Unity must do**, **how that attaches to the existing WebSocket client**, **what is still missing**, **edge cases**, and **final deliverables**. Do not rewrite `Assets/SynthCohost` networking to land avatar, Host, or LiveKit work.

---

## 1. Product definition (clear)

Unity is the **live AI co-host runtime**:

1. Renders and animates the AI avatar.
2. Stays connected to the Synth Cohost **cloud** backend (WebSocket + HTTP auth).
3. Plugs into the **Host** experience so the creator dashboard can control and interact with the co-host.
4. Later joins the **live stream** through **LiveKit** (not a forever-standalone desktop toy).

Next.js owns Host UI, Broadcast Viewer UI, and Meeting Participant UI. Unity does **not** implement those screens.

Confirmed client line (21 July 2026), summarized in root `README.md`:

- The three UIs are host and viewer-facing dashboard/streaming.
- Unity powers the AI co-host itself.
- Current WebSocket work is part of that architecture, not a side track.
- Host UI controls and interacts with the live co-host.
- LiveKit comes later so the co-host is in the stream.

---

## 2. Architecture Unity must respect

| Piece | Owner | Unity action |
|-------|--------|----------------|
| Host UI | Next.js | Consume / embed / be driven. Do not rebuild the dashboard. |
| Broadcast Viewer UI | Next.js | Out of scope |
| Meeting Participant UI | Next.js | Out of scope |
| Unity AI co-host | **This repo** | Avatar, rendering, animation, interactions, backend client |
| Backend | Synth Cohost API / WS | Auth, AI, avatar state, session |
| LiveKit | Shared, later | Put Unity output into the live session |

Frontend join routing (`/join/{roomId}`, `sessionType = broadcast | meeting`) is **not** Unity work.

**Do not:**

- Hardcode temporary Vercel preview URLs.
- Implement draft WebSocket 2.1 until the backend ships it.
- Treat OBS / RTMP / NDI / scene switching as Unity work (backend/sidecar).
- Depend on a frontend domain as a WebSocket endpoint.
- Fold desktop-companion IPC or Tauri OS code into this client.

---

## 3. Clear networking requirements (live contract)

Canonical doc: `Assets/Docs/websocket-protocol-spec.docx` **as deployed today** (spec §9), plus backend confirmations recorded in `UNITY_WEBSOCKET_IMPLEMENTATION_PLAN.md`.

**Implementation status:** the live contract is already in `Assets/SynthCohost`. Remaining networking work is validation and backend-gated follow-ups — see leftover file section C. New product work **subscribes** to this client; it does not replace it.

Live targets:

- WebSocket: `wss://synth-cohost-app-bzi4.onrender.com/ws`
- REST: `https://synth-cohost-app-bzi4.onrender.com`
- Optional local: `ws://127.0.0.1:8080/ws`

### 3.1 Envelope (deployed v2)

Every message, including auth:

```json
{
  "v": 2,
  "type": "<event_name>",
  "session_id": "<client-generated-uuid>",
  "ts": "<rfc3339-utc>",
  "payload": {}
}
```

- `v` is integer `2`, not a string.
- Unity generates a fresh session UUID per socket and repeats it on every frame.
- Unknown `type` values are ignored, not fatal.
- One avatar is selected at auth and stays fixed for that connection.

### 3.2 Handshake

1. Obtain short-lived access token + tenant-owned avatar UUID.
2. Open the configured socket (no extra headers, query, or subprotocol).
3. Send `auth` as the first application frame within 10 seconds of socket **open**.
4. There is **no** `session.ready` on live v2. After auth send succeeds, enter provisional Ready.
5. Rejected auth arrives as `system.error` and/or close 4000/4001.

Auth payload:

```json
{ "token": "<access-jwt>", "avatar_id": "<uuid>" }
```

Refresh tokens must not be sent on the socket. HTTP refresh is a separate REST flow.

### 3.3 Unity → server (required)

| Type | Payload | Rule |
|------|---------|------|
| `auth` | `{ token, avatar_id }` | First frame |
| `heartbeat` | `{}` | Every 20 seconds after auth; no ack required |
| `stt.partial` | `{ text, final: false }` | Optional product use; DTO required |
| `stt.final` | `{ text, final: true }` | One in flight; never replay after disconnect |
| `state.ack` | `{ behavior }` | Only after Unity **successfully applied** the behavior |

Limits: STT text non-empty, ≤ 4000 UTF-8 bytes; message ≤ 64 KiB; JSON depth 32; ≤ 60 client messages / 10 seconds.

### 3.4 Server → Unity (required)

| Type | Payload | Unity must |
|------|---------|------------|
| `avatar.state` | `{ behavior }` | Apply behavior on main thread, then `state.ack` |
| `ai.response` | `{ text, emotion, intent }` | Publish typed caption/data; do not require exact wording |
| `system.error` | `{ code, message }` | Sanitize; connection may stay open or close |

Expected happy path after `stt.final`: `avatar.state` thinking → `avatar.state` response behavior → `ai.response`.

Ignore until wired by backend: `speech.start`, `speech.viseme`, `speech.end`.

### 3.5 Enums (fail safe on unknown values)

- Behavior: `idle`, `listening`, `thinking`, `speaking`, `happy`, `celebrate`
- Emotion: `neutral`, `happy`, `excited`, `concerned`, `confused`, `celebrate`
- Intent: `chat`, `question`, `command`, `greeting`, `farewell`, `unknown`

Unknown wire strings must not crash the socket loop or force an invalid Animator state. The codec already diagnoses unknown enums; presentation must not throw on a future C# `default` either.

### 3.6 Close codes

| Code | Meaning | Unity policy (provisional) |
|------|---------|----------------------------|
| 4000 | `MALFORMED_PAYLOAD` | Stop retrying; surface protocol/config error |
| 4001 | `AUTH_FAILED` | `AuthRequired`; never retry the same token |
| 4002 | `SESSION_MISMATCH` | One fresh connection; stop if repeated |
| 4003 | `HEARTBEAT_TIMEOUT` | Reconnect with new session |
| 4004 | `RATE_LIMIT_EXCEEDED` | Wait ≥ 10s + backoff |

Reconnect = new socket + new session id + new auth. No session resumption. No replay of an interrupted `stt.final`.

### 3.7 HTTP auth (implemented; still a dashboard requirement)

- `POST /auth/login`, `/auth/refresh`, `/auth/logout`
- Access ~15 minutes, refresh ~30 days, refresh rotation (replace **both** tokens)
- Refresh before handshake when JWT is inside skew window
- Background renewal ~2 minutes before access expiry while Ready
- One automatic refresh+reconnect on `AUTH_FAILED` / 4001, then full login
- Never serialize tokens into tracked Unity assets, scenes, or Git

Token rotation **reconnects the WebSocket** (deployed v2 has no mid-session re-auth). Presentation must survive that reconnect (see §11).

### 3.8 Draft 2.1 — requirement is “do not build yet”

The DOCX also describes, **not deployed**:

- `protocol_version` string instead of integer `v`
- Auth **without** `session_id`
- Server-generated session id via `session.ready`
- `heartbeat_interval_ms` advertised by server

Keep `IProtocolDialect`. Implement 2.1 only after backend names version, endpoint, date, and fixtures.

---

## 4. Clear avatar / presentation requirements

These are dashboard requirements even though the **character is not wired yet**.

1. **Apply `avatar.state` behaviors** with a real character (not only a debug recorder).
2. Send `state.ack` only after a successful apply. Do not ack if the controller is missing, the Animator is missing, or the trigger/clip is missing.
3. Use `ai.response` text, emotion, and intent for captions and expression logic.
4. Keep transport, protocol, session, routing, and presentation **independent** (already true in `Assets/SynthCohost`).
5. Design the state machine so conversational presence can land later (see §5) without rewriting networking.
6. Bind the visual character to the **same** `avatar_id` used at auth (today the UUID is sent on the wire only; Unity does not load a mesh from it).

Windows Standalone 64-bit is the current target. WebGL only if product chooses embed-in-browser. A WebGL build needs a new `IWebSocketTransport` — do not assume `ClientWebSocket` ports.

---

## 5. Directional requirements (not immediate)

From `Letter for Unity Side.docx`. Client: understand the direction; **do not build it as the current task**.

Goal: reduce **perceived** latency. As soon as the user speaks, the avatar must look engaged — not frozen until the LLM returns.

Unity should eventually support continuous participation:

- Idle breathing and micro-movement (always alive)
- Listening: eye contact, nods, posture shifts, attentive face
- Optional visual reaction during backend backchannels while staying in listening
- Thinking while the response is generated
- Smooth transitions: listening → acknowledging → thinking → speaking → reacting → idle

Not Unity’s job to decide **when** a verbal backchannel happens (backend/LLM). Unity’s job is animation and timing inside the main app.

Also eventual (backend first): streaming STT while the avatar briefly speaks, without transcribing its own voice (barge-in / echo). No contract yet.

**Attachment note:** letter states are a **presentation layer** on top of the six wire behaviors. Do not invent new WebSocket event types for nods/breathing. Local loops can run while `avatar.state` is `listening` / `thinking` / `idle`.

---

## 6. Host / streaming requirements (clear intent, incomplete contract)

**Clear**

- Unity plugs into Host; Host controls the live co-host.
- Unity does not own Host/Viewer/Participant UI.
- LiveKit is how the co-host becomes part of the stream.

**Not specified in the docs folder (blockers — see §10)**

- Embed vs native window vs capture vs LiveKit-only
- Resolution, alpha, frame rate, audio routing into the stream
- How dashboard settings/personality/controls are sent to Unity
- How a live turn **starts** in production (who produces `stt.final`)

Until those are written, keep the client modular and wait on Host/LiveKit code. Avatar presentation can still proceed against the existing live-test STT sender.

---

## 7. Agreement items that stay on dashboard after the split

From Appendix A, mapped to this project:

- WebSocket connections, API communication, JSON, session, events, real-time updates
- Receiving dashboard configurations, applying avatar settings, behaviour parameters, user-selected controls (schema pending)
- Streaming-related avatar rendering, runtime communication, stream interaction (LiveKit pending)
- Facial expression / body / emotional reactions driven by **AI outputs** and **behaviour commands from the backend**

Desktop presence, sitting on OS windows, companion idle-on-desktop, runtime movement across the desktop → **companion project**, not here.

---

## 8. Out of dashboard Unity scope

- Next.js Host / Viewer / Meeting implementation
- Frontend layout, Vercel domains
- Backend AI, moderation, persistence, plan-tier enforcement
- OBS credentials and OBS scene control
- Real microphone STT / TTS / visemes until `speech.*` is wired and specified
- Desktop companion shell (Tauri), local companion IPC, desktop physics
- Proposed protocol 2.1 until deployed

---

## 9. How leftover work attaches to the existing WebSocket client

**Rule:** do not add Animator, captions, Host, or LiveKit inside `CohostSessionController`, the codec, or the transport. Those layers already dispatch typed events. Presentation **implements the existing interfaces** and is assigned on `SynthCohostClientBehaviour`.

### 9.1 What is already composed

`SynthCohostClientBehaviour.Compose()` builds:

```
Credential providers  →  CohostSessionController
                              │
                    ProtocolMessageRouter (main thread)
                              │
          ┌───────────────────┼───────────────────┐
          ▼                   ▼                   ▼
 AvatarStateMessageHandler  AiResponseMessageHandler  SystemErrorMessageHandler
          │                   │                   │
          ▼                   ▼                   ▼
 IAvatarBehaviorController  IAiResponseSink     ISystemErrorSink
          │                   │                   │
   then SendStateAck     PresentAsync         PresentAsync
```

Serialized Inspector slots (already on the bootstrap component):

| Slot | Interface | If left empty today |
|------|-----------|---------------------|
| `avatarController` | `IAvatarBehaviorController` | `NullAvatarBehaviorController` — `ApplyAsync` returns **false**, so **no `state.ack`** |
| `aiResponseSink` | `IAiResponseSink` | `NullAiResponseSink` — drops the response |
| `systemErrorSink` | `ISystemErrorSink` | `NullSystemErrorSink` — drops the error |

Live-test scene currently assigns **development** adapters (`LiveTestAvatarBehaviorAdapter` always returns true so ack can be tested without a character). Production dashboard scene must **not** ship those.

Existing production-shaped adapter (unused until a real Animator exists):

- `AnimatorAvatarBehaviorAdapter` — maps the six `AvatarBehavior` values to Animator **triggers**. Returns false if Animator or trigger is missing (correct ack behavior).

Existing caption-shaped adapter:

- `UnityEventAiResponseAdapter` — fires `(text, emotion, intent)` UnityEvents. No on-screen caption prefab yet.

Outbound STT already exists on the session:

- `ICohostOutboundSession.SendPartialTranscriptAsync`
- `ICohostOutboundSession.SendFinalTranscriptAsync`
- `FinalTurnGate` — one `stt.final` in flight; released on `ai.response`, terminal error, disconnect, cancel, or turn timeout

Auth already exists on the same behaviour:

- `SetRuntimeCredentials` / `SetAuthSession` / `LogoutAsync`
- Background `AccessTokenRenewalService` → quiet WS reconnect with a new session id

### 9.2 Attachment map (do this, not a rewrite)

| Upcoming work | Attach here | Do not |
|---------------|-------------|--------|
| Real character + clips | Replace live-test avatar adapter with `AnimatorAvatarBehaviorAdapter` (or a successor) on the **same** `avatarController` slot | Parse `avatar.state` again in a new MonoBehaviour that talks to the socket |
| Captions / subtitle UI | New `AiResponseSinkBehaviour` (or extend `UnityEventAiResponseAdapter`) on `aiResponseSink` | Subscribe to raw JSON in the panel |
| Face / emotion from `ai.response` | Same sink, or a **second** presenter that also implements `IAiResponseSink` via a small multiplexer — **or** have the avatar adapter also implement `IAiResponseSink` and assign it to both slots | Add emotion onto the `avatar.state` wire |
| Local alive-idle / listening micro-moves (letter, later) | Inside the avatar presenter, driven by **current applied behavior**, not new WS types | New client→server events for nods |
| Connection / error chrome for Host | `session.StateChanged` + `ISystemErrorSink` + `ConnectionStatusViewModel` | Log tokens or raw backend messages |
| Host “send this transcript” | Call `Outbound.SendFinalTranscriptAsync` / `SendPartialTranscriptAsync` from a Host bridge | Open a second WebSocket |
| Host “use these credentials” | `SetAuthSession` / `SetRuntimeCredentials` then `ConnectAsync` | Bake JWT into a scene |
| Dashboard config (when schema exists) | New **handler** on the router **or** a Host→Unity bridge that mutates the presenter. Prefer a new `IProtocolMessageHandler` only if backend adds a versioned event | Stuff settings into `avatar.state` |
| LiveKit | Separate publisher component that **reads the same Camera** (and later audio). Session/auth stay as they are | Put LiveKit inside `ClientWebSocketTransport` |
| Protocol 2.1 (later) | New `IProtocolDialect` implementation; swap in Compose | Fork presentation code |
| WebGL (only if Host embeds) | New `IWebSocketTransport` / factory; keep session + adapters | Copy-paste session logic |

### 9.3 Required data flow (production)

```
Host or live-test UI
    │  credentials + optional transcript text
    ▼
SynthCohostClientBehaviour
    │  HTTP refresh as needed
    ▼
CohostSessionController  ──auth / heartbeat / stt.*──►  cloud WS
    │
    │  inbound avatar.state / ai.response / system.error
    ▼
Feature adapters (Animator, captions, errors)
    │  state.ack only if ApplyAsync == true
    ▼
Dashboard / stream pixels (camera)
```

Happy path that presentation must visually support (already on the wire):

1. (Optional) local or Host text → `stt.final` (gate opens).
2. `avatar.state` `thinking` → Animator thinking → `state.ack`.
3. `avatar.state` `speaking` | `happy` | `celebrate` | … → Animator → `state.ack`.
4. `ai.response` → captions + emotion/intent on face.
5. Gate closes. Character returns to a living `idle` if the server does not send one.

### 9.4 Scene / prefab attachment

Keep `SynthCohostLiveTest` as a **developer harness**. Add a product scene (name TBD) that contains:

- `SynthCohostClientBehaviour` + `SynthCohostLiveConnectionSettings` (or a production settings asset with auto-connect policy defined with Host)
- Real avatar prefab with Animator + `AnimatorAvatarBehaviorAdapter`
- Caption canvas (or Host-composited captions — confirm)
- Camera framed for Host/stream (solid vs alpha — confirm)
- **No** live-test credential panel in player builds (`Development` assembly / `UNITY_EDITOR` / debug flag)

Do not put presentation scripts in `SynthCohost.Protocol` or `SynthCohost.Transport`.

---

## 10. What is missing (docs + product + code)

These are gaps, not companion work. They must be resolved or explicitly deferred.

### 10.1 Product contracts still absent

| Gap | Why it blocks | Until then |
|-----|----------------|------------|
| How Host **displays** Unity (WebGL vs Windows vs capture vs LiveKit-only) | Chooses build target, camera, alpha | Avatar work can use Standalone; do not start WebGL transport |
| How a **turn starts** in production | Protocol has Unity send `stt.final`. Host might send text to Unity, talk to backend itself, or Unity might own a mic | Live-test panel remains the only STT sender |
| Whether Host can push `avatar.state` **without** Unity sending STT | Presentation must work even if Unity never calls `SendFinalTranscriptAsync` | Adapters must not assume a local turn is in flight |
| Dashboard config JSON (settings, personality, controls) | Agreement lists it; no event name or transport | Do not guess a WS type |
| LiveKit: who publishes, room/token owner, video vs audio, resolution, alpha | Stream milestone | Camera should still be “streamable” (clean background) |
| `speech.*` viseme vocabulary, timing, cancel, reconnect | Lip-sync | Ignore unknown `speech.*` (already) |
| Character asset for `avatar_id` | Auth selects an avatar; Unity has no package load by UUID | One hardcoded dashboard character until they define a catalog |
| Computer-vision overlay owner | 8-clip / CV chat vs companion split | Do not implement |

### 10.2 Presentation gaps vs existing code

| Gap | Current code | Required |
|-----|--------------|----------|
| Real mesh / Animator / clips | Live-test adapter records enums only; `AnimatorAvatarBehaviorAdapter` exists but has no rig | Wire adapter + controller + clips |
| Emotion / intent on the face | `ai.response` is parsed and can fire UnityEvents; Animator adapter only uses **behavior triggers** | Map `AiEmotion` / `AiIntent` (layer or blendshapes) without blocking `state.ack` |
| Caption UI | Panel text in live-test only | Host-visible captions or confirmed Host-side captions |
| Rapid `avatar.state` replacement | `SetTrigger` can **queue** Animator triggers | Reset/cancel previous trigger; last requested behavior wins |
| Ack vs animation length | Ack is sent as soon as `SetTrigger` succeeds, not when the clip finishes | **Keep that.** Spec: ack = applied, not “clip over”. Do not wait for `animation_finished` on this protocol |
| Unknown Animator trigger | Returns false → no ack (good) | Log a safe diagnostic; stay on previous visual |
| `avatar_id` → which prefab | UUID only on auth | At least validate the loaded character is the intended one; load-by-id when they define assets |
| Production vs live-test adapters | Repair tool assigns live-test recorder | Product scene must assign Animator adapter |
| Single instance / persist | `[DisallowMultipleComponent]` only; plan checkbox for DontDestroyOnLoad still open | Decide with Host embed |
| Emotion `celebrate` vs behavior `celebrate` | Two different enums | Independent layers; do not conflate |

### 10.3 STT / Host control gap

The live-test panel is a **stand-in Host**. Production must define one of:

1. Host sends transcript strings into Unity → existing `SendFinalTranscriptAsync`, or
2. Host/backend run STT; Unity only receives `avatar.state` / `ai.response`, or
3. Unity captures microphone (out of scope until `speech.*` / media contract).

Until that is named, Unity still must handle inbound states with **no local STT** (adapters are inbound-driven).

---

## 11. Edge cases (networking already handles many; presentation must not undo them)

### 11.1 Session / transport (client already implements — presentation must cooperate)

| Case | Networking behavior | Presentation must |
|------|---------------------|-------------------|
| Render cold start (~50s) | Stay `Connecting` / waking; one attempt | Show “waking / connecting”, not a failed avatar; no duplicate Connect |
| Auth send timeout after open | 5s auth send timeout | Do not animate “speaking” |
| `system.error` `AUTH_FAILED` while provisionally Ready | Leave Ready, `AuthRequired`, close, **do not** retry same token | Idle/error pose; hide captions; wait for new credentials |
| Close 4001 then refresh token exists | One refresh + reconnect | Treat as brief reconnect, not a new character spawn if possible |
| Background token rotation | Quiet WS reconnect, **new session id**, new auth | Cancel in-flight visual turn; do not `state.ack` old behaviors; return to living idle; **do not replay** last `stt.final` |
| Close 4000 | Fault, stay disconnected | Permanent config/protocol error UI (sanitized) |
| Close 4002 / 4003 | Reconnect | Same as rotation: new session, no replay |
| Close 4004 | Reconnect after ≥10s | Disable spam of partials; character idle |
| Abnormal network drop | Backoff reconnect | Living idle or “reconnecting”; never loop a speak clip |
| Stale frame (old `session_id`) | Router drops | Animator must not apply it (already dropped before adapter) |
| Unknown `type` (`speech.*` today) | Ignored | No-op |
| Unknown enum on known type | Malformed payload diagnosed; not applied | Keep last valid visual; no ack |
| Malformed JSON / oversize / depth | Drop / close per transport | No throw into Animator |
| Second `stt.final` while gate active | `TurnAlreadyInFlight` | Do not start a second speak; keep current thinking/speaking |
| Turn timeout (default 120s) | Gate releases | Stop thinking loop; idle; sanitized timeout |
| `ai.response` with empty text | Should not apply as a caption | Keep connection; optional thinking→idle |
| Handler exception | Isolated from receive loop | Character recoverable to idle |
| Play Mode exit / quit / scene unload | Idempotent shutdown | No leftover Animator jobs, no ack after dispose |
| App unfocused | `runInBackground` default true so heartbeats continue | If product disables it, document 4003 risk |
| Local `ws://` vs live `wss://` | URL change only while disconnected | Same avatar adapters |
| Late callback after reconnect | Generation/session checks | Ignore; do not ack |

### 11.2 Avatar apply / ack

| Case | Required behavior |
|------|-------------------|
| Null / missing Animator | `ApplyAsync` → false → **no ack** |
| Missing trigger name on controller | false → no ack; stay on previous clip |
| `ApplyAsync` cancelled (session stopping) | false / throw cancel; **no ack** |
| Behavior equal to current (duplicate `idle`) | Apply is still success if valid; ack **that** request (server asked) |
| `thinking` then `speaking` before thinking clip ends | Cancel/replace thinking; ack speaking when speaking is applied |
| `speaking` then `idle` mid-sentence | Cut speak; go idle; ack idle |
| Celebrate/happy as short bursts | Play burst then return to idle **locally** if server does not send idle (define a max burst length so the character does not freeze in celebrate) |
| `ai.response` arrives before the matching `avatar.state` speaking | Show caption when it arrives; do not require order beyond “do not crash”. Prefer speaking pose if a speaking state is already applied |
| `ai.response` with no preceding `avatar.state` | Still present caption; optional local thinking off |
| Emotion changes without new behavior | Face layer only; do not send `state.ack` (ack is for `avatar.state` only) |
| User/Host sends partials under rate limit | Coalesce (already on outbound limiter); listening pose can be local |

### 11.3 Host / embed (once delivery is named)

| Case | Required behavior |
|------|-------------------|
| Host reloads while Unity stays up | Re-auth or reuse session per Host contract; do not leak previous captions |
| Two Unity clients same avatar | Backend problem; Unity still one-session locally |
| Host sends connect with expired JWT | Preflight reject (already) + Host-visible sanitized error |
| Transparent background vs viewer UI | Camera clear flags / URP renderer per agreed alpha |
| Tiny dashboard panel (preview) vs full stream | Readable silhouette; no companion-scale desktop locomotion |
| Live-test panel accidentally in release | Strip or disable; credentials UI is not the product |

### 11.4 What presentation must never do

- Call `SendStateAcknowledgementAsync` unless `ApplyAsync` returned true.
- Replay `stt.final` after reconnect.
- Log token, avatar UUID, transcript, or AI text in production logs (development truncated preview only).
- Block the receive loop on a long animation (ack on apply, blend in Animator).
- Talk to Tauri / OS desktop APIs.

---

## 12. Final deliverables

Deliverables are **Unity dashboard** only. Companion viewport/IPC is a different project.

### D0 — Networking client (implemented; close with validation)

**Deliverable:** deployed-v2 + HTTP auth client that can be reused by product scenes.

- [x] Modular client (`SynthCohostClientBehaviour` + settings + providers)
- [x] Live-test harness for Connect / STT / inbound events
- [ ] Windows Standalone smoke of that harness
- [ ] Fresh-token happy path on the panel when a live account is available
- [ ] Documented operator path (`UNITY_LIVE_TEST_GUIDE.md`) — already exists; keep in sync

**Done means:** a developer can authenticate, stay connected through heartbeats, send one final, see inbound states/responses, disconnect cleanly, without secrets in Git.

### D1 — Avatar presentation (next product increment)

**Deliverable:** a product scene where a real character is driven **only** through the existing handlers.

Must include:

1. Humanoid (or agreed) character + Animator with six behavior states.
2. `AnimatorAvatarBehaviorAdapter` (or successor) assigned on `avatarController`.
3. Living idle (breath/micro-move) that does not require a new WS event.
4. Caption (or confirmed Host-owned captions) from `IAiResponseSink`.
5. Emotion/intent hooked at least to a face layer or debug readout.
6. Camera composition suitable for Host preview (even if embed is TBD).
7. Correct ack/no-ack, rapid-state replace, reconnect reset (see §11).
8. EditMode/PlayMode tests for apply/ack/false-ack; no live backend required for adapter tests.

**Done means:** sending `stt.final` from the existing outbound API produces thinking → speaking (or other behavior) on the **character**, plus visible `ai.response` text, with `state.ack` only after a real apply. Live-test recorder is not used in this scene.

**Blocked on:** client rig/FBX/Animator export.

### D2 — Host plug-in (after they name delivery)

**Deliverable:** the D1 scene driven without the live-test panel.

Must include:

1. Written delivery method (WebGL / native window / capture / LiveKit-only).
2. Matching build target and a smoke build.
3. Credential + avatar id + connect injected from Host (`SetAuthSession` / equivalent).
4. Turn-start path named (Host text → `SendFinalTranscriptAsync`, or inbound-only).
5. One-instance / persist-across-scene rule implemented.
6. Release build has no credential debug panel.

**Done means:** a creator can run the co-host from Host with the agreed display method. Unity still does not implement Host UI.

### D3 — Dashboard config apply (after schema)

**Deliverable:** runtime apply of Host settings without restarting Unity.

- Versioned schema, transport (WS event vs Host bridge vs HTTP).
- Presenter updates (size, variant, captions on/off, etc.) without touching codec defaults.
- Unknown fields ignored.

### D4 — LiveKit (after contract)

**Deliverable:** Unity co-host pixels (and audio if specified) in the live session.

- Separate from the Synth Cohost WS transport.
- Uses D1 camera; does not require OBS in Unity.
- Reconnect of **WS** must not require a full LiveKit room rebuild unless the contract says so (document whichever is true).

### D5 — Conversational presence (letter; later)

**Deliverable:** listening/thinking/speaking blends that mask LLM wait, still mapped to the **six wire behaviors** plus local loops.

Not a new protocol. Not a companion desktop pack.

### D6 — Media / 2.1 (backend-gated)

**Deliverable:** dialect + viseme presenter when they ship fixtures.

Until then, ignoring `speech.*` and staying on integer `v: 2` **is** the deliverable.

---

## 13. Acceptance snapshot (what “dashboard Unity done” means)

A creator, through Host (once D2 exists), gets a Unity co-host that:

1. Authenticates and refreshes without secrets in Git.
2. Holds deployed v2 without reconnect storms or replayed turns.
3. Shows a **real** character that follows `avatar.state` and acks only successful applies.
4. Shows AI reply text and uses emotion/intent.
5. Survives token rotation, cold start, and dropped sockets without a frozen or duplicated speak.
6. Can be placed into the live session via the agreed LiveKit/Host path.

Until D1, this repo is a **complete networking client with a debug harness**, not a finished dashboard co-host. Until D2–D4, it is not plugged into Host/stream even if the character looks right in Editor.
