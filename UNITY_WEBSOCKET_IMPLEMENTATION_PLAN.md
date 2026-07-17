# Synth Cohost Unity WebSocket Implementation Plan

Status: **Unity deployed-v2 implementation, runtime live-test input workflow, bridge audit, and auth/UI hardening complete; a fresh-token Unity-panel happy path, Windows build, and broader backend release acceptance remain pending.**

The live WebSocket and REST hosts are available. A temporary live test account and matching avatar UUID have now been created through the verified REST flow; a fresh short-lived access token must be minted immediately before each live run. The backend foundation is reported stable, but production runtime integrations, mock replacement, and final communication-protocol sign-off are still in progress.

Implementation checkpoint (2026-07-17): the deployed-v2 protocol, Windows WebSocket transport, runtime-only credentials, session/heartbeat/reconnect orchestration, ordered routing, Unity feature adapters, Inspector settings/bootstrap, outbound rate safety, reusable live-test prefab, and dedicated live-test scene are implemented. A real live attempt proved WebSocket/TLS reachability but used an expired JWT and received `system.error/AUTH_FAILED`. Unity now leaves provisional `Ready` immediately on that error, enters `AuthRequired`, closes the rejected session, and never retries the same token. Terminal results are generation-checked under the lifecycle lock so a delayed old handler cannot reject or complete a newer session. The width-constrained panel allows the endpoint, token, and avatar UUID to be reviewed or replaced at runtime before Connect; it can prefill them from process environment variables or an explicitly user-managed, Git-ignored `UserSettings` draft without serializing them into Unity assets. It reports JWT expiry safely and applies endpoint overrides in memory without dirtying the settings asset. Every panel operation and pre-network rejection now writes a bounded on-screen activity entry and a Unity Console breadcrumb; cold-start waits add 5/30/60-second progress notices. Safe diagnostics show initialization, state transitions, endpoint authority, event names/codes, UTC times, retry timing, close-policy decisions, and exception types without logging untrusted identifiers, close reasons, endpoint credentials, tokens, avatar UUIDs, session IDs, transcript/AI text, backend raw messages, or exception messages. A fresh-token wire smoke received two `avatar.state` frames and a valid `ai.response`. Unity `6000.3.10f1` passes 169/169 EditMode tests and the 1/1 live-scene PlayMode smoke test. The bridge update through `79fbf85` introduces no approved/deployed Unity wire change. Windows build and the same fresh-token happy path through the Unity panel remain open.

## Source-of-truth order

Use sources in this order when details conflict:

1. `Assets/websocket-protocol-spec.docx` — canonical document covering current v2 and proposed 2.1.
2. Backend/client confirmations recorded from 2026-07-13 through 2026-07-15 in §5.
3. `E:\Downloads\test_ws.sh` — exact current-v2 happy-path wire example.
4. `synth-cohost-unity-bridge` at commit `79fbf85` — exported Rust/domain/media/OBS reference. Its README and event document remain draft, so they clarify implementation details but do not override confirmed deployed-v2 decisions.
5. `E:\Downloads\WEBSOCKET_DOCS_SAAD.md` — earlier draft notes; context only.

The earlier Markdown notes must not override the DOCX, the latest written backend decisions, or the current smoke-test frames.

This plan covers Unity-side work only. The ignored `synth-cohost-unity-bridge/` repository is read-only reference material and must never be edited, staged, or committed with the Unity project.

## 1. Outcome

Build a modular Unity client that:

- Uses a complete `ws://` or `wss://` Inspector URL as its default while allowing endpoint, token, and avatar UUID changes from the development Game-view panel before Connect.
- Switches between local and live endpoints without code or asset mutation, assuming credentials are valid for the selected environment.
- Uses `wss://synth-cohost-app.onrender.com/ws` for the current live integration target.
- Has no dependency on a frontend/Vercel deployment; frontend preview and production domains are not WebSocket endpoints.
- Implements the deployed integer `v: 2` envelope.
- Generates one client session ID per WebSocket connection and includes it on every frame, including auth.
- Authenticates with a short-lived access token and one avatar UUID.
- Sends a heartbeat every 20 seconds.
- Handles close codes 4000–4004 without uncontrolled reconnect loops.
- Sends `stt.partial`, `stt.final`, and `state.ack`.
- Receives `avatar.state`, `ai.response`, and `system.error`.
- Allows only one `stt.final` turn in flight until backend ordering/correlation rules are finalized.
- Never replays an interrupted turn after reconnect.
- Keeps transport, protocol dialect, session lifecycle, routing, and Unity presentation independent and testable.
- Can adopt proposed 2.1 later without rewriting avatar, UI, or STT feature code.
- Never writes or logs access tokens and never serializes them into Unity assets, source, or Git. An explicitly user-created, Git-ignored local development draft may contain a short-lived token in plaintext and remains the user's responsibility.

## 2. Scope

### Current milestone

- Unity configuration and bootstrap.
- WebSocket transport and lifecycle cancellation.
- Current-v2 serialization, deserialization, validation, and typed DTOs.
- Authentication/session orchestration.
- Fixed 20-second heartbeat.
- Provisional reconnect policy behind a replaceable interface.
- Single-flight final-turn control.
- Message routing.
- Avatar behavior, AI response, STT, state acknowledgement, and error adapters.
- Inspector diagnostics.
- EditMode, PlayMode, live integration, and optional local verification.
- Windows Standalone first, matching the current project target.
- A transport boundary that permits mobile/WebGL adapters later.

### Out of scope

- Any backend or bridge change.
- Dashboard/frontend labels, popups, graphics, layout, Vercel deployment, or domain configuration.
- Backend AI, authentication internals, moderation, persistence, deployment, or rate-limit implementation.
- Proposed 2.1 wire behavior until it is approved and deployed.
- `session.ready` and server-generated session IDs in current v2.
- `heartbeat.ack`.
- Real microphone speech-to-text, TTS, audio streaming, `speech.*`, lip sync, and visemes. The text-based `stt.final` protocol event remains in scope.
- Direct OBS control, OBS credentials, RTMP/NDI output, screen/video capture, broadcast start/stop, and dashboard video delivery. The exported OBS transport is backend/sidecar code and is not exposed through the Synth Cohost WebSocket contract.
- Backend plan-tier/entitlement enforcement. The exported plan limits do not appear in any Unity-facing event.
- Multiple concurrent `stt.final` turns.
- Session resumption or replay after disconnect.
- Final avatar art, animation graphs, captions UI, and audio assets that are not present yet.

## 3. Current Unity baseline

- Unity Editor: `6000.3.10f1`.
- Active target: Windows Standalone 64-bit.
- Render pipeline: URP `17.3.0`.
- Input System: `1.18.0`.
- Test Framework: `1.6.0`.
- Two enabled scenes: the clean starter `Assets/Scenes/SampleScene.unity` and configured `Assets/Scenes/SynthCohostLiveTest.unity`.
- Networking, protocol, session, routing, adapter, bootstrap, and EditMode test architecture now lives under `Assets/SynthCohost`.
- Six assembly definitions isolate protocol, transport, runtime, shared EditMode tests, transport-internal tests, and PlayMode tests.
- `Application.runInBackground` is disabled and must be enabled for reliable heartbeat behavior while unfocused.
- `System.Net.WebSockets.ClientWebSocket` is available for the current Windows/.NET target.

