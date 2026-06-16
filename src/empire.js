// The "Empire" layer: visible lifestyle evolution.
// Net worth is the *score*; this is the *reward*. Profit converts into status
// you can see — homes, rides, security, a crew — each with a gameplay perk and
// a resale value that folds back into net worth. This is also the safe
// monetization surface: cosmetic skins of these assets, never advantage.

// Each category is an upgrade ladder; the player holds one tier per category.
// tier fields:
//   cost  - cash to buy (net of trading in the current tier's resale)
//   gate  - net worth required before it unlocks
//   resale- illiquid value this tier adds to net worth
//   icon  - flex-strip glyph
//   perk* - category-specific effect (see game.js for how they apply)
export const LIFESTYLE = {
  home: {
    label: "Home",
    blurb: "Energy + calm. Where the work happens.",
    tiers: [
      { name: "Rented room", cost: 0, gate: 0, resale: 0, icon: "🛏️", energy: 0, calm: 0, flavor: "A mattress, a laptop, a dream." },
      { name: "Studio apartment", cost: 3000, gate: 5000, resale: 2700, icon: "🏢", energy: 0, calm: 3, flavor: "Walls that are mostly yours." },
      { name: "City condo", cost: 14000, gate: 22000, resale: 12600, icon: "🏙️", energy: 1, calm: 4, flavor: "A real office nook with a view." },
      { name: "Penthouse", cost: 65000, gate: 95000, resale: 59000, icon: "🌆", energy: 1, calm: 6, flavor: "The skyline finally works for you." },
      { name: "Hillside mansion", cost: 260000, gate: 360000, resale: 236000, icon: "🏔️", energy: 2, calm: 8, flavor: "A gym you will never use." },
      { name: "Private island", cost: 780000, gate: 900000, resale: 740000, icon: "🏝️", energy: 3, calm: 12, flavor: "Your own jurisdiction, basically." },
    ],
  },
  ride: {
    label: "Ride",
    blurb: "Status. Opens doors (and rep). Depreciates.",
    tiers: [
      { name: "Transit card", cost: 0, gate: 0, resale: 0, icon: "🚌", rep: 0, flavor: "The bus knows your name." },
      { name: "Used hatchback", cost: 1500, gate: 3000, resale: 800, icon: "🚗", rep: 1, flavor: "It starts. Usually." },
      { name: "Sports coupe", cost: 18000, gate: 30000, resale: 11000, icon: "🏎️", rep: 2, flavor: "Loud enough to be a personality." },
      { name: "Lambo", cost: 90000, gate: 130000, resale: 55000, icon: "🟩", rep: 3, flavor: "Subtlety has left the chat." },
      { name: "Superyacht", cost: 380000, gate: 480000, resale: 230000, icon: "🛥️", rep: 4, flavor: "International waters, international vibes." },
      { name: "Private jet", cost: 850000, gate: 950000, resale: 520000, icon: "✈️", rep: 5, flavor: "Time zones are now optional." },
    ],
  },
  vault: {
    label: "Security",
    blurb: "Lowers hack & rug risk. The boring flex.",
    tiers: [
      { name: "Phone wallet", cost: 0, gate: 0, resale: 0, icon: "📱", sec: 0, flavor: "One screenshot from disaster." },
      { name: "Hardware wallet", cost: 400, gate: 1500, resale: 250, icon: "🔑", sec: 14, flavor: "Keys offline, mind at ease." },
      { name: "Multisig setup", cost: 6000, gate: 15000, resale: 3500, icon: "🔐", sec: 28, flavor: "Two keys, half the panic." },
      { name: "Cold storage bunker", cost: 45000, gate: 80000, resale: 28000, icon: "🏦", sec: 45, flavor: "Faraday cage optional but encouraged." },
    ],
  },
  crew: {
    label: "Crew",
    blurb: "Capability: intel, cheaper fills, passive income.",
    tiers: [
      { name: "Solo operator", cost: 0, gate: 0, resale: 0, icon: "🧑‍💻", flavor: "Just you and the charts." },
      { name: "Research analyst", cost: 5000, gate: 12000, resale: 2000, icon: "🔍", flavor: "Starts every week with sharper intel." },
      { name: "Quant trader", cost: 30000, gate: 55000, resale: 12000, icon: "📊", flavor: "Cuts your trading fees in half." },
      { name: "Fund team", cost: 150000, gate: 220000, resale: 60000, icon: "🏛️", flavor: "Management fees: passive weekly income." },
    ],
  },
};

export const CATEGORIES = ["home", "ride", "vault", "crew"];

// Mogul level = how far up the net-worth ladder. Drives the avatar scene.
export const MOGUL_LEVELS = [
  { at: 0, title: "Wallet intern", place: "Rented room" },
  { at: 1500, title: "Market survivor", place: "Studio life" },
  { at: 5000, title: "Chain climber", place: "Co-working loft" },
  { at: 15000, title: "Portfolio manager", place: "Private office" },
  { at: 50000, title: "Alpha operator", place: "Penthouse desk" },
  { at: 200000, title: "Cycle legend", place: "Glass tower" },
  { at: 1_000_000, title: "Crypto mogul", place: "Island command" },
];

