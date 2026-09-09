# Desktop companion — Unity plan, implementation, edge cases, deliverables

**Project:** Desktop companion (different project from this dashboard repo)  
**Do not implement in the current Synth Cohost Unity cloud-client tree** unless the client re-scopes it here.  
**Product split:** [FINDINGS-two-projects.md](./FINDINGS-two-projects.md)  
**Not this project:** Dashboard AI co-host — [DASHBOARD-requirements.md](./DASHBOARD-requirements.md)

This document is the companion source of truth for **what Unity must do**, **how to implement it**, **what is still missing**, **edge cases**, and **final deliverables**. Preferred IPC is a **local** WebSocket. That is **not** the cloud `v: 2` client in `Assets/SynthCohost`. Do not share DTOs, session ids, JWT auth, or `avatar.state` with dashboard.

Primary specs:

- `Unity Architecture _ Responsibility Specification (1).docx` — Unity vs Tauri; **only document with a concrete Unity ship sentence**
- `Companion Systems Specification.Synth Cohost.docx` — platform vision (MVP / acceptance sections **listed in the TOC and never written**)
- `Animation DEV (2) (1) (1) (1).docx` — animator pack
- `8 remaining animations (1) (1).docx` — extra locomotion / object / pickup clips

---

## 0. Gate: do not start billed v1 until these are written down

Ask the client, in writing:

- [ ] Confirm companion is a **separate** Unity + Tauri project (not this dashboard repo)
- [ ] First-milestone date and what “done” means (viewport + IPC + N clips, not the whole spec)
- [ ] Who owns Tauri (window, tray, click-through, OS facts, installer)
- [ ] Frozen IPC schema v0 (envelope, version, who is WS server, port, auth)
- [ ] **Window model:** one Unity overlay per monitor vs one virtual-desktop overlay vs moving a small window with the character (see §10)
- [ ] **Hit-test / click-through contract** (see §10)
- [ ] Coordinate space + DPI (desktop pixels vs Unity world)
- [ ] Which animation clips are in v1 (see §5 priority)
- [ ] Humanoid-ready FBX + metadata sheet
- [ ] Target OS for v1 (spec wants Windows, Linux, macOS; Unity viewport must still run independently)

Until those exist, estimate only **C0–C2**, not “the companion spec.”

---

## 1. Product definition (clear)

Synth Cohost desktop companion is a **persistent living desktop entity** (“Syn”). The character is the product. Settings windows and future AI are supporting systems.

It is **not**: a widget, a Tauri-wrapped web app, an animation viewer, or a launcher.

It **is**: a desktop companion engine + behavior system + (later) AI runtime + character platform.

### Unity vs Tauri (non-negotiable)

| Unity (this plan) | Tauri / Rust (not Unity) |
|-------------------|---------------------------|
| 2D/3D character rendering | Transparent frameless window, always-on-top, click-through |
| Animation, motion, VFX, expressions | System tray, autostart, settings UI, login/update windows |
| Character behavior, interactions, look-at | App lifecycle, persistence, packaging (Win/Linux/macOS) |
| Click / drag / animation-finished events | Global mouse, monitors, active window, taskbar, icon positions |
| Independent character **viewport exe** | IPC bridge, account sync, news/API, OS secure token storage |

Unity **must not** query the OS. Tauri gathers OS facts and sends messages. Unity interprets them visually.

**Clear Unity deliverable (Architecture spec):**

> The final Unity executable should be capable of running independently as a character viewport/runtime while accepting external commands through the agreed communication layer.

Preferred IPC: **local WebSocket**. Keep both apps decoupled. Alternative IPC later is allowed if the **message** design stays.

---

## 2. What Unity is not doing (companion)

Leave these to Tauri or to later companion phases — do not put them in a first Unity bid as if they were avatar work:

- Window chrome, click-through, tray, installer, autostart
- Settings / Studio / Login / Update UI (React in Tauri)
- Direct filesystem, monitor enumeration, global hooks
- Cloud JWT to `synth-cohost-app-bzi4.onrender.com` as the **companion runtime bus** (that is the **dashboard** client)
- Marketplace, plugin sandbox, multi-Syn teams, voice cloning
- Real collision vs windows (spec: **MVP = no collisions**)
- Multi-monitor **traversal** (spec MVP: store current monitor only)
- Window-as-platform sitting as a **future** navigation layer (spec labels window awareness Future)
- AI behavior tree execution (spec: pipeline only, “not going to be implemented now”)