This is a greenfield integration, so the module boundaries can be established cleanly.

## 4. Confirmed deployed v2 contract

### 4.1 Endpoint

Live integration:

```text
wss://synth-cohost-app.onrender.com/ws
```

REST/auth/avatar base:

```text
https://synth-cohost-app.onrender.com
```

Optional local development:

```text
ws://127.0.0.1:8080/ws
```

- No special headers.
- No query parameters have been specified.
- No WebSocket subprotocol.
- The live WebSocket endpoint was independently probed without credentials on 2026-07-14: TLS/WebSocket upgrade succeeded while the service was warm. This verifies reachability, not authenticated protocol behavior.
- The REST host is reachable, but `GET /` returns 404. Do not use the base path as a health check; use a documented REST route when the HTTP contract is supplied.

#### Render free-tier hosting behavior

- The service sleeps after about 15 minutes without activity.
- The first request/connection after sleep can take up to approximately 50 seconds.
- This delay occurs while opening the transport, before WebSocket `OnOpen`; it must not be treated as an auth or protocol failure.
- Use a configurable connection timeout with a 75-second default for this deployment.
- After 10 seconds of a pending live connection, diagnostics may show `Waking server` while the same single connection attempt continues.
- Never start parallel connection attempts while a cold-start connection is still pending.
- The server's 10-second auth deadline begins only after the WebSocket opens; Unity must still send auth immediately on open.

### 4.2 Envelope

Every message, including `auth`, uses:

```json
{
  "v": 2,
  "type": "<event_name>",
  "session_id": "<client-generated-session-id>",
  "ts": "<rfc3339-utc>",
  "payload": {}
}
```

Rules:

- `v` is the integer `2`, not a string.
- Unity creates a fresh session ID for each socket and repeats it on every frame for that socket.
- The server currently accepts a non-empty string; `test_ws.sh` uses `smoke-test-<unix-time>`. Unity should generate a UUID v4 string for consistency and safety.
- `session_id` is at envelope level, including on `auth`.
- `ts` is a UTC RFC3339 timestamp.
- Unknown `type` values must be ignored rather than treated as fatal errors.
- One avatar is selected during auth and remains fixed for that connection.

### 4.3 Auth

Exact current frame:

```json
{
  "v": 2,
  "type": "auth",
  "session_id": "<client-generated-session-id>",
  "ts": "<rfc3339-utc>",
  "payload": {
    "token": "<short-lived-access-token>",
    "avatar_id": "<uuid>"
  }
}
```

- Auth must be the first application frame.
- Send it immediately after socket open and within the server's 10-second handshake timeout.
- A Render cold start can delay socket open by approximately 50 seconds; it does not extend or consume the post-open auth deadline.
- There is no positive auth response and no `session.ready` in current v2.
- After the auth frame is sent successfully, Unity enters `Ready` provisionally. A rejected auth is reported through `system.error` and/or close 4000/4001.
- Tokens are short-lived and should be reacquired for a new handshake.

### 4.4 Current lifecycle

1. Obtain a short-lived token and avatar UUID.
2. Generate a new client session UUID.
3. Open the configured socket.
4. Send auth first.
5. Enter provisional `Ready` after the auth send completes; do not wait for a nonexistent success event.
6. Start the 20-second heartbeat schedule.
7. Permit normal sends while the socket remains open.
8. Permit only one final STT turn at a time.
9. On disconnect, stop heartbeat, fail the in-flight turn, discard the session ID, and clear old routing state.
10. A reconnect obtains credentials as needed, creates a new socket and session ID, and sends new auth.
11. Never replay the interrupted final turn or old acknowledgements.

### 4.5 Unity → server messages

| Type | Payload | Unity behavior |
|---|---|---|
| `auth` | `{ token, avatar_id }` | First frame; full v2 envelope includes client session ID. |
| `heartbeat` | `{}` | Send every 20 seconds after auth; no ack expected. |
| `stt.partial` | `{ text, final: false }` | DTO is supported; product use of live partial streaming remains optional. |
| `stt.final` | `{ text, final: true }` | Exactly one in flight; never replay. |
| `state.ack` | `{ behavior }` | Send only after Unity successfully applies the requested behavior. The server sends no reply. |

`final` is required on both STT payloads even though the older exported Rust DTO does not model it.

### 4.6 Server → Unity messages

| Type | Payload | Unity behavior |
|---|---|---|
| `avatar.state` | `{ behavior }` | Request a behavior transition through an avatar adapter. |
| `ai.response` | `{ text, emotion, intent }` | Publish typed response/caption data. The current live v2 happy path returns AI/moderation responses, but broader production runtime integrations and mock replacement are still in progress. |
| `system.error` | `{ code, message }` | Publish a sanitized error; the connection may remain open or a close may follow. |

The backend previously confirmed that the live current-v2 `stt.final → AI generation → moderation → response` happy path works end to end. The 2026-07-15 update also says Attention Routing, Event Response, Memory, and remaining mock replacements are still being integrated, so this does not yet prove that every response is backed by the final production runtime. Expected current response flow:

1. `avatar.state` with `thinking`.
2. Another `avatar.state` containing the generated response behavior.
3. `ai.response` containing text, emotion, and intent.

The final-turn gate is released on `ai.response`, a terminal `system.error`, disconnect, cancellation, or a configurable response timeout. Do not rely on a finalized ordering beyond the one-turn rule. Live tests must assert valid event shapes/enums and non-empty response text, not deterministic AI wording.

### 4.7 Current enums

- Behavior: `idle`, `listening`, `thinking`, `speaking`, `happy`, `celebrate`.
- Emotion: `neutral`, `happy`, `excited`, `concerned`, `confused`, `celebrate`.
- Intent: `chat`, `question`, `command`, `greeting`, `farewell`, `unknown`.

Unknown future enum values must fail safely without crashing the socket loop or applying an invalid Unity state.

### 4.8 Heartbeat

- Start after the auth frame has been sent.
- Send one full v2 `heartbeat` envelope every 20 seconds.
- Use monotonic/realtime timing, not scaled game time.
- No `heartbeat.ack` exists.
- The server closes with 4003 after three missed intervals/60 seconds.
- Any reconnect receives a fresh heartbeat scheduler tied to the new socket/session.

### 4.9 Validation and limits

- STT text: non-empty and no more than 4,000 UTF-8 bytes.
- `final`: required and exactly `false` for partial or `true` for final.
- Message ceiling: 64 KiB.
- Current exported JSON nesting limit: 32.
- Current session ID validation: non-empty and no more than 128 bytes; Unity will use UUID v4.
- Rate limit: no more than 60 client messages in 10 seconds per connection.
- Malformed known messages are diagnosed and never applied to Unity objects.
- Unknown event types are ignored and optionally logged at debug level.
- Incoming callbacks must never directly mutate Unity objects from a background thread.

### 4.10 Close codes and provisional Unity policy

