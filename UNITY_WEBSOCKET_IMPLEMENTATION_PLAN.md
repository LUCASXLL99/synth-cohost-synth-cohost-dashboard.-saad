# Synth Cohost Unity WebSocket Implementation Plan

Status: Planning complete; target-contract integration is blocked pending backend confirmation.

Primary sources reviewed:

- `Assets/websocket-protocol-spec.docx` — target Unity ↔ backend contract, reference version 2.0 draft.
- `synth-cohost-unity-bridge` at commit `ca5bd8a` — current exported backend reference.
- Current Unity project at Unity `6000.3.10f1`.

This plan covers Unity-side work only. The imported `synth-cohost-unity-bridge/` repository is reference material and must not be edited, staged, or committed as part of the Unity project.

## 1. Outcome

Build a modular Unity WebSocket client that:

- Connects to a complete `ws://` or `wss://` endpoint configured as one Inspector field.
- Switches between localhost and live environments by changing only that endpoint field, assuming the credentials are valid for the selected environment.
- Authenticates using a short-lived access token and avatar UUID.
- Implements the approved session handshake, heartbeat, reconnect, routing, and close-code behavior.
- Sends `stt.partial`, `stt.final`, and `state.ack` messages.
- Receives `avatar.state`, `ai.response`, `speech.*`, and `system.error` messages.
- Keeps transport, wire protocol, session lifecycle, and scene/avatar presentation independent and testable.
- Never stores or commits access tokens in a scene, prefab, ScriptableObject, source file, or settings asset.

## 2. Scope

### In scope

- Unity configuration, transport, serialization, protocol validation, and typed message models.
- Authentication/session lifecycle and heartbeat scheduling.
- Reconnect policy and close-code handling.
- Typed message routing and Unity-facing events/interfaces.
- Avatar behavior, AI response, speech event, error, STT, and state-ack integration points.
- Inspector configuration, diagnostics, EditMode tests, PlayMode tests, and backend integration tests.
- Windows Standalone support first, matching the project's current active build target.
- A transport boundary that permits a WebGL/mobile adapter later without changing protocol or feature code.

### Out of scope

- Any change to `synth-cohost-unity-bridge/` or the backend.
- Backend authentication, AI generation, moderation, rate limiting, deployment, or persistence.
- Session resumption or replaying a message lost during disconnect.
- Inventing an audio channel, viseme vocabulary, or backend behavior not defined by the approved contract.
- Building final avatar art, animation graphs, captions UI, or audio playback assets that are not yet present in this Unity project. The plan supplies replaceable adapters for those systems.

## 3. Current Unity baseline

- Unity Editor: `6000.3.10f1`.
- Active target: Windows Standalone 64-bit.
- Render pipeline: URP `17.3.0`.
- Input: Input System `1.18.0`.
- Test Framework: `1.6.0`.
- One enabled starter scene: `Assets/Scenes/SampleScene.unity`.
- No existing networking, protocol, application, avatar, UI, speech, or test architecture.
- No assembly definitions currently exist.
- `Application.runInBackground` is disabled; continuous WebSocket/heartbeat operation will require enabling it or setting it during bootstrap.
- `System.Net.WebSockets.ClientWebSocket` is available for the current Windows/.NET target.

This is effectively a greenfield integration, so the architecture can be established cleanly.

## 4. Target protocol summary

### 4.1 Envelope

After contract approval, every non-auth message should use:

```json
{
  "protocol_version": "2.0",
  "type": "<event_name>",
  "session_id": "<server-generated-uuid>",
  "ts": "<rfc3339-utc>",
  "payload": {}
}
```

The initial `auth` is the only message without `session_id`:

```json
{
  "protocol_version": "2.0",
  "type": "auth",
  "ts": "<rfc3339-utc>",
  "payload": {
    "token": "<short-lived-access-token>",
    "avatar_id": "<uuid>"
  }
}
```

Rules:

- Parse the version as a `major.minor` string.
- Accept additive changes within the approved major version.
- Ignore unknown `type` values without closing or faulting the session.
- Treat a different major version as incompatible and surface a clear client error.
- Generate timestamps in UTC RFC3339 form.
- Use the `session_id` returned by the server on every post-auth outbound message.