---

## 3. How this will be implemented

There is **no companion runtime in this repo today**. Implementation is a **new Unity project** (or a new assembly that cannot reference `SynthCohost.Protocol` / cloud session types).

### 3.1 Layering (mandatory separation)

Companion spec: OS event → Interaction → Behavior engine → Animation controller. Runtime **never** plays clips directly. AI (later) **never** plays clips directly.

```
Tauri / fake stub
        │  local WebSocket (companion IPC, not cloud v2)
        ▼
Companion.Ipc          envelope, version, typed messages, ignore unknown type
Companion.Transport    local WS client (or server — freeze in schema)
        ▼
IpcRouter              marshal to Unity main thread
        ▼
Event bus              all systems subscribe; no Settings→Animator shortcuts
        ▼
┌───────────────┬───────────────┬────────────────┬──────────────┐
│ Interaction   │ Behavior      │ Navigation     │ Animation    │
│ (zones, click,│ (queues,      │ (monitor rect, │ (layers,     │
│  drag, dwell) │  cooldowns,   │  wander, edges)│  blend,      │
│               │  priorities)  │                │  registry)   │
└───────────────┴───────────────┴────────────────┴──────────────┘
        ▼
SynRuntime             one instance: id, state, pose, emotion, monitor, x/y
        ▼
Character presenter    Humanoid + look-at IK + face layers
```

**Do not** call `Animator.Play` from the IPC handler. IPC sets a **command** or **context**; behavior/animation modules consume it.

Suggested assemblies (names can change):

| Assembly | Allowed to know |
|----------|-----------------|
| `Companion.Ipc` | JSON DTOs only — no `MonoBehaviour`, no Animator |
| `Companion.Transport` | Local socket — no Animator, no behavior trees |
| `Companion.Runtime` | Session of the Syn, event bus, IPC routing |
| `Companion.Presentation` | Animator, IK, VFX, camera |
| Tests | Fake transport + fake clock |

Dashboard `Assets/SynthCohost` stays out of this graph. If companion later talks to **cloud** AI, that is a **second** client (dashboard protocol), not a merge of IPC and `avatar.state`.

### 3.2 Data flow

```
Tauri: mouse / windows / tray / settings
    → IPC command or context frame
        → InteractionSystem (zones, drag threshold, click vs drag)
            → BehaviorEngine (priority, cooldown, queue, interrupt)
                → NavigationSystem (optional move request)
                → AnimationController (clip id from registry)
                    → Presenter (Animator / IK)
    ← IPC events: character_clicked, animation_finished (non-loop),
                  state_changed, hit_test / bounds (if required)
```

60 fps loop (companion spec):

1. Drain IPC (already marshalled).
2. Advance interaction (dwell timers, drag).
3. Evaluate state + behavior (cooldowns, autonomy clocks on **unscaled** time).
4. Evaluate navigation.
5. Evaluate animation.
6. Render.

Autonomy and sleep **must not** use `Time.timeScale`. Use realtime/unscaled clocks so a paused game-like scale cannot freeze the companion.

### 3.3 IPC implementation (proposed until schema is frozen)

Architecture spec examples are **unversioned fragments**. Implement a real envelope from day one so Tauri and Unity can evolve:

```json
{
  "v": 1,
  "type": "mouse_position",
  "ts": "<rfc3339>",
  "payload": { "x": 1450, "y": 620 }
}
```

Rules to freeze with Tauri:

- Integer or string version — pick one; **do not** copy dashboard `v: 2` cloud semantics.
- Unknown `type` → ignore (same idea as dashboard, different catalog).
- Malformed payload → diagnostic + ignore; **do not** kill the character runtime.
- Who hosts the socket (recommend: **Tauri listens on localhost**, Unity connects; easier for Unity-as-child).
- Port / named pipe fallback; no cloud host.
- Optional localhost token so other apps cannot drive the avatar.
- **Coalesce** `mouse_position` (do not apply 200 Hz raw moves to Animator). Cap IPC rate.

**Receive (Tauri → Unity)** — Architecture spec + companion runtime commands:

| type | payload (proposed) | Unity |
|------|-------------------|--------|
| `idle` | `{}` | Request idle behavior |
| `follow_cursor` | `{ enabled? }` | Follow-cursor behavior |
| `look_at` | `{ x, y }` | Look-at in **agreed** coordinates |
| `react` | `{ emotion }` | Emotion layer; not a body clip by itself |
| `play_animation` | `{ animation }` | User/Tauri command — high priority vs autonomous |
| `update_context` | **unspecified** — freeze | Awareness/context object |
| `mouse_position` | `{ x, y }` | Cursor awareness; coalesced |
| `mouse_button` | `{ button, down, x, y }` if Tauri owns input | Click/drag pipeline |
| `active_window_changed` | `{ window }` | Context; v1 may only log/react lightly |
| `screen_geometry` | monitors, bounds, work area, DPI | Navigation + scale |
| `spawn_syn` / `despawn_syn` | package id, monitor, pose | Runtime lifecycle (companion spec Rust commands) |
| `pause_syn` / `resume_syn` / `hide_syn` | `{}` | Pause autonomy; hide = Tauri may also hide window |
| `move_syn` / `set_monitor` | `{ x, y }` / `{ monitor }` | Navigation / spawn |

**Emit (Unity → Tauri)**:

| type | when |
|------|------|
| `character_clicked` | Left click hit on character (after click-vs-drag) |
| `character_right_clicked` | Right click — Tauri opens menu (Unity does **not** draw Settings) |
| `interaction_triggered` | Named interaction |
| `animation_finished` | **Non-looping** clips only |
| `state_changed` | Runtime state enum changed |
| `user_action` | Generic |
| `hit_bounds` / `pointer_over` | If Tauri needs click-through holes (recommended) |
| `runtime_ready` / `runtime_fault` | Startup and unrecoverable-then-recovered |
| Later: `destination_reached`, `navigation_failed`, `monitor_changed` |

`play_animation` from Tauri **overrides** autonomous idle but **loses** to `being_dragged`. Looping idle must **not** emit `animation_finished` every cycle.

### 3.4 Coordinates, camera, scale

Missing from the client specs — **implement only after freeze**. Recommended default to propose:

- Tauri sends **desktop pixels** (origin top-left or bottom-left — **must name it**), per-monitor geometry, DPI scale.
- Unity uses an **orthographic** camera mapped to that monitor’s work area (character readable at **5–15% of screen height**).
- SynRuntime stores `monitor` + `{ x, y }` in **desktop space**; presenter converts to Unity world.
- Locomotion in v1: move the **character transform** inside a transparent overlay that **covers the monitor**. Do not implement “tiny window that follows the character” unless they choose that window model (different math, more Tauri).

Root-motion clips: apply only when Navigation requested a move; idle/emotes are **in-place**.

### 3.5 Hit-testing and click-through (implementation-critical)

Desktop Mate-style apps are click-through **except** on the character. Specs split input: Tauri captures mouse; Unity emits `character_clicked`. Both can be true if:

1. Tauri sends global `mouse_position` always (look-at, zones).
2. Unity reports **character screen AABB / mesh hit** each frame or on move (`hit_bounds`).
3. Tauri toggles click-through when the cursor is over that region, **or** Unity’s window receives WM_NCHITTEST-style passthrough (platform-specific — **Tauri-owned**).

Unity still:

- Converts pointer to character-local hit.
- Distinguishes click vs drag (pixel threshold + time).
- While `being_dragged`, position follows cursor; Tauri may move overlay or Unity moves the mesh — **one owner**.

Do not implement both “Unity Input System on a full-screen overlay” and “Tauri-only mouse” without the freeze. Pick one input authority in C0.

### 3.6 Animation controller implementation

- Clip **registry JSON** (id → clip, loop flag, priority, fallback id). Never `animator.Play("Wave")` in behavior code.
- Layers: body / emotion / face (blink independent).
- Priorities: emergency (wake, drop) > user `play_animation` / click > conversation (later) > autonomous > idle.
- Interrupt: sleep + click → wake clip then greeting; cancel sleep loop.
- Missing clip → fallback `idle`; emit diagnostic; **do not** crash; **do not** lie `animation_finished` for a clip that never played.
- `SetTrigger` queue problem (same as dashboard): reset triggers; last command wins unless `being_dragged`.

### 3.7 Behavior + navigation implementation