The backend confirms codes 4000–4004, but final reconnect rules are still open.

| Close | Meaning | Provisional Unity policy |
|---|---|---|
| Normal/user shutdown | Intentional | Do not reconnect. |
| Network/abnormal close | Transport loss | Reconnect with exponential backoff and jitter. |
| 4000 | `MALFORMED_PAYLOAD` | Stop retrying and surface a protocol/configuration error. |
| 4001 | `AUTH_FAILED` | Enter `AuthRequired`; never retry the same rejected token. A user/provider must supply fresh credentials before a new Connect. |
| 4002 | `SESSION_MISMATCH` | Clear state and attempt one fresh connection/session; stop if repeated. |
| 4003 | `HEARTBEAT_TIMEOUT` | Reconnect automatically with a new session. |
| 4004 | `RATE_LIMIT_EXCEEDED` | Wait at least the server's 10-second window plus backoff before retrying. |

This table belongs in a replaceable reconnect policy so later backend/client sign-off changes do not affect transport, serialization, or feature code.

### 4.11 Proposed 2.1 — future only

The DOCX also describes an undeployed proposal, reconfirmed as pending sign-off on 2026-07-14:

- String `protocol_version` replaces integer `v`.
- Auth omits `session_id`.
- Server generates the session ID.
- Server returns `session.ready`.
- `session.ready` advertises `heartbeat_interval_ms`.

Do not emit or expect these shapes now. Keep an `IProtocolDialect` boundary so a future dialect can replace current v2 without changing Unity presentation code. The proposal's final version string still requires sign-off.

## 5. Conversation and decision log

### 5.1 Original Unity/client requirements

- Unity-side work is the main deliverable.
- Do not change the imported bridge/backend repository.
- Read the DOCX thoroughly and use the bridge only as a reference.
- Keep the implementation modular.
- Make the full WebSocket endpoint editable in the Inspector so local/live switching requires only a URL change.
- Verify backend readiness and identify anything that must be sent to the backend developer.

### 5.2 Initial audit result

The initial audit found that the bridge implemented integer `v: 2`, client-owned sessions, and no `session.ready`, while the DOCX's proposed flow described a string version, server-owned sessions, and `session.ready`. Because the bridge README called the proposal a draft, the first plan correctly paused final wire selection and requested backend confirmation.

### 5.3 First backend response

The backend developer clarified:

- The discrepancy was expected.
- Deployed v2 remains the implementation target; the proposed 2.1 contract is not deployed.
- Local endpoint form was `ws://localhost:<port>/ws`, with no special headers or subprotocol.
- Authentication uses register/login; avatar creation uses `POST /avatars`.
- Current version is integer `v: 2`.
- `final` is required on both STT message types.
- `heartbeat.ack` is not implemented.
- Close codes 4000–4004 are confirmed.
- Reconnect behavior and turn ordering are not finalized.
- TTS/audio/visemes are out of scope.
- `test_ws.sh` could be shared as the current end-to-end reference.

### 5.4 Unity follow-up

Unity confirmed it would target deployed v2 and keep the protocol modular for future 2.1. It requested:

- `test_ws.sh`.
- Exact auth envelope/session placement.
- Heartbeat interval.
- Actual local port and API base details.

Unity also proposed excluding TTS/visemes and allowing only one `stt.final` at a time.

### 5.5 Latest backend response

The backend developer then confirmed:

- Exact local socket: `ws://127.0.0.1:8080/ws`.
- Auth uses integer `v: 2` with a client-generated envelope-level session ID.
- No `session.ready` response.
- Heartbeat every 20 seconds.
- Server close 4003 after three missed heartbeats/60 seconds.
- `test_ws.sh` demonstrates the current handshake/happy path.
- The earlier Markdown was based on draft notes; the DOCX is the source of truth for current v2 and proposed 2.1.
- The client must remain modular.
- TTS and visemes remain out of scope.
- Process one `stt.final` at a time until reconnect/ordering rules are finalized.

Files received and reviewed:

- `E:\Downloads\WEBSOCKET_DOCS_SAAD.md`
- `E:\Downloads\test_ws.sh`

### 5.6 Live deployment update — 2026-07-14

The backend developer confirmed:

- Live WebSocket: `wss://synth-cohost-app.onrender.com/ws`.
- Live REST/auth/avatar base: `https://synth-cohost-app.onrender.com`.
- The deployed protocol remains current v2: integer `v`, client-generated session ID, and no `session.ready`.
- Render free tier sleeps after approximately 15 minutes idle; the first connection after sleep may take up to approximately 50 seconds.
- The live text pipeline `stt.final → AI → moderation → response` is wired and working end to end.
- Real audio speech-to-text and TTS are still pending.
- Proposed v2.1 remains gated on sign-off.
- The backend developer will notify Unity when real audio features or v2.1 change status.

An unauthenticated connectivity probe on 2026-07-14 confirmed that the warm live WebSocket accepted a TLS/WebSocket upgrade. The REST host also responded, although its base `/` route returns 404. No token or application frame was sent, so authenticated v2 behavior still requires credentials to verify.

### 5.7 Client and backend integration update — 2026-07-15

The client clarified:

- The shared Vercel URL is a temporary frontend review deployment, not the production frontend or a Unity/backend endpoint.
- The final production frontend deployment/domain will be supplied after the frontend is merged.
- Dashboard label, popup, graphic, and layout changes are frontend-only and do not change the deployed Unity v2 contract.
- Unity must not hardcode or depend on either the temporary or final frontend URL. It continues to use the separately configurable backend WebSocket endpoint.
- The wider dashboard panel intended for live-avatar testing introduces an unresolved presentation boundary: confirm whether it will show a Windows client, WebGL embed, captured/streamed output, or another integration before changing the Unity build target.

The backend status update says:

- Authentication, WebSockets, moderation, storage, and API infrastructure are reported stable.
- Attention Routing, Event Response, and Memory are being connected to the existing runtime.
- Remaining mock services are being replaced with production integrations.
- Communication protocols are still being finalized.
- No new Unity-visible event type, payload, ordering rule, protocol version, or endpoint accompanied this update.

This is release-readiness context, not a replacement wire contract. Continue using deployed v2 until the backend supplies an explicit versioned protocol change. Do not implement Unity handlers for the named runtime systems unless they expose Unity-facing events with schemas, ordering/correlation/error rules, fixtures, and a migration date.

### 5.8 Bridge export update — 2026-07-16/17

Before this audit, the existing Unity changes were checkpointed as `e1df3a2` and pushed to `origin/main`. The ignored bridge repository was then pulled through the personal-account SSH remote, fast-forwarded from `ca5bd8a` to `79fbf85`, and audited without modifying it. The refresh promotes active workspace copies under `crates/` for `runtime_bridge`, `domain`, and `media`, adds an OBS WebSocket transport and an interim `docs/websocket-events.md`, then removes a duplicate root copy of that event document.

The audit found no approved or deployed Unity wire change:

- The bridge README still marks the target spec as draft/pending sign-off and explicitly says not to implement `protocol_version` or `session.ready` yet.
- The proposed document is internally inconsistent about whether the breaking session/version change would be `2.1` or a new major version. A future dialect must not ship until the backend names the final version, approved backend commit, deployed endpoint/date, and final fixtures.
- The exported runtime still uses integer `v: 2`, an envelope-level client session ID, auth with `token` plus UUID `avatar_id`, and the existing current-v2 event names.
- Behavior, emotion, and intent values match the Unity enums exactly.
- The exported limits match Unity's existing safeguards: 128-byte session IDs, 4,000-byte STT text, and JSON nesting depth 32. Unity additionally retains its 64 KiB frame cap and connection-wide outbound safety limit from the confirmed current contract.
- `AI_GENERATION_FAILED` is now documented as a possible `system.error` for a failed final-turn generation. Unity already parses arbitrary sanitized error codes/messages, presents this code, and releases the single-turn gate on a handled error.
- The Rust `SttTextPayload` currently models only `text`, while the bridge event document and prior explicit backend confirmation still require `final: false/true`. Serde accepts the extra field, so Unity must continue sending and validating `final`; this discrepancy should be corrected or clarified in the exported Rust model before any generated-contract workflow is adopted.
- `speech.start`, `speech.viseme`, and `speech.end` are scaffolded, but the bridge document says they are not wired and the media crate contains only mock STT/TTS providers. No audio URL/bytes/codec, viseme vocabulary, cancellation, ordering, or reconnect rules are defined. Unity must keep safely ignoring these unknown types for now.
- The OBS WebSocket transport connects from the backend/sidecar process directly to OBS and only issues `SetCurrentProgramScene`. No Synth Cohost envelope routes an OBS command to Unity, and no Unity video/audio delivery path is defined. OBS credentials and control therefore must not be added to Unity.
- Backend plan tiers and fallback AI responses need no Unity DTO: fallbacks arrive as ordinary `ai.response` frames and tier limits are not on the socket.

Because the current Unity implementation already conforms to every deployable item in this export, the required implementation action is to preserve the existing deployed-v2 dialect and update this plan. Adding speculative speech or OBS production code would create an unapproved contract rather than implement one.

### 5.9 Client live-placeholder handoff — 2026-07-17

The client supplied the confirmed live WebSocket endpoint, a matching avatar UUID, and a 15-minute access token for Unity testing. The token expired at 2026-07-17 12:49:36 UTC (17:49:36 PKT), so it cannot prove the current authenticated happy path or avatar ownership now. An auth-only smoke with those exact local placeholders opened the WebSocket and received `system.error/AUTH_FAILED`; no close frame was observed during the 12-second post-error window, after which the probe aborted locally. No supplied credential is recorded in this plan or tracked Unity content.

Implementation response:

- Keep the three values editable in the development Game-view panel before Connect.
- Prefill development values only from process environment variables or an explicitly user-created, Git-ignored `UserSettings/SynthCohostLiveTest.local.json` draft.
- Treat the local draft as plaintext developer convenience, never production credential storage.
- Inspect JWT `exp` locally for operator guidance only; this is not signature validation.
- Reject expired tokens and tokens too near expiry for a possible Render cold start before opening the socket.
- Obtain a fresh token immediately before the remaining Unity-panel happy-path run.

### 5.10 Final implementation decision

Implement one deployed-v2 dialect now. Do not build a hybrid or Inspector toggle between current v2 and proposed 2.1. Isolate protocol/session ownership and reconnect behavior behind interfaces, while keeping avatar, AI-response, STT, and UI code independent.

## 6. Attachment audit and readiness

### 6.1 What `test_ws.sh` demonstrates

The script:

1. Starts the real backend API on port 8080 against development Postgres.
2. Mints a real access token using the backend auth crate.
3. Creates a client session string.
4. Connects to `ws://127.0.0.1:8080/ws` with `websocat`.
5. Sends exact v2 auth with envelope-level session ID.
6. Sends heartbeat.
7. Sends one `stt.final` with `final: true`.
8. Expects no `system.error`, then thinking/response avatar states and `ai.response`.

It is a useful wire reference but not a complete automated acceptance test:

- It requires the full backend Cargo workspace, Rust/Cargo, development Postgres, Bash utilities, and `websocat`.
- It cannot run standalone from Downloads or from this Unity repository.
- Cargo is not installed on this Unity machine.
- `timeout ... || true` suppresses socket/test failure status.
- Received JSON is printed but never parsed or asserted.
- It sends only one early heartbeat and does not verify the 20/60-second timing.
- It does not test partials, `state.ack`, reconnect, rate limiting, malformed input, auth failure, or close codes 4000–4004.

### 6.2 What `WEBSOCKET_DOCS_SAAD.md` contributes

It confirms the broad v2 envelope, auth/avatar purpose, event names, fixed heartbeat requirement, mocked development AI behavior, and out-of-scope speech work. It also contains abbreviated/older examples and open notes, so it remains secondary to the DOCX and latest backend confirmation.

### 6.3 What bridge `79fbf85` contributes

The latest bridge export gives source-level confirmation of the existing envelope, enum strings, auth/avatar payload, validation limits, unknown-event behavior, and `AI_GENERATION_FAILED`. It also exposes future/backend-only scaffolding for speech media, plan limits, and direct OBS scene switching.

The export is useful corroborating evidence, not a new deployment notice: its README says draft/pending sign-off, its event document calls itself interim, and its speech rows explicitly say not wired. The active Cargo workspace points at `crates/*`, while some README paths and adjacent test wiring still reflect the older layout. Cargo is not installed on this workstation, so these Rust crates were source-audited but not independently compiled; their OBS tests are protocol/parser tests rather than live OBS I/O. The current Unity codec, DTOs, router, session controller, and live harness already cover all deployable v2 items. No new Unity runtime handler is justified by this bridge revision.

### 6.4 Readiness verdict

| Area | Status |
|---|---|
| Unity architecture | Ready. |
| Current-v2 DTOs/codec | Ready. |
| Transport and Inspector URL | Ready. |
| Auth/session/heartbeat logic | Ready. |
| Single final-turn workflow | Ready. |
| Avatar/AI/error adapters | Ready. |
| Live endpoint connectivity | TLS/WebSocket and server error delivery verified while warm; a temporary matching test account/avatar is available. Fresh-token happy-path remains to run. |
| Frontend/Vercel deployment | Not a Unity networking dependency; the production frontend domain is still pending. |
| Live text/AI/moderation happy path | Previously confirmed working for current v2; final production-runtime/mock status must be revalidated. |
| Backend runtime integrations | Attention Routing, Event Response, Memory, mock replacement, and communication-protocol finalization are still in progress; no Unity-visible delta supplied. |
| Bridge `79fbf85` current-v2 compatibility | Audited; existing Unity implementation matches all deployable fields, enums, limits, and event behavior. |
| Bridge speech/media scaffolding | Not ready for Unity; mock/not wired and missing audio/viseme lifecycle details. |
| Bridge OBS scene control | Backend/sidecar-only; no Unity event or defined Unity-to-OBS/dashboard media path. |
| Live avatar presentation boundary | Needs confirmation before choosing Windows-only, WebGL, captured/streamed output, or another delivery path. |
| Local happy-path integration | Optional; requires the full backend workspace and local credentials. |
| Unity-owned register/login/avatar creation | REST login/refresh/avatar routes were verified externally; implementing them inside Unity remains out of scope unless requested. |
| Full close/error acceptance | Needs backend fixtures or manual test cases. |
| Live authenticated verification | Expired-token rejection verified and hardened; refresh the token and run the text happy path with the existing matching avatar. |
| Render cold-start behavior | Requirements known; must be tested with a delayed connection and one real idle wake-up. |
| Proposed 2.1 | Future work; does not block v2. |