export function mogulLevel(worth) {
  let lvl = 0;
  MOGUL_LEVELS.forEach((m, i) => {
    if (worth >= m.at) lvl = i;
  });
  return lvl;
}

// ---------------------------------------------------------------------------
// The avatar scene — a stylized room that visibly evolves with each level.
// Drawn as inline SVG so it scales crisply and needs no asset pipeline.
// ---------------------------------------------------------------------------
const SCENES = [
  { wall: ["#dde0db", "#cdd0c9"], floor: ["#ccbfa6", "#b6a98d"], sky: ["#cdd4d2", "#e6e9e3"], view: "overcast", mon: 1, screen: "#1b2a2a", warm: false },
  { wall: ["#dfe6e0", "#ccd6cd"], floor: ["#cdbfa4", "#b6a98c"], sky: ["#cfe0df", "#eaf1ea"], view: "cityfar", mon: 1, screen: "#16302c", warm: false },
  { wall: ["#d6e7e7", "#c2d6d4"], floor: ["#c8bba1", "#b1a286"], sky: ["#bfe2e8", "#e8f5ef"], view: "city", mon: 2, screen: "#143028", warm: false },
  { wall: ["#efd9b4", "#e7c79a"], floor: ["#4c4339", "#3a342d"], sky: ["#f7cd8f", "#fce6c4"], view: "skyline", mon: 3, screen: "#16302c", warm: true },
  { wall: ["#efd2a8", "#e8c391"], floor: ["#463f37", "#332f29"], sky: ["#f4b878", "#fbe6c2"], view: "hills", mon: 3, screen: "#16302c", warm: true },
  { wall: ["#cde7e6", "#bfe0e2"], floor: ["#e7d6ad", "#d8c596"], sky: ["#8fd8e8", "#e0f4ee"], view: "tropical", mon: 3, screen: "#143028", warm: true },
];

const WIN = { x: 296, y: 28, w: 152, h: 104 };