- States bias selection; they do **not** call Animator.
- `being_dragged` hard override.
- Cooldowns (wave 30s) and last-20 history.
- Wander v1: random point **inside work-area rect** (no collisions, no window meshes).
- Edges: if pose would leave work area, clamp or peek behavior; never lose the character.
- Fullscreen context (`update_context` / Tauri): spec suggests `move_to_edge` — v1 can idle at edge; hide is Tauri.

### 3.8 Packages (C7, not required to demo one Syn)

Tauri Asset service (companion spec) validates and supplies files. Unity loads via a path Tauri sends (`load_package`), not by scanning the OS itself. StreamingAssets is OK for the **dev stub** only.

### 3.9 Tests without a real desktop

Fake Tauri (tiny WS server) is part of C0:

- Inject `mouse_position`, `play_animation`, synthetic click.
- Assert `animation_finished`, `character_clicked`, state recover to idle.
- Fake clock for sleep timers (10 min wait must not be a real 10 min in tests).

---

## 4. Phased Unity plan (maps to C0–C8)

### Phase 0 — Contract and assets → **C0**

- [ ] Separate companion Unity project / package. Do not fold into `Assets/SynthCohost`.
- [ ] Written IPC schema v0
- [ ] Written v1 clip list
- [ ] Rig + FBX + metadata sheet
- [ ] Fake Tauri stub
- [ ] Window model + hit-test + coordinate freeze

**Exit:** Unity prints parsed IPC; stub round-trip works.

### Phase 1 — Viewport bootstrap → **C1**

- [ ] Transparent-capable camera / character-only scene (no settings UI)
- [ ] One Humanoid, ground-aligned, 5–15% screen height
- [ ] Runtime object: id, state, behavior, animation, emotion, monitor, x/y
- [ ] 60 fps loop as in §3.2
- [ ] Recover to **Idle** on any error
- [ ] Spawn animation then idle; despawn state payload **to Tauri**, not OS files

**Exit:** Character stands in a frameless-looking viewport with a looping idle.

### Phase 2 — IPC API → **C2**

Implement against frozen names. Until frozen, use §3.3 tables.

- [ ] Ignore unknown `type`
- [ ] Never call OS APIs
- [ ] Viewport still runs if Tauri is down (last pose / idle), then resync
- [ ] Mouse coalescing

**Exit:** Stub plays `wave` and sees `animation_finished`; mouse turns the head.

### Phase 3 — Animation controller → **C3**

- [ ] Layers, queue, interrupt, registry, fallback
- [ ] Import signed v1 clips (Humanoid, consistent axis)

**Exit:** `play_animation` and idle variants blend; click can interrupt sleep.

### Phase 4 — Interaction → **C4**

Animation DEV priority: **Mouse Pickup, Idle, Cursor Interaction, Desktop Navigation, Sleep.**

**Cursor**

- [ ] Zone A 0–100px: `look_at_cursor`
- [ ] Zone B 100–250px: weighted observe / head tilt / wave (cooldowns)
- [ ] Zone C: ignore
- [ ] Hover dwell (~3s) before optional wave
- [ ] Eyes lead head 2–6 frames

**Click**

- [ ] Left click → greeting pool with cooldown
- [ ] Right click → emit only (Tauri menu)
- [ ] Double click → emit only

**Drag / pickup**

- [ ] `pickup_start` / `dragging` / `release`
- [ ] Pickup pose per 8-clip spec (waist/back lift, hanging limbs)
- [ ] `being_dragged` overrides all
- [ ] Release: drop / land / recovery / idle

**Exit:** Look-at, wave, pick up, drag, drop via stub messages.

### Phase 5 — Idle, sleep, autonomy → **C5**

- [ ] Idle A/B/C distinct loops
- [ ] 0–30s / 30s / 5 min / 10 min pools
- [ ] Sleep start → loop → any input wakes → wake → stretch → idle
- [ ] Cooldowns, history, autonomous vs scheduled
- [ ] MVP: no collisions; store current monitor only; window platforms future

**Exit:** Left alone, breathes, fidgets, sleeps, wakes on interaction.

### Phase 6 — Screen-edge + monitor-rect navigation → **C6**

- [ ] Peek, hang, pull-up, sit edge, dangle, walk/run/turn/jump/land as **signed clips allow**
- [ ] Follow-cursor locomotion: keep distance, never overlap cursor
- [ ] Nav failure → nearest valid pose → idle

Defer unless in signed v1: window platforms, icon pathfinding, monitor portals, crawl upside-down, carry objects.

