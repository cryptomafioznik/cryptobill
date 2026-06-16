// View layer: DOM wiring + rendering. All game logic lives in engine.js, which
// this drives. Keeping them separate is what lets tools/sim.js balance the real
// game headlessly.
import { dailySeed, freeSeed } from "./rng.js";
import { PERKS, TUTORIAL } from "./content.js";
import { ACTS, EDGES, actIndexForWeek } from "./cycle.js";
import { loadMeta, saveMeta, recordRun, unlockedPerkIds } from "./meta.js";
import { buildRecapCanvas, resultText } from "./share.js";
import { LIFESTYLE, CATEGORIES, MOGUL_LEVELS, mogulLevel, sceneSVG } from "./empire.js";
import * as E from "./engine.js";
import * as FX from "./fx.js";

const { money, compact, pct, MAX_WEEKS } = E;

let meta = loadMeta();
let mode = "daily";
let state = null;
let currentFreeSeed = freeSeed();
let betFraction = 0.5;

// ---------------------------------------------------------------------------
// Run lifecycle
// ---------------------------------------------------------------------------
function selectedPerk() {
  if (mode !== "free" || !meta.selectedPerk) return null;
  return unlockedPerkIds(meta, PERKS).includes(meta.selectedPerk) ? meta.selectedPerk : null;
}

function startRun() {
  const seedKey = mode === "daily" ? dailySeed() : `free-${currentFreeSeed}`;
  const seedLabel = mode === "daily" ? dailySeed().replace("daily-", "") : currentFreeSeed;
  state = E.makeRun(seedKey, seedLabel, { mode, perk: selectedPerk() });
  els.endingBox.hidden = true;
  els.empireModal.hidden = true;
  els.edgeModal.hidden = true;
  resetTweens();
  render();
  if (state.pendingOfferWeek) openEdgeOffer();
}

function onNextWeek(pathIndex) {
  const before = E.netWorth(state);
  const rankBefore = E.rank(state);
  if (!E.advanceWeek(state, pathIndex)) return;
  const after = E.netWorth(state);
  const delta = after - before;

  // Juice: react to what just happened this week.
  if (state.lastHit) {
    FX.play("rug");
    FX.shake(els.appShell);
    FX.haptic([35, 40, 35]);
    FX.toast(state.lastHit === "rug" ? "Rugged 💀" : "Vault exploited 💀", "bad");
  } else {
    FX.play(delta >= 0 ? "profit" : "loss");
    FX.toast(`${state.thisEvent.title}   ${delta >= 0 ? "+" : "−"}${compact(Math.abs(delta))}`, delta >= 0 ? "good" : "bad");
  }
  const rankAfter = E.rank(state);
  if (rankAfter !== rankBefore && after > before) {
    FX.play("levelup");
    FX.haptic(60);
    FX.toast(`New rank — ${rankAfter} 🎉`, "celebrate", 3200);
  }

  if (state.ended && !state.recorded) {
    state.recorded = true;
    meta = recordRun(meta, { seedKey: state.seedKey, worth: state.result.worth, rank: state.result.rank, isDaily: state.mode === "daily" });
    setTimeout(() => FX.play(state.result.win ? "win" : "loss"), 220);
    showEnding(state);
  } else if (state.pendingOfferWeek) {
    openEdgeOffer();
  }
  render();
}

const uiActions = {
  research: () => E.research(state),
  btc: () => E.buyAsset(state, "btc", betFraction),
  sol: () => E.buyAsset(state, "sol", betFraction),
  meme: () => E.buyMeme(state, betFraction),
  defi: () => E.buyDefi(state, betFraction),
  secure: () => E.secure(state),
};

function doAction(name) {
  const energyBefore = state.energy;
  uiActions[name]?.();
  if (state.energy < energyBefore) FX.play(name === "research" || name === "secure" ? "click" : "buy");
  render();
}
function doSell(key, frac) {
  const cashBefore = state.cash;
  E.sell(state, key, frac);
  if (state.cash > cashBefore) FX.play("sell");
  render();
}
function doBuyLifestyle(cat) {
  if (E.buyLifestyle(state, cat)) {
    FX.play("profit");
    FX.toast(`Acquired ${E.tierOf(cat, state).name} ${E.tierOf(cat, state).icon}`, "celebrate");
  }
  renderEmpire();
  render();
}

