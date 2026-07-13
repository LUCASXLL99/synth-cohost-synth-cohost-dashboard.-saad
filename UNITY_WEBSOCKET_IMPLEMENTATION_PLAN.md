# Synth Cohost Unity WebSocket Implementation Plan

Status: **Ready to begin Unity-side development against the currently deployed v2 contract.**

Local end-to-end acceptance still requires a running backend plus a valid development access token and avatar ID. Those items do not block implementation of the modular Unity client.

## Source-of-truth order

Use sources in this order when details conflict:

1. `Assets/websocket-protocol-spec.docx` — canonical document covering current v2 and proposed 2.1.
2. Backend developer confirmations recorded on 2026-07-13 in §5.
3. `E:\Downloads\test_ws.sh` — exact current-v2 happy-path wire example.
4. `synth-cohost-unity-bridge` at commit `ca5bd8a` — exported Rust payload/type reference.
5. `E:\Downloads\WEBSOCKET_DOCS_SAAD.md` — earlier draft notes; context only.

The earlier Markdown notes must not override the DOCX, the latest written backend decisions, or the current smoke-test frames.

This plan covers Unity-side work only. The ignored `synth-cohost-unity-bridge/` repository is read-only reference material and must never be edited, staged, or committed with the Unity project.

## 1. Outcome

Build a modular Unity client that:

- Connects to a complete `ws://` or `wss://` URL configured in one Inspector field.
- Switches between local and live endpoints by changing only that URL, assuming credentials are valid for the selected environment.
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
- Never persists or logs access tokens.

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
- EditMode, PlayMode, local integration, and eventual live/staging verification.
- Windows Standalone first, matching the current project target.
- A transport boundary that permits mobile/WebGL adapters later.

### Out of scope

- Any backend or bridge change.
- Backend AI, authentication internals, moderation, persistence, deployment, or rate-limit implementation.
- Proposed 2.1 wire behavior until it is approved and deployed.
- `session.ready` and server-generated session IDs in current v2.
- `heartbeat.ack`.
- TTS, audio streaming, `speech.*`, lip sync, and visemes.
- Multiple concurrent `stt.final` turns.
- Session resumption or replay after disconnect.
- Final avatar art, animation graphs, captions UI, and audio assets that are not present yet.

## 3. Current Unity baseline

- Unity Editor: `6000.3.10f1`.
- Active target: Windows Standalone 64-bit.
- Render pipeline: URP `17.3.0`.
- Input System: `1.18.0`.
- Test Framework: `1.6.0`.
- One enabled starter scene: `Assets/Scenes/SampleScene.unity`.
- No existing networking, protocol, application, avatar, UI, or test architecture.
- No assembly definitions.
- `Application.runInBackground` is disabled and must be enabled for reliable heartbeat behavior while unfocused.
- `System.Net.WebSockets.ClientWebSocket` is available for the current Windows/.NET target.

This is a greenfield integration, so the module boundaries can be established cleanly.

## 4. Confirmed deployed v2 contract

### 4.1 Endpoint

Local development:

```text
ws://127.0.0.1:8080/ws
```

- No special headers.
- No query parameters have been specified.
- No WebSocket subprotocol.
- Live/staging will use the complete supplied `wss://.../ws` URL through the same Inspector field.

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
| `ai.response` | `{ text, emotion, intent }` | Publish typed response/caption data. Dev responses may use a mock AI provider. |
| `system.error` | `{ code, message }` | Publish a sanitized error; the connection may remain open or a close may follow. |

Expected happy-path response to `stt.final`:

1. `avatar.state` with `thinking`.
2. Another `avatar.state` containing the generated response behavior.
3. `ai.response` containing text, emotion, and intent.

The final-turn gate is released on `ai.response`, a terminal `system.error`, disconnect, cancellation, or a configurable response timeout. Do not rely on a finalized ordering beyond the one-turn rule.

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
| 4001 | `AUTH_FAILED` | Reacquire credentials once; retry only if successful, otherwise enter `AuthRequired`. |
| 4002 | `SESSION_MISMATCH` | Clear state and attempt one fresh connection/session; stop if repeated. |
| 4003 | `HEARTBEAT_TIMEOUT` | Reconnect automatically with a new session. |
| 4004 | `RATE_LIMIT_EXCEEDED` | Wait at least the server's 10-second window plus backoff before retrying. |

This table belongs in a replaceable reconnect policy so later backend/client sign-off changes do not affect transport, serialization, or feature code.

### 4.11 Proposed 2.1 — future only

The DOCX also describes an undeployed proposal:

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

### 5.6 Final implementation decision

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

### 6.3 Readiness verdict

