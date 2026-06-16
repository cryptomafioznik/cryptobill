// Pure simulation core — NO DOM, no side effects beyond the passed state `s`.
// This is what makes the game testable/balanceable: the headless sim
// (tools/sim.js) and the browser UI (game.js) both drive this same engine,
// so balancing in Node reflects the real game exactly.

import { makeRng } from "./rng.js";
import { EVENTS, MEME_NAMES, MILESTONES, LESSONS } from "./content.js";
import { ACTS, EDGES, actForWeek, actIndexForWeek, EDGE_OFFER_WEEKS } from "./cycle.js";
import { LIFESTYLE, CATEGORIES } from "./empire.js";

export const STARTING_CASH = 1000;
export const MAX_WEEKS = 24;
export const MILLIONAIRE_TARGET = 1_000_000;
export const BUST_FLOOR = 75;
export const MAX_ENERGY = 3;
export const START_PRICES = { btc: 64250, sol: 148, meme: 0.0042 };

// ---------------------------------------------------------------------------
// Run construction (fully deterministic — all randomness pre-rolled here)
// ---------------------------------------------------------------------------
export function makeRun(seedKey, seedLabel, opts = {}) {
  const mode = opts.mode || "daily";
  const rng = makeRng(seedKey);

  // Each week offers a CHOICE of two pre-rolled events (the branching map).
  // Path 0 is the calmer road, path 1 the spicier one — the player picks which
  // week to face. The dice (noise/rug/hack) for the week are shared either way.
  const spice = (e) => e.risk + Math.abs(e.meme) * 12;
  const eventChoices = [];
  const rolls = [];
  for (let i = 0; i < MAX_WEEKS; i += 1) {
    const act = actForWeek(i + 1);
    const pool = [];
    EVENTS.forEach((e) => {
      const w = act.bias[e.tone] || 1;
      for (let k = 0; k < w; k += 1) pool.push(e);
    });
    const a = pool[Math.floor(rng.next() * pool.length)];
    let b = pool[Math.floor(rng.next() * pool.length)];
    let guard = 0;
    while (b.id === a.id && guard < 8) { b = pool[Math.floor(rng.next() * pool.length)]; guard += 1; }
    eventChoices.push(spice(a) <= spice(b) ? [a, b] : [b, a]);
    rolls.push({
      noiseBtc: rng.float(-0.03, 0.035),
      noiseSol: rng.float(-0.05, 0.06),
      noiseMeme: rng.float(-0.16, 0.2),
      rug: rng.next(),
      hack: rng.next(),
    });
  }

  const memeQueue = [];
  for (let i = 0; i < MAX_WEEKS; i += 1) {
    memeQueue.push({ name: rng.pick(MEME_NAMES), price: START_PRICES.meme * rng.float(0.4, 2.4) });
  }

  const edgePool = EDGES.map((e) => e.id);
  for (let i = edgePool.length - 1; i > 0; i -= 1) {
    const j = Math.floor(rng.next() * (i + 1));
    [edgePool[i], edgePool[j]] = [edgePool[j], edgePool[i]];
  }
  const edgeOffers = {};
  EDGE_OFFER_WEEKS.forEach((wk, k) => { edgeOffers[wk] = edgePool.slice(k * 3, k * 3 + 3); });

  const s = {
    seedKey, seedLabel, mode,
    week: 1,
    cash: STARTING_CASH,
    energy: MAX_ENERGY,
    skill: 8, reputation: 8, security: 10, stress: 12,
    stressResist: 0, intelBase: 0.12,
    prices: { ...START_PRICES, meme: memeQueue[0].price },
    moves: { btc: 0, sol: 0, meme: 0 },
    holdings: { btc: 0, sol: 0, meme: 0, defi: 0 },
    lifestyle: { home: 0, ride: 0, vault: 0, crew: 0 },
    edges: [],
    edgeOffers,
    pendingOfferWeek: edgeOffers[1] ? 1 : null,
    memeName: memeQueue[0].name, memePtr: 0,
    apy: 38, risk: 34, intel: 0.12,
    eventChoices, rolls, memeQueue,
    thisEvent: {
      title: "Opening bell",
      body: "You start with $1,000 virtual cash, a clean wallet, and a timeline full of people pretending they are early.",
      tone: "neutral",
    },
    history: [STARTING_CASH],
    feed: [],
    worstHit: null,
    lastHit: null, // "rug" | "hack" this week — drives UI juice
    ended: false, recorded: false, result: null, log: [],
  };

  if (opts.perk) applyPerk(s, opts.perk);
  s.intel = baseIntel(s);
  track(s, "run_start", { seed: seedLabel, mode });
  return s;
}