### 4.2 Target connection lifecycle

1. Unity opens the configured WebSocket endpoint.
2. On socket open, Unity immediately sends `auth` as the first application message and well within the server's 10-second deadline.
3. Server validates the access token/avatar and returns `session.ready`.
4. Unity verifies the echoed avatar ID, stores the server-generated session ID, and reads `heartbeat_interval_ms`.
5. Unity enters `Ready`, starts heartbeats, and permits domain messages.
6. On disconnect, Unity discards the session ID and all in-flight state.
7. A reconnect performs a new connection, auth, and `session.ready`; it never resumes or replays the old session.

Target `session.ready`:

```json
{
  "protocol_version": "2.0",
  "type": "session.ready",
  "session_id": "<uuid>",
  "ts": "<rfc3339-utc>",
  "payload": {
    "avatar_id": "<uuid>",
    "heartbeat_interval_ms": 20000
  }
}
```

### 4.3 Messages

Unity → server:

| Type | Payload | Unity behavior |
|---|---|---|
| `auth` | `{ token, avatar_id }` | First message only; no session ID. |
| `heartbeat` | `{}` | Send at the interval supplied by `session.ready`. |
| `stt.partial` | `{ text, final: false }` | Send only while ready; server currently validates but does not consume it. |
| `stt.final` | `{ text, final: true }` | Send only while ready; do not replay after reconnect. |
| `state.ack` | `{ behavior }` | Send after Unity successfully applies the requested behavior. |

Server → Unity:

| Type | Payload | Unity behavior |
|---|---|---|
| `session.ready` | `{ avatar_id, heartbeat_interval_ms }` | Establish the session and heartbeat cadence. |
| `heartbeat.ack` | Not formally specified | Ignore safely or record diagnostics; never require it for liveness. |
| `avatar.state` | `{ behavior }` | Request a behavior transition through an avatar adapter. |
| `ai.response` | `{ text, emotion, intent }` | Publish typed response/caption data to presentation code. |
| `speech.start` | `{ utterance_id }` | Open an utterance lifecycle. |
| `speech.viseme` | `{ utterance_id, frame: { t_ms, shape, weight } }` | Buffer/forward timed viseme data when a mapping is available. |
| `speech.end` | `{ utterance_id }` | Close the matching utterance lifecycle. |
| `system.error` | `{ code, message }` | Surface/log a sanitized protocol/backend error; the socket may remain open. |

Contract enums:

- Behavior: `idle`, `listening`, `thinking`, `speaking`, `happy`, `celebrate`.
- Emotion: `neutral`, `happy`, `excited`, `concerned`, `confused`, `celebrate`.
- Intent: `chat`, `question`, `command`, `greeting`, `farewell`, `unknown`.

### 4.4 Validation constraints to mirror client-side

- Outbound STT text: non-empty and no more than 4,000 UTF-8 bytes.
- Outbound serialized message: keep below 64 KiB.
- Incoming JSON must be handled defensively and must never crash the Unity main thread.
- Unknown event types are ignored and optionally logged at debug level.
- Malformed known messages are reported through diagnostics and not applied to scene objects.
- Do not send messages faster than the server's 60 messages per 10-second connection limit.
- Do not make `heartbeat.ack` a liveness requirement.

## 5. Backend readiness audit

### 5.1 Verdict

The Unity architecture can be started, but the exported backend reference does **not** prove that the target protocol is ready for end-to-end implementation. Contract approval and an updated backend reference/smoke test are required before locking the Unity wire DTOs and handshake behavior.

### 5.2 Evidence

| Area | DOCX target | Current exported Rust reference | Result |
|---|---|---|---|
| Contract status | Reference contract, but marked draft | README says draft/pending sign-off | Not final. |
| Version field | `protocol_version: "2.0"` | `v: 2` (`u32`) | Breaking mismatch. |
| Auth session ID | Auth omits `session_id` | `Envelope.session_id` is mandatory | Breaking mismatch. |
| Session owner | Server-generated UUID | Client-provided string | Breaking mismatch. |
| Ready message | Server sends `session.ready` | No implementation exists | Missing. |
| Heartbeat cadence | Supplied in `session.ready` | No ready/cadence implementation in export | Missing. |
| STT payload | Includes `final` boolean | Rust payload contains `text` only | Schema mismatch/ambiguity. |
| Endpoint | Complete local/live URL required | Only `wss://.../ws` placeholder | Missing. |
| Token flow | Short-lived access token | Auth internals intentionally omitted | Cannot test. |
| Close codes | 4000–4004 | Handling is outside exported code | Cannot verify. |
| Rate limit | 60 messages/10 seconds | Generic limiter exists; live wiring absent | Cannot verify. |
| Speech | `speech.*` shapes listed | Mock TTS events only; no actual audio channel | Not wired. |
| Integration proof | Live server/smoke test required | Referenced `ws.rs` and `test_ws.sh` are absent | Cannot verify. |

