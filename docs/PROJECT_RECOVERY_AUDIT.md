# CRYPTOBILL — PROJECT RECOVERY AUDIT
*Date: 2026-06-16. Auditor hats: senior producer · technical architect · gameplay programmer · QA. No flattery. Stabilization-focused.*

---

## TL;DR — the one thing you must hear

After all the work, **we still do not actually know if this game is fun**, because it has essentially **never been properly playtested**. Almost every "verification" in this project was done with headless bot simulations and frozen screenshots — i.e. *"does the code run and do the numbers move,"* not *"is this enjoyable to play on a phone with sound."* The two things that matter most for this specific game — **touch feel and audio** — were literally never tested by the people building it. Your repeated gut reaction ("hollow / messy / лишь бы было / что дальше?") is the most reliable signal in the entire project, and it has been right every time.

The code is **not** the disaster. The **process** is. We built breadth (≈7 major systems in the last push alone) on top of an unvalidated core.

**Verdict: REFACTOR + STABILIZE. Do not rebuild. Do not add a single new feature until the core ride is proven fun on a real device.**

---

## 1. What game we are actually building

A **crypto-themed endless side-scrolling physics runner**. You ride a motocross bike across a procedurally-generated neon **price line** (candlesticks as backdrop). Core fantasy: ride the chart, manage a Gravity-Defied-style pitch/throttle, and outrun a chasing **"liquidation wave" (the dump)** — "go as far as you can without getting liquidated." Wrapped in a degen-trader career skin (leverage, PnL, ranks Shrimp→Whale).

This is a coherent, marketable concept with a real hook. **The concept is not the problem.**

## 2. Current tech stack

- **Vanilla HTML + Canvas 2D + WebAudio**, hand-written. No framework, no engine, no build step, no dependencies, no bundler.
- Served as a **static file** (`python3 -m http.server`, port 4173).
- `package.json` exists but is a **stale relic** of a dead "tycoon" direction (`type: module`, a `sim` script pointing at `tools/sim.js` that belongs to abandoned code). It describes a different game. It is misleading and currently meaningless to the live product.

**Assessment:** Canvas 2D + single static file is a **perfectly appropriate** stack for a game this size. Many shipped indie games are exactly this. The stack is **not** a reason to rebuild.

## 3. Current folder / file structure — *this is a mess*

The live game is **one file**: `toys/chartrider.html` (783 lines, ~80 KB, **everything in a single `<script>`**). `index.html` is a symlink to it.

Everything else in the repo is **noise**:
- **Root:** `cipher.html`, `concept.html`, `moon.html`, `index-tycoon-old.html`, `styles.css` (23 KB), `assets/`, `tools/`, `package.json` — all leftovers from abandoned directions.
- **`src/`** — 9 JS files (~80 KB: `engine.js`, `game.js`, `empire.js`, `content.js`, …) — the **dead tycoon engine**. Not used by the live game at all.
- **`toys/`** — ~**18 dead prototype files** (`horde-v1/2/3`, `signal-v1…v8`, `cipher-keyframe`, `glyph-slice`, `sever`, `glb-check`, …, ~350 KB). One live file buried among 18 corpses.
- **`docs/`** — `HANDOFF.md` is **42 KB** (sprawling, current) plus **7 stale docs** from the tycoon era (`crypto-millionaire-game-plan.md`, `genre-analysis-and-systems-design.md`, `cipher-handoff.md`, …).

**A new contributor opening this repo cannot tell what the project is.** ~90% of the files are dead. That alone causes confusion and accidental-edit risk.

## 4. Main gameplay systems already implemented (in the one file)

