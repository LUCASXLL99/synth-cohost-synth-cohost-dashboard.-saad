# Synth Cohost Dashboard — Technical Handoff & Roadmap

**Project:** Dashboard AI co-host (Unity)  
**Repo:** Synth Cohost Unity client  
**Audience:** Product, LLM/backend, Host/streaming developers  
**Date:** 15 Sep 2026  
**Purpose:** Clear handoff of what is built, how systems connect today, and what still must connect for Host embed + streaming (LiveKit).

This document describes the **Dashboard** product only. It is **not** the Desktop Companion app (separate product, same avatar assets).

---

## 1. Executive status

| Layer | Status |
|-------|--------|
| WebSocket v2 + HTTP auth | **Complete** |
| Avatar character (`Anim model_v008`) + body behaviors | **Complete** |
| Facial expressions (CC blendshapes from latest expression pack) | **Complete** |
| Local TTS + lip-sync (interim) | **Interim** — wired, still Unity-owned polish; **not** the product voice |
| Backend speech/TTS contract (`speech.audio`) | **Wired in Unity** — waiting on Lucas/Render to emit the event |
| Host embed / production delivery | **Pending product decision** |
| LiveKit / stream publish | **Next phase** |

**Unity estimate for current-phase handoff (test, polish, docs):** ~2–3 working days.  
**Backend speech/TTS full cutover:** depends on LLM Dev providing live events/API for joint testing.

---

## 2. System map (who owns what)

```
┌─────────────────────┐     ┌──────────────────────┐     ┌─────────────────────┐
│  Next.js Host UI    │     │  Synth Cohost API    │     │  Unity Dashboard    │
│  (creator controls) │────▶│  REST + WebSocket v2 │◀───▶│  AI co-host avatar  │
│  Viewer / Meeting   │     │  Auth · LLM · state  │     │  (this repository)  │
└─────────────────────┘     └──────────┬───────────┘     └──────────┬──────────┘
                                       │                              │
                                       ▼                              ▼
                              ┌─────────────────┐           ┌─────────────────┐
                              │  LLM / TTS      │           │  LiveKit        │
                              │  (backend)      │           │  (next phase)   │
                              └─────────────────┘           └─────────────────┘
```

| Piece | Owner | Role |
|-------|--------|------|
| Host / Viewer / Meeting UIs | Next.js | Creator & audience UI, stream controls |
| Auth, session, LLM, avatar state | Backend | REST + `wss` v2 |
| Avatar, animation, expressions, mouth, WS client | **Unity (this repo)** | Live AI co-host |
| Putting co-host into the broadcast | LiveKit (shared) | Next phase |

---

## 3. Current live data flow

### 3.1 Connect & auth

1. Unity loads credentials (live-test panel / env / gitignored local JSON — **not** committed secrets).
2. Optional REST: `login` / `refresh` / `logout` (`https://synth-cohost-app-bzi4.onrender.com`).
3. WebSocket connect: `wss://synth-cohost-app-bzi4.onrender.com/ws`.
4. First frame: `auth` → session becomes provisionally **Ready** (deployed v2).
5. Heartbeat ~20s; access-token renewal in background; quiet reconnect on rotation.

### 3.2 User turn → LLM → avatar

```
Host / live-test STT text
        │
        ▼
  stt.partial / stt.final   ──────────────►  Backend
        │
        ▼
  avatar.state { behavior }  ◄──────────────  Backend
        │
        ▼
  DashboardAvatarPresenter
  · Map behavior → Animator body clip
  · Face overlay (expression curves / heuristics)
  · Send state.ack { behavior } on successful apply
        │
        ▼
  ai.response { text, emotion, intent, behavior? }  ◄──  Backend / LLM
        │
        ▼
  DashboardAvatarPresenter
  · Captions (emotion · intent)
  · Emotion face (e.g. smile)
  · Wait ~10s for speech.audio (do not start Windows TTS yet)
        │
        ▼
  speech.audio { inline MP3/PCM, frames[].t/o, seq, final_packet }  ◄──  Backend TTS
        │
        ▼
  · Play backend audio
  · Map mouth openness o (0–1) → mouth_open_M
  · Local Windows TTS only if speech.failed or wait timeout
  · Return to living idle when speech ends
```

### 3.3 Wire behaviors (confirmed reaching presenter)