Additional inconsistencies:

- The specification calls the target `2.0`, but also describes its new handshake fields as a `2.1` delta.
- The DOCX says the §5–§7 tables are pending final backend content even though the tables are populated.
- The bridge Markdown calls that note possibly stale, so the DOCX and Markdown are not fully synchronized.
- `heartbeat.ack` appears in the sequence but has no formal payload schema.
- There is no turn/request identifier or defined rule for concurrent `stt.final` messages and response ordering.
- The protocol provides visemes but no audio data/URL or viseme shape vocabulary, so synchronized TTS playback cannot be completed from these materials.

The exported Rust source and its tests were inspected, but the tests could not be executed on this machine because the Rust/Cargo toolchain is not installed. This does not change the wire-contract mismatch above, which is explicit in both the source and repository documentation.

### 5.3 Backend gate

Do not implement a guessed hybrid of the legacy and target protocols. Before Phase 2 is finalized, obtain:

- The exact deployed protocol version and envelope field names.
- Confirmation that `session.ready` and server-generated session IDs are live.
- Exact local, staging, and production WebSocket URLs, including path/port.
- Any required headers, query parameters, or WebSocket subprotocol.
- A safe test-token/avatar acquisition flow and token refresh behavior.
- A backend smoke test or exported integration test for the approved handshake.
- Clarification of `stt.*.final`, `heartbeat.ack`, close-code retry policy, ordering/concurrency, and speech/audio scope.

## 6. Message to send to the backend developer

