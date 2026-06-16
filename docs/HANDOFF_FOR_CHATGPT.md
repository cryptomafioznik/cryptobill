# CRYPTOBILL — Handoff for a Second AI Advisor

*Purpose: you (the advisor) have never seen this project. This file is self-contained. After reading it you should understand the game and be able to help us decide how to continue. Written to be honest, not flattering — please push back hard where you disagree.*

---

## 1. Game concept in simple words

A **crypto-themed side-scrolling physics bike runner**. You ride a motocross bike across an endless, procedurally-generated **neon price chart** (the ground IS a candlestick/price line). You manage the bike's balance and throttle (Gravity-Defied / Hill-Climb style), and a **rising "liquidation wave" (a market dump)** chases you from behind. The fantasy: *ride the chart, don't get liquidated, go as far as you can.* It's skinned as a degen-trader career (leverage, profit/PnL, trader ranks).

## 2. Target platform

**Mobile web, portrait, touch-first** (the owner plays on a phone via a LAN URL). Also runs on desktop with a keyboard. It is a **browser game** (single HTML file), not a native app.

## 3. Engine / framework

**None.** Hand-written **vanilla HTML + Canvas 2D + WebAudio**. No engine, no framework, no libraries, no build step, no dependencies. Served as a static file (`python3 -m http.server`).

## 4. Current project status

**A running prototype that is broad but unproven.** It boots and runs without console errors. It has ~12 gameplay systems implemented. **But:** it has **never been properly playtested** (almost all verification was done with headless bot simulations + screenshots, never real human play on a device with sound), the **balance is entirely guessed**, there is **no version control**, and the owner repeatedly reports it feels **"hollow / messy / not engaging."** So: technically alive, experientially unvalidated.

## 5. Core gameplay loop

**Per run:** spawn → ride the price line (hold gas) → feather the throttle on climbs (too much gas = flip backward / "loop-out") → jump over obstacles → do tricks for boost → outrun the chasing dump wave → eventually die (caught by the wave, crash a landing, or loop-out) → see score + gems earned → restart.

**Meta loop:** gems from runs → buy permanent upgrades in a "garage" → go deeper → climb trader ranks (Shrimp → … → Whale) → unlock/earn more → push your distance record.

## 6. Player controls

**Touch (primary):** on-screen buttons — **GAS** (bottom-right, hold), **BRAKE** (bottom-left, hold), **JUMP** (center, tap). Tap the **PUMP** meter to boost when charged. Multitouch supported (hold gas + tap jump simultaneously).

**Keyboard:** `Space / → / D` = gas (hold), `↓ / ← / S / A` = brake, `↑ / W` = jump, `Shift` = boost, `Esc / P` = pause, `M` = mute, `R` = restart.

**In the air:** hold gas = lean back / backflip; hold brake = nose down / frontflip; release = auto-level toward the landing.

## 7. Main mechanics

- **Two-wheel pitch physics** — the bike has real pitch + angular momentum on the ground. Over-gas a climb → wheelie → **loop-out (crash)**. Brake hard / land nose-down → **endo (crash)**. Momentum is real (climbs cost speed, descents build it). This is the intended *skill core*.
- **On-demand jump** — hop over hazards or for air/tricks.
- **The dump (chasing wave)** — a red liquidation wall behind you; catching you = death. It **escalates with distance without a cap**, so it eventually becomes an unbeatable wall → this **gates how deep you can go**, and upgrades push the wall further out.
- **PUMP boost** — charges from tricks / air / near-misses; spend it to surge ahead of the wave (a clutch escape).
- **Leverage multiplier + PnL** — score system; leverage builds from airtime/speed/tricks, multiplies profit.
- **Market events** (telegraphed set-pieces): pump-rally (green climb, bonus), whale-dump (wave surges), flash-crash (a cliff you plunge off).
- **5 biomes** (market eras) — mood/visual shifts by distance.
- **Meta-progression** — 6 upgrades (engine/grip/pump/wave-shield/air-control/gem-magnet), trader ranks with gem rewards + a PnL multiplier, gem bank, daily login bonus, depth milestones, 3 missions per run.
- **Onboarding** — a gentler difficulty warm-up + one-time contextual coach hints.
- **Audio** — generative adaptive synthwave music + procedural SFX + haptics (vibration on impacts; Android only — iOS Safari ignores web vibration).

## 8. Existing "enemies" / obstacles / items

There are **no AI enemies**. Threats are environmental/physics-based:
- **The liquidation wave / "the dump"** — the main antagonist; a chasing wall that ends the run on contact.
- **Sell-walls (hazards)** — hit on the ground = "REKT" (lose ~60% speed); clear them in the air = "DODGE" bonus. You can now jump them.
- **Flash-crash cliffs** — sudden drops you launch off and must land.
- **Steep climbs** — implicit obstacle (loop-out risk if greedy on the throttle).
- **Items:** gems (◆, the currency), coins along the track, PUMP charge from tricks.

