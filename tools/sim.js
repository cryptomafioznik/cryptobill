// Headless balance harness. Plays strategy bots across many seeds against the
// real engine and reports the outcome distribution + death causes.
//   node tools/sim.js [seeds]

import * as E from "../src/engine.js";
import { actForWeek } from "../src/cycle.js";

const N = Number(process.argv[2]) || 500;

function sellAll(s) {
  E.sell(s, "btc", 1);
  E.sell(s, "sol", 1);
  E.sell(s, "meme", 1);
  E.sell(s, "defi", 1);
}
const calmer = () => 0;
const spicier = (s, c) => (c.length > 1 ? 1 : 0);
const evScore = (e) => (e.tone === "bad" ? -1 : 0) + e.btc + e.sol + e.meme * 0.4;

// Each strategy: edgePref, choosePath(s,choices)->index, act(s, chosenEvent).
const STRATEGIES = [
  {
    name: "hodler",
    note: "naive buy-and-hold BTC, never sells",
    edgePref: ["diversified", "whale-watcher", "leverage", "market-maker"],
    choosePath: calmer,
    act(s) { while (s.energy > 0 && s.cash > 0) E.buyAsset(s, "btc", 1); },
  },
  {
    name: "safe-cycle",
    note: "reads the cycle: buy the bull, sell the top, cash the bear",
    edgePref: ["contrarian", "whale-watcher", "diamond-hands", "market-maker"],
    choosePath: calmer,
    act(s) {
      const act = actForWeek(s.week);
      if (act.id === "bear") { sellAll(s); return; }
      if (act.id === "top") { E.sell(s, "btc", 0.5); E.sell(s, "sol", 0.5); return; }
      if (s.security < 25 && s.energy > 0) E.secure(s);
      while (s.energy > 0 && s.cash > 0) E.buyAsset(s, "btc", 0.5);
    },
  },
  {
    name: "degen",
    note: "apes meme to the max, never secures or rests",
    edgePref: ["degen", "leverage", "kol", "whale-watcher"],
    choosePath: spicier,
    act(s) { while (s.energy > 0 && s.cash > 0) E.buyMeme(s, 1); },
  },
  {
    name: "defi-farmer",
    note: "farms DeFi yield, hardens wallet",
    edgePref: ["yield-farmer", "security-pilled", "market-maker", "diversified"],
    choosePath: calmer,
    act(s) {
      if (s.security < 50 && s.energy > 0) E.secure(s);
      while (s.energy > 0 && s.cash > 0) E.buyDefi(s, 1);
    },
  },
  {
    name: "diversified",
    note: "holds BTC+SOL+DeFi for the synergy",
    edgePref: ["diversified", "market-maker", "whale-watcher", "security-pilled"],
    choosePath: calmer,
    act(s) {
      if (s.energy > 0 && s.cash > 0) E.buyAsset(s, "btc", 0.34);
      if (s.energy > 0 && s.cash > 0) E.buyAsset(s, "sol", 0.5);
      if (s.energy > 0 && s.cash > 0) E.buyDefi(s, 1);
    },
  },
  {
    name: "meme-sniper",
    note: "picks the pump path, rides at 70%, banks weekly, low-rug build",
    edgePref: ["security-pilled", "whale-watcher", "market-maker", "diamond-hands"],
    choosePath: (s, c) => (c.length > 1 && c[1].meme > c[0].meme ? 1 : 0),
    act(s, ev) {
      E.sell(s, "meme", 1);
      if (s.security < 35 && s.energy > 0) E.secure(s);
      if (actForWeek(s.week).id === "bear") { sellAll(s); return; }
      if (ev.meme > 0.25 && s.energy > 0) E.buyMeme(s, 0.7);
      else while (s.energy > 0 && s.cash > 0) E.buyAsset(s, "btc", 0.5);
    },
  },
  {
    name: "cycle-master",
    note: "OMNISCIENT ceiling: picks the best path, locks meme weekly",
    edgePref: ["leverage", "whale-watcher", "contrarian", "diversified"],
    choosePath: (s, c) => (c.length > 1 && evScore(c[1]) > evScore(c[0]) ? 1 : 0),
    act(s, ev) {
      E.sell(s, "meme", 1);
      const act = actForWeek(s.week);
      if (act.id === "bear" || ev.tone === "bad") { sellAll(s); return; }
      if (ev.meme > 0.3) { if (s.energy > 0) E.buyMeme(s, 0.6); return; }
      while (s.energy > 0 && s.cash > 0) E.buyAsset(s, ev.sol > ev.btc ? "sol" : "btc", 1);
    },
  },
  {
    name: "moonshot",
    note: "all-in meme through the bull, exit at the top — high bust, $1M shot",
    edgePref: ["degen", "leverage", "whale-watcher", "kol"],
    choosePath: spicier,
    act(s) {
      const act = actForWeek(s.week);
      if (act.id === "top" || act.id === "bear") { sellAll(s); return; }
      if (act.id === "bull") { sellAll(s); while (s.energy > 0 && s.cash > 0) E.buyMeme(s, 1); return; }
      while (s.energy > 0 && s.cash > 0) E.buyAsset(s, "btc", 1);
    },
  },
  {
    name: "random",
    note: "floor: random actions & paths",
    edgePref: [],
    choosePath: (s, c) => Math.floor(Math.random() * c.length),
    act(s) {
      const moves = ["btc", "sol", "meme", "defi", "research", "secure"];
      while (s.energy > 0) {
        const m = moves[Math.floor(Math.random() * moves.length)];
        if (m === "research") E.research(s);
        else if (m === "secure") E.secure(s);
        else if (m === "meme") E.buyMeme(s, 0.5);
        else if (m === "defi") E.buyDefi(s, 0.5);
        else E.buyAsset(s, m, 0.5);
      }
    },
  },
];

