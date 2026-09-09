# Findings: Dashboard vs Desktop Companion

**Date:** 3 September 2026  
**Purpose:** Separate the two products that the original docs treated as one Unity job.  
**Override:** The client later said the **current Unity implementation is for dashboard** (that work came up later). **Desktop companion requirements are a different project.**

Related write-ups:

- [DASHBOARD-requirements.md](./DASHBOARD-requirements.md) — what dashboard Unity must do
- [DASHBOARD-leftover.md](./DASHBOARD-leftover.md) — what is still open on dashboard, including WebSocket
- [DESKTOP-COMPANION-plan.md](./DESKTOP-COMPANION-plan.md) — companion plan and todos

---

## 1. What the client said, and what that means

The signed agreement and the early specs describe **one** Synth Cohost ecosystem: AI, avatars, desktop companion, streaming, dashboard/control studio, WebSockets.

Later product direction (Host / Viewer / Meeting + LiveKit) and the later client line split that into two Unity jobs:

| Project | What it is | This GitHub repo |
|---------|------------|------------------|
| **Dashboard AI co-host** | Unity avatar that talks to the **cloud** backend and plugs into the **Next.js Host** dashboard, then later into LiveKit | **Yes — current implementation** |
| **Desktop companion** | Desktop Mate-style character that lives on the OS desktop, with **Tauri/Rust** as the shell | **No — different project** |

Do not implement companion IPC, desktop physics, window-sitting, or the 63-clip desktop pack **in this repo** unless the client re-opens that as in-scope here.

---

## 2. Documents reviewed

All source files live in `Assets/Docs/`. Repo status notes used as well: root `README.md`, `UNITY_WEBSOCKET_IMPLEMENTATION_PLAN.md`, `UNITY_LIVE_TEST_GUIDE.md`.

| File | Belongs to | What it actually specifies |
|------|------------|----------------------------|
| `Agreement Unity Dev_signed.docx` | Legal umbrella for both | Appendix A lists **both** dashboard/streaming **and** desktop companion as possible Unity work. It is a capability menu, not a milestone. |
| `Companion Systems Specification.Synth Cohost.docx` | **Companion** | Full Tauri + runtime + behavior + asset platform. Table of contents lists MVP Scope, Future Roadmap, Technical Constraints, Acceptance Criteria — **those sections were never written in the body.** |
| `Unity Architecture _ Responsibility Specification (1).docx` | **Companion** | Strict Unity vs Tauri split. Unity deliverable: independent character viewport that receives OS facts over IPC. |
| `Animation DEV (2) (1) (1) (1).docx` | **Companion** (animator input) | 63 body clips, 9 faces, 7 eye states. Desktop Mate interaction, readable at 5–15% screen height. |
| `8 remaining animations (1) (1).docx` | **Companion first** | Shoulder/head/climb/object/pickup. Footnote: same clips can later attach to a live-stream person/object via computer vision. |
| `websocket-protocol-spec.docx` | **Dashboard** | Unity ↔ **cloud** backend. Live wire is integer `v: 2`. Spec also drafts 2.1 (`session.ready`, `protocol_version` string) which is **not live**. |
| `Letter for Unity Side.docx` | **Dashboard direction** | Conversational body language (listening / acknowledging / thinking / speaking) to hide LLM latency. Explicitly **not** an immediate build. |

---

## 3. How the two products differ

Sharing a character **rig** later is possible. Sharing the **current WebSocket client** is not. The two sockets are different contracts with different owners.

| Axis | Dashboard AI co-host (this repo) | Desktop companion (other project) |
|------|----------------------------------|-----------------------------------|
| Product job | Live AI co-host inside Host / stream | Always-on desktop character that feels alive |
| User | Creator on Host; audience via Next.js | Person at their PC; character sits on windows / taskbar |
| Unity talks to | Cloud: `wss://…/ws` + HTTPS auth | Local Tauri/Rust IPC (local WebSocket, different message types) |
| Shell owner | Next.js Host / Viewer / Meeting UIs | Tauri: transparent windows, always-on-top, click-through, tray |
| OS facts | Not needed for the current WS milestone | Tauri: mouse, monitors, windows, taskbar, icons |
| Unity must not do | Build the three Next.js UIs, OBS, backend AI | Read the OS directly, own settings UI, package the installer |
| Primary states | `idle`, `listening`, `thinking`, `speaking`, `happy`, `celebrate` | Idle / sleep / drag, sit, peek, wander, chase cursor |
| AI loop | `stt.final` → `avatar.state` → `ai.response` (live today) | Offline companion still runs; cloud AI is future/sync |
| Animation brief | Conversation presence + later visemes | 63+8 desktop clips, small-scale, pickup / sit / climb |
| Offline | Needs backend for the AI turn | Companion keeps running; no AI / no sync while offline |
| Code in this repo today | WS + HTTP client + live-test harness | None of the companion runtime / IPC / desktop physics |

### 3.1 Two different WebSockets

**Dashboard (already built here)**

- Envelope: integer `v: 2`, client `session_id`, RFC3339 `ts`, `payload`
- Client → server: `auth`, `heartbeat`, `stt.partial`, `stt.final`, `state.ack`
- Server → client: `avatar.state`, `ai.response`, `system.error`
- Auth: short-lived JWT + tenant `avatar_id`
- Close codes: 4000–4004