| Area | Status |
|---|---|
| Unity architecture | Ready. |
| Current-v2 DTOs/codec | Ready. |
| Transport and Inspector URL | Ready. |
| Auth/session/heartbeat logic | Ready. |
| Single final-turn workflow | Ready. |
| Avatar/AI/error adapters | Ready. |
| Local happy-path integration | Ready once a backend and credentials are available. |
| Unity-owned register/login/avatar creation | Needs the exact HTTP API contract if included. |
| Full close/error acceptance | Needs backend fixtures or manual test cases. |
| Live/staging verification | Needs live URL and matching credentials later. |
| Proposed 2.1 | Future work; does not block v2. |

### 6.4 Remaining inputs

No additional answer is required before starting Unity WebSocket development.

Before local end-to-end acceptance, obtain one of these:

- Access to a running backend at `ws://127.0.0.1:8080/ws`, plus a valid short-lived token and tenant-owned avatar ID; or
- The full backend workspace/run instructions and prerequisites required to run `test_ws.sh`.

If Unity is expected to own account/avatar provisioning in this milestone, also request:

- HTTP base URL and exact register/login/refresh/`POST /avatars` routes.
- Request/response JSON and error schemas.
- Token expiry/refresh rules.

Before release, obtain:

- Staging/live `wss://` endpoint and credentials.
- Final reconnect and response-ordering sign-off.
- Repeatable close-code/error cases.

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
      Stt/
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

## 8. Inspector configuration

Create a `SynthCohostConnectionSettings` ScriptableObject referenced by the bootstrap component.

Fields:

- `Endpoint URL`, defaulting locally to `ws://127.0.0.1:8080/ws`.
- `Auto Connect`.
- Final-turn response timeout.
- Reconnect base delay, maximum delay, jitter, and attempt limits.
- Diagnostic log level.
- `Run In Background`, enabled by default.

Requirements:

- Keep the complete URL in one field; do not split scheme/host/port/path.
- Validate with `Uri.TryCreate`; allow only `ws` and `wss`.
- Pass the URL unchanged to the transport.
- Permit `ws` only for local/loopback development.
- Require `wss` for live/non-loopback endpoints and never bypass certificate validation.
- Apply a changed URL on the next connect/reconnect.
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
- [ ] Obtain a running backend and non-committed test credentials/avatar before local integration testing.
- [ ] Obtain the HTTP auth/avatar contract only if Unity must implement that flow.

Exit: protocol implementation may start now; open items affect integration/account provisioning only.

### Phase 1 — Project foundation

- [ ] Remove or replace the unused `NewMonoBehaviourScript` template when implementation begins.
- [ ] Create the `Assets/SynthCohost` folder structure and namespaces.
- [ ] Add assembly definitions from §7.
- [ ] Add a serializer behind `IProtocolCodec`; prefer Unity's supported Newtonsoft JSON package for exact field control and envelope inspection.
- [ ] Use `ClientWebSocket` for the current Windows target behind `IWebSocketTransport`.
- [ ] Keep room for a separate mobile/WebGL transport adapter without changing session or feature code.
- [ ] Enable `Application.runInBackground` through bootstrap/settings.
- [ ] Add a dedicated bootstrap prefab or integration scene without coupling networking to sample-scene content.

Exit: assemblies compile and EditMode/PlayMode test assemblies run.

### Phase 2 — Implement and test deployed-v2 DTOs

- [ ] Add exact event-name constants.
- [ ] Implement `DeployedV2ProtocolDialect` with integer `v: 2`.
- [ ] Model one common envelope that always includes `session_id`, including auth.
- [ ] Implement typed DTOs for auth, heartbeat, STT partial/final, state ack, avatar state, AI response, and system error.
- [ ] Use explicit JSON property names.
- [ ] Add explicit enum converters/TryParse behavior.
- [ ] Generate invariant UTC RFC3339 timestamps.
- [ ] Implement session-ID, UTF-8 STT-length, and serialized-size checks.
- [ ] Inspect `type` before selecting a payload DTO.
- [ ] Ignore unknown event types safely.
- [ ] Diagnose malformed known payloads without throwing into the frame loop.
- [ ] Add golden JSON fixtures matching all three frames in `test_ws.sh`.
- [ ] Add inbound fixtures for the expected avatar and AI responses.

Exit: exact fixtures round-trip without a real socket.

### Phase 3 — WebSocket transport

- [ ] Define transport states, callbacks, async connect/send/close, and cancellation.
- [ ] Implement `ClientWebSocketTransport` for Windows.
- [ ] Reassemble fragmented text frames correctly.
- [ ] Diagnose unsupported binary frames; current scope has no binary/audio contract.
- [ ] Enforce a bounded receive buffer/message ceiling.
- [ ] Serialize outbound sends through one async gate.
- [ ] Preserve numeric close codes and reasons in `TransportCloseInfo`.
- [ ] Make shutdown idempotent across Play Mode exit, scene teardown, quit, cancellation, and domain reload.
- [ ] Marshal all Unity-facing callbacks to the Unity main thread.
- [ ] Test transport behavior through a fake adapter and optional loopback fixture.

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