### 6.5 Remaining inputs

No additional backend answer is required before starting Unity WebSocket development.

Before authenticated live acceptance, log in with the existing temporary test account immediately before the run and use its matching avatar UUID. Do not reuse a JWT from an earlier session after it expires.

Local backend access is now optional because a live endpoint exists. If local verification is still desired, obtain the full backend workspace/run instructions and prerequisites required by `test_ws.sh`.

If Unity is expected to own account/avatar provisioning in this milestone, also request:

- HTTP base URL and exact register/login/refresh/`POST /avatars` routes.
- Request/response JSON and error schemas.
- Token expiry/refresh rules.
- Cold-start-aware HTTP timeout/retry rules. Never blindly retry non-idempotent registration/avatar creation after an ambiguous timeout.

Before release, obtain:

- Live credentials and, if required, a separate staging endpoint/account.
- Final reconnect and response-ordering sign-off.
- Repeatable close-code/error cases.
- A real-versus-mock environment matrix and confirmation that the required production runtime integrations are active.
- A versioned protocol diff plus fixtures for any Unity-visible change introduced by the finalized communication protocols.
- The dashboard live-avatar delivery method and resulting Unity build/deployment target.
- If OBS/broadcast delivery is part of Unity's later scope: where the OBS connector runs; how Unity pixels and audio reach it; required platform, resolution, frame rate, latency, alpha, and audio routing; who owns OBS credentials; and whether Unity controls scenes at all.
- Before future `speech.*` work: exact audio transport and codec, viseme shape vocabulary, timing/order guarantees, utterance cancellation/reconnect semantics, payload size/rate limits, and golden fixtures.

## 7. Proposed Unity architecture

```text
Assets/SynthCohost/
  Runtime/
    Bootstrap/
      SynthCohostClientBehaviour.cs
    Configuration/
      SynthCohostConnectionSettings.cs
      ReconnectPolicySettings.cs
    Authentication/
      IAccessTokenProvider.cs
      IAvatarIdProvider.cs
      RuntimeCredentialProvider.cs
    Protocol/
      IProtocolDialect.cs
      DeployedV2ProtocolDialect.cs
      ProtocolConstants.cs
      EnvelopeHeader.cs
      ProtocolCodec.cs
      ProtocolValidation.cs
      Messages/
        Client/
        Server/
      Values/
        Behavior.cs
        Emotion.cs
        Intent.cs
    Transport/
      IWebSocketTransport.cs
      ClientWebSocketTransport.cs
      IWebSocketTransportFactory.cs
      TransportCloseInfo.cs
    Session/
      CohostSessionController.cs
      SessionState.cs
      HeartbeatScheduler.cs
      ReconnectController.cs
      FinalTurnGate.cs
    Routing/
      ProtocolMessageRouter.cs
      IProtocolMessageHandler.cs
    Features/
      Avatar/
      AiResponse/
      Transcripts/
      Errors/
    Diagnostics/
      CohostDiagnostics.cs
      ConnectionStatusViewModel.cs
  Tests/
    EditMode/
      Protocol/
      Session/
      Routing/
    PlayMode/
      Bootstrap/
      Integration/
```

Recommended assemblies:

- `SynthCohost.Protocol`: Unity-independent current-v2 DTOs, dialect, codec, and validation.
- `SynthCohost.Transport`: transport abstraction and Windows `ClientWebSocket` adapter.
- `SynthCohost.Runtime`: session, routing, feature adapters, bootstrap, and diagnostics.
- `SynthCohost.Tests.EditMode` and `SynthCohost.Tests.PlayMode`: test-only assemblies.

Dependency direction:

```text
Presentation/Avatar adapters -> Runtime session/routing -> Protocol + Transport interfaces
Transport implementation -------------------------------> Transport interfaces
```

Protocol code must not reference `MonoBehaviour`, scenes, animators, UI, or the concrete WebSocket implementation.

## 8. Inspector and runtime configuration

Create a `SynthCohostConnectionSettings` ScriptableObject referenced by the bootstrap component.

Fields:

- `Endpoint URL`, defaulting for current integration to `wss://synth-cohost-app.onrender.com/ws`; optional local value: `ws://127.0.0.1:8080/ws`.
- `Auto Connect`.
- `Connect Timeout Seconds`, default 75 seconds for Render cold starts.
- `Auth Send Timeout Seconds`, default 5 seconds after socket open.
- Final-turn response timeout, default 120 seconds for live AI/moderation processing.
- Reconnect base delay, maximum delay, jitter, and attempt limits.
- Diagnostic log level.
- `Run In Background`, enabled by default.

Requirements:

- Keep the complete URL in one field; do not split scheme/host/port/path.
- Validate with `Uri.TryCreate`; allow only `ws` and `wss`.
- Pass the URL unchanged to the transport.
- Permit `ws` only for local/loopback development.
- Require `wss` for live/non-loopback endpoints and never bypass certificate validation.
- Expose editable endpoint, masked-token, and avatar-UUID fields in the development Game-view panel before Connect.
- Apply a changed runtime URL before a new Connect and leave the settings asset unchanged.
- Allow runtime endpoint replacement only while `Disconnected`, `AuthRequired`, or `Faulted`; disconnect an active session first.
- Prefill precedence is process environment, then the ignored local `UserSettings` draft, then the settings endpoint fallback.
- Support `SYNTH_COHOST_ENDPOINT`, `SYNTH_COHOST_ACCESS_TOKEN`, and `SYNTH_COHOST_AVATAR_ID` process variables.
- Treat the optional local draft as Git-ignored, development-only plaintext rather than production credential storage.
- Use JWT `exp` inspection only as an advisory preflight, not signature validation; reject expired or cold-start-inadequate tokens locally.
- Keep transport-connect, auth-send, and final-turn timeouts separate; a slow Render wake-up is not an auth or AI timeout.
- Keep access tokens out of this asset, scenes, prefabs, source, and Git.
- Supply token and avatar ID through runtime providers independent of the URL.
- Keep the 20-second deployed-v2 heartbeat in the dialect/session policy, not as an environment-specific endpoint field.

## 9. Detailed implementation phases

### Phase 0 — Confirm current inputs