**Exit:** Walks the **work-area rect** Tauri sent; peeks edges; sits a corner.

### Phase 7 — Character package → **C7** (v1.1 unless they require it in v1)

- [ ] Package layout, validation, unique ids, fallbacks, swap without Unity rebuild

### Phase 8 — Later → **C8** (do not bid inside v1)

- Voice runtime, awareness layer, multi-Syn, plugins, marketplace, collisions, window-as-platform, cloud AI as a **second** client

Letter conversational listening is **dashboard** first. Reuse animation philosophy when companion voice lands; do not block C1–C6 on it.

---

## 5. Animation integration (inputs Unity consumes)

Animator delivers clips; Unity integrates. If a movement is unclear, **request a visual reference** — the spec forbids guessing.

Polish first: (1) mouse pickup (2) idles (3) cursor (4) desktop navigation (5) sleep.

Export: separate FBX per clip, 30 or 60 fps, clean start/end, metadata (loop, transition in/out, root-motion vs in-place).

### 5.1 Eight remaining clips

Shoulder mount, head climb, vertical climb loop, object mantle, lean keyboard, lean mug, sit mug, pickup pose.

Animate **as if** the surface exists. Attach at runtime to **anchors Tauri sends** (schema missing — §10). Do not put a person mesh in the clip.

Live-stream CV overlay is **not** automatic companion v1. Same clips may later attach to CV rects if product assigns that slice.

---

## 6. Behavior / state (Unity runtime)

**States** (do not play clips): `idle`, `active`, `focused`, `curious`, `reacting`, `sleeping`, `being_dragged`.  
Override: `being_dragged` wins. Priority: dragged > reacting > focused > active > idle > sleeping.

**Behavior engine:** reactive / autonomous / scheduled (AI later); emergency > user > conversation > reaction > autonomous > idle; cooldowns; current/next/fallback; interrupt sleep; last ~20 history; AI stub path only.

---

## 7. Mapping: do not confuse with dashboard

| If someone asks for… | Put it on… |
|----------------------|------------|
| Cloud `auth` / `stt.final` / `avatar.state` | Dashboard |
| Host embed / LiveKit | Dashboard |
| Local `play_animation` / `mouse_position` | **This plan** |
| Sitting on Chrome / riding a window | Companion, after Tauri window geometry, likely **C8** |
| Shoulder sit on a **streamer in OBS/camera** | Unnamed (ask) |
| Letter: listening while LLM thinks | Dashboard; later companion voice |

---

## 8. First week of work (once gates pass)

1. New companion Unity project; **zero** references to dashboard protocol types.
2. Local WS stub (fake Tauri).
3. One idle + one wave + look-at + pickup placeholder on a temp Humanoid.
4. Tests: click → `character_clicked`; `play_animation` → `animation_finished`.
5. Clip registry JSON; import real clips as they arrive.

Do not wait for all 63 clips to prove the IPC viewport.

---

## 9. What is missing (docs + product)

Companion TOC promised MVP Scope, Future Roadmap, Technical Constraints, Acceptance Criteria — **absent**. The following must be frozen or explicitly deferred.

### 9.1 Product / IPC gaps

| Gap | Why it blocks | Until then |
|-----|----------------|------------|
| Who is local WS **server**, port, localhost auth | Unity cannot connect | Stub on a documented port in C0 |
| Envelope version + correlation ids | Settings vs animation race | Use §3.3 proposal |
| `update_context` payload | Awareness and fullscreen | Ignore extra fields; v1 may only read `fullscreen` if present |
| Window model (overlay vs tiny moving window) | All navigation math | Propose monitor overlay |
| Hit-test / click-through owner | Click and pickup cannot ship | Propose Unity `hit_bounds` + Tauri passthrough |
| Coordinate origin + Y-down vs Y-up + DPI | Look-at and walk will be wrong | Tests with stub geometry |
| Input authority (Tauri mouse_button vs Unity raycast) | Double clicks / missed drags | One owner in C0 |
| `spawn_syn` / pause / hide / switch Syn on IPC | Tray menu in companion spec | Add to schema; Unity implements pause/hide flags |
| Anchor schema for 8 clips (mug, keyboard, shoulder) | Cannot attach mantle/sit | Rect `{ x, y, w, h, kind }` from Tauri |
| Emotion vocabulary vs dashboard `AiEmotion` | `react` payload | Companion-local enum; do not import dashboard protocol |
| 2D vs 3D / ortho vs perspective | Camera and scale | Recommend ortho 3D Humanoid |
| Settings live-reload payload | Companion spec live settings | Tauri sends `settings_changed`; Unity applies scale/opacity **if** those are Unity-side (opacity may be Tauri window) |
| Persistence | Save x/y on shutdown | Unity emits state; **Tauri writes disk** |
| Multi-monitor v1 | Spec MVP = store current only | No hop; if monitor unplugged, Tauri sends new geometry |
| Linux/macOS window quirks | Packaging | Unity still independent; Tauri absorbs |

