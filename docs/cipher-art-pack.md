# CIPHER // OVERRIDE — art asset pack & AI prompts

Game: top-down survivors-like (Vampire Survivors / Halls of Torment style), mobile.
World: **Freedom vs Control** (cyberpunk / surveillance).
- **Hero = Freedom** → cyan / teal / electric-green neon, sleek, heroic.
- **Enemies = Control** → red / orange / amber neon, cold metal, "eye / camera / surveillance" motifs.
- **Backgrounds** → near-black with subtle neon grid / city.

## How to use
1. Paste **MASTER STYLE** + the subject line into your image tool (ChatGPT/DALL·E, Midjourney, Leonardo, etc.).
2. Export each as a **transparent PNG (alpha), ~1024×1024, single subject centered**.
3. Send me the PNGs named as below → I cut, scale and **animate them in-engine** (facing, bob, recoil, hit-flash, death). Single images are enough to start; frame/sheets optional later.
4. Midjourney: append ` --ar 1:1 --no text` (and `--style raw` for less stylization). For backgrounds use `--ar 16:9` or `--tile`.

## MASTER STYLE (reuse in EVERY prompt — keeps them cohesive)
> high-quality 2D game sprite, 3/4 top-down view, modern cyberpunk, bold clean shapes with painterly shading and strong neon rim-lighting, vivid glow, crisp readable silhouette, single subject centered, transparent background, no text, no watermark, game-ready

---

## Assets + prompts

**1. hero.png — the Runner (player, "free node")**
> [MASTER STYLE]. A lone cypherpunk rebel hero: sleek hooded figure in dark techwear with glowing cyan circuit lines, holographic visor, wielding a futuristic energy pistol crackling with cyan data-energy, confident dynamic action pose, electric-teal rim light, heroic and cool.
> *Alt:* same hero as a sleek cyber-operative with a small hovering ally drone.

**2. foe_drone.png — Surveillance Drone (basic grunt)**
> [MASTER STYLE]. Small hostile surveillance drone: a hovering single-eye camera-orb with a glowing red lens, sleek black-metal body, tiny thrusters, menacing, red neon rim light.

**3. foe_hound.png — Tracer Hound (fast enemy)**
> [MASTER STYLE]. Fast quadruped robot hunter-dog: sleek angular sentinel hound with a red scanner eye, sharp legs built for speed, orange-red neon accents, aggressive lunging pose.

**4. foe_brute.png — Enforcer (heavy enemy)**
> [MASTER STYLE]. Heavy armored riot-enforcer mech: bulky humanoid robot with a riot shield and a single red optic, thick armor plating, slow intimidating tank, amber-red neon.

**5. boss_overseer.png — The Overseer (boss, later)**
> [MASTER STYLE]. Massive boss — a floating panopticon: a giant mechanical all-seeing eye surrounded by orbiting camera lenses, antennae and cables, oppressive, glowing red iris, the embodiment of total surveillance/Control, dramatic.

**6. fx_bolt.png / fx_muzzle.png / fx_explosion.png — weapon FX**
> [MASTER STYLE], glowing VFX element on transparent background: an elongated cyan-white energy "data bolt" projectile. *(separate prompts:)* a bright cyan muzzle-flash burst · a small impact spark · a larger neon cyan explosion with debris.

**7. pickup_shard.png — XP "data shard"**
> [MASTER STYLE]. Small collectible floating "data shard": a glowing cyan crystalline fragment / decrypted key, bright, gem-like.

**8. bg_floor.png — arena background (tileable)**
> Top-down **seamless tileable** game background, dark cyberpunk server-floor / surveilled neon plaza, near-black with faint cyan grid lines and subtle circuitry, moody, no characters, no text, high detail, seamless tiling.
> *Variants for "scenes/levels":* neon data-center · rain-slick surveilled city street from above · glitching red firewall void.

---

## Drop assets here
`/assets/game/` → hero.png, foe_drone.png, foe_hound.png, foe_brute.png, boss_overseer.png, fx_bolt.png, fx_muzzle.png, fx_explosion.png, pickup_shard.png, bg_floor.png

Once even a few of these exist, I swap them into the existing survivors systems and it instantly reads as a real game.