export function applyPerk(s, id) {
  if (id === "research-desk") s.intelBase = 0.45;
  if (id === "hardware-wallet") s.security += 20;
  if (id === "steady-hands") s.stressResist = 0.3;
  if (id === "seed-capital") s.cash += 500;
}

// ---------------------------------------------------------------------------
// Derived helpers
// ---------------------------------------------------------------------------
export function hasEdge(id, s) { return s.edges.includes(id); }
function repGain(s, n) { return n * (hasEdge("kol", s) ? 2 : 1); }

export function tierOf(cat, s) { return LIFESTYLE[cat].tiers[s.lifestyle[cat]]; }
export function nextTier(cat, s) { return LIFESTYLE[cat].tiers[s.lifestyle[cat] + 1] || null; }
export function lifestyleValue(s) { return CATEGORIES.reduce((sum, c) => sum + tierOf(c, s).resale, 0); }
export function effectiveMaxEnergy(s) {
  return Math.min(6, MAX_ENERGY + tierOf("home", s).energy + (hasEdge("paper-hands", s) ? 1 : 0));
}
export function effectiveSecurity(s) { return clamp(s.security + tierOf("vault", s).sec, 0, 100); }
function crewIntelBonus(s) { return s.lifestyle.crew >= 1 ? 0.18 : 0; }
function crewFeeFactor(s) { return s.lifestyle.crew >= 2 ? 0.5 : 1; }
function crewIncome(s) { return s.lifestyle.crew >= 3 ? s.cash * 0.012 : 0; }

export function baseIntel(s) {
  return clamp((s.intelBase || 0.12) + s.skill / 500 + crewIntelBonus(s) + (hasEdge("insider", s) ? 0.3 : 0), 0, 0.85);
}

export function netWorth(s) {
  return (
    s.cash +
    s.holdings.btc * s.prices.btc +
    s.holdings.sol * s.prices.sol +
    s.holdings.meme * s.prices.meme +
    s.holdings.defi +
    lifestyleValue(s)
  );
}
export function liquidWorth(s) { return netWorth(s) - lifestyleValue(s); }

export function tradeFee(s, isSell = false) {
  let f = clamp(0.02 - s.skill * 0.0001, 0.005, 0.02) * crewFeeFactor(s);
  if (hasEdge("market-maker", s)) f *= 0.5;
  if (isSell && hasEdge("diamond-hands", s)) f *= 2;
  return f;
}

export function rugProb(s) {
  if (s.holdings.meme <= 0) return 0;
  let p = (s.stress * 0.5 + s.risk * 0.6 - s.skill * 0.6 - effectiveSecurity(s) * 0.4) / 260;
  if (hasEdge("degen", s)) p += 0.1;
  if (hasEdge("security-pilled", s)) p *= 0.6;
  if (hasEdge("diamond-hands", s)) p *= 0.7;
  return clamp(p, 0.02, 0.35);
}

export function hackProb(s) {
  if (s.holdings.defi <= 0) return 0;
  let p = (s.risk * 0.5 - effectiveSecurity(s) * 0.6 + 6) / 240;
  if (hasEdge("yield-farmer", s)) p += 0.05;
  if (hasEdge("security-pilled", s)) p *= 0.6;
  return clamp(p, 0.01, 0.25);
}

function addStress(s, n) {
  let d = n;
  if (d > 0) {
    d *= 1 - (s.stressResist || 0);
    if (hasEdge("paper-hands", s)) d *= 1.25;
  }
  const cap = hasEdge("cold-blooded", s) ? 60 : 100;
  s.stress = clamp(s.stress + d, 0, cap);
}

function spendEnergy(s, cost = 1) {
  if (s.ended || s.energy < cost) return false;
  s.energy -= cost;
  return true;
}