| `behavior` value | Role |
|------------------|------|
| `idle` | Living idle / relaxed variation |
| `listening` | Listening pose |
| `thinking` | Thinking pose |
| `speaking` | Present-to-camera body + talking mouth |
| `happy` | Short happy burst → local idle |
| `celebrate` | Short celebrate burst → local idle |

**Confirmed for LLM Dev:** inbound `avatar.state.behavior` is applied by `DashboardAvatarPresenter` and acknowledged with `state.ack` when apply succeeds.

### 3.4 Face & mouth (what Unity does today)

| Concern | Source | Unity handling |
|---------|--------|----------------|
| Body motion | Animator clips (`AvatarVerify.controller`) | Direct state play / CrossFade |
| Facial expressions (smile, blink, sad, yawn, …) | Latest expression FBX/JSON pack | Baked to CC blendshape **face curve** assets; played with Animator state |
| Lip-sync while speaking (fallback) | Local Windows TTS `VisemeReached` | Mapped to CC phonemes (`aaa_M`, `ohh_M`, `iee_M`, `mbp_M`, …) only if backend speech never arrives |
| Production audio + mouth | Backend `speech.audio` | **Wired** — play inline audio; `frames[].o` (0–1) → `mouth_open_M`. Phoneme IDs are not required for this path |
| Reserved speech names | `speech.start` / `speech.viseme` / `speech.end` / `speech.chunk` | Still ignored with a canned log (not the live TTS path) |
| Final voice quality / female voice | Backend Deepgram (`aura-asteria-en`) via `speech.audio` | **Not inside `ai.response`**. Unity waits for the speech event; local SAPI is fallback only |
| `ai.response` field `response` + `behavior` | Unity | **Wired** — accepts `text` or `response`; applies `behavior` on the presenter |

**Important integration note:** Expression FBX alone does not drive this Character Creator face (Maya face joints ≠ skinned CC blendshapes). Dashboard Unity uses blendshape curves + phoneme weights on `Full_Body` / mouth meshes. Desktop Companion using the same assets may follow a different path; do not assume identical face wiring.

---

## 4. Completed components (Unity)

### 4.1 Networking & session (D0)

- Deployed WebSocket **v2** client (do not replace with draft 2.1 until backend ships it)
- Events handled: `auth`, `heartbeat`, `stt.partial`, `stt.final`, `avatar.state`, `ai.response`, `system.error`, `state.ack`, `speech.audio`, `speech.failed`
- Unknown inbound types ignored. Reserved unimplemented names (`speech.start` / `speech.viseme` / `speech.end` / `speech.chunk`) still log a canned `Ignored reserved speech.*` line. `speech.audio` is handled, not ignored.
- HTTP login / refresh / logout + background token renewal
- Live-test scene + safe diagnostics (no tokens/transcript text in logs)

### 4.2 Avatar presentation (D1)

- `DashboardAvatar` prefab / scenes: `SynthCohostLiveTest`, `SynthCohostDashboard`
- Character: **Anim model_v008**
- `DashboardAvatarPresenter` on existing `avatarController` / `aiResponseSink` slots
- Six behaviors → Animator states; living idle; keep-in-place; eye lock
- Happy / celebrate local bursts (no extra ack)
- Captions + emotion/intent
- Facial expression curve catalog (latest Drive expression pack)
- Backend `speech.audio` playback + `mouth_open_M` from mouth-openness frames; local TTS + phoneme lip-sync kept as fallback

### 4.3 Key Unity attachment points (for next developers)

| Slot / type | Implementer | Purpose |
|-------------|-------------|---------|
| `avatarController` (`IAvatarBehaviorController`) | `DashboardAvatarPresenter` | Apply `avatar.state` |
| `aiResponseSink` (`IAiResponseSink`) | `DashboardAvatarPresenter` | Apply `ai.response` (text / emotion / behavior) |
| `ISpeechAudioSink` (via same presenter) | `DashboardAvatarPresenter` | Play `speech.audio`; fallback on `speech.failed` |
| `systemErrorSink` | Adapter / panel | Surface `system.error` |
| Outbound session | Client compose | `stt.*`, `state.ack` |
| Stream camera | `DashboardStreamCameraMarker` | Marks framed camera for later LiveKit publish |

**Rule:** Do not open a second WebSocket or re-parse `avatar.state` in ad-hoc Update loops. Attach to the existing client compose.

---

## 5. Remaining work

