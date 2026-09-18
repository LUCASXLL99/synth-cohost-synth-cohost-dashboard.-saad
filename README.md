# Synth Cohost — Unity AI Co-Host

Unity runtime for the **Synth Cohost AI co-host**: character rendering, animation, interaction systems, and the client that talks to the Synth Cohost backend in real time.

This repository is **not** the Next.js host/viewer dashboards. Those live in a separate frontend project. This repo powers the **live AI co-host itself**.

---

## End goal

Deliver a Unity AI co-host that:

1. Renders and animates the AI avatar (appearance, expressions, behaviors, interactions).
2. Stays connected to the backend over the Synth Cohost WebSocket + HTTP auth contract.
3. Plugs into the **Host** experience so creators can control and interact with the live co-host from the dashboard.
4. Becomes part of the **live streaming** experience through **LiveKit** (not a forever-standalone desktop toy).

**Long-term shape:** Next.js handles creator/audience UI and stream controls; Unity handles the avatar, AI behaviors, rendering, and real-time co-host interactions; LiveKit carries the co-host into the broadcast/meeting session.

---

## Product architecture (confirmed)

| Piece | Owner | Role |
|-------|--------|------|
| **Host UI** | Next.js | Creator control dashboard (stream controls, AI controls, camera, etc.) |
| **Broadcast Viewer UI** | Next.js | Twitch-style audience experience |
| **Meeting Participant UI** | Next.js | Zoom-style participant experience |
| **Unity AI co-host** | **This repo** | Avatar, rendering, animation, interactions, backend client |
| **Backend** | Synth Cohost API / WS | Auth, AI, avatar state, session, streaming infra |
| **LiveKit** | Shared integration | Puts the Unity co-host into the live stream |

### Streaming UI routing (frontend — not Unity)

Users join via `/join/{roomId}`. Room metadata chooses the UI:

- `sessionType = broadcast` → Broadcast Viewer UI  
- `sessionType = meeting` → Meeting Participant UI  

The creator already chose the session type when starting the room; viewers do not pick the interface.

Host and Viewer/Participant experiences stay separate. Moderators may gain elevated permissions during a session, but they still enter from the viewer/join side.

### How Unity fits

- The **current Unity WebSocket / auth work is part of the same architecture**, not a side project.
- The Unity client **plugs into the Host experience**: Host UI controls and interacts with the live AI co-host.
- Next.js = dashboard + streaming controls. Unity = avatar + AI behaviors + rendering + real-time interactions.
- **LiveKit** will be integrated so the Unity AI co-host is part of the live stream rather than only a standalone app.

### What Unity must not do

- Do **not** hardcode temporary Vercel preview URLs. They are UI review deployments only. Production frontend domain comes later.
- Do **not** treat Next.js Host/Viewer/Participant screens as Unity’s implementation responsibility.
- Do **not** implement draft WebSocket 2.1 (`session.ready`, `protocol_version` rename, etc.) until the backend ships and signs them off.

---

## Current milestone status (Unity)

### Done

- Live WebSocket client (`v: 2`, auth, heartbeat, STT text, avatar state, AI response, `speech.audio` / `speech.failed`, errors, reconnect)
- HTTP login / refresh / logout with background access-token renewal (15‑min access, 30‑day refresh, rotation)
- Dashboard avatar presentation (behaviors, captions, local TTS fallback, mouth-openness from `speech.audio`)
- Live-test + dashboard scenes; safe diagnostics (no tokens / secrets in logs)

Live endpoint (configurable): `wss://synth-cohost-app-bzi4.onrender.com/ws`  
REST base: `https://synth-cohost-app-bzi4.onrender.com`

### Next

1. **Host integration** — how Host UI embeds / drives Unity (Windows vs WebGL vs capture; credential inject).
2. **LiveKit** — Unity co-host participates in the live stream.
3. **Protocol follow-ups** — only after backend ships: draft `session.ready` / `protocol_version`, phoneme viseme IDs.

### Explicitly out of this Unity repo’s product scope

- Building or owning the three Next.js streaming UIs
- Frontend layout, Vercel domains, OBS/RTMP dashboard plumbing owned by backend/sidecar
- Backend AI/moderation internals

---

## Repo layout (high level)

| Path | Purpose |
|------|---------|
| `Assets/SynthCohost/` | Runtime client, auth, session, protocol, live-test harness |
| `Assets/Scenes/SynthCohostLiveTest.unity` | Live backend test scene (debug panel) |
| `Assets/Scenes/SynthCohostDashboard.unity` | Product-shaped Standalone scene (no debug panel; repair via Tools menu) |
| `Assets/websocket-protocol-spec.docx` | Canonical WS contract (live v2 + draft future) |
| `synth-cohost-unity-bridge/` | **Git-ignored** backend bridge export (reference only; do not commit) |
| `UNITY_WEBSOCKET_IMPLEMENTATION_PLAN.md` | Deployed v2 contract and remaining work |
| `UNITY_LIVE_TEST_GUIDE.md` | How to run live Connect / Get access token tests |

---

## Quick start (developers)

1. Open the project in Unity (`6000.3.x`).
2. Follow **[UNITY_LIVE_TEST_GUIDE.md](UNITY_LIVE_TEST_GUIDE.md)** for live WebSocket testing.
3. Use **[UNITY_WEBSOCKET_IMPLEMENTATION_PLAN.md](UNITY_WEBSOCKET_IMPLEMENTATION_PLAN.md)** for the deployed v2 contract and remaining work.
4. Prefer the DOCX + latest backend confirmations over draft notes when contracts conflict.

---

## Source of truth (contracts)

1. `Assets/websocket-protocol-spec.docx` — WebSocket contract (what is live vs draft).
2. Bridge `docs/auth-token-flow.md` (in `synth-cohost-unity-bridge`) — HTTP auth/refresh.
3. Client/product confirmations on architecture (Host / Viewer / Meeting / Unity / LiveKit) — summarized in this README.
4. Implementation plan Markdown — deployed v2 contract; does not override product architecture above.

---

## Platforms

- **Current target:** Windows Standalone (live-test and co-host client).
- **Later:** LiveKit-integrated co-host path; transport boundary allows other adapters (e.g. WebGL) only when product chooses that delivery path.