- [x] Confirm deployed integer `v: 2`.
- [x] Confirm envelope-level client session ID on auth and all later messages.
- [x] Confirm no `session.ready` and no heartbeat ack.
- [x] Confirm local endpoint and lack of headers/subprotocol.
- [x] Confirm 20-second heartbeat and 60-second/4003 timeout.
- [x] Confirm required STT `final` booleans.
- [x] Confirm one final turn in flight.
- [x] Confirm close codes 4000–4004.
- [x] Confirm TTS/audio/visemes are out of scope.
- [x] Review the attached smoke script and earlier Markdown.
- [x] Confirm the live WebSocket and REST base URLs.
- [x] Confirm Render's approximately 15-minute idle sleep and up-to-50-second cold start.
- [x] Record the backend's confirmation that the current-v2 text → AI → moderation → response happy path is operational.
- [x] Reconfirm that real audio STT/TTS and v2.1 remain pending.
- [x] Confirm Unity has no dependency on the temporary Vercel frontend URL.
- [x] Record the 2026-07-15 integration-stage update; it supplied no current-v2 frame change.
- [x] Pull and audit bridge `79fbf85`; confirm it supplies no approved/deployed Unity wire delta.
- [x] Confirm exported OBS control, plan tiers, mock media, and unwired `speech.*` do not enter the current Unity milestone.
- [x] Record the Rust `SttTextPayload`/required-`final` discrepancy and preserve the explicitly confirmed wire discriminator.
- [ ] Obtain non-committed test credentials/avatar before authenticated live integration testing.
- [ ] Obtain the HTTP auth/avatar contract only if Unity must implement that flow.
- [ ] Confirm how the dashboard live-avatar panel will receive Unity output: Windows client, WebGL embed, captured/streamed output, or another integration.
- [ ] Reconfirm which live services are production-backed versus mocked before release acceptance.
- [ ] Obtain an updated DOCX/versioned protocol diff and fixtures if finalized runtime systems introduce Unity-visible changes.

Exit: current-v2 implementation may proceed; open items affect scene integration, production acceptance, account provisioning, and release packaging.

### Phase 1 — Project foundation

- [x] Remove or replace the unused `NewMonoBehaviourScript` template when implementation begins.
- [x] Create the `Assets/SynthCohost` folder structure and namespaces.
- [x] Add assembly definitions from §7.
- [x] Add a serializer behind `IProtocolCodec`; prefer Unity's supported Newtonsoft JSON package for exact field control and envelope inspection.
- [x] Use `ClientWebSocket` for the current Windows target behind `IWebSocketTransport`.
- [x] Keep room for a separate mobile/WebGL transport adapter without changing session or feature code.
- [x] Enable `Application.runInBackground` through bootstrap/settings.
- [x] Add a dedicated bootstrap prefab and integration scene without coupling networking to sample-scene content.

Exit: assemblies compile and EditMode/PlayMode test assemblies run.

### Phase 2 — Implement and test deployed-v2 DTOs

- [x] Add exact event-name constants.
- [x] Implement `DeployedV2ProtocolDialect` with integer `v: 2`.
- [x] Model one common envelope that always includes `session_id`, including auth.
- [x] Implement typed DTOs for auth, heartbeat, STT partial/final, state ack, avatar state, AI response, and system error.
- [x] Use explicit JSON property names.
- [x] Add explicit enum converters/TryParse behavior.
- [x] Generate invariant UTC RFC3339 timestamps.
- [x] Implement session-ID, UTF-8 STT-length, and serialized-size checks.
- [x] Enforce the exported maximum JSON nesting depth of 32.
- [x] Inspect `type` before selecting a payload DTO.
- [x] Ignore unknown event types safely.
- [x] Diagnose malformed known payloads without throwing into the frame loop.
- [x] Add golden JSON fixtures matching all three frames in `test_ws.sh`.
- [x] Add inbound fixtures for the expected avatar and AI responses.

Exit: exact fixtures round-trip without a real socket.

### Phase 3 — WebSocket transport

- [x] Define transport states, callbacks, async connect/send/close, and cancellation.
- [x] Implement `ClientWebSocketTransport` for Windows.
- [x] Apply a cancellable 75-second transport-connect timeout for the live Render deployment.
- [x] Allow `ConnectAsync` to remain pending through a normal approximately 50-second cold start without faulting or creating another attempt.
- [x] Reassemble fragmented text frames correctly.
- [x] Diagnose unsupported binary frames; current scope has no binary/audio contract.
- [x] Enforce a bounded receive buffer/message ceiling.
- [x] Serialize outbound sends through one async gate.
- [x] Preserve numeric close codes and reasons in `TransportCloseInfo`.
- [x] Make shutdown idempotent across Play Mode exit, scene teardown, quit, cancellation, and domain reload.
- [x] Marshal all Unity-facing callbacks to the Unity main thread.
- [x] Test transport behavior through a fake adapter and optional loopback fixture.

Exit: connect/send/receive/close/cancel behavior is deterministic and leak-free.

### Phase 4 — Auth and session state machine

States:

```text
Disconnected -> Connecting -> Authenticating -> Ready
      ^              |              |             |
      +--------------+--------------+-------------+
                         Reconnecting
```

Also support `Stopping`, `Faulted`, and `AuthRequired`.

- [x] Define token and avatar providers.
- [x] Validate avatar UUID and token presence before connect.
- [ ] Obtain/refresh the short-lived token immediately before connecting and ensure it has enough validity for a possible cold start.
- [x] Generate a fresh session UUID for each connection attempt.
- [x] Keep the state `Connecting` while Render wakes; expose `Waking server` as diagnostic presentation rather than a separate protocol state.
- [x] Send auth immediately as the first application frame, using a separate 5-second send timeout after socket open.
- [x] Enter provisional `Ready` after auth send completes; do not wait for `session.ready`.
- [x] Treat subsequent 4000/4001 as a failed provisional handshake.
- [x] Treat `system.error/AUTH_FAILED` as an immediate authentication rejection, leave `Ready`, clear the session, and require a fresh token without an unchanged-token retry.
- [x] Permit domain sends only in `Ready`.
- [x] Clear heartbeat, current session, stale callbacks, and in-flight turn on disconnect.
- [x] Publish typed state changes for diagnostics/UI.
- [x] Runtime code never writes or logs tokens and never serializes them into tracked Unity content; the optional user-created, Git-ignored development draft is explicitly plaintext and local-only.

Exit: fake transport tests prove every valid/invalid transition and silent-success handshake behavior.

### Phase 5 — Heartbeat, reconnect, and final-turn control

- [x] Start heartbeat after auth send succeeds.
- [x] Send a full v2 heartbeat every 20 seconds using monotonic/realtime timing.
- [x] Stop the scheduler before replacing the session/socket.
- [x] Never wait for heartbeat acknowledgement.
- [x] Implement single-flight reconnect with exponential backoff and jitter.
- [x] Do not start reconnect backoff or another attempt while the current cold-start connection remains pending; retry only after its configured timeout or a definite failure.
- [x] Apply the provisional close policy from §4.10 behind a replaceable interface.
- [x] Fetch/reacquire credentials as required for each new handshake.
- [x] Generate a fresh session ID on reconnect.
- [x] Never replay STT or acknowledgements.
- [x] Implement `FinalTurnGate` so only one `stt.final` can be sent.
- [x] Release the gate on AI response, terminal system error, disconnect, cancellation, or the configurable 120-second response timeout.
- [x] Do not let partial transcripts bypass the connection rate limit.