function playRun(seedKey, strat) {
  const s = E.makeRun(seedKey, seedKey, { mode: "free" });
  let guard = 0;
  while (!s.ended && guard < 60) {
    guard += 1;
    if (s.pendingOfferWeek) {
      const offered = s.edgeOffers[s.pendingOfferWeek];
      const pick = strat.edgePref.find((id) => offered.includes(id)) || offered[Math.floor(Math.random() * offered.length)];
      E.pickEdge(s, pick);
    }
    const choices = E.pathChoices(s);
    const pathIndex = strat.choosePath ? strat.choosePath(s, choices) : 0;
    strat.act(s, choices[pathIndex] || choices[0]);
    if (!E.advanceWeek(s, pathIndex)) break;
  }
  return s;
}

function pctile(sorted, p) { return sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))]; }
function fmt(v) {
  if (v >= 1e6) return `${(v / 1e6).toFixed(2)}M`;
  if (v >= 1e3) return `${(v / 1e3).toFixed(1)}k`;
  return `${Math.round(v)}`;
}
const pad = (s, n) => String(s).padEnd(n);
const padL = (s, n) => String(s).padStart(n);

const seeds = Array.from({ length: N }, (_, i) => `sim-${i}`);
const MILES = [["$5k", 5000], ["$15k", 15000], ["$50k", 50000]];

console.log(`\nCryptoBill balance sim — ${N} seeds/strategy\n${"=".repeat(100)}`);
console.log(pad("strategy", 14) + padL("median", 9) + padL("p90", 9) + padL("max", 10) + padL("end%", 8) + padL("$1M%", 7) + "   " + MILES.map((m) => padL(m[0] + "%", 7)).join("") + "   deaths");
console.log("-".repeat(100));

const summary = {};
for (const strat of STRATEGIES) {
  const worths = [];
  let died = 0;
  let mil = 0;
  const causes = {};
  for (const seed of seeds) {
    const s = playRun(seed, strat);
    worths.push(E.netWorth(s));
    if (E.netWorth(s) >= 1e6) mil += 1;
    const title = s.result ? s.result.title : "—";
    const isDeath = ["Liquidated", "Burnout", "Exiled"].includes(title);
    if (isDeath) { died += 1; causes[title] = (causes[title] || 0) + 1; }
  }
  worths.sort((a, b) => a - b);
  summary[strat.name] = { median: pctile(worths, 50), p90: pctile(worths, 90), max: worths[worths.length - 1], died: (died / N) * 100, mil: (mil / N) * 100 };
  const milesPct = MILES.map(([, v]) => `${Math.round((worths.filter((w) => w >= v).length / N) * 100)}%`);
  const deathStr = Object.entries(causes).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${k[0]}${Math.round((v / N) * 100)}%`).join(" ") || "—";
  console.log(
    pad(strat.name, 14) + padL(fmt(summary[strat.name].median), 9) + padL(fmt(summary[strat.name].p90), 9) + padL(fmt(summary[strat.name].max), 10) +
      padL(`${((died / N) * 100).toFixed(0)}%`, 8) + padL(`${((mil / N) * 100).toFixed(1)}%`, 7) + "   " + milesPct.map((p) => padL(p, 7)).join("") + "   " + deathStr,
  );
}

console.log("-".repeat(100));
console.log("  death key: L=Liquidated  B=Burnout  E=Exiled\n");
for (const s of STRATEGIES) console.log(`  ${pad(s.name, 14)} ${s.note}`);

const skilled = STRATEGIES.filter((s) => s.name !== "random" && s.name !== "cycle-master").map((s) => summary[s.name].median);
const bestMed = Math.max(...skilled);
const anyMil = STRATEGIES.some((s) => summary[s.name].mil > 0);
const tooEasyMil = STRATEGIES.some((s) => s.name !== "cycle-master" && summary[s.name].mil > 8);
console.log("\nDiagnosis:");
console.log(`  • $1M reachable: ${anyMil ? "yes" : "NO"}${tooEasyMil ? "  ⚠ too common" : ""}`);
console.log(`  • cycle-master(omniscient) median ${fmt(summary["cycle-master"].median)} vs best-real ${fmt(bestMed)} — foresight edge ${(summary["cycle-master"].median / Math.max(bestMed, 1)).toFixed(1)}x`);
console.log(`  • careful play (safe-cycle/diversified) death rate: ${summary["safe-cycle"].died.toFixed(0)}% / ${summary["diversified"].died.toFixed(0)}%  (should be low)\n`);