- [ ] Define token and avatar providers.
- [ ] Validate avatar UUID and token presence before connect.
- [ ] Generate a fresh session UUID for each connection attempt.
- [ ] Send auth immediately as the first application frame.
- [ ] Enter provisional `Ready` after auth send completes; do not wait for `session.ready`.
- [ ] Treat subsequent 4000/4001 as a failed provisional handshake.
- [ ] Permit domain sends only in `Ready`.
- [ ] Clear heartbeat, current session, stale callbacks, and in-flight turn on disconnect.
- [ ] Publish typed state changes for diagnostics/UI.
- [ ] Never persist or log tokens.

Exit: fake transport tests prove every valid/invalid transition and silent-success handshake behavior.

### Phase 5 — Heartbeat, reconnect, and final-turn control

- [ ] Start heartbeat after auth send succeeds.
- [ ] Send a full v2 heartbeat every 20 seconds using monotonic/realtime timing.
- [ ] Stop the scheduler before replacing the session/socket.
- [ ] Never wait for heartbeat acknowledgement.
- [ ] Implement single-flight reconnect with exponential backoff and jitter.
- [ ] Apply the provisional close policy from §4.10 behind a replaceable interface.
- [ ] Fetch/reacquire credentials as required for each new handshake.
- [ ] Generate a fresh session ID on reconnect.
- [ ] Never replay STT or acknowledgements.
- [ ] Implement `FinalTurnGate` so only one `stt.final` can be sent.
- [ ] Release the gate on AI response, terminal system error, disconnect, cancellation, or timeout.
- [ ] Do not let partial transcripts bypass the connection rate limit.

Exit: tests prove heartbeat timing, no ack dependency, backoff, fresh sessions, no replay, and one-turn enforcement.

### Phase 6 — Message routing

- [ ] Route by exact `type` string.
- [ ] Register one typed handler for avatar state, AI response, and system error.
- [ ] Keep routing independent of scenes and concrete feature implementations.
- [ ] Ignore unknown types.
- [ ] Reject messages whose session ID does not match the active socket session.
- [ ] Prevent late callbacks from a disposed socket/session reaching the current scene.
- [ ] Preserve per-socket receive order when dispatching to the main thread.
- [ ] Isolate handler exceptions from the receive loop.

Exit: routing tests cover every known message, unknown types, malformed payloads, stale sessions, and handler errors.

### Phase 7 — Unity feature adapters

#### Avatar state

- [ ] Define `IAvatarBehaviorController`, independent of `Animator` and avatar assets.
- [ ] Map all six current behavior values.
- [ ] Apply transitions on the Unity main thread.
- [ ] Send `state.ack` only after successful application.
- [ ] Do not send a false ack if the avatar/controller is unavailable.

#### AI response

- [ ] Publish text, emotion, and intent as a typed event.
- [ ] Keep mock-provider response text valid for integration testing.
- [ ] Add replaceable caption/presentation subscribers when UI exists.
- [ ] Keep emotion and intent available for later presentation logic.

#### STT outbound API

- [ ] Expose partial and final transcript methods.
- [ ] Validate ready state, non-empty text, UTF-8 length, and required `final` value.
- [ ] Enforce one final in flight.
- [ ] Throttle/coalesce partial messages to remain under the rate limit.
- [ ] Return explicit success/failure results.

#### System errors

- [ ] Publish typed code/message data to diagnostics/UI.
- [ ] Distinguish an in-session error from an error followed by close.
- [ ] Avoid exposing sensitive raw backend details in production UI/logs.

Exit: fake feature adapters prove correct dispatch and acknowledgements.

### Phase 8 — Bootstrap and Inspector workflow

- [ ] Create `SynthCohostClientBehaviour` as lifecycle owner.
- [ ] Reference settings and adapters through serialized fields/composition.
- [ ] Validate missing settings, invalid URL, or missing providers in the Inspector.
- [ ] Support auto-connect and explicit Connect/Disconnect/Reconnect.
- [ ] Decide whether the client persists across scenes and enforce one instance if it does.
- [ ] Add a developer status view: connection state, endpoint host, session presence, last event, reconnect attempt, current-turn status, and sanitized error.
- [ ] Verify changing only `Endpoint URL` switches local/live socket targets.

Exit: a designer can configure and operate the socket without changing code.

### Phase 9 — Diagnostics, security, and resilience