## 9. Current visual style

**Alto's-Odyssey-inspired neon dusk.** Dark gradient sky that shifts color per biome, parallax silhouette ridges, a glowing neon **price-line track** with dim candlesticks behind it, a vector-silhouette motocross bike with a standing rider that leans dynamically + head/tail lights, particles, speed streaks, vignette, screen shake, floating score popups, a terminal-style HUD (BTC ticker, $PnL, leverage badge). For "code-only art, no artist," it looks **decent** — clean and stylish, not premium.

## 10. Current file structure (honest: ~90% of the repo is dead)

```
cryptobill/
├── index.html                  → symlink to toys/chartrider.html
├── toys/
│   ├── chartrider.html         ← THE LIVE GAME (783 lines, ~80KB, ALL code in one <script>)
│   └── (~18 DEAD prototype files: horde-v1/2/3, signal-v1..v8, cipher-*, glyph-slice, sever, …)
├── src/                        ← DEAD (old "tycoon" engine: engine.js, game.js, empire.js… ~80KB)
├── docs/
│   ├── HANDOFF.md              ← 42KB running narrative dev log (current, but sprawling)
│   ├── PROJECT_RECOVERY_AUDIT.md← honest audit (recent)
│   ├── HANDOFF_FOR_CHATGPT.md   ← this file
│   └── (7 STALE docs from the dead tycoon era)
├── cipher.html, concept.html, moon.html, index-tycoon-old.html  ← DEAD prototypes
├── styles.css, assets/, tools/, package.json                    ← STALE leftovers (package.json describes a different, dead game)
└── .claude/launch.json         ← dev server config
```

There is **no git repository** — no version history at all.

## 11. Most important files and what they do

- **`toys/chartrider.html`** — *the entire game.* One file, one `<script>`: terrain generation, physics, bike/render, input (touch+keyboard), audio, meta-progression, all UI/screens. ~62 functions, ~32 global mutable variables, sharing one scope. If you only look at one file, look at this.
- **`docs/HANDOFF.md`** — exhaustive 42KB narrative of the whole development history (every pivot and feature). Useful context, but it records *what was done*, not *what the game should be*.
- **`docs/PROJECT_RECOVERY_AUDIT.md`** — a blunt technical/design audit with the same conclusions summarized here.
- **`.claude/launch.json`** — how the dev server is launched.
- Everything else is dead or stale and should be ignored.

## 12. Current bugs (known + suspected)

- 🔴 **No version control.** Not a bug in the code, but it means any bad edit is unrecoverable. Existential.
- 🔴 **The integrated experience is unverified.** Touch feel and audio — the highest-impact dimensions — **cannot be tested in the current dev loop** (the preview tool is silent and runs in a throttled background tab). They were only ever confirmed as "no exception thrown." We genuinely **do not know** if controls feel good or music sounds good on a real phone.
- 🟠 **Flaky LAN preview** — the local server gets killed and the host IP changed mid-project, so "play it on the phone" keeps breaking.
- 🟡 **Music levels are unmixed** (unknown if too loud / annoying / fine — never heard in context).
- 🟡 **Haptics are Android-only** (iOS Safari silently ignores `navigator.vibrate`) — known limitation, not fixable in web.
- ⚪ **No automated tests / smoke check** → unknown bugs almost certainly lurk; nothing catches regressions except the owner noticing.
- (A garage-screen stutter existed and was patched; it was caused by re-rendering the whole game world behind full-screen menus every frame.)

## 13. Current design problems

- **The core ride has never been confirmed *fun* by real play.** This is the central unknown.
- **Scope sprawl:** ~12 systems competing for attention (and for tuning bandwidth) on top of an unproven core.
- **Balance is entirely guessed** — every number is "first pass, tune later"; difficulty/economy/pacing are functionally untuned.
- **The "hollow" feeling keeps recurring** — each system was added to fix it and it came back, suggesting we've been treating symptoms, not the cause.
- **No crisp definition of the minimum game** — it's unclear what the irreducible fun is.
- The crypto theme is broad but, mechanically, still mostly a skin (improving, but skin-deep).

## 14. Current technical problems

- No git / no history / no rollback.
- No reliable real-device + audio playtest loop.
- **Monolithic single file**, ~32 globals, one shared scope — everything can mutate everything.
- **Tuning scattered** across 8 config blocks (`PHYS`, `BIKES`, `EVENTS`, `RANKS`, `UPGRADES`, `BIOMES`, `MISSION_POOL`, `BASE_PHYS`) **plus** many inline magic numbers — no single source of truth for balance.
- **Hacks:** `draw` is monkey-patched at the end of the file to add screen-shake; the whole world re-renders behind menus every frame.
- Repo clutter (~20 dead files), misleading stale `package.json`.
- No automated tests.