function noteWorstHit(s, type, amount) {
  if (!s.worstHit || amount > s.worstHit.amount) s.worstHit = { type, amount, week: s.week };
}

export function buyAmount(s, frac) { return Math.max(0, Math.min(s.cash, s.cash * frac)); }

// ---------------------------------------------------------------------------
// Player actions (mutate `s`, no rendering)
// ---------------------------------------------------------------------------
export function research(s) {
  if (!spendEnergy(s)) return;
  s.skill = clamp(s.skill + 6, 0, 100);
  s.reputation = clamp(s.reputation + repGain(s, 2), 0, 100);
  addStress(s, -7);
  s.intel = clamp(s.intel + 0.35 * (1 + s.skill / 120), 0, 1);
  pushFeed(s, "Research sharpened the read on next week's signal.", "neutral");
  track(s, "action", { type: "research" });
}

export function buyAsset(s, key, frac) {
  if (!spendEnergy(s)) return;
  const amount = buyAmount(s, frac);
  if (amount <= 0) return;
  const units = (amount * (1 - tradeFee(s))) / s.prices[key];
  s.holdings[key] += units;
  s.cash -= amount;
  pushFeed(s, `Bought ${money(amount)} of ${key.toUpperCase()}. Slower, but the floor is less cursed.`, "good");
  track(s, "action", { type: `buy_${key}`, amount: Math.round(amount) });
}

export function buyMeme(s, frac) {
  if (!spendEnergy(s)) return;
  const amount = buyAmount(s, frac);
  if (amount <= 0) return;
  const kolBoost = hasEdge("kol", s) ? 1 + Math.min(s.reputation, 100) / 200 : 1;
  const units = (amount * (1 - tradeFee(s)) * kolBoost) / s.prices.meme;
  s.holdings.meme += units;
  s.cash -= amount;
  addStress(s, 2);
  s.reputation = clamp(s.reputation + repGain(s, 1), 0, 100);
  pushFeed(s, `Aped ${money(amount)} into ${s.memeName}. Your pulse is now part of the chart.`, "warning");
  track(s, "action", { type: "buy_meme", amount: Math.round(amount) });
}

export function buyDefi(s, frac) {
  if (!spendEnergy(s)) return;
  const amount = buyAmount(s, frac);
  if (amount <= 0) return;
  s.holdings.defi += amount * (1 - tradeFee(s));
  s.cash -= amount;
  s.skill = clamp(s.skill + 1, 0, 100);
  pushFeed(s, `Deposited ${money(amount)} into a DeFi vault at ${Math.round(s.apy)}% APY.`, "good");
  track(s, "action", { type: "deposit_defi", amount: Math.round(amount) });
}

export function secure(s) {
  if (!spendEnergy(s)) return;
  const cost = Math.min(s.cash, 60 + s.security * 2);
  s.cash -= cost;
  s.security = clamp(s.security + 14, 0, 100);
  addStress(s, -6);
  pushFeed(s, `Spent ${money(cost)} hardening the wallet. Future exploits have less room to breathe.`, "neutral");
  track(s, "action", { type: "secure" });
}

export function sell(s, key, fraction) {
  if (s.ended) return;
  if (key === "defi") {
    const amount = s.holdings.defi * fraction;
    if (amount <= 0) return;
    s.holdings.defi -= amount;
    s.cash += amount * (1 - tradeFee(s, true));
    addStress(s, -3);
    pushFeed(s, `Withdrew ${money(amount)} from DeFi. Survival is not optional.`, "good");
  } else {
    const units = s.holdings[key] * fraction;
    if (units <= 0) return;
    const gross = units * s.prices[key];
    s.holdings[key] -= units;
    s.cash += gross * (1 - tradeFee(s, true));
    addStress(s, -3);
    pushFeed(s, `Took ${money(gross)} off the table (${key.toUpperCase()}). Screenshots optional; survival is not.`, "good");
  }
  track(s, "action", { type: `sell_${key}`, fraction });
}