// ---------------------------------------------------------------------------
// Edge draft
// ---------------------------------------------------------------------------
function openEdgeOffer() {
  const choiceIds = state.edgeOffers[state.pendingOfferWeek];
  els.edgeOfferTitle.textContent = `${ACTS[actIndexForWeek(state.week)].name} — choose an Edge`;
  els.edgeChoices.innerHTML = choiceIds
    .map((id) => {
      const e = EDGES.find((x) => x.id === id);
      return `<button class="edge-card" data-edge="${id}"><b class="edge-ic">${e.icon}</b><strong>${escapeHtml(e.name)}</strong><em>${escapeHtml(e.desc)}</em></button>`;
    })
    .join("");
  els.edgeModal.hidden = false;
}

function pickEdge(id) {
  E.pickEdge(state, id);
  els.edgeModal.hidden = true;
  FX.play("levelup");
  const e = EDGES.find((x) => x.id === id);
  FX.toast(`Edge equipped — ${e.name} ${e.icon}`, "celebrate");
  render();
}

// ---------------------------------------------------------------------------
// DOM
// ---------------------------------------------------------------------------
const els = {};
const IDS = [
  "modeDaily", "modeFree", "seedLabel", "newSeedButton",
  "weekLabel", "netWorthLabel", "newRunButton", "muteButton",
  "cycleBar", "actName", "actBlurb",
  "btcPrice", "solPrice", "memePrice", "memeTicker", "btcMove", "solMove", "memeMove", "apyLabel", "riskLabel",
  "eventTitle", "eventBody", "feedList",
  "portfolioLabel", "cashLabel", "milestoneNow", "milestoneNext", "milestoneFill",
  "worthChart",
  "btcValue", "solValue", "memeValue", "defiValue", "memeRowName",
  "betButtons",
  "avatarScene", "mogulLevel", "placeLabel", "rankLabel", "flexStrip", "empireButton", "edgesStrip",
  "skillLabel", "repLabel", "securityLabel", "stressLabel",
  "skillMeter", "repMeter", "securityMeter", "stressMeter",
  "energyLabel", "pathChoices", "intelFill",
  "rugRisk", "hackRisk",
  "perksPanel", "perksList", "perksNote", "bestStat", "runsStat",
  "endingBox", "endingTitle", "endingBody", "endingLesson", "endingFlex",
  "downloadRecap", "copyResult", "shareRecap", "playAgain",
  "empireModal", "empireNet", "empireList", "empireClose",
  "edgeModal", "edgeOfferTitle", "edgeChoices",
  "tutorialModal", "tutorialBody", "tutorialClose",
];

function init() {
  IDS.forEach((id) => (els[id] = document.getElementById(id)));
  els.appShell = document.querySelector(".app-shell");

  FX.setMuted(!!meta.muted);
  syncMuteButton();
  els.muteButton.addEventListener("click", () => {
    meta.muted = !FX.isMuted();
    FX.setMuted(meta.muted);
    saveMeta(meta);
    syncMuteButton();
  });

  document.querySelectorAll("[data-action]").forEach((b) => b.addEventListener("click", () => doAction(b.dataset.action)));
  document.querySelectorAll("[data-sell]").forEach((b) => b.addEventListener("click", () => doSell(b.dataset.sell, Number(b.dataset.frac))));
  document.querySelectorAll("[data-bet]").forEach((b) => b.addEventListener("click", () => { betFraction = Number(b.dataset.bet); render(); }));

  els.pathChoices.addEventListener("click", (e) => {
    const b = e.target.closest("[data-path]");
    if (b && !b.disabled) onNextWeek(Number(b.dataset.path));
  });
  els.newRunButton.addEventListener("click", startRun);
  els.modeDaily.addEventListener("click", () => setMode("daily"));
  els.modeFree.addEventListener("click", () => setMode("free"));
  els.newSeedButton.addEventListener("click", () => { currentFreeSeed = freeSeed(); if (mode === "free") startRun(); else render(); });
  els.playAgain.addEventListener("click", startRun);
  els.downloadRecap.addEventListener("click", downloadRecap);
  els.copyResult.addEventListener("click", copyResult);
  els.shareRecap.addEventListener("click", shareRecap);
  els.tutorialClose.addEventListener("click", closeTutorial);

  els.empireButton.addEventListener("click", openEmpire);
  els.empireClose.addEventListener("click", () => (els.empireModal.hidden = true));
  els.empireModal.addEventListener("click", (e) => { if (e.target === els.empireModal) els.empireModal.hidden = true; });
  els.empireList.addEventListener("click", (e) => { const b = e.target.closest("[data-buy]"); if (b) doBuyLifestyle(b.dataset.buy); });
  els.edgeChoices.addEventListener("click", (e) => { const b = e.target.closest("[data-edge]"); if (b) pickEdge(b.dataset.edge); });

  if (!meta.seenTutorial) openTutorial();
  startRun();
}