### 9.2 Implementation gaps (not in current repo)

- No companion assemblies, scene, or stub.
- No clip registry.
- No fake clock for 10-minute sleep tests.
- Dashboard `AnimatorAvatarBehaviorAdapter` is **six cloud behaviors** — **do not reuse** for companion clip ids (`wave`, `sleep_loop`, pickup).

### 9.3 Animation / acting gaps

- Rig still incomplete on the client side (export blocked historically).
- 63 clips vs a dated milestone — need a **signed v1 subset**.
- Secondary motion (hair/skirt) bone-driven — confirm rig has those bones.
- Loop construction (no identical start/end) is animator quality; Unity must still wrap with blend trees.

---

## 10. Edge cases

### 10.1 IPC and lifecycle

| Case | Required behavior |
|------|-------------------|
| Tauri not running | Unity idles locally; retry connect with backoff; no crash; no OS scrape |
| Tauri restart | New IPC session; resync geometry + pose; do not duplicate Syn |
| Malformed JSON | Ignore; stay idle |
| Unknown `type` | Ignore |
| `mouse_position` flood | Coalesce to render rate |
| `play_animation` while dragging | Ignore or queue until drop (drag wins) |
| `play_animation` unknown id | Fallback idle; no fake `animation_finished` |
| Burst of commands | Queue with priority; drop stale look-at |
| Pause from tray | Freeze autonomy and sleep clocks; keep last pose; still allow resume IPC |
| Hide | Tauri may hide window; Unity pause autonomy |
| Switch Syn | Despawn/save A; spawn B; no two actives in v1 |
| Stub vs real Tauri | Same IPC; swap transport host only |

### 10.2 Character / animation

| Case | Required behavior |
|------|-------------------|
| Missing idle clip | Built-in T-pose only as last resort; diagnostic; still accept IPC |
| Missing wave | Fallback idle; click still emits `character_clicked` |
| Looping idle | **No** `animation_finished` spam |
| Sleep + click | Interrupt → wake → greeting; cooldown applies to wave not wake |
| Sleep + drag | Wake/pickup; dragged wins |
| Two emotes | Queue or replace by priority; no hard cut if clips allow blend |
| Animator trigger queue | Reset; last wins (except drag) |
| Root-motion idle | Ignore root delta on in-place states |
| Off-screen / NaN pose | Clamp to work area; recover idle |
| Scale 5–15% | Recompute when `screen_geometry` / DPI changes |

### 10.3 Cursor, click, drag

| Case | Required behavior |
|------|-------------------|
| Cursor in zone C | No look spam |
| Zone A jitter | Hysteresis so look-at does not flicker |
| Click vs drag | Threshold (e.g. 8px) + time; do not greet on drop |
| Drag outside monitor | Clamp to work area |
| Drop from “height” | Play land/recovery even if overlay is 2D (stylized) |
| Click-through failed (clicks hit desktop) | Tauri bug; Unity still tracks look-at from `mouse_position` |
| Right-click | No Unity menu |
| Double-click | Emit only; do not also fire two greetings |
| Poke while celebrating | Priority: interaction vs current clip — user interaction wins |

### 10.4 Desktop geometry

| Case | Required behavior |
|------|-------------------|
| Taskbar auto-hide | New `screen_geometry`; clamp pose |
| DPI / scale change | Rescale character; keep desktop x/y meaning |
| Monitor unplug | Tauri new geometry; if current monitor gone, spawn on primary (Tauri `set_monitor`) |
| Resolution change | Same as geometry update |
| Fullscreen game | Tauri context; Unity `move_to_edge` or Tauri hides; Unity does not Alt-Tab |
| Multi-monitor MVP | Do **not** walk off primary; store id only |
| Work area vs full monitor | Use **work area** so the character is not under the taskbar unless that is the sit-taskbar feature (future) |