Exit: tests prove heartbeat timing, no ack dependency, backoff, fresh sessions, no replay, and one-turn enforcement.

### Phase 6 — Message routing

- [x] Route by exact `type` string.
- [x] Register one typed handler for avatar state, AI response, and system error.
- [x] Keep routing independent of scenes and concrete feature implementations.
- [x] Ignore unknown types.
- [x] Reject messages whose session ID does not match the active socket session.
- [x] Prevent late callbacks from a disposed socket/session reaching the current scene.
- [x] Preserve per-socket receive order when dispatching to the main thread.
- [x] Isolate handler exceptions from the receive loop.

Exit: routing tests cover every known message, unknown types, malformed payloads, stale sessions, and handler errors.

### Phase 7 — Unity feature adapters

#### Avatar state

- [x] Define `IAvatarBehaviorController`, independent of `Animator` and avatar assets.
- [x] Map all six current behavior values.
- [x] Apply transitions on the Unity main thread.
- [x] Send `state.ack` only after successful application.
- [x] Do not send a false ack if the avatar/controller is unavailable.

#### AI response

- [x] Publish text, emotion, and intent as a typed event.
- [x] Accept nondeterministic AI text; validate shape, enums, moderation-safe flow, and non-empty response rather than exact wording. Environment-specific production/mock behavior must be confirmed before release acceptance.
- [x] Add replaceable caption/presentation subscribers when UI exists.
- [x] Keep emotion and intent available for later presentation logic.

#### Transcript outbound API (`stt.*`)

- [x] Expose partial and final transcript methods.
- [x] Accept already-produced text from a debug UI or another provider; this module does not perform real microphone speech recognition.
- [x] Validate ready state, non-empty text, UTF-8 length, and required `final` value.
- [x] Enforce one final in flight.
- [x] Throttle/coalesce partial messages to remain under the rate limit.
- [x] Return explicit success/failure results.

#### System errors

- [x] Publish typed code/message data to diagnostics/UI.
- [ ] Distinguish an in-session error from an error followed by close.
- [ ] Avoid exposing sensitive raw backend details in production UI/logs.

Exit: fake feature adapters prove correct dispatch and acknowledgements.

### Phase 8 — Bootstrap and Inspector workflow

- [x] Create `SynthCohostClientBehaviour` as lifecycle owner.
- [x] Reference settings and adapters through serialized fields/composition.
- [x] Validate missing settings, invalid URL, or missing providers in the Inspector.
- [x] Support auto-connect and explicit Connect/Disconnect/Reconnect.
- [x] Add a credential-safe live-test panel for runtime connect, transcript sends, avatar state, AI response, and system-error inspection.
- [x] Constrain the panel, masked-token field, and action rows so long JWTs cannot expand the scroll content or displace/oversize buttons.
- [x] Allow endpoint, token, and avatar UUID editing from the live-test Game-view panel before Connect.
- [x] Support environment and ignored `UserSettings` placeholder prefills without serializing credentials into Unity assets.
- [x] Recompose immutable connection options safely when a terminal-state runtime endpoint changes.
- [x] Display JWT expiry and reject expired or nearly expired tokens before connection.
- [x] Add a bounded in-panel activity log mirrored to the Unity Console for initialization, validation, connect/reconnect/disconnect, sends, backend event categories, and failures.
- [x] Add 5/30/60-second progress breadcrumbs while a live connect/reconnect may be waiting on a Render cold start.
- [ ] Decide whether the client persists across scenes and enforce one instance if it does.
- [x] Add a developer status view for connection state/wake status, endpoint, session/turn state, last inbound/outbound events and UTC times, reconnect attempt, sanitized close result, and safe error.
- [ ] Verify changing only `Endpoint URL` switches local/live socket targets.

Exit: a designer can configure and operate the socket without changing code.

### Phase 9 — Diagnostics, security, and resilience

- [x] Add structured categories for transport, protocol, session, heartbeat, reconnect, turns, and features.
- [x] Redact tokens and authorization material from every path.
- [x] Keep runtime credentials out of tracked Unity content and document the optional ignored local draft as plaintext development data.
- [x] Log panel validation blockers that occur before the reusable transport/session diagnostics are reached.
- [x] Restrict development logs to fixed text, enums, state, endpoint authority, normalized codes, timeouts, and exception types; never log raw credentials, identifiers, content, backend messages, or exception messages.
- [x] Do not log full transcripts by default; allow sanitized/truncated development previews.
- [x] Bound/allowlist backend error identifiers, omit unknown event identifiers and close reasons, hide auth-error text, and reject endpoint user-info/query/fragment before transport use.
- [x] Revalidate completed inbound handlers against the current connection generation while holding the lifecycle gate before applying terminal state/turn effects.
- [ ] Add counters for connection duration/cold starts, auth sends/failures, reconnects, messages by type, malformed messages, turn timeouts, and close codes.
- [x] Require `wss` and normal certificate validation for non-loopback/live endpoints.
- [x] Observe all async exceptions.
- [ ] Bound receive buffers, pending callbacks, logs, reconnect delays, and partial-message rate.

Exit: failures are diagnosable without exposing secrets or destabilizing Unity.

### Phase 10 — Live and optional local integration

- [ ] Obtain a freshly minted non-committed access token immediately before Unity-panel testing; the matching avatar UUID is already available.
- [ ] Connect to `wss://synth-cohost-app.onrender.com/ws` through the runtime Game-view endpoint field, which defaults from the settings asset.
- [x] Confirm the latest supplied placeholder token has expired and cannot be used as evidence of a transport or backend failure.
- [ ] After at least 15 minutes of backend inactivity, verify one real cold start can remain `Connecting`/`Waking server` for up to approximately 50 seconds without a false failure or duplicate attempt.
- [ ] Verify auth is the first full v2 frame and includes the client session ID.
- [ ] Verify no `session.ready`/heartbeat ack is required.
- [ ] Remain connected for more than 60 seconds while 20-second heartbeats prevent close 4003.
- [ ] Run a connected soak test beyond 15 minutes to confirm heartbeat traffic keeps the live service/session active.
- [ ] Repeat `stt.final -> avatar.state -> ai.response` through the Unity panel with a fresh token and verify a valid non-empty AI response.
- [ ] Verify the final-turn gate rejects a second simultaneous turn.
- [ ] Apply avatar behavior and verify state ack server-side.
- [ ] Exercise partial transcript behavior if included in the milestone.
- [ ] Exercise malformed/auth/session/heartbeat/rate-limit close cases through backend fixtures or manual cases.
- [ ] Force network loss and prove a new session ID, fresh auth, and no replay.
- [ ] Confirm unknown future types do not break the session.
- [ ] After backend runtime integration and mock replacement are declared complete, repeat authenticated text-flow acceptance against the documented production services.
- [ ] Run a contract regression against the finalized communication protocol and fixtures before release.
- [ ] If Attention Routing, Event Response, or Memory becomes Unity-visible, test only the explicitly documented events and semantics.
- [ ] Do not treat unwired `speech.*` scaffolding or backend-side OBS scene control as live Unity acceptance criteria until their contracts and deployment topology are approved.
- [ ] If local backend access is provided, change only the Inspector endpoint to `ws://127.0.0.1:8080/ws` and repeat the socket happy path with local credentials.