function setMode(next) { mode = next; startRun(); }

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------
function render() {
  const s = state;
  const worth = E.netWorth(s);

  els.modeDaily.classList.toggle("active", mode === "daily");
  els.modeFree.classList.toggle("active", mode === "free");
  els.newSeedButton.hidden = mode !== "free";
  els.seedLabel.textContent = s.seedLabel;

  els.weekLabel.textContent = `${Math.min(s.week, MAX_WEEKS)}/${MAX_WEEKS}`;
  FX.setNum(els.netWorthLabel, worth, compact);
  FX.setNum(els.portfolioLabel, worth, money);
  FX.setNum(els.cashLabel, s.cash, money);

  const actIdx = actIndexForWeek(s.week);
  els.cycleBar.querySelectorAll(".cycle-seg").forEach((seg, i) => {
    seg.classList.toggle("active", i === actIdx);
    seg.classList.toggle("done", i < actIdx);
  });
  els.actName.textContent = ACTS[actIdx].name;
  els.actBlurb.textContent = ACTS[actIdx].blurb;

  FX.setNum(els.btcPrice, s.prices.btc, money);
  FX.setNum(els.solPrice, s.prices.sol, money);
  els.memePrice.textContent = `$${s.prices.meme.toFixed(5)}`;
  els.memeTicker.textContent = s.memeName;
  renderMove(els.btcMove, s.moves.btc);
  renderMove(els.solMove, s.moves.sol);
  renderMove(els.memeMove, s.moves.meme);
  els.apyLabel.textContent = `${Math.round(s.apy)}%`;
  els.riskLabel.textContent = `Risk ${Math.round(s.risk)}`;

  els.eventTitle.textContent = s.thisEvent.title;
  els.eventBody.textContent = s.thisEvent.body;

  const mp = E.milestoneProgress(s);
  els.milestoneNow.textContent = mp.current.name;
  els.milestoneNext.textContent = mp.next ? `Next: ${mp.next.name} · ${compact(mp.next.at)}` : "Max tier reached";
  els.milestoneFill.style.width = `${Math.round(mp.fill * 100)}%`;

  FX.setNum(els.btcValue, s.holdings.btc * s.prices.btc, money);
  FX.setNum(els.solValue, s.holdings.sol * s.prices.sol, money);
  FX.setNum(els.memeValue, s.holdings.meme * s.prices.meme, money);
  FX.setNum(els.defiValue, s.holdings.defi, money);
  els.memeRowName.textContent = `${s.memeName} plays`;

  document.querySelectorAll("[data-bet]").forEach((b) => b.classList.toggle("active", Number(b.dataset.bet) === betFraction));
  const spend = E.buyAmount(s, betFraction);
  setActionPreview("btc", `Spend ${money(spend)}`);
  setActionPreview("sol", `Spend ${money(spend)}`);
  setActionPreview("meme", `Spend ${money(spend)}`);
  setActionPreview("defi", `Stake ${money(spend)}`);
  setActionPreview("secure", `Cost ${money(Math.min(s.cash, 60 + s.security * 2))}`);

  const level = mogulLevel(worth);
  els.avatarScene.innerHTML = sceneSVG(level);
  els.mogulLevel.textContent = `LVL ${level + 1}`;
  els.placeLabel.textContent = MOGUL_LEVELS[level].place;
  els.rankLabel.textContent = E.rank(s);
  els.flexStrip.innerHTML = CATEGORIES.map((c) => {
    const t = E.tierOf(c, s);
    return `<div class="flex-chip" title="${LIFESTYLE[c].label}: ${escapeHtml(t.name)}"><b>${t.icon}</b><em>${escapeHtml(t.name)}</em></div>`;
  }).join("");
  els.edgesStrip.innerHTML = s.edges.length
    ? s.edges.map((id) => { const e = EDGES.find((x) => x.id === id); return `<span class="edge-chip" title="${escapeHtml(`${e.name}: ${e.desc}`)}">${e.icon}</span>`; }).join("")
    : `<span class="edges-empty">Earn an Edge at each market phase.</span>`;

  els.skillLabel.textContent = Math.round(s.skill);
  els.repLabel.textContent = Math.round(s.reputation);
  els.securityLabel.textContent = Math.round(E.effectiveSecurity(s));
  els.stressLabel.textContent = Math.round(s.stress);
  els.skillMeter.value = s.skill;
  els.repMeter.value = s.reputation;
  els.securityMeter.value = E.effectiveSecurity(s);
  els.stressMeter.value = s.stress;
  els.energyLabel.textContent = `${s.energy}/${E.effectiveMaxEnergy(s)}`;

  // Branching week-map: two path cards, each telegraphed at current intel.
  const choices = E.pathChoices(s);
  els.intelFill.style.width = `${Math.round(s.intel * 100)}%`;
  els.pathChoices.innerHTML = choices
    .map((ev, i) => {
      const tg = E.telegraphFor(s, ev);
      const pt = E.pathType(ev);
      const riskCls = tg.risk === "high" || tg.risk === "elevated" ? "danger" : tg.revealed ? "ok" : "";
      return `<button class="path-card" data-path="${i}" ${s.ended ? "disabled" : ""}>
        <span class="path-type">${pt.icon} ${pt.name}</span>
        <strong>${escapeHtml(tg.title)}</strong>
        <em class="path-risk ${riskCls}">Risk: ${tg.risk}</em></button>`;
    })
    .join("");

  // Two-sided failure: warn as stress / reputation approach their death lines.
  els.stressLabel.classList.toggle("hot", s.stress >= 80);
  els.repLabel.classList.toggle("hot", s.reputation <= 3);

  const rug = E.rugProb(s);
  const hack = E.hackProb(s);
  els.rugRisk.textContent = s.holdings.meme > 0 ? `${Math.round(rug * 100)}%` : "—";
  els.rugRisk.classList.toggle("hot", s.holdings.meme > 0 && rug > 0.25);
  els.hackRisk.textContent = s.holdings.defi > 0 ? `${Math.round(hack * 100)}%` : "—";
  els.hackRisk.classList.toggle("hot", s.holdings.defi > 0 && hack > 0.2);

  document.querySelectorAll("[data-action]").forEach((b) => {
    const free = b.dataset.action === "research" || b.dataset.action === "secure";
    b.disabled = s.ended || s.energy <= 0 || (!free && s.cash <= 0);
  });
  document.querySelectorAll("[data-sell]").forEach((b) => {
    const key = b.dataset.sell;
    const has = key === "defi" ? s.holdings.defi > 0 : s.holdings[key] > 0;
    b.disabled = s.ended || !has;
  });

  els.feedList.innerHTML = s.feed.map((it) => `<div class="feed-item ${it.tone}">${escapeHtml(it.message)}</div>`).join("");

  renderPerks();
  els.bestStat.textContent = compact(meta.bestNetWorth);
  els.runsStat.textContent = String(meta.runs);

  if (!els.empireModal.hidden) renderEmpire();
  drawChart();
}