## 15. What we tried to build but failed (honest history)

Before this game, the project thrashed through **many abandoned directions**: a crypto **tycoon/idle sim** (felt like a spreadsheet — killed), a long **"mechanic hunt"** of throwaway mini-games, a **"CIPHER: Freedom vs Control" cypherpunk** thing, multiple **survivors-likes** (`horde-v1/2/3` — rejected as cheap-looking), **3D experiments** (came out blobby), and an **"award-art living-cipher swarm-of-light"** concept (rejected — it had been over-hyped to the owner). That's why `toys/` and `src/` are full of corpses.

**Within the current chart-runner**, the recurring failure pattern is: the owner says *"boring / hollow,"* a new system gets added to fix it (physics rewrite → events → onboarding → jump/touch → meta-progression → depth-gating → audio), it's "verified" with bots, and the hollow feeling returns. **We kept building breadth instead of validating fun.**

## 16. What we should probably keep

- The **concept** (ride-the-chart + dump-chase).
- The **rendering / visual style** (a genuine asset).
- The **two-wheel pitch physics** (the strongest, hardest-to-rebuild mechanic) — *if* it proves fun on touch.
- The **dump-chase tension loop** (clearest source of stakes).
- The **procedural terrain generator**, persistence plumbing, and WebAudio scaffold.

## 17. What we should probably remove

- **Delete/archive (clutter, zero risk):** the ~18 dead prototypes in `toys/`, the dead `src/` engine, the old root HTMLs (`cipher/concept/moon/index-tycoon-old`), stale `styles.css`/`assets/`/`tools/`, the misleading `package.json`, the 7 stale docs.
- **Temporarily disable (NOT delete) for focus:** events, biomes, ranks, daily bonus, depth milestones, music — everything outside the core ride+chase — until the core is proven and stable. Re-enable one at a time with validation.

## 18. Claude's recommendation

**REFACTOR + STABILIZE — do not rebuild, do not keep adding.**

- **Rebuild is wrong:** the foundation isn't broken (good concept, appropriate stack, working code, real physics/art). Rebuilding discards weeks of working code to re-acquire the *same* discipline a refactor provides.
- **"Continue adding" is wrong:** the owner keeps saying it isn't working; more systems compound the mess.
- **The plan:** put it under git, get a real device+sound playtest loop, **cut scope to a provably-fun core MVP** (one bike, ride, gas/jump/brake, the dump, a distance score, restart — everything else OFF), stabilize that until a real person wants to press "again" unprompted, centralize tuning, then reintroduce systems one at a time, each gated by real play.
- **The honest caveat that outranks the code question:** we *still don't know if the core ride is fun.* If a real-device playtest shows it isn't, that's a **design** problem (rethink the core mechanic), not a code-rebuild problem. **Validate the core's fun before anything else.**

## 19. Exact questions we need you (ChatGPT) to help answer

Please answer these directly and feel free to disagree with Claude:

1. **Is "ride a chart + outrun a market dump" a strong enough *core* to carry the whole game** — or is the core mechanic itself the reason it keeps feeling hollow? Be blunt.
2. The owner keeps feeling it's **"hollow / not engaging"** even as systems are added. In your read, is that most likely a **(a) core-design problem, (b) balance/tuning problem, (c) game-feel/juice problem, or (d) a "never actually playtested" problem**? Where would you put your money?
3. **Physics-skill direction (Gravity Defied / Trials — technical, higher skill floor) vs. simpler one-button flow (Alto / Tiny Wings — broad, low floor):** which fits this concept and a solo dev better? Should we commit harder to one?
4. Is the **meta-progression (upgrades, ranks, daily)** helping retention, or is it **premature scaffolding** on an unproven core that we should shelve until the ride is fun?
5. What is the **single most important thing to fix first** to make the *second-to-second ride* feel good?
6. For a **solo dev with no QA pipeline who can barely playtest reliably**, what's the most efficient way to validate "is this fun?" fast and cheaply?
7. Is **single-file vanilla Canvas 2D** fine to take to a shippable product, or will it become a real bottleneck we should pre-empt?
8. Market reality check: does this concept have legs **vs. StonkRider and the crowded runner genre**, or is it too niche to be worth hardening? What would make it stand out without more feature-bloat?

## 20. Next best action

1. **`git init` + commit the current build as a baseline** (2 minutes; until this exists, everything is at risk).
2. **Stand up a reliable "play on the real phone, with sound" loop**, then **do one honest human playtest of the current build** — separating *actual bugs* from *actual feel problems*.
3. In parallel, **de-clutter the repo** so the live surface is unambiguous.

**The single most valuable next step is #2:** get real human signal on whether the bare ride+chase is fun. Every "continue / refactor / rebuild" decision hinges on that answer, and we have been avoiding it by building more instead.