export function buyLifestyle(s, cat) {
  if (s.ended) return false;
  const cur = tierOf(cat, s);
  const nxt = nextTier(cat, s);
  if (!nxt) return false;
  if (netWorth(s) < nxt.gate) return false;
  const netCost = Math.max(0, nxt.cost - cur.resale);
  if (s.cash < netCost) return false;
  const prevEnergyMax = effectiveMaxEnergy(s);
  s.cash -= netCost;
  s.lifestyle[cat] += 1;
  const newEnergyMax = effectiveMaxEnergy(s);
  if (newEnergyMax > prevEnergyMax) s.energy += newEnergyMax - prevEnergyMax;
  pushFeed(s, `Acquired ${nxt.name} ${nxt.icon}. ${nxt.flavor}`, "good");
  track(s, "buy_lifestyle", { cat, tier: s.lifestyle[cat] });
  return true;
}

export function pickEdge(s, id) {
  if (!s.pendingOfferWeek) return;
  s.edges.push(id);
  delete s.edgeOffers[s.pendingOfferWeek];
  s.pendingOfferWeek = null;
  if (id === "paper-hands") s.energy = effectiveMaxEnergy(s);
  const e = EDGES.find((x) => x.id === id);
  pushFeed(s, `Edge acquired: ${e.name} ${e.icon}.`, "good");
  track(s, "pick_edge", { id });
}

// ---------------------------------------------------------------------------
// Week resolution
// ---------------------------------------------------------------------------
export function advanceWeek(s, pathIndex = 0) {
  if (s.ended || s.pendingOfferWeek) return false;
  const idx = s.week - 1;
  const choices = s.eventChoices[idx];
  const event = choices[Math.min(Math.max(pathIndex, 0), choices.length - 1)];
  const roll = s.rolls[idx];
  const act = actForWeek(s.week);
  s.lastHit = null;

  const moveBtc = event.btc + roll.noiseBtc + act.drift.btc;
  const moveSol = event.sol + roll.noiseSol + act.drift.sol;
  const moveMeme = event.meme + roll.noiseMeme + act.drift.meme;
  s.prices.btc *= 1 + moveBtc;
  s.prices.sol *= 1 + moveSol;
  s.prices.meme = Math.max(0.000001, s.prices.meme * (1 + moveMeme));
  s.moves = { btc: moveBtc, sol: moveSol, meme: moveMeme };
  s.apy = clamp(s.apy + event.apy, 8, 160);
  s.risk = clamp(s.risk + event.risk - effectiveSecurity(s) / 15, 4, 98);
  pushFeed(s, event.title, toneOf(event.tone));

  if (hasEdge("leverage", s)) {
    s.holdings.btc *= 1 + moveBtc * 0.4;
    s.holdings.sol *= 1 + moveSol * 0.4;
    s.holdings.meme *= 1 + moveMeme * 0.4;
  }
  if (hasEdge("whale-watcher", s)) {
    if (moveBtc >= moveSol && moveBtc > 0) s.holdings.btc *= 1.05;
    else if (moveSol > 0) s.holdings.sol *= 1.05;
  }
  if (hasEdge("degen", s) && moveMeme > 0) s.holdings.meme *= 1 + Math.min(moveMeme, 1) * 0.4;
  if (hasEdge("contrarian", s) && act.id === "bear") {
    if (moveBtc < 0) s.holdings.btc *= 1 + Math.abs(moveBtc) * 0.5;
    if (moveSol < 0) s.holdings.sol *= 1 + Math.abs(moveSol) * 0.5;
  }
  if (hasEdge("diversified", s) && s.holdings.btc > 0 && s.holdings.sol > 0 && s.holdings.defi > 0) {
    s.holdings.btc *= 1.05;
    s.holdings.sol *= 1.05;
    s.holdings.defi *= 1.05;
  }

  if (s.holdings.defi > 0) {
    const apy = s.apy * (hasEdge("yield-farmer", s) ? 1.4 : 1);
    s.holdings.defi *= 1 + apy / 100 / 24;
    if (roll.hack < hackProb(s)) {
      const lossRate = 0.12 + (roll.hack / Math.max(hackProb(s), 0.0001)) * 0.22;
      const loss = s.holdings.defi * lossRate;
      s.holdings.defi -= loss;
      addStress(s, 16);
      noteWorstHit(s, "hack", loss);
      s.lastHit = "hack";
      if (hasEdge("tax-loss", s)) s.skill = clamp(s.skill + 4, 0, 100);
      pushFeed(s, `DeFi exploit drained ${money(loss)} from your vaults. Security mattered.`, "danger");
    }
  }

  if (s.holdings.meme > 0 && roll.rug < rugProb(s)) {
    const loss = s.holdings.meme * s.prices.meme * 0.93;
    noteWorstHit(s, "rug", loss);
    s.holdings.meme = 0;
    s.reputation = clamp(s.reputation - 5, 0, 100);
    s.lastHit = "rug";
    addStress(s, 18);
    if (hasEdge("tax-loss", s)) s.skill = clamp(s.skill + 4, 0, 100);
    pushFeed(s, `${s.memeName} rugged. ${money(loss)} gone. The chat renamed it a learning experience.`, "danger");
    respawnMeme(s);
  } else if (idx > 0 && idx % 6 === 0 && s.holdings.meme === 0) {
    respawnMeme(s);
  }

  // Exposure stress: holding volatile risk (especially meme) wears you down —
  // the pressure that forces profit-taking. Cash and majors are calm.
  const nw = Math.max(netWorth(s), 1);
  const exposure =
    ((s.holdings.meme * s.prices.meme) / nw) * 11 +
    (s.holdings.defi / nw) * 3 +
    Math.abs(moveMeme) * 5 +
    Math.max(0, event.risk) / 8 -
    s.skill / 30;
  addStress(s, Math.round(exposure));
  addStress(s, -5 - tierOf("home", s).calm); // a week of recovery
  s.reputation = clamp(s.reputation + repGain(s, tierOf("ride", s).rep), 0, 100);
  if (hasEdge("market-maker", s)) s.cash += netWorth(s) * 0.005;
  const income = crewIncome(s);
  if (income > 0) {
    s.cash += income;
    pushFeed(s, `Fund team booked ${money(income)} in management fees.`, "good");
  }

  s.thisEvent = event;
  s.week += 1;
  s.energy = effectiveMaxEnergy(s);
  s.intel = baseIntel(s);
  s.history.push(netWorth(s));
  track(s, "week_end", { week: s.week - 1, worth: Math.round(netWorth(s)) });

  checkEnd(s);
  if (!s.ended && s.edgeOffers[s.week]) s.pendingOfferWeek = s.week;
  return true;
}

