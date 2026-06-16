// The market cycle (Slay the Spire acts) and Edges (Balatro jokers).
// These two systems turn a flat run into a build-craft with an arc:
//   - ACTS give the 24 weeks a shape (Accumulation -> Bull -> Top -> Bear),
//     each with its own event bias and price drift, so "read the cycle" is a skill.
//   - EDGES are collectible rule-modifiers, offered 3-pick-1 at each act start,
//     that stack into a playstyle. Discovering synergies = replayability.

export const ACTS = [
  {
    id: "accumulation",
    name: "Accumulation",
    start: 1,
    end: 6,
    drift: { btc: 0.025, sol: 0.03, meme: 0.0 },
    bias: { good: 1, neutral: 2, bad: 1 },
    blurb: "Quiet, boring green. Build your base and read the room.",
  },
  {
    id: "bull",
    name: "Bull Run",
    start: 7,
    end: 12,
    drift: { btc: 0.15, sol: 0.18, meme: 0.2 },
    bias: { good: 3, neutral: 1, bad: 1 },
    blurb: "Euphoria and FOMO. Easy gains — and the urge to overextend.",
  },
  {
    id: "top",
    name: "The Top",
    start: 13,
    end: 18,
    drift: { btc: -0.01, sol: -0.015, meme: -0.04 },
    bias: { good: 2, neutral: 1, bad: 2 },
    blurb: "Distribution. Mixed signals. The smart money is quietly leaving.",
  },
  {
    id: "bear",
    name: "Bear Market",
    start: 19,
    end: 24,
    drift: { btc: -0.05, sol: -0.07, meme: -0.13 },
    bias: { good: 1, neutral: 1, bad: 3 },
    blurb: "The flush. Survival is the whole game. Cash is a position.",
  },
];

export function actIndexForWeek(week) {
  for (let i = 0; i < ACTS.length; i += 1) {
    if (week >= ACTS[i].start && week <= ACTS[i].end) return i;
  }
  return week < 1 ? 0 : ACTS.length - 1;
}

export function actForWeek(week) {
  return ACTS[actIndexForWeek(week)];
}

// One edge is offered at the start of each act.
export const EDGE_OFFER_WEEKS = [1, 7, 13, 19];

// Edge effects are implemented in game.js by id (kept data-clean here).
export const EDGES = [
  { id: "diamond-hands", name: "Diamond Hands", icon: "💎", desc: "Rug & hack risk hit 30% less often. But selling costs double fees." },
  { id: "insider", name: "Insider", icon: "🕵️", desc: "Begin every week with the next signal already partly revealed." },
  { id: "degen", name: "Degen", icon: "🎰", desc: "Meme upside +40% on green weeks. But rug risk +10%." },
  { id: "market-maker", name: "Market Maker", icon: "⚖️", desc: "Trading fees halved, plus a small spread income every week." },
  { id: "whale-watcher", name: "Whale Watcher", icon: "🐋", desc: "Each green week, your best major position gets an extra +5%." },
  { id: "tax-loss", name: "Tax-Loss Harvester", icon: "🧾", desc: "Every rug or hack you suffer grants +4 skill." },
  { id: "paper-hands", name: "Paper Hands", icon: "🧻", desc: "+1 energy every week. But stress builds 25% faster." },
  { id: "yield-farmer", name: "Yield Farmer", icon: "🌾", desc: "DeFi APY +40%. But hack risk +5%." },
  { id: "security-pilled", name: "Security-Pilled", icon: "🛡️", desc: "Rug & hack risk cut by 40%. The boring edge that saves runs." },
  { id: "contrarian", name: "Contrarian", icon: "🔄", desc: "In the bear market, your majors recover half of every drop." },
  { id: "kol", name: "KOL", icon: "📣", desc: "Reputation grows 2x; high rep buys you bigger meme entries." },
  { id: "cold-blooded", name: "Cold-Blooded", icon: "🧊", desc: "Stress can never rise above 60. Burnout is off the table." },
  { id: "leverage", name: "Leverage Junkie", icon: "⚡", desc: "Every weekly move on your bags is amplified 25% — both ways." },
  { id: "diversified", name: "Diversified", icon: "🧺", desc: "Hold BTC, SOL and DeFi at once and each gains an extra +5%/week." },
];