### 10.5 Autonomy and time

| Case | Required behavior |
|------|-------------------|
| 10 min sleep | Unscaled clock; tests use fake clock |
| User active during sleep countdown | Reset idle timers on mouse/keyboard **context from Tauri** (Unity does not hook keyboard) |
| Wave cooldown vs Tauri `play_animation` wave | Tauri/user command **bypasses** cooldown or documented exception |
| Behavior spam | History + cooldown; recover idle |
| Error in behavior tick | Catch; idle; `runtime_fault` then recover |

### 10.6 What Unity must never do

- Call Win32 / AppKit / X11 directly.
- Use dashboard cloud envelope (`v: 2`, JWT, `stt.final`) as companion IPC.
- Ack companion animations with dashboard `state.ack`.
- Write save files to `%AppData%` itself (emit to Tauri).
- Block the render thread on IPC.
- Disappear, leave the work area permanently, or deadlock in `being_dragged` if mouse-up is lost → timeout release to idle.

**Lost mouse-up:** if drag starts and no `release` for N ms and cursor is up (Tauri sends button state) **or** timeout, force `release` recovery.

---

## 11. Final deliverables

Unity companion only. Tauri installers / tray / click-through implementation are **not** Unity deliverables, but Unity cannot accept C4 without a frozen hit-test contract.

### C0 — Contract + stub

**Deliverable:** versioned IPC + fake Tauri + empty viewport that parses messages.

- [ ] Schema doc (envelope, types, coordinates, server/port)
- [ ] Stub WS + Unity connect
- [ ] Ignore unknown types; no crash on malformed
- [ ] Independent of `Assets/SynthCohost`

**Done means:** a developer can send `play_animation` / `mouse_position` from the stub and see logs/events.

### C1 — Viewport exe

**Deliverable:** standalone Unity executable as character viewport.

- Transparent-capable camera, one character, living idle, recover-to-idle
- Runtime state object
- Spawn/despawn messages to Tauri

**Done means:** the exe runs without Tauri (idle only) and with stub (IPC).

### C2 — IPC API

**Deliverable:** frozen receive/emit table implemented.

- Commands in §3.3
- Events including `animation_finished` (non-loop) and `character_clicked`
- Resync after stub restart

**Done means:** real Tauri can replace the stub without Unity rewrite.

### C3 — Animation controller + signed v1 clips

**Deliverable:** registry-driven playback, blends, interrupts, fallbacks.

**Done means:** signed clip set plays; missing clip ≠ crash.

### C4 — Interaction

**Deliverable:** zones, look-at, click greeting, pickup/drag/drop per specs.

**Done means:** stub mouse can operate the character like a simple Desktop Mate.

### C5 — Autonomy / sleep

**Deliverable:** idle pools, sleep/wake, cooldowns, history, unscaled timers.

**Done means:** unattended character stays alive and recoverable.

### C6 — Monitor-rect navigation + edges

**Deliverable:** walk/peek/sit **inside** Tauri work area; no window-as-platform unless signed.

**Done means:** character cannot be lost off-screen; nav failure recovers.

### C7 — Packages (optional in first bid)

**Deliverable:** swap two Syn packages without rebuilding Unity.

### C8 — Explicitly later

Voice, visemes, awareness, multi-Syn, plugins, collisions, window platforms, monitor portals, cloud AI client, CV streamer overlay (unless separately assigned).

---

## 12. Acceptance snapshot (propose as Unity v1)

Unity companion v1 is **done** when:

1. Independent Unity exe shows one character, always recoverable to idle.
2. Local IPC: play clip, look-at, mouse move, click, drag/drop, `animation_finished` for one-shots.
3. Idle / sleep / wake / greeting / pickup work with the **signed** clip set.
4. Screen-edge peek + walk inside the **work-area rect** Tauri sends.
5. No OS APIs in Unity; stub documented; real Tauri drop-in.
6. Missing animations fall back; unknown IPC types ignored.
7. Lost mouse-up, DPI change, Tauri restart, and malformed IPC do not freeze or duplicate the Syn.

Out of v1 unless they explicitly add them: window platforms, icon pathfinding, multi-monitor hop, voice, multi-Syn, marketplace, cloud AI, streamer CV overlay.

Until C0–C2 exist, there is **no companion Unity product** — only documents. Until C4, it is a viewport, not a desktop companion.