function setActionPreview(action, text) {
  const el = document.querySelector(`[data-action="${action}"] [data-preview]`);
  if (el) el.textContent = text;
}

function renderPerks() {
  const unlocked = unlockedPerkIds(meta, PERKS);
  els.perksPanel.hidden = mode !== "free";
  if (mode !== "free") return;
  if (unlocked.length === 0) {
    els.perksNote.textContent = "Reach $25k in a run to unlock your first loadout perk.";
    els.perksList.innerHTML = "";
    return;
  }
  els.perksNote.textContent = "Pick one, then start a new run to apply it. (Daily mode is always standard.)";
  els.perksList.innerHTML = PERKS.map((p) => {
    const isUnlocked = unlocked.includes(p.id);
    const active = meta.selectedPerk === p.id;
    return `<button class="perk ${active ? "active" : ""}" data-perk="${p.id}" ${isUnlocked ? "" : "disabled"} title="${escapeHtml(p.desc)}">
      <strong>${p.name}</strong><em>${isUnlocked ? escapeHtml(p.desc) : `Unlock at ${compact(p.unlockAt)}`}</em></button>`;
  }).join("");
  els.perksList.querySelectorAll("[data-perk]").forEach((b) =>
    b.addEventListener("click", () => {
      meta.selectedPerk = meta.selectedPerk === b.dataset.perk ? null : b.dataset.perk;
      saveMeta(meta);
      render();
    }),
  );
}

