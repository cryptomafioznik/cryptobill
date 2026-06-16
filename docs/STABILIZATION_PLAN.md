# CRYPTOBILL — Stabilization Plan

*Companion to `docs/PROJECT_RECOVERY_AUDIT.md`. This is the live working doc for getting the game from "broad but unvalidated" to "a stable, provably-fun core."*

Status: **CORE TEST MODE is ON.** (Step 6 of the audit's task list.)

---

## The switch

Single config flag at the **top of `toys/chartrider.html`** (right after the `lerp`/`clamp` helpers):

```js
const CORE_TEST_MODE = true;   // true = minimal ride-and-chase test · false = full game
```

- **`true`** → strips the game down to the core ride-and-chase loop. Everything else is gated OFF.
- **`false`** → the full game returns, exactly as before. Nothing was deleted.

To switch: change the one boolean, save, hard-refresh the page (**Cmd+Shift+R** / pull-to-refresh — the cache will otherwise serve the old build). That's it.

All gating is done with `if(CORE_TEST_MODE)` / `if(!CORE_TEST_MODE)` guards. **No system was removed** — every feature is intact and returns when the flag is flipped.

---

## What CORE TEST MODE keeps (the loop under test)

- Two-wheel **bike physics** (pitch, wheelie/loop-out, momentum).
- **Gas / brake / jump** controls (touch + keyboard), with auto-level in the air.
- **PUMP boost** (charge from tricks, spend to surge — it's part of escaping the wave).
- **Terrain / price-line track** (procedural).
- **Sell-walls (hazards)** — kept on purpose: they give the jump a purpose and feed the chase (hit on ground = slow = wave catches you). *If you want it even purer, say so and they go behind the flag too.*
- **Liquidation wave (the dump)** — the chase, escalating with distance so it eventually walls you.
- **Distance score** (meters) — the only number. Hero on the HUD + a "★ record" line.
- **Crash / death** (wave / loop-out / endo / bad landing) and **instant restart**.

The HUD shows: `CORE TEST` label · big distance · `dist · ★ record` · km/h. Nothing else.

## What CORE TEST MODE gates OFF (disabled, not deleted)

| System | How it's gated |
|---|---|
| Garage screen | title + death buttons hidden (`UI.garage`/`UI.dgarage` null) |
| Upgrades | `upgLvl()` returns 0 → stock bike |
| Trader ranks + rank rewards + PnL rank-multiplier | `rankMul=1`, rank UI hidden, rank-up skipped in `wipeout()` |
| Daily bonus | `checkDaily()` skipped in `toTitle()` |
| Missions | `checkMissions()` early-returns; mission list hidden on title |
| Market events (pump-rally/whale-dump/flash-crash) | event scheduler in `genSeg()` skipped |
| Economy (PnL / leverage / gems as score) | HUD shows distance; leverage badge hidden; bank/career/bestPnl frozen in `wipeout()` |
| Score popups & banners (REKT/DODGE/BIG AIR/FLIP/`+$`/depth-milestones/biome banner) | `popText()`/`floatPop()` no-op; biome banner + milestone gated |

**Persistence note:** in CORE TEST MODE, runs do **not** touch the saved bank / career / rank / PnL-best — the economy is frozen so testing doesn't pollute the save. Only the **distance record** (`cipher_run_best`) is written, because distance is the test's own metric.

---

## What we are actually testing

> **The one question:** is the second-to-second **ride + dump-chase fun on its own** — does a real person play 3–4 runs on a phone and want to press "again" unprompted, with no meta, no economy, no music distractions?

If **yes** → the game has a real core; everything we flip back on later is enhancement. If **no** → that is a **design** signal (rethink the core mechanic), and no amount of upgrades/ranks/music would have saved it. Either way, this is the answer we have been avoiding by building more.

## How to test it (on the phone)

1. Make sure the LAN server is running, open **the LAN URL** on the phone, **hard-refresh** (cache!).
2. The title shows a **`CORE TEST`** label and `— CORE TEST · только езда и погоня —`. If you don't see those, the cache served the old build — refresh again.
3. Play several runs. Judge **only the ride and the chase.** Specifically:
   - Does the bike feel **good to control** (gas, feather-off on climbs, jump, brake)?
   - Is the **dump chase** tense but fair — can you recover from one mistake, does it catch you honestly?
   - Without any rewards, do you still want **one more run** to beat your distance?
4. Report back **two separate lists:** *(a) actual bugs* and *(b) actual feel problems.* Keep them apart.

---

## Stabilization roadmap (from the recovery audit)

- [x] **1. Version control** — `git init` + baseline commit done.
- [ ] **2. Reliable "play on phone with sound" loop.**
- [ ] **3. Honest human playtest of the current build** → bugs vs feel, separated. *(This is what CORE TEST MODE unblocks.)*
- [ ] **4. De-clutter the repo** (archive ~18 dead prototypes, dead `src/`, stale root files/docs, misleading `package.json`).
- [ ] **5. Write a 1-page MVP spec** (`docs/MVP.md`).
- [x] **6. Feature-flag down to the MVP** — `CORE_TEST_MODE` (this doc).
- [ ] **7. Stabilize the core ride+chase on the device until it passes the "press again" test.** ← the make-or-break.
- [ ] **8. Centralize all tuning** into one config; remove inline magic numbers.
- [ ] **9. Tune the dump-chase balance by real play.**
- [ ] **10. Light structural cleanup** of the single file (kill the `draw` monkey-patch, group sections) — only after behavior is stable.

**Rule for re-enabling anything (events, ranks, music, …):** only after the core still passes the "press again" test on the device, and one system at a time, each behind its own real-play check.

---

## Changelog
- **2026-06-16** — `CORE_TEST_MODE` added (single flag, ~16 gated points in `toys/chartrider.html`). Verified: game runs end-to-end in core mode (ride → distance score → dump death → restart), all listed systems gated off, economy frozen, 0 popups/events generated. Sound/touch feel still pending a real-device playtest.