> The Unity bridge repository at commit `ca5bd8a` still marks the WebSocket contract as draft. Its README and §9 say the current wire format uses integer `v: 2`, a client-generated session ID, and no `session.ready`, while the Unity target DOCX requires string `protocol_version`, auth without a session ID, and a server-generated session returned through `session.ready`.
>
> Please confirm the exact currently deployed wire contract and update the bridge repository/spec if the target implementation is now live. Please also provide:
>
> 1. Exact local/staging/live WebSocket URLs and any required headers, query parameters, or subprotocol.
> 2. The approved test access-token and avatar-ID acquisition/refresh flow.
> 3. Confirmation of the protocol version (`2.0` versus the document's reference to these additions as `2.1`).
> 4. Whether the `final` boolean is required in `stt.partial` and `stt.final` payloads.
> 5. The exact `heartbeat.ack` schema and whether the server emits it.
> 6. The intended reconnect/backoff behavior for close codes 4000–4004.
> 7. Ordering/concurrency rules for multiple `stt.final` turns and whether a turn/request ID will be added.
> 8. Whether TTS/audio/visemes are out of scope or available through another channel; if available, provide the audio transport and viseme shape vocabulary.
> 9. An updated smoke test or exported WebSocket integration test proving the approved handshake.
>
> We will make Unity-side changes only. Until the above is confirmed, we can build the modular client foundation, but final wire integration and end-to-end tests remain blocked.

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
      RuntimeAccessTokenProvider.cs
    Protocol/
      ProtocolConstants.cs
      ProtocolVersion.cs
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
    Routing/
      ProtocolMessageRouter.cs
      IProtocolMessageHandler.cs
    Features/
      Avatar/
      AiResponse/
      Speech/
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

Recommended assembly boundaries:

- `SynthCohost.Protocol`: Unity-independent DTOs, version parsing, codec, and validation.
- `SynthCohost.Transport`: socket abstraction and Windows `ClientWebSocket` adapter.
- `SynthCohost.Runtime`: session orchestration, routing, Unity feature adapters, bootstrap, and diagnostics.
- `SynthCohost.Tests.EditMode` and `SynthCohost.Tests.PlayMode`: test-only assemblies.

Dependency direction must remain one-way:

```text
Presentation/Avatar adapters -> Runtime session/routing -> Protocol + Transport interfaces
Transport implementation -------------------------------> Transport interfaces
```

Protocol code must not reference `MonoBehaviour`, scenes, animators, UI, or the concrete socket implementation.

## 8. Configuration design

Create a `SynthCohostConnectionSettings` ScriptableObject referenced by the bootstrap component.

Inspector fields:

- `Endpoint URL`: one complete URL, for example `ws://localhost:8080/ws` or `wss://api.example.com/ws`.
- `Auto Connect`.
- `Ready Timeout Seconds` for detecting a missing `session.ready` after auth.
- Reconnect base delay, maximum delay, jitter, and attempt policy.
- Diagnostic log level.
- Optional `Run In Background` toggle, enabled by default for a persistent cohost.

Requirements:

- Do not split scheme, host, port, and path into separate fields.
- Validate with `Uri.TryCreate` and allow only `ws`/`wss`.
- Pass the URL to the transport unchanged.
- Permit `ws` for local development.
- Require `wss` for non-loopback/live hosts; never disable certificate validation.
- A URL edit in the Inspector followed by reconnect must be sufficient to switch local/live endpoints.
- Keep token and other secrets out of this asset.
- Keep `avatar_id` and credentials supplied through an authentication/session provider so configuration and secret ownership remain separate.

## 9. Detailed implementation phases

### Phase 0 — Freeze the contract

- [ ] Send the backend message in §6.
- [ ] Receive an updated spec/reference commit or explicit written confirmation of the deployed dialect.
- [ ] Record the approved protocol version, envelope, auth shape, session ownership, and endpoint details in this plan.
- [ ] Obtain valid local integration credentials through a non-committed mechanism.
- [ ] Obtain or request a backend smoke test that demonstrates auth → `session.ready` → heartbeat → `stt.final`.
- [ ] Decide whether speech/audio is in the first Unity milestone.
- [ ] Do not implement legacy/target dual-mode compatibility unless the backend explicitly requires a migration window.

Exit criteria: one unambiguous wire contract and a reachable test endpoint exist.

### Phase 1 — Project foundation

- [ ] Remove or replace the unused `NewMonoBehaviourScript` template once implementation begins.
- [ ] Create the `Assets/SynthCohost` folder structure and namespaces.
- [ ] Add the assembly definitions described in §7.
- [ ] Add a JSON serializer behind `IProtocolCodec`; prefer Unity's supported Newtonsoft JSON package for exact field names, typed payloads, and resilient envelope inspection.
- [ ] Use `ClientWebSocket` for the current Windows target behind `IWebSocketTransport`.
- [ ] If WebGL becomes a required target, add a WebGL-compatible transport adapter without changing session/protocol/feature code.
- [ ] Enable or bootstrap `Application.runInBackground` for stable heartbeat behavior when unfocused.
- [ ] Add a dedicated integration/bootstrap scene or prefab without coupling networking to `SampleScene` content.

Exit criteria: assemblies compile, dependencies are locked, and test assemblies run.

### Phase 2 — Model and test the approved wire contract

- [ ] Add protocol constants for known event names and the approved major/minor version.
- [ ] Implement `ProtocolVersion` parsing and compatibility checks.
- [ ] Implement an envelope header/parser that can inspect `type` and version before selecting a payload DTO.
- [ ] Create separate auth and post-auth envelope DTOs so auth cannot accidentally serialize `session_id`.
- [ ] Create typed DTOs for every message in §4.3.
- [ ] Use explicit JSON property names; do not depend on C# naming-policy defaults.
- [ ] Represent wire enum strings through explicit converters/TryParse functions.
- [ ] Generate UTC RFC3339 timestamps with invariant culture.
- [ ] Implement UTF-8 byte-length validation for STT text.
- [ ] Implement serialized-size checks below 64 KiB for outbound messages.
- [ ] Ignore unknown event types while emitting a debug diagnostic.
- [ ] Reject/diagnose malformed known payloads without throwing into Unity's frame loop.
- [ ] Add golden JSON fixtures for every inbound and outbound message.
- [ ] Add tests proving auth omits `session_id` and post-auth messages include the current one.

Exit criteria: exact JSON fixtures round-trip and contract tests pass independently of a socket.

### Phase 3 — Implement the transport abstraction

- [ ] Define socket states, open/message/error/close callbacks, async connect/send/close, and cancellation in `IWebSocketTransport`.
- [ ] Implement the Windows `ClientWebSocketTransport` adapter.
- [ ] Use one receive loop with correct fragmented-frame reassembly.
- [ ] Accept text frames; diagnose unsupported binary frames until an audio/binary contract exists.
- [ ] Enforce a receive-size ceiling and cancel safely on oversized frames.
- [ ] Serialize sends through a single async gate so concurrent feature calls cannot interleave frames.
- [ ] Propagate close status and reason into a transport-neutral `TransportCloseInfo`.
- [ ] Make shutdown idempotent and cancel connect/receive/send work during Play Mode exit, scene teardown, application quit, or domain reload.
- [ ] Marshal callbacks to Unity's main thread before touching Unity objects.
- [ ] Write transport tests with a fake transport and, where practical, a local loopback fixture.

Exit criteria: connect/send/receive/close and cancellation are deterministic and leak-free.

### Phase 4 — Implement session/authentication orchestration

Use explicit states:

```text
Disconnected -> Connecting -> Authenticating -> Ready
      ^              |              |             |
      +--------------+--------------+-------------+
                         Reconnecting
```

Also support `Stopping` and terminal `Faulted/AuthRequired` states.

- [ ] Define `IAccessTokenProvider`; retrieve a short-lived access token at connection time.
- [ ] Never serialize the access token into Unity assets or logs.
- [ ] Validate the configured avatar ID before connecting.
- [ ] On socket open, send auth immediately as the first application frame.
- [ ] Start a client-side ready timeout after auth so a silent/legacy server cannot leave Unity stuck in `Authenticating`.
- [ ] Accept no domain sends until `session.ready` has been validated.
- [ ] Validate the returned session UUID, avatar ID, and positive/sane heartbeat interval.
- [ ] Store session data in memory only.
- [ ] Clear the session, heartbeat, pending operations, and feature lifecycle state on every disconnect.
- [ ] Reject or explicitly return `NotReady` for calls made outside `Ready`; do not silently queue and later replay STT turns.
- [ ] Publish typed state-change events for UI/diagnostics.

Exit criteria: a fake transport can prove the full state transition sequence and all invalid transitions.

### Phase 5 — Heartbeat and reconnection

- [ ] Start heartbeats only after `session.ready`.
- [ ] Schedule from `heartbeat_interval_ms` using realtime/monotonic timing rather than scaled game time.
- [ ] Include version, current session ID, timestamp, and `{}` payload.
- [ ] Do not wait for or require `heartbeat.ack`.
- [ ] Stop and dispose the heartbeat scheduler before replacing a session.
- [ ] Implement exponential backoff with jitter and a configurable cap; reset after a stable ready session.
- [ ] Guarantee only one active connect/reconnect attempt.
- [ ] On reconnect, fetch/refresh credentials, create a new socket, re-authenticate, and require a new session ID.
- [ ] Never replay the interrupted `stt.final` or prior state acknowledgements.
- [ ] Apply the approved close-code policy. Proposed default until backend/product confirmation:

| Close condition | Proposed Unity policy |
|---|---|
| User/app shutdown or normal close | Do not reconnect. |
| Network loss/abnormal close | Reconnect with exponential backoff. |
| 4000 `MALFORMED_PAYLOAD` | Stop automatic looping; surface protocol/configuration error. |
| 4001 `AUTH_FAILED` | Refresh credentials once; reconnect only if refresh succeeds, otherwise enter `AuthRequired`. |
| 4002 `SESSION_MISMATCH` | Clear all state and attempt one fresh session; stop and surface if repeated. |
| 4003 `HEARTBEAT_TIMEOUT` | Reconnect automatically with a fresh session. |
| 4004 `RATE_LIMIT_EXCEEDED` | Wait at least the server window plus backoff, then reconnect; surface repeated violations. |

Exit criteria: automated tests prove backoff, cancellation, fresh session IDs, single-flight reconnect, and no replay.

### Phase 6 — Message routing

- [ ] Route by the envelope's exact `type` string.
- [ ] Register one typed handler per known server message.
- [ ] Keep router registration independent of scenes and feature implementations.
- [ ] Ignore unknown types without erroring or replying.
- [ ] Verify post-auth inbound session IDs match the active session before routing.
- [ ] Prevent a late message from a disposed socket/session from reaching the current scene.
- [ ] Preserve per-socket receive order when dispatching to the main thread.
- [ ] Add tests for every route, unknown types, malformed payloads, stale sessions, and handler failures.

Exit criteria: routing is deterministic and one failed feature handler cannot terminate the network loop.

### Phase 7 — Unity feature adapters

#### Avatar state

- [ ] Define `IAvatarBehaviorController` independent of `Animator` and concrete avatar assets.
- [ ] Map all six behavior values to typed requests.
- [ ] Apply transitions on the Unity main thread.
- [ ] Send `state.ack` only after successful application.
- [ ] Define safe behavior for an unavailable avatar or unsupported transition; log and do not send a false acknowledgement.

#### AI response

- [ ] Publish a typed event containing text, emotion, and intent.
- [ ] Add a replaceable caption/presentation subscriber when UI exists.
- [ ] Keep emotion/intent available for animation and downstream behavior without coupling the protocol handler to those systems.

#### STT outbound API

- [ ] Expose `SendPartialTranscriptAsync` and `SendFinalTranscriptAsync` through a Unity-facing service.
- [ ] Validate readiness, non-empty text, and UTF-8 length before serialization.
- [ ] Set the approved `final` boolean exactly as confirmed by the backend.
- [ ] Prevent uncontrolled partial-message flooding and respect the connection rate limit.
- [ ] Return explicit success/failure results to the caller.

#### Speech and visemes

- [ ] Track utterances by `utterance_id` and enforce start → zero-or-more visemes → end locally.
- [ ] Define `ISpeechPlaybackController` and `IVisemeDriver` adapters.
- [ ] Buffer timed frames only within bounded limits.
- [ ] Treat unknown viseme shapes safely.
- [ ] Until the backend supplies audio and a viseme vocabulary, keep this as a tested event/lifecycle adapter rather than inventing playback behavior.

#### System errors

- [ ] Publish typed `system.error` data to diagnostics/UI.
- [ ] Distinguish recoverable in-session errors from errors followed by a close.
- [ ] Avoid showing raw sensitive server details in production UI/logs.

Exit criteria: fake feature adapters prove every protocol event reaches the correct Unity-facing API and acknowledgements occur only after successful state application.

### Phase 8 — Bootstrap and Inspector workflow

- [ ] Create `SynthCohostClientBehaviour` as the Unity lifecycle owner.
- [ ] Reference the connection settings asset and feature adapters through serialized fields/interfaces resolved at bootstrap.
- [ ] Add clear Inspector validation for missing settings, invalid URL, missing avatar provider, or missing feature adapters.
- [ ] Support auto-connect and explicit Connect/Disconnect/Reconnect controls.
- [ ] Decide whether the client persists through scene loads; if so, enforce a single `DontDestroyOnLoad` instance.
- [ ] Add a small developer status panel showing state, endpoint host, session presence, last event type, reconnect attempt, and sanitized error—never the token.
- [ ] Verify changing only `Endpoint URL` switches between the supplied local and live endpoints.

Exit criteria: a designer can configure and operate the connection without editing code.

### Phase 9 — Diagnostics, security, and resilience

- [ ] Use structured categories for transport, protocol, session, heartbeat, reconnect, and features.
- [ ] Redact tokens and authorization material from every log/error path.
- [ ] Avoid logging complete user transcript content by default; provide a sanitized/truncated development mode.
- [ ] Include session correlation data only where safe.
- [ ] Add counters for connects, successful auths, reconnects, messages by type, malformed messages, and close codes.
- [ ] Ensure live endpoints use `wss` and normal platform certificate validation.
- [ ] Ensure exceptions from async callbacks are observed and reported.
- [ ] Bound receive buffers, viseme queues, logs, and reconnect attempts/delays.

Exit criteria: failures are diagnosable without exposing secrets or destabilizing Unity.

### Phase 10 — Verification matrix

#### EditMode tests

- [ ] Version parser: valid same-major versions, newer minor, malformed value, incompatible major.
- [ ] Exact serialization for auth, heartbeat, STT partial/final, and state ack.
- [ ] Exact deserialization for ready, avatar, AI, speech, heartbeat ack, and system error.
- [ ] Auth omits `session_id`; all ready-state sends contain the active one.
- [ ] RFC3339 UTC timestamps.
- [ ] Unknown `type` ignored.
- [ ] Invalid known payload diagnosed.
- [ ] Enum mappings and unknown enum handling.
- [ ] UTF-8 4,000-byte STT boundary and 64-KiB message boundary.
- [ ] Session-state transitions, ready timeout, heartbeat cadence, reconnect backoff, and cancellation using a fake clock/transport.
- [ ] Close-code policy and no-replay behavior.
- [ ] Router isolation and stale-session rejection.

#### PlayMode tests

- [ ] Bootstrap lifecycle and single-instance behavior.
- [ ] Main-thread delivery to fake avatar/UI adapters.
- [ ] Application focus/background heartbeat behavior.
- [ ] Scene change and application quit cleanup.
- [ ] Inspector URL validation and reconnect after a URL edit.

#### Local backend integration

- [ ] Connect using the exact local URL from the Inspector.
- [ ] Send auth first and receive `session.ready`.
- [ ] Verify server-generated session ID and advertised heartbeat interval.
- [ ] Keep the connection alive for at least three heartbeat intervals.
- [ ] Send valid/invalid partial and final transcripts.
- [ ] Verify `thinking`/response behavior events and `ai.response` data.
- [ ] Apply avatar behavior and verify `state.ack` server-side.
- [ ] Exercise `system.error` and close codes 4000–4004 using backend fixtures.
- [ ] Force a network drop and prove fresh auth/session with no replay.
- [ ] Confirm unknown future event types do not break the session.

#### Live/staging verification

- [ ] Change only the Inspector endpoint from local `ws://...` to supplied `wss://...`.
- [ ] Use valid environment credentials without changing transport/protocol code.
- [ ] Verify TLS, handshake, heartbeat, one complete STT/AI turn, reconnect, and sanitized logs.
- [ ] Restore the intended checked-in default endpoint/configuration before committing.

#### Build verification

- [ ] Run all EditMode and PlayMode tests.
- [ ] Produce and smoke-test a Windows Standalone development build.
- [ ] Verify IL2CPP/AOT serialization if Android/iOS becomes in scope.
- [ ] Add a WebGL transport/build test only if WebGL becomes an approved target.

Exit criteria: the approved local and live environments pass by changing only the endpoint field, with no bridge changes.

## 10. Suggested commit sequence

1. `chore: add Synth Cohost assemblies and connection settings`
2. `feat: add typed WebSocket protocol models and contract tests`
3. `feat: add WebSocket transport abstraction and Windows adapter`
4. `feat: add authentication and session state machine`
5. `feat: add heartbeat and reconnect lifecycle`
6. `feat: add protocol routing and diagnostics`
7. `feat: add avatar AI STT speech and error adapters`
8. `test: add PlayMode and backend integration coverage`
9. `docs: document Unity setup local live testing and troubleshooting`

Each commit must remain Unity-only and must not include the ignored bridge repository.

## 11. Definition of done

- [ ] Backend has confirmed one final protocol dialect and supplied reachable endpoints/test credentials.
- [ ] Unity sends and receives every approved message with exact field names and types.
- [ ] Auth is first, session IDs are handled according to the approved contract, and heartbeats use the server cadence.
- [ ] Reconnect always creates a fresh session and never replays an interrupted STT turn.
- [ ] Unknown event types and malformed input cannot crash or deadlock Unity.
- [ ] Avatar, AI response, speech, STT, and errors are connected through replaceable interfaces.
- [ ] Access tokens are neither persisted nor logged.
- [ ] Local and live/staging tests pass by changing only the Inspector endpoint URL.
- [ ] EditMode, PlayMode, Windows build, and backend integration tests pass.
- [ ] The `synth-cohost-unity-bridge/` folder remains ignored, clean, and unmodified.