**Companion IPC (not built; not this protocol)**

- Transport: local WebSocket between Unity and Tauri
- Tauri → Unity examples: `idle`, `follow_cursor`, `look_at`, `react`, `play_animation`, `update_context`, `mouse_position`, `active_window_changed`, `screen_geometry`
- Unity → Tauri examples: `character_clicked`, `interaction_triggered`, `animation_finished`, `state_changed`, `user_action`

Do not extend `Assets/SynthCohost` cloud protocol code to speak companion IPC. If companion is a new repo, start a new Unity runtime there.

### 3.2 Shared assets vs shared product

The eight extra clips (shoulder sit, head climb, vertical climb, mantle, lean on keyboard/mug, sit on mug, mouse pickup) are written as **companion** animations that can later attach to a detected person or object **in a live stream**.

That reuse is a possible **streaming overlay** feature. It is not proof that companion and dashboard are one Unity milestone. Ownership of computer-vision overlay is still unnamed (see §6).

---

## 4. What each spec promised Unity (original mixed contract)

The agreement Appendix A listed all of this under one engagement. After the client split, map it as follows.

| Appendix A item | After the split |
|-----------------|-----------------|
| Unity avatar runtime (load, face, body, state) | **Both**, but different state machines |
| Desktop companion (presence, idle, interactions, movement) | **Companion only** |
| Avatar behaviour integration (AI outputs, emotion, events) | **Dashboard** uses cloud `avatar.state` / `ai.response`; companion uses Tauri + local behavior engine |
| Animation trigger systems | Both, different triggers |
| Behaviour engine JSON / personality | Companion local; dashboard waits on dashboard-config schema |
| WebSocket and API integration | **Dashboard cloud WS + HTTP** (done). Companion uses **local** IPC, not this client |
| Streaming platform integration | **Dashboard** (LiveKit later) |
| Dashboard and Control Studio integration | **Dashboard** (no schema yet) |
| Avatar positioning / environment | **Companion** (desktop). Dashboard is a Host/stream viewport, not desktop physics |

---

## 5. Current repo reality

This repository is already a **dashboard networking client**, not a companion app and not a finished avatar product.

**Shipped**

- Deployed WebSocket v2 client
- HTTP login / refresh / logout and background access-token renewal
- Live-test scene and runtime credential panel
- Feature adapters that **record** `avatar.state` and send `state.ack` — they do **not** animate a character
- Live authenticated text path proven (July 2026)

**Not in this repo**

- Desktop Mate window / click-through / tray
- Tauri IPC
- Desktop navigation, sitting on windows, pickup/drag as a product
- Character package format / marketplace
- The 63-clip or 8-clip animation integration (rig still being exported on the client side)

---

## 6. Still unclear — ask the client before more bidding

These are the gaps that still mix the two projects or block a clean close-out.

1. **Confirm this GitHub Unity repo is dashboard-only going forward.**  
   Companion IPC in this tree would mix two protocols.

2. **How does Host display the Unity avatar?**  
   WebGL embed vs Windows client vs capture vs LiveKit-only. This chooses the build target. Do not guess.

3. **Is live-stream computer vision (sit on streamer / mug / keyboard) dashboard, companion, or a third slice?**  
   The 8-clip note and earlier CV chat attach it to streaming; the later client line moved companion out. Do not start without a named owner.

4. **Dashboard config JSON** (settings, personality, controls).  
   The agreement lists it. There is no schema in `Assets/Docs`.

5. **LiveKit and `speech.*` contracts.**  
   README lists them as next. The protocol spec says speech is unwired. Implement only after backend sign-off.

6. **Companion first milestone.**  
   The architecture spec is full vision. Animation DEV is 63 clips. The companion spec never wrote MVP or acceptance. Do not accept “build the companion spec” against a date.

---

## 7. Recommended bid language

**Dashboard (this repo)**  
Keep and harden the deployed v2 + HTTP client. Wire a real avatar to existing `avatar.state` / `ai.response` when the rig ships. Host plug-in after they name embed vs stream. LiveKit when they provide a contract. Do not quote desktop sitting, Tauri IPC, or the 63-clip pack against this repo.

**Companion (other project)**  
Unity slice only: character viewport exe, versioned IPC API with Tauri, integrate animation pack (pickup, idle, cursor, sit/peek, sleep first), desktop behavior runtime that **consumes** Tauri OS facts. Window chrome, tray, click-through, and installers are Tauri. Require a written MVP before estimating.

---

## 8. Source-of-truth order (when docs conflict)

1. Later client product lines (dashboard vs companion split; Host / Viewer / Meeting / LiveKit).
2. Live backend confirmations for **deployed** WebSocket v2 (integer `v`, client session id, no `session.ready`).
3. `websocket-protocol-spec.docx` — use §9 to tell live vs draft 2.1.
4. `Unity Architecture _ Responsibility Specification` for companion Unity vs Tauri.
5. Companion Systems Specification for companion platform vision — not for a dated MVP.
6. Agreement Appendix A — capability list only.
7. Implementation plan Markdown — execution history; does not override product split.
