// Juice: the layer that makes a mechanically-complete game feel alive.
// All view-only — number tweening, toasts, synthesized sound, shake, haptics.
// No assets, no dependencies; sound is generated with the Web Audio API.

// --- Number tweening ------------------------------------------------------
// Smoothly counts an element from its current displayed value to a new one.
export function setNum(el, value, fmt) {
  if (!el) return;
  const cur = el._num ?? value;
  if (cur === value) { el.textContent = fmt(value); el._num = value; return; }
  if (el._raf) cancelAnimationFrame(el._raf);
  const start = cur;
  const delta = value - cur;
  const t0 = performance.now();
  const dur = 480;
  el._num = value;
  const step = (t) => {
    const p = Math.min(1, (t - t0) / dur);
    const e = 1 - Math.pow(1 - p, 3); // easeOutCubic
    el.textContent = fmt(start + delta * e);
    if (p < 1) el._raf = requestAnimationFrame(step);
    else { el.textContent = fmt(value); el._raf = null; }
  };
  el._raf = requestAnimationFrame(step);
}

// --- Toasts ---------------------------------------------------------------
let toastHost = null;
function host() {
  if (!toastHost) {
    toastHost = document.createElement("div");
    toastHost.className = "toast-host";
    document.body.appendChild(toastHost);
  }
  return toastHost;
}
export function toast(message, kind = "info", ms = 2600) {
  const el = document.createElement("div");
  el.className = `toast toast-${kind}`;
  el.textContent = message;
  host().appendChild(el);
  requestAnimationFrame(() => el.classList.add("show"));
  setTimeout(() => {
    el.classList.remove("show");
    setTimeout(() => el.remove(), 320);
  }, ms);
}

// --- Shake / flash --------------------------------------------------------
export function shake(el) {
  if (!el) return;
  el.classList.remove("shake");
  void el.offsetWidth; // restart animation
  el.classList.add("shake");
  setTimeout(() => el.classList.remove("shake"), 500);
}

// --- Haptics --------------------------------------------------------------
export function haptic(pattern) {
  try { if (navigator.vibrate) navigator.vibrate(pattern); } catch (e) { /* unsupported */ }
}

// --- Synthesized sound ----------------------------------------------------
let audio = null;
let muted = false;
function ctx() {
  if (!audio) {
    try { audio = new (window.AudioContext || window.webkitAudioContext)(); } catch (e) { audio = null; }
  }
  return audio;
}
function blip(freq, dur, type = "sine", gain = 0.05, when = 0) {
  const c = ctx();
  if (!c || muted) return;
  const o = c.createOscillator();
  const g = c.createGain();
  o.type = type;
  o.frequency.value = freq;
  o.connect(g);
  g.connect(c.destination);
  const t = c.currentTime + when;
  g.gain.setValueAtTime(0, t);
  g.gain.linearRampToValueAtTime(gain, t + 0.012);
  g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
  o.start(t);
  o.stop(t + dur + 0.02);
}
const SOUNDS = {
  click: () => blip(420, 0.05, "triangle", 0.03),
  buy: () => blip(540, 0.08, "triangle", 0.045),
  sell: () => { blip(620, 0.08, "sine", 0.05); blip(820, 0.1, "sine", 0.04, 0.05); },
  profit: () => { blip(660, 0.1, "sine", 0.05); blip(880, 0.12, "sine", 0.045, 0.06); },
  loss: () => blip(190, 0.18, "sawtooth", 0.045),
  rug: () => { blip(170, 0.26, "sawtooth", 0.06); blip(110, 0.32, "square", 0.04, 0.06); },
  levelup: () => { blip(523, 0.1, "sine", 0.05); blip(659, 0.1, "sine", 0.05, 0.08); blip(784, 0.18, "sine", 0.05, 0.16); },
  win: () => [523, 659, 784, 1047].forEach((f, i) => blip(f, 0.2, "sine", 0.055, i * 0.1)),
};
export function play(name) {
  const c = ctx();
  if (c && c.state === "suspended") c.resume();
  SOUNDS[name]?.();
}
export function setMuted(m) { muted = m; }
export function isMuted() { return muted; }