### 5.1 Unity — current phase (handoff)

- [ ] Final regression pass (Connect → turn → speaking → idle; expressions; lip-sync)
- [ ] Cleanup / packaging notes for handoff build
- [ ] Confirm Host credential injection path (replace live-test-only secrets flow)

### 5.2 Backend / LLM dependent

- [ ] Confirm Render currently emits `speech.audio` (Lucas/João) and capture one live payload/format
- [ ] Joint test: Deepgram female TTS plays in Unity; `mouth_open_M` follows `frames[].o`
- [ ] Prefer backend `speech.failed` when TTS fails silently, so Unity does not wait the fallback timeout
- [ ] Keep WS frames ≤ 64 KB (MP3 inline is the intended format; raw PCM can exceed the cap)

### 5.3 Host integration (D2) — blocked on product

- [ ] Written decision: Windows client vs WebGL embed vs capture vs LiveKit-only
- [ ] Host injects session credentials into Unity
- [ ] If WebGL: new transport (do not assume `ClientWebSocket` ports as-is)

### 5.4 Streaming / LiveKit (D4) — next phase

- [ ] LiveKit room/token ownership & publish contract
- [ ] Unity publishes framed avatar camera (+ audio routing as specified)
- [ ] Resolution / alpha / reconnect rules vs WebSocket session
- [ ] End-to-end: Host starts room → Unity co-host visible in stream

### 5.5 Explicitly out of scope until contracted

- Draft protocol 2.1 (`session.ready`, etc.) until backend ships
- Desktop Companion features (multi-dashboard, OS notification acting, etc.)
- Owning Next.js Host/Viewer/Meeting UIs

---

## 6. Integration checklist for streaming phase

What the streaming/avatar developer should **reuse**:

1. Existing WS session + auth (do not rebuild).
2. `DashboardAvatarPresenter` as the avatar brain.
3. Same framed camera marked for stream (`DashboardStreamCameraMarker`).
4. Ignore-unknown policy for new event types until registered.

What they should **add** (not replace):

1. LiveKit publisher component reading that camera (and agreed audio).
2. Host-driven room join using backend-issued tokens.
3. Do not rebuild `speech.audio` handling in the LiveKit publisher — presenter already plays backend TTS + mouth openness.

Suggested connection sketch for next phase:

```
Backend ──WS──► Unity presenter (unchanged)
Backend ──LiveKit token──► Unity LiveKit publisher ──► Stream room
Host UI ──controls──► Backend ──avatar.state / config──► Unity
```

---

## 7. Environments

| Service | URL |
|---------|-----|
| WebSocket | `wss://synth-cohost-app-bzi4.onrender.com/ws` |
| REST | `https://synth-cohost-app-bzi4.onrender.com` |

Build target today: **Windows Standalone 64-bit** (WebGL only if Host chooses browser embed).

---

## 8. Definition of done (suggested)

**Current phase (Dashboard Unity handoff)**  
- Live Connect + LLM turn works end-to-end in Editor/live-test.  
- Behaviors, expressions, and speech path verified (backend `speech.audio` when live; local SAPI fallback).  
- This handoff doc accepted by product / next-phase owners.

**Phase complete with backend speech**  
- Backend TTS voice in use.  
- `speech.audio` drives audio + `mouth_open_M` in sync.  
- Local SAPI path retained only as fallback/dev.

**Streaming phase complete**  
- Agreed Host delivery path live.  
- Unity co-host visible/audible in LiveKit session per contract.

---

## 9. Contact points inside the repo

| Topic | Location |
|-------|----------|
| Product architecture | `README.md` |
| Dashboard requirements | `Assets/Docs/plans/DASHBOARD-requirements.md` |
| Remaining checklist | `Assets/Docs/plans/DASHBOARD-leftover.md` |
| Dashboard vs Companion split | `Assets/Docs/plans/FINDINGS-two-projects.md` |
| Runtime client | `Assets/SynthCohost/Runtime/` |
| Avatar presenter | `Assets/SynthCohost/Runtime/Features/Avatar/DashboardAvatarPresenter.cs` |
| Live-test scene | `Assets/Scenes/SynthCohostLiveTest.unity` |
| Dashboard scene | `Assets/Scenes/SynthCohostDashboard.unity` |

---

*Prepared for handoff into Host integration and streaming/avatar work. Update this file when backend `speech.*` and LiveKit contracts are locked.*