function openEmpire() { renderEmpire(); els.empireModal.hidden = false; }

function renderEmpire() {
  const s = state;
  const worth = E.netWorth(s);
  els.empireNet.textContent = compact(worth);
  els.empireList.innerHTML = CATEGORIES.map((cat) => {
    const cur = E.tierOf(cat, s);
    const owned = s.lifestyle[cat];
    const rows = LIFESTYLE[cat].tiers.map((t, i) => {
      const isOwned = i === owned;
      const isPassed = i < owned;
      const isNext = i === owned + 1;
      const locked = isNext && worth < t.gate;
      const netCost = isNext ? Math.max(0, t.cost - cur.resale) : 0;
      const cantAfford = isNext && s.cash < netCost;
      const cls = isOwned ? "owned" : isPassed ? "passed" : isNext ? "next" : "future";
      let status;
      if (isOwned) status = "OWNED";
      else if (isPassed) status = "✓";
      else if (isNext) status = locked ? `Unlock ${compact(t.gate)}` : compact(netCost);
      else status = compact(t.cost);
      const btn = isNext
        ? `<button class="buy-btn" data-buy="${cat}" ${locked || cantAfford || s.ended ? "disabled" : ""}>${locked ? "Locked" : cantAfford ? "Need cash" : "Buy"}</button>`
        : "";
      return `<div class="tier ${cls}"><b class="tier-ic">${t.icon}</b>
        <div class="tier-meta"><strong>${escapeHtml(t.name)}</strong><em>${escapeHtml(t.flavor)}</em></div>
        <div class="tier-end"><span class="tier-status">${status}</span>${btn}</div></div>`;
    }).join("");
    return `<div class="ladder"><div class="ladder-head"><strong>${LIFESTYLE[cat].label}</strong><span>${escapeHtml(LIFESTYLE[cat].blurb)}</span></div>${rows}</div>`;
  }).join("");
}

function renderMove(element, value) {
  element.classList.remove("up", "down");
  if (value > 0.0005) element.classList.add("up");
  if (value < -0.0005) element.classList.add("down");
  element.textContent = pct(value);
}

function drawChart() {
  const canvas = els.worthChart;
  const ctx = canvas.getContext("2d");
  const w = canvas.width;
  const h = canvas.height;
  const pad = 28;
  const values = state.history.length > 1 ? state.history : [E.STARTING_CASH, E.netWorth(state)];
  const max = Math.max(...values, E.STARTING_CASH * 1.2);
  const min = Math.min(...values, E.STARTING_CASH * 0.8);
  const up = E.netWorth(state) >= E.STARTING_CASH;

  ctx.clearRect(0, 0, w, h);
  ctx.fillStyle = "#fbfbf8";
  ctx.fillRect(0, 0, w, h);
  ctx.strokeStyle = "#e3e2db";
  ctx.lineWidth = 1;
  for (let i = 0; i < 5; i += 1) {
    const y = pad + ((h - pad * 2) / 4) * i;
    ctx.beginPath();
    ctx.moveTo(pad, y);
    ctx.lineTo(w - pad, y);
    ctx.stroke();
  }

  const xAt = (i) => pad + ((w - pad * 2) / Math.max(values.length - 1, 1)) * i;
  const yAt = (v) => h - pad - ((v - min) / Math.max(max - min, 1)) * (h - pad * 2);

  const grad = ctx.createLinearGradient(0, pad, 0, h - pad);
  grad.addColorStop(0, up ? "rgba(24,137,88,0.22)" : "rgba(201,63,50,0.2)");
  grad.addColorStop(1, "rgba(255,255,255,0)");
  ctx.beginPath();
  values.forEach((v, i) => (i === 0 ? ctx.moveTo(xAt(i), yAt(v)) : ctx.lineTo(xAt(i), yAt(v))));
  ctx.lineTo(xAt(values.length - 1), h - pad);
  ctx.lineTo(pad, h - pad);
  ctx.closePath();
  ctx.fillStyle = grad;
  ctx.fill();

  ctx.strokeStyle = up ? "#188958" : "#c93f32";
  ctx.lineWidth = 4;
  ctx.lineJoin = "round";
  ctx.lineCap = "round";
  ctx.beginPath();
  values.forEach((v, i) => (i === 0 ? ctx.moveTo(xAt(i), yAt(v)) : ctx.lineTo(xAt(i), yAt(v))));
  ctx.stroke();

  ctx.fillStyle = "#171716";
  ctx.beginPath();
  ctx.arc(xAt(values.length - 1), yAt(values[values.length - 1]), 6, 0, Math.PI * 2);
  ctx.fill();
}