All of these live inside `toys/chartrider.html`:
- Procedural terrain (price-line heightmap + candle backdrop, market "regimes", multi-octave roll).
- **Two-wheel pitch physics** (wheelie / loop-out / endo / momentum) — config in a `PHYS` object.
- Vector bike rendering (dynamic rider lean, lights, knobby tires) + Alto-style dusk visuals, parallax, particles, vignette.
- **Chasing liquidation wave** (escalates with distance; now also a difficulty *gate*).
- Hazards (sell-walls), **on-demand jump**, **PUMP boost**, leverage multiplier + PnL scoring.
- **Market events** (pump-rally / whale-dump / flash-crash set-pieces).
- **5 market biomes** (mood/visual shifts by distance).
- **Onboarding** (warm-up difficulty curve + one-time coach hints).
- **Touch controls** (visible gas/brake/jump buttons + multitouch) and keyboard.
- **Meta-progression**: 6 upgrades + garage screen, trader ranks (Shrimp→Whale) with rewards + a PnL multiplier, gem bank, daily bonus, depth milestones, missions.
- **Generative adaptive music** + procedural SFX + haptics.
- Title / garage / paused / death screens.

That is **a lot** of systems for an unproven core. Breadth ≠ depth. (See §7.)

## 5. What currently works (with honest confidence levels)

- ✅ **It boots and runs without console errors.** Verified repeatedly. (High confidence.)
- ✅ **Rendering / visuals.** The bike, track, dusk palette, particles, HUD render correctly and look decent. (High confidence — screenshots are valid for visuals.)
- ✅ **Core physics is mechanically sound in simulation.** Two-wheel pitch model is numerically stable; loop-out skill is real (greedy bot loops 7/10, skilled 0/10). (Medium-high confidence — *bot* play, not human.)
- ✅ **Meta-progression plumbing works.** Buying upgrades, persistence (localStorage), ranks, garage navigation — all verified via simulated input. Upgrade effects measurably change gameplay (e.g. +27% engine power, −28% wave speed). (Medium confidence — logic verified, *feel* not.)
- ✅ **Difficulty gating is real.** Bare bike walls ≈1015 m; upgraded ≈3545 m (+249%). (Medium confidence — bot-measured.)

**Crucial caveat on this whole section:** "works" here mostly means *"the code executes and the numbers are correct,"* established by **headless bot loops and frozen screenshots**. That is a weak form of QA for an action game.

## 6. What is broken or unstable