- [ ] Add structured categories for transport, protocol, session, heartbeat, reconnect, turns, and features.
- [ ] Redact tokens and authorization material from every path.
- [ ] Do not log full transcripts by default; allow sanitized/truncated development previews.
- [ ] Add counters for connections, auth sends/failures, reconnects, messages by type, malformed messages, turn timeouts, and close codes.
- [ ] Require `wss` and normal certificate validation for non-loopback/live endpoints.
- [ ] Observe all async exceptions.
- [ ] Bound receive buffers, pending callbacks, logs, reconnect delays, and partial-message rate.

Exit: failures are diagnosable without exposing secrets or destabilizing Unity.

### Phase 10 — Local and live integration

- [ ] Obtain the running backend and valid token/avatar.
- [ ] Connect to `ws://127.0.0.1:8080/ws` through the Inspector setting.
- [ ] Verify auth is the first full v2 frame and includes the client session ID.
- [ ] Verify no `session.ready`/heartbeat ack is required.
- [ ] Remain connected for more than 60 seconds while 20-second heartbeats prevent close 4003.
- [ ] Send one final transcript and verify thinking, response behavior, and AI response.
- [ ] Verify the final-turn gate rejects a second simultaneous turn.
- [ ] Apply avatar behavior and verify state ack server-side.
- [ ] Exercise partial transcript behavior if included in the milestone.
- [ ] Exercise malformed/auth/session/heartbeat/rate-limit close cases through backend fixtures or manual cases.
- [ ] Force network loss and prove a new session ID, fresh auth, and no replay.
- [ ] Confirm unknown future types do not break the session.
- [ ] Change only the Inspector URL to the supplied live/staging `wss://` endpoint and repeat the happy path with matching credentials.

Exit: local and live/staging pass with no bridge changes and only the endpoint URL changed between environments.

## 10. Verification matrix

### EditMode

- [ ] Exact current-v2 auth, heartbeat, partial, final, and ack serialization.
- [ ] Exact avatar, AI, and error deserialization.
- [ ] Integer `v: 2` and session ID on every frame.
- [ ] RFC3339 UTC timestamps.
- [ ] Enum mapping and unknown enum behavior.
- [ ] Unknown event ignored.
- [ ] Malformed known payload diagnosed.
- [ ] Session mismatch rejected.
- [ ] STT 4,000 UTF-8-byte boundary and message 64-KiB boundary.
- [ ] Rate-limit/throttling behavior.
- [ ] State transitions after silent auth success.
- [ ] Heartbeat cadence and cancellation using a fake clock.
- [ ] Close-code policy and reconnect backoff.
- [ ] Fresh session on reconnect and no replay.
- [ ] Single final-turn gate and timeout.

### PlayMode

- [ ] Bootstrap lifecycle and single-instance behavior.
- [ ] Main-thread delivery to fake avatar/UI adapters.
- [ ] Application focus/background heartbeat behavior.
- [ ] Scene change, Play Mode exit, and application-quit cleanup.
- [ ] Inspector URL validation and reconnect after URL edit.

### Build

- [ ] Run EditMode and PlayMode suites.
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
7. `feat: add avatar AI STT and error adapters`
8. `test: add PlayMode and backend integration coverage`
9. `docs: document Unity setup local live testing and troubleshooting`

Every commit must remain Unity-only and exclude the ignored bridge repository.

## 12. Definition of done

- [ ] Unity emits exact deployed-v2 field names and types.
- [ ] Auth is first and contains integer `v: 2`, client session ID, timestamp, token, and avatar ID.
- [ ] Unity does not wait for `session.ready` or heartbeat ack.
- [ ] Heartbeat sends every 20 seconds and stops with the session.
- [ ] Close codes 4000–4004 produce bounded, visible behavior.
- [ ] Reconnect creates a new session and never replays an interrupted turn.
- [ ] Only one final turn can be active.
- [ ] Unknown types and malformed input cannot crash/deadlock Unity.
- [ ] Avatar, AI response, STT, state ack, and errors use replaceable interfaces.
- [ ] TTS/audio/visemes are absent from the current implementation.
- [ ] Tokens are neither persisted nor logged.
- [ ] EditMode, PlayMode, Windows build, and real-backend tests pass.
- [ ] Local/live socket switching requires only the Inspector endpoint URL change.
- [ ] The bridge folder remains ignored, clean, and unmodified.

## Final readiness answer

We are good to start Unity-side WebSocket development now.

The only items still needed are for integration and release validation, not for starting implementation:

1. A running local backend and a valid short-lived token/avatar ID.
2. Exact HTTP auth/avatar schemas only if Unity must implement register/login/avatar creation.
3. A live/staging endpoint and credentials before live testing.
4. Final reconnect/ordering rules before release sign-off.
