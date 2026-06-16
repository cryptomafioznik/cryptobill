// Cross-run meta-progression, persisted in localStorage.
// This is the layer that turns a single toy run into a roguelite: every run
// leaves something behind (best score, unlocks, daily history).

const KEY = "cryptobill.meta.v1";

const DEFAULT_META = {
  runs: 0,
  bestNetWorth: 0,
  bestRank: "Wallet intern",
  seenTutorial: false,
  selectedPerk: null,
  dailyScores: {}, // { "daily-2026-06-08": 12345 }
};

export function loadMeta() {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return { ...DEFAULT_META };
    return { ...DEFAULT_META, ...JSON.parse(raw) };
  } catch (err) {
    return { ...DEFAULT_META };
  }
}

export function saveMeta(meta) {
  try {
    localStorage.setItem(KEY, JSON.stringify(meta));
  } catch (err) {
    /* storage blocked (private mode) — run is still playable, just not saved */
  }
}

// Fold a finished run into the meta record. Returns the updated meta.
export function recordRun(meta, { seedKey, worth, rank, isDaily }) {
  const next = { ...meta };
  next.runs += 1;
  if (worth > next.bestNetWorth) {
    next.bestNetWorth = Math.round(worth);
    next.bestRank = rank;
  }
  if (isDaily) {
    const prev = next.dailyScores[seedKey] || 0;
    if (worth > prev) {
      next.dailyScores = { ...next.dailyScores, [seedKey]: Math.round(worth) };
    }
  }
  saveMeta(next);
  return next;
}

export function unlockedPerkIds(meta, perks) {
  return perks.filter((p) => meta.bestNetWorth >= p.unlockAt).map((p) => p.id);
}