Exit: the live endpoint passes warm/cold authenticated tests, and optional local testing requires only the endpoint URL change on the socket client.

## 10. Verification matrix

### EditMode

- [x] Exact current-v2 auth, heartbeat, partial, final, and ack serialization.
- [x] Exact avatar, AI, and error deserialization.
- [x] Integer `v: 2` and session ID on every frame.
- [x] RFC3339 UTC timestamps.
- [x] Enum mapping and unknown enum behavior.
- [x] Unknown event ignored.
- [x] Malformed known payload diagnosed.
- [x] Session mismatch rejected.
- [x] STT 4,000 UTF-8-byte boundary and message 64-KiB boundary.
- [x] Rate-limit/throttling behavior.
- [x] State transitions after silent auth success.
- [x] `AUTH_FAILED` system error and close 4001 enter `AuthRequired`, dispose the socket, and do not retry the unchanged token.
- [x] Non-auth `system.error` releases the final-turn gate without disconnecting a healthy session.
- [x] Diagnostic lifecycle/event logs exclude tokens and transcript contents.
- [x] Responsive panel math keeps fields and button rows inside narrow Game views.
- [x] Placeholder precedence, malformed-file handling, and safe notices.
- [x] Git-ignored `UserSettings` path and absence of serialized credential fields.
- [x] Runtime endpoint validation, settings non-mutation, and terminal-state change restrictions.
- [x] JWT expiry parsing with a synthetic non-secret token.
- [x] Exact safe pre-network validation logging for missing/invalid/expired connection inputs.
- [x] Host-only connect logging, normalized system-error logging, exception-type-only logging, content redaction, and bounded activity history.
- [x] Heartbeat cadence and cancellation using a fake clock.
- [ ] A simulated 50-second pending connect remains `Connecting`, does not time out at 10–15 seconds, and never creates a duplicate connection attempt.
- [ ] Cancellation and the 75-second transport timeout terminate a delayed connection cleanly without stale callbacks.
- [x] Close-code policy and reconnect backoff.
- [ ] Fresh session on reconnect and no replay.
- [ ] Single final-turn gate and timeout.

### PlayMode

- [x] Dedicated live-test scene loads with configured client/adapters and waits safely for runtime credentials.
- [ ] Bootstrap lifecycle and single-instance behavior.
- [ ] Main-thread delivery to fake avatar/UI adapters.
- [ ] Application focus/background heartbeat behavior.
- [ ] `Waking server` status and elapsed connection time during a simulated Render cold start.
- [ ] Scene change, Play Mode exit, and application-quit cleanup.
- [ ] Inspector URL validation and reconnect after URL edit.

### Build

- [x] Run the EditMode suite in Unity `6000.3.10f1` (169/169 passing on 2026-07-17).
- [x] Add and run the PlayMode suite (live-scene smoke test 1/1 passing on 2026-07-17).
- [ ] Produce and smoke-test a Windows Standalone development build.
- [ ] Verify IL2CPP/AOT serialization if Android/iOS becomes in scope.
- [ ] Add a WebGL transport/build test only if WebGL becomes an approved target.

## 11. Suggested commit sequence

1. `chore: add Synth Cohost assemblies and connection settings`
2. `feat: add deployed v2 protocol models and contract tests`
3. `feat: add WebSocket transport abstraction and Windows adapter`
4. `feat: add v2 authentication and session lifecycle`
5. `feat: add heartbeat reconnect and final-turn control`
6. `feat: add protocol routing and diagnostics`
7. `feat: add avatar AI transcript and error adapters`
8. `test: add PlayMode and backend integration coverage`
9. `docs: document Unity setup local live testing and troubleshooting`

Every commit must remain Unity-only and exclude the ignored bridge repository.

## 12. Definition of done

- [x] Unity emits exact deployed-v2 field names and types.
- [x] Auth is first and contains integer `v: 2`, client session ID, timestamp, token, and avatar ID.
- [x] Unity does not wait for `session.ready` or heartbeat ack.
- [x] Heartbeat sends every 20 seconds and stops with the session.
- [x] Close codes 4000–4004 produce bounded, visible behavior.
- [x] Reconnect creates a new session and never replays an interrupted turn.
- [x] Only one final turn can be active.
- [x] Unknown types and malformed input cannot crash/deadlock Unity.
- [x] Avatar, AI response, transcript transport, state ack, and errors use replaceable interfaces.
- [x] Real microphone STT, TTS, audio, and visemes are absent from the current implementation.
- [x] Runtime code neither writes nor logs tokens and no credential is serialized into tracked Unity content; the optional local placeholder draft is explicitly Git-ignored plaintext development data.
- [ ] EditMode, PlayMode, Windows build, and real-backend tests pass.
- [ ] The exact live endpoint passes warm and cold-start-aware authenticated tests without reconnect storms.
- [ ] Local/live socket switching requires only changing the Inspector default or runtime Game-view endpoint field, without code changes.
- [ ] Finalized backend communication-protocol regression and production-runtime acceptance pass.
- [ ] The dashboard live-avatar delivery method and Unity build target are agreed before release packaging.
- [x] The bridge folder remains ignored, clean, and unmodified.

## Final readiness answer

The Unity current-v2 foundation, dedicated live-test scene, runtime input workflow, and safe dual-surface diagnostics are implemented. All 169 EditMode tests and the 1/1 live-scene PlayMode smoke test pass in Unity. The latest bridge export through `79fbf85` has been compared with the Unity runtime and requires no production-code migration: every deployable v2 field/event is already covered, while OBS and speech/media additions are backend-side, mock, or explicitly unwired. An earlier fresh-token wire smoke with the same temporary account/avatar completed `stt.final -> avatar.state -> ai.response`, so the deployed text path has been demonstrated outside the Unity panel. The latest client-supplied token has expired, however, so the corrected Unity panel still requires one fresh-token happy-path run. That preflight rejection is now explicit in both the panel activity log and Unity Console. No credential is stored in tracked Unity content; the optional local placeholder draft remains Git-ignored plaintext development data. The frontend/Vercel deployment does not change the Unity socket implementation. Broader production acceptance remains dependent on a Windows build plus the backend finishing its runtime integrations, replacing remaining mocks, and finalizing any Unity-visible communication-protocol changes.

The remaining inputs are for integration and release validation:

1. A fresh access token from the existing temporary account immediately before authenticated live testing; the matching avatar UUID already exists.
2. Exact REST auth/avatar schemas only if Unity must implement register/login/avatar creation.
3. A separate staging endpoint/account only if staging is required.
4. Final reconnect/ordering rules before release sign-off.
5. A real-versus-mock environment matrix and versioned protocol diff/fixtures for any finalized Unity-visible changes.
6. Confirmation of how the dashboard will display the live Unity avatar and which build/deployment target that requires.
7. If OBS or `speech.*` enters Unity scope, the media topology and complete versioned contract listed in §6.5.