function respawnMeme(s) {
  s.memePtr = Math.min(s.memePtr + 1, s.memeQueue.length - 1);
  const next = s.memeQueue[s.memePtr];
  s.memeName = next.name;
  s.prices.meme = next.price;
}

function checkEnd(s) {
  const worth = netWorth(s);
  if (worth >= MILLIONAIRE_TARGET) {
    finish(s, { win: true, title: "Crypto mogul", body: "Seven figures, an island, and mostly on purpose. The legend tier is yours.", epitaph: "Seven figures, mostly on purpose." });
  } else if (liquidWorth(s) <= BUST_FLOOR) {
    const cause = s.worstHit ? s.worstHit.type : "overexposed";
    finish(s, { win: false, title: "Liquidated", body: "The wallet is technically alive, but the dream needs a new seed phrase and a long walk.", lesson: LESSONS[cause] || LESSONS.default, epitaph: epitaphForCause(cause) });
  } else if (s.stress >= 100) {
    finish(s, { win: false, title: "Burnout", body: "You stared at the charts until the charts stared back. The body cashed out before the portfolio could.", lesson: LESSONS.burnout, epitaph: "Cause of death: burnout. The market never closes — you should have." });
  } else if (s.reputation <= 0) {
    finish(s, { win: false, title: "Exiled", body: "Every group chat went quiet at once. When your name becomes a warning label, the deals dry up.", lesson: LESSONS.exile, epitaph: "Cause of death: zero reputation. Nobody left to take the other side." });
  } else if (s.week > MAX_WEEKS) {
    const grew = worth > STARTING_CASH;
    const big = worth > STARTING_CASH * 5;
    finish(s, {
      win: big,
      title: big ? "Cycle survivor" : grew ? "Bear market graduate" : "Bear market tuition",
      body: big ? "You did not hit a million, but you beat the market and built a life." : "You learned expensive lessons with fake money — the best kind of expensive lessons.",
      lesson: big ? null : LESSONS.bear,
      epitaph: big ? "Beat the market, kept my head." : "Paid tuition to the market in fake money.",
    });
  }
}