**Technical / stability:**
- 🔴 **NO VERSION CONTROL.** Not a git repo. An 80 KB single file with 783 lines of tangled global state and **zero history or rollback**. One bad edit and work is gone. Dozens of edits have been made this session with no commits. *This is the single most dangerous fact in the project.*
- 🟠 **The integrated experience is unverified.** Touch feel and audio — the two highest-impact dimensions — **cannot be tested in the dev loop** (preview is silent and runs in a throttled background tab). They have only ever been "verified" as *"no exceptions thrown."* We do not know if the controls feel good or the music sounds good. We are flying blind on exactly the parts that decide whether the game is enjoyable.
- 🟠 **The LAN preview link keeps breaking** (background server gets killed; the Mac's IP changed mid-project). Symptom of an ad-hoc, fragile test setup with no reliable "play it on the device" loop.
- 🟡 **No automated tests, no smoke test, no asserts.** Regressions are caught only by chance or by you noticing in chat.

**Gameplay / "feels broken":**
- 🔴 **You repeatedly report it feels hollow / messy / unengaging.** That is the real bug report. Each new system was added *in response to* that feeling, and each time the feeling came back — meaning the additions treated symptoms, not the cause.
- 🟠 **All balance is guessed.** Every number is "first pass, tune by real play." The difficulty curve, economy, upgrade costs, wave speed, event frequency — none confirmed to feel right by a human. The game's pacing is, functionally, **untuned**.

## 7. What feels messy or overcomplicated

- 🔴 **One 783-line file, ~62 functions, ~32 globals, all mutable, one shared `<script>` scope.** Everything can touch everything. There is no module boundary, no encapsulation. Reasoning about state requires holding the whole file in your head.
- 🟠 **Tuning is scattered.** There are 8 config blocks (`PHYS`, `BIKES`, `EVENTS`, `RANKS`, `UPGRADES`, `BIOMES`, `MISSION_POOL`, `BASE_PHYS`) **and** a large amount of inline magic numbers in `update()`/`draw()`/`genSeg()`. To "tune the game" you must hunt across the whole file. There is no single source of truth for balance.
- 🟠 **Hacks baked in.** `draw` is monkey-patched at the bottom (`const _draw=draw; draw=function(){…}`) to bolt on screen-shake. `screen` shadows the global `window.screen`. The render path re-draws the entire world *behind* full-screen menus every frame (was the cause of the garage stutter; patched, but symptomatic of "render everything always").
- 🟠 **The game does too much at once.** Physics skill + jump + dump-chase + hazards + boost + events + biomes + leverage/PnL + missions + ranks + upgrades + daily + music are all competing for the player's attention and for *your* tuning bandwidth, on a core that isn't yet proven fun. This is *the* design-level mess: **scope sprawl outpacing validated fun.**
- 🟡 **The HANDOFF doc (42 KB) is itself a symptom** — a giant running narrative instead of a tight spec. It records *what was done*, not *what the game is supposed to be*.

## 8. What is worth keeping

Most of the *code*. Specifically:
- **The concept** (ride-the-chart + dump-chase). Keep.
- **Rendering / visual style** (dusk palette, bike art, particles, parallax). Real asset. Keep.
- **The two-wheel pitch physics** — it's the strongest, most defensible mechanic and the hardest thing to rebuild. Keep, but it must be **proven fun by a human on touch**, which has not happened.
- **The dump-chase tension loop** — the clearest source of stakes. Keep.
- **The procedural terrain generator.** Keep.
- Persistence plumbing and the WebAudio scaffold are reusable.

## 9. What should be rewritten / cut (not necessarily now)

- **Rewrite (structurally, later):** split the monolith into clear sections/modules (input, physics, render, audio, meta, UI), kill the `draw` monkey-patch, and **centralize all tuning into one config object**.
- **Cut from the repo immediately (clutter, zero risk):** the ~18 dead prototypes in `toys/`, the dead `src/` tycoon engine, `cipher.html`/`concept.html`/`moon.html`/`index-tycoon-old.html`, the stale `styles.css`/`assets/`/`tools/`, the misleading `package.json`, and the 7 stale docs.
- **Temporarily disable (not delete) for MVP focus:** events, biomes, ranks, daily bonus, depth milestones, music — everything outside the core ride+chase — so the core can be evaluated and stabilized **without noise**. Re-enable one at a time, each behind a real-play validation gate.

## 10. Recommendation — continue, refactor, or rebuild?

### → REFACTOR + STABILIZE. (A hard "continue," with the feature tap shut off.)

- **Rebuild is wrong.** The foundation is not broken: the concept is good, the stack is appropriate, the code runs, the physics and visuals are real, hard-won assets. Rebuilding throws those away to re-acquire the *same* discipline a refactor gives you. Net loss of weeks for an **organizational** problem, not a foundational one.
- **"Continue (keep adding)" is wrong.** You've told us repeatedly it isn't working. More systems on an unproven core compounds the mess.
- **Refactor + stabilize is right:** stop adding, put it under version control, get a real device+sound play loop, cut scope to a provably-fun core MVP, harden it, consolidate tuning, *then* reintroduce systems one at a time with validation.

**One honest caveat that outranks the code question:** the open risk is **not** "is the code salvageable" (it is) — it's **"is the core ride actually fun?"**, which is still *unanswered* after all this work. If a real-device playtest shows the core ride/chase isn't enjoyable, that's a **design** problem (rethink the core mechanic), not a code-rebuild problem. **Validate the core's fun before spending another hour on anything else.**

---

## Design problems vs. technical problems (kept separate, as requested)

| # | TECHNICAL problems | DESIGN problems |
|---|---|---|
| 1 | No git / no version control (existential) | Core ride never confirmed *fun* by real play |
| 2 | No real device + audio test loop (the things that matter are untestable in dev) | Scope sprawl: too many systems competing on an unproven core |
| 3 | Monolithic 783-line file, ~32 globals, shared scope | Balance entirely guessed / untuned |
| 4 | Tuning scattered across 8 configs + inline magic numbers | "Hollow" feeling unresolved — additions treat symptoms |
| 5 | Hacks (monkey-patched `draw`, render-everything-always) | No crisp definition of the *minimum* game |
| 6 | ~20 dead files / stale `package.json` / 42 KB narrative doc | Theme is broad but mechanically still mostly skin-deep |
| 7 | No automated tests / smoke test | — |

**Do not conflate these.** A clean refactor fixes the left column and changes *nothing* about whether the game is fun. The right column is only answered by a human playing the real build.

---

## 11. Smallest playable MVP to focus on from here

**The irreducible game = ride + control + survive the dump.** Nothing else.

> One bike. Procedural price-line track. Three inputs (gas / feather-off, jump, brake). The chasing liquidation wave. A distance score. Instant restart. **That's the whole MVP.**

Turn **off** (don't delete): leverage/PnL scoring, events, biomes, ranks, garage/upgrades, daily, milestones, missions, music (until its sound is judged), multiple bikes. Strip the HUD to: distance + a clear "the dump is N behind you" read.

**The MVP's single success test:** *a real person plays 3–4 runs on a phone and wants to press "again" without being told to.* If the bare ride+chase passes that, the game is real and everything else is enhancement. If it fails that, **no amount of meta-progression or music will save it** — and that's the most important thing the MVP tells you.

---

## 12. Next 10 development tasks (priority order) — stabilization only, no new features

1. **`git init` + first commit (baseline) right now**, then commit after *every* change. This is non-negotiable and takes 2 minutes. Until this exists, all other work is at risk.
2. **Establish a reliable "play it on the real device, with sound" loop** (stable LAN/tunnel or sideload). The dev process cannot continue blind to touch + audio.
3. **Do a real-device playtest of the *current* build** and write down — concretely, separated — (a) actual bugs, (b) actual feel problems. Replace bot-sim "QA" with human QA. This pass *defines* the rest of the backlog.
4. **De-clutter the repo:** move all dead files (old prototypes, `src/`, stale root HTML/CSS/docs, misleading `package.json`) into an `_archive/` folder so the live surface is unambiguous. Zero behavior risk, large clarity gain.
5. **Write a 1-page MVP spec** (`docs/MVP.md`): the §11 scope, the success test, and an explicit "OFF for now" list. One source of truth for *what the game is*, replacing the 42 KB narrative.
6. **Feature-flag the game down to the MVP** (gate events/biomes/ranks/daily/garage/music behind a single `MODE` switch). Don't delete — disable. Now the core can be evaluated clean.
7. **Stabilize the core ride+jump+chase feel on the device** until it reliably passes the §11 "press again" test. Fix the *real* problems found in #3, not guessed ones. This is the make-or-break task — give it the most time.
8. **Centralize all tuning** into one `CONFIG`/`TUNE` object; pull inline magic numbers out of `update`/`draw`/`genSeg`. Makes balancing possible at all.
9. **Tune the dump-chase balance by real play** (tense but fair; recoverable from one mistake; honest catch). Only meaningful once #2/#3/#7/#8 exist.
10. **Light structural cleanup** of the single file *after* behavior is stable: group into labeled sections, remove the `draw` monkey-patch, reduce global-state foot-guns, add a 10-line smoke check. (Do this last — refactoring before the behavior is pinned down just risks breaking working code.)

**Re-introducing any disabled system (events, ranks, music, …) is a *future* task, each gated behind "the core still passes the press-again test on device."** Not now.

---

### Bottom line
The codebase is **salvageable and worth saving** — refactor, don't rebuild. But the project's real problem isn't the mess in the file; it's that **we built a lot and validated almost nothing that matters.** Stop adding. Put it in git. Play it for real, on a phone, with sound. Cut to the core. Prove the ride is fun. *Then* — and only then — rebuild the breadth back on top, one validated piece at a time.