function cloud(x, y) {
  return `<ellipse cx="${x}" cy="${y}" rx="15" ry="7" fill="#fff" opacity="0.75"/><ellipse cx="${x + 13}" cy="${y + 3}" rx="11" ry="6" fill="#fff" opacity="0.75"/>`;
}
function blds(list, fill) {
  return list.map(([x, y, w, h]) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="1.5" fill="${fill}" opacity="0.9"/>`).join("");
}

function windowView(scene, uid) {
  const { x, y, w, h } = WIN;
  let v = "";
  switch (scene.view) {
    case "overcast": v = cloud(x + 34, y + 32) + cloud(x + 96, y + 60); break;
    case "cityfar": v = blds([[x + 16, y + 64, 16, 40], [x + 38, y + 74, 12, 30], [x + 92, y + 60, 18, 44], [x + 116, y + 72, 14, 32]], "#aeb9bf"); break;
    case "city": v = blds([[x + 10, y + 46, 20, 58], [x + 34, y + 60, 16, 44], [x + 92, y + 42, 22, 62], [x + 120, y + 58, 18, 46]], "#9fb6bd"); break;
    case "skyline": v = `<circle cx="${x + w - 32}" cy="${y + 32}" r="15" fill="#fff1cf"/>` + blds([[x + 8, y + 36, 18, 68], [x + 30, y + 54, 16, 50], [x + 50, y + 46, 13, 58], [x + 92, y + 32, 20, 72], [x + 118, y + 52, 16, 52]], "#c79a63"); break;
    case "hills": v = `<circle cx="${x + w - 30}" cy="${y + 28}" r="17" fill="#fff0c8"/><path d="M${x},${y + h} Q${x + 48},${y + 56} ${x + w / 2},${y + 80} T${x + w},${y + 66} L${x + w},${y + h} Z" fill="#cdb27e"/>`; break;
    default: v = `<circle cx="${x + 32}" cy="${y + 28}" r="15" fill="#ffe9a8"/><rect x="${x}" y="${y + h - 32}" width="${w}" height="32" fill="#67c2cc"/><path d="M${x + 110},${y + h - 30} q-5,-25 7,-37 q-17,7 -19,16 q12,-5 12,21 z" fill="#2f7d52"/><rect x="${x + 113}" y="${y + h - 31}" width="5" height="25" fill="#7a5a36"/>`;
  }
  const frame = `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="none" stroke="#f1f0ea" stroke-width="7"/><rect x="${x}" y="${y}" width="${w}" height="${h}" fill="none" stroke="#bdbab0" stroke-width="2"/><line x1="${x + w / 2}" y1="${y}" x2="${x + w / 2}" y2="${y + h}" stroke="#f1f0ea" stroke-width="4"/><line x1="${x}" y1="${y + h / 2}" x2="${x + w}" y2="${y + h / 2}" stroke="#f1f0ea" stroke-width="4"/>`;
  return `<g clip-path="url(#winclip${uid})"><rect x="${x}" y="${y}" width="${w}" height="${h}" fill="url(#wsky${uid})"/>${v}</g>${frame}`;
}

function chartLine(cx, top) {
  const pts = [[cx - 15, top + 19], [cx - 7, top + 14], [cx + 1, top + 16], [cx + 8, top + 8], [cx + 15, top + 5]];
  return `<polyline points="${pts.map((p) => p.join(",")).join(" ")}" fill="none" stroke="#5fe0a8" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>`;
}

function monitorsAt(scene) {
  const cfg = scene.mon === 1 ? [206] : scene.mon === 2 ? [180, 236] : [158, 208, 258];
  return cfg
    .map(
      (x) => `<rect x="${x - 22}" y="136" width="44" height="32" rx="3" fill="#1b1e21"/>
        <rect x="${x - 18}" y="140" width="36" height="24" rx="2" fill="${scene.screen}"/>
        ${chartLine(x, 140)}
        <rect x="${x - 3}" y="168" width="6" height="6" fill="#2a2d30"/>
        <rect x="${x - 10}" y="173" width="20" height="3" rx="1.5" fill="#2a2d30"/>`,
    )
    .join("");
}

function avatar(scene) {
  const skin = "#e7b58f";
  const hair = scene.warm ? "#3a2c22" : "#2e2a26";
  const shirt = scene.warm ? "#46506a" : "#3f514f";
  return `<rect x="92" y="150" width="46" height="66" rx="15" fill="#2c2f33"/>
    <rect x="98" y="178" width="40" height="38" rx="11" fill="${shirt}"/>
    <circle cx="120" cy="150" r="15" fill="${skin}"/>
    <path d="M105,149 a15,15 0 0 1 30,0 q-16,-11 -30,0 z" fill="${hair}"/>
    <path d="M104,144 a17,17 0 0 0 -1,12" fill="none" stroke="#26292c" stroke-width="3"/>
    <circle cx="105" cy="156" r="3.4" fill="#26292c"/>`;
}

export function sceneSVG(level) {
  const s = SCENES[Math.max(0, Math.min(level, SCENES.length - 1))];
  const plant = level >= 2
    ? `<rect x="40" y="190" width="16" height="22" rx="3" fill="#b9743b"/><path d="M48,190 q-13,-10 -6,-30 q11,7 6,30" fill="#2f7d52"/><path d="M48,190 q13,-8 8,-28 q-11,7 -8,28" fill="#36925f"/>`
    : "";
  const rug = level >= 3 ? `<ellipse cx="178" cy="214" rx="158" ry="12" fill="#7a4f3a" opacity="0.4"/>` : "";
  const trophy = level >= 3
    ? `<rect x="236" y="98" width="42" height="5" rx="2" fill="#b59a63"/><path d="M251,82 h12 v3 a6,6 0 0 1 -12,0 z" fill="#f0cf6a"/><rect x="255" y="86" width="4" height="8" fill="#e3b94a"/><rect x="251" y="94" width="12" height="4" rx="1" fill="#e3b94a"/>`
    : "";
  const uid = `s${level}`;
  return `<svg viewBox="0 0 480 240" preserveAspectRatio="xMidYMid slice" role="img" aria-label="Operator scene">
    <defs>
      <linearGradient id="wall${uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${s.wall[0]}"/><stop offset="1" stop-color="${s.wall[1]}"/></linearGradient>
      <linearGradient id="floor${uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${s.floor[0]}"/><stop offset="1" stop-color="${s.floor[1]}"/></linearGradient>
      <linearGradient id="wsky${uid}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${s.sky[0]}"/><stop offset="1" stop-color="${s.sky[1]}"/></linearGradient>
      <clipPath id="winclip${uid}"><rect x="${WIN.x}" y="${WIN.y}" width="${WIN.w}" height="${WIN.h}"/></clipPath>
    </defs>
    <rect width="480" height="170" fill="url(#wall${uid})"/>
    <rect y="168" width="480" height="72" fill="url(#floor${uid})"/>
    <rect y="166" width="480" height="3" fill="rgba(0,0,0,0.08)"/>
    ${windowView(s, uid)}
    ${trophy}
    ${rug}
    <rect x="70" y="170" width="222" height="9" rx="3" fill="#3a342e"/>
    <rect x="86" y="179" width="13" height="37" fill="#3a342e"/>
    <rect x="263" y="179" width="13" height="37" fill="#3a342e"/>
    ${avatar(s)}
    ${monitorsAt(s)}
    <rect x="248" y="160" width="9" height="9" rx="2" fill="#c2452f"/>
    ${plant}
  </svg>`;
}