function finish(s, result) {
  if (s.ended) return;
  s.ended = true;
  s.result = { ...result, worth: netWorth(s), rank: rank(s), week: Math.min(s.week, MAX_WEEKS) };
}

function epitaphForCause(cause) {
  if (cause === "rug") return "Cause of death: aped the top, forgot the exit.";
  if (cause === "hack") return "Cause of death: skipped wallet security, paid full price.";
  if (cause === "macro") return "Cause of death: all-in right before the flush.";
  return "Cause of death: too much conviction, not enough cash.";
}

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------
export function pathChoices(s) {
  return s.ended ? [] : s.eventChoices[s.week - 1] || [];
}

// Node archetype shown on a path card — gives each road character.
export function pathType(event) {
  if (event.risk >= 12 || Math.abs(event.meme) >= 0.5) return { icon: "⚡", name: "Volatile" };
  if (event.tone === "good") return { icon: "📈", name: "Momentum" };
  if (event.tone === "bad") return { icon: "🌩️", name: "Storm" };
  return { icon: "🌊", name: "Calm" };
}

export function telegraphFor(s, event) {
  if (s.ended || !event) return { title: "—", hint: "Run complete.", risk: "—", intel: 1 };
  const i = s.intel;
  const cat = event.category.toUpperCase();
  if (i < 0.25) return { title: "Unclear signal", hint: "Nothing confirmed yet. Research to sharpen the read.", risk: "?", intel: i, tone: event.tone };
  if (i < 0.6) {
    const dir = event.tone === "good" ? "bullish" : event.tone === "bad" ? "bearish" : "mixed";
    return { title: `${cat} ${dir} setup`, hint: `Something ${dir} building in ${cat.toLowerCase()}. Magnitude still fuzzy.`, risk: event.tone === "bad" ? "elevated" : "normal", intel: i, tone: event.tone };
  }
  const deltas = `Majors ${pct(event.btc)} · Meme ${pct(event.meme)} · APY ${event.apy >= 0 ? "+" : ""}${event.apy}`;
  return { title: event.title, hint: `${event.body}  (${deltas})`, risk: event.risk > 10 ? "high" : "normal", intel: i, revealed: true, tone: event.tone };
}

export function rank(s) {
  const worth = netWorth(s);
  let name = MILESTONES[0].name;
  for (const m of MILESTONES) if (worth >= m.at) name = m.name;
  return name;
}

export function milestoneProgress(s) {
  const worth = netWorth(s);
  let current = MILESTONES[0];
  let next = null;
  for (const m of MILESTONES) {
    if (worth >= m.at) current = m;
    else { next = m; break; }
  }
  const fill = next ? clamp((worth - current.at) / (next.at - current.at), 0, 1) : 1;
  return { current, next, fill };
}

// ---------------------------------------------------------------------------
// State helpers + formatting (shared by UI and sim)
// ---------------------------------------------------------------------------
export function pushFeed(s, message, tone = "neutral") { s.feed.unshift({ message, tone }); s.feed = s.feed.slice(0, 6); }
export function track(s, name, data = {}) { s.log.push({ t: name, week: s.week, ...data }); }
export function toneOf(tone) { return tone === "bad" ? "danger" : tone === "good" ? "good" : "neutral"; }
export function clamp(v, min, max) { return Math.min(max, Math.max(min, v)); }
export function money(value) {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: value >= 1000 ? 0 : 2 }).format(Math.max(0, value));
}
export function compact(value) {
  const v = Math.max(0, value);
  if (v >= 1e6) return `$${(v / 1e6).toFixed(2)}M`;
  if (v >= 1e3) return `$${(v / 1e3).toFixed(1)}k`;
  return `$${Math.round(v)}`;
}
export function pct(value) { return `${value >= 0 ? "+" : ""}${(value * 100).toFixed(1)}%`; }