// ---------------------------------------------------------------------------
// Ending + recap
// ---------------------------------------------------------------------------
function showEnding(s) {
  const r = s.result;
  els.endingBox.hidden = false;
  els.endingBox.classList.toggle("loss", !r.win);
  els.endingTitle.textContent = `${r.title} · ${compact(r.worth)}`;
  els.endingBody.textContent = r.body;
  els.endingLesson.textContent = r.lesson ? `Lesson: ${r.lesson}` : "";
  els.endingLesson.hidden = !r.lesson;
  const flex = CATEGORIES.map((c) => `<span>${E.tierOf(c, s).icon} ${escapeHtml(E.tierOf(c, s).name)}</span>`);
  const edges = s.edges.map((id) => { const e = EDGES.find((x) => x.id === id); return `<span>${e.icon} ${escapeHtml(e.name)}</span>`; });
  els.endingFlex.innerHTML = flex.concat(edges).join("");
}

function recapData() {
  const s = state;
  const r = s.result || { win: false, epitaph: "Run in progress." };
  return {
    worth: E.netWorth(s),
    rank: E.rank(s),
    seedLabel: s.seedLabel,
    week: Math.min(s.week, MAX_WEEKS),
    maxWeeks: MAX_WEEKS,
    epitaph: r.epitaph,
    history: s.history,
    win: r.win,
    empire: `${E.tierOf("home", s).icon} ${E.tierOf("home", s).name}   ${E.tierOf("ride", s).icon} ${E.tierOf("ride", s).name}`,
  };
}

function downloadRecap() {
  const canvas = buildRecapCanvas(recapData());
  const a = document.createElement("a");
  a.download = `cryptobill-${state.seedLabel}.png`;
  a.href = canvas.toDataURL("image/png");
  a.click();
}

async function copyResult() {
  try { await navigator.clipboard.writeText(resultText(recapData())); flash(els.copyResult, "Copied!"); }
  catch (err) { flash(els.copyResult, "Copy failed"); }
}

async function shareRecap() {
  const data = recapData();
  const text = resultText(data);
  const canvas = buildRecapCanvas(data);
  if (navigator.canShare && canvas.toBlob) {
    canvas.toBlob(async (blob) => {
      const file = new File([blob], `cryptobill-${data.seedLabel}.png`, { type: "image/png" });
      try {
        if (navigator.canShare({ files: [file] })) { await navigator.share({ files: [file], text }); return; }
      } catch (err) { /* fall through */ }
      if (navigator.share) await navigator.share({ text }).catch(() => {});
    });
  } else if (navigator.share) {
    navigator.share({ text }).catch(() => {});
  } else {
    copyResult();
  }
}

function flash(btn, text) {
  const original = btn.textContent;
  btn.textContent = text;
  setTimeout(() => (btn.textContent = original), 1400);
}

// ---------------------------------------------------------------------------
// Tutorial
// ---------------------------------------------------------------------------
function openTutorial() {
  els.tutorialBody.innerHTML = TUTORIAL.map((t, i) => `<li><span>${i + 1}</span>${escapeHtml(t)}</li>`).join("");
  els.tutorialModal.hidden = false;
}
function closeTutorial() { els.tutorialModal.hidden = true; meta.seenTutorial = true; saveMeta(meta); }

function syncMuteButton() {
  els.muteButton.textContent = FX.isMuted() ? "🔇" : "🔊";
  els.muteButton.setAttribute("aria-label", FX.isMuted() ? "Unmute" : "Mute");
}

// Elements whose numbers count smoothly; reset on a new run so they don't tween
// down from the previous run's totals.
const TWEENED = ["netWorthLabel", "portfolioLabel", "cashLabel", "btcPrice", "solPrice", "btcValue", "solValue", "memeValue", "defiValue"];
function resetTweens() { TWEENED.forEach((id) => { if (els[id]) delete els[id]._num; }); }

function escapeHtml(str) {
  return String(str).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
}

init();
