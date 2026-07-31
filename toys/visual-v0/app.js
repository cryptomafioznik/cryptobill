const canvas = document.querySelector('#scene');
const ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });
const params = new URLSearchParams(location.search);
const VALID_VARIANTS = ['sideplus', 'perspective'];
const VALID_STATES = ['flat', 'climb', 'air', 'compression'];
let variant = VALID_VARIANTS.includes(params.get('variant')) ? params.get('variant') : 'sideplus';
let state = VALID_STATES.includes(params.get('state')) ? params.get('state') : 'flat';
const clean = params.get('clean') === '1';
const sheet = params.get('sheet') || '';
const debug = params.get('debug') === '1';
const forcedW = Math.max(0, Math.min(2400, Number(params.get('width')) || 0));
const forcedH = Math.max(0, Math.min(4000, Number(params.get('height')) || 0));
let assetsReady = false;
if (clean) document.body.classList.add('clean');
if (sheet) document.body.classList.add('sheet', 'clean');

const TAU = Math.PI * 2;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const lerp = (a, b, t) => a + (b - a) * t;
const smooth = (a, b, v) => {
  const t = clamp((v - a) / (b - a), 0, 1);
  return t * t * (3 - 2 * t);
};

const ASSET_ROOT = 'assets/trials25d/';
const assets = {};
function loadImage(key, src) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => { assets[key] = image; resolve(image); };
    image.onerror = () => reject(new Error(`Не удалось загрузить ${src}`));
    image.src = src;
  });
}

const assetPromise = Promise.all([
  loadImage('sky', `${ASSET_ROOT}bg_far_sky_chart.png`),
  loadImage('city', `${ASSET_ROOT}city_mid_rgba.png`),
  loadImage('palms', `${ASSET_ROOT}foreground_palms_rgba.png`),
  loadImage('bike', `${ASSET_ROOT}bike_atlas_4x2.png`),
]);

const stateMeta = {
  flat: { title: 'РАЗГОН', progress: .18, time: '00:08.42', cp: 'CP 0/3', pressed: ['gas'] },
  climb: { title: 'ТЕХНИЧЕСКИЙ ПОДЪЁМ', progress: .43, time: '00:19.07', cp: 'CP 1/3', pressed: ['gas', 'forward'] },
  air: { title: 'РАЗРЫВ / ПРИЗЕМЛЕНИЕ', progress: .67, time: '00:28.64', cp: 'CP 2/3', pressed: ['back'] },
  compression: { title: 'КОМПРЕССИЯ', progress: .71, time: '00:30.11', cp: 'CP 2/3', pressed: ['brake', 'forward'] },
};

const poseFrame = {
  flat: [3, 0],
  climb: [1, 0],
  air: [0, 1],
  compression: [3, 1],
};

function seeded(i, salt = 0) {
  const x = Math.sin(i * 127.1 + salt * 311.7) * 43758.5453;
  return x - Math.floor(x);
}

function fitCover(image, x, y, w, h, scale = 1) {
  const r = Math.max(w / image.naturalWidth, h / image.naturalHeight) * scale;
  const dw = image.naturalWidth * r;
  const dh = image.naturalHeight * r;
  ctx.drawImage(image, x + (w - dw) * .5, y + (h - dh), dw, dh);
}

function roundedRect(x, y, w, h, r) {
  const rr = Math.min(r, w * .5, h * .5);
  ctx.beginPath();
  ctx.moveTo(x + rr, y);
  ctx.arcTo(x + w, y, x + w, y + h, rr);
  ctx.arcTo(x + w, y + h, x, y + h, rr);
  ctx.arcTo(x, y + h, x, y, rr);
  ctx.arcTo(x, y, x + w, y, rr);
  ctx.closePath();
}

function pathProfile(kind, stateName, x, w, h) {
  const t = clamp(x / w, 0, 1);
  if (kind === 'perspective') {
    if (stateName === 'flat') return Math.sin(t * 7.1) * h * .010;
    if (stateName === 'climb') return -smooth(.16, .62, t) * h * .11 + smooth(.70, 1, t) * h * .018;
    if (stateName === 'air') return t < .36 ? -smooth(.08, .34, t) * h * .075 : smooth(.48, .83, t) * h * .07 - h * .055;
    return smooth(.08, .38, t) * h * .035 - smooth(.40, .72, t) * h * .055;
  }
  if (stateName === 'flat') return h * (.615 + Math.sin(t * 8.5) * .008 + Math.sin(t * 2.2) * .006);
  if (stateName === 'climb') {
    return h * (.695 - smooth(.06, .68, t) * .215 + smooth(.75, 1, t) * .025 + Math.sin(t * 18) * .004);
  }
  if (stateName === 'air') {
    if (t < .34) return h * (.675 - smooth(.06, .32, t) * .125);
    return h * (.555 + smooth(.54, .80, t) * .155 - smooth(.82, 1, t) * .025);
  }
  return h * (.595 + smooth(.05, .34, t) * .07 - smooth(.34, .68, t) * .095 + smooth(.72, 1, t) * .025);
}

function sideIsGap(stateName, x, w) {
  const t = x / w;
  return stateName === 'air' && t > .335 && t < .535;
}

function drawBackground(w, h, stateName) {
  ctx.fillStyle = '#09091b';
  ctx.fillRect(0, 0, w, h);

  ctx.save();
  ctx.globalAlpha = .44;
  fitCover(assets.sky, 0, 0, w, h, 1.02);
  ctx.restore();

  const shade = ctx.createLinearGradient(0, 0, 0, h);
  shade.addColorStop(0, 'rgba(5,8,31,.64)');
  shade.addColorStop(.42, 'rgba(11,10,35,.42)');
  shade.addColorStop(.74, 'rgba(5,7,20,.80)');
  shade.addColorStop(1, 'rgba(2,4,12,.98)');
  ctx.fillStyle = shade;
  ctx.fillRect(0, 0, w, h);

  const stateShift = { flat: 0, climb: 14, air: 27, compression: 34 }[stateName];
  ctx.save();
  ctx.globalAlpha = .22;
  ctx.translate(-stateShift * .08, h * .02);
  fitCover(assets.city, 0, 0, w * 1.08, h * .78, 1);
  ctx.restore();

  drawProceduralCity(w, h, .495, .08, 38, 'far', stateName);
  drawProceduralCity(w, h, .555, .16, 56, 'mid', stateName);

  const calm = ctx.createRadialGradient(w * .22, h * .50, 0, w * .22, h * .50, w * .35);
  calm.addColorStop(0, 'rgba(5,7,20,.66)');
  calm.addColorStop(.48, 'rgba(5,7,20,.30)');
  calm.addColorStop(1, 'rgba(5,7,20,0)');
  ctx.fillStyle = calm;
  ctx.fillRect(0, h * .24, w * .65, h * .48);

  ctx.save();
  ctx.globalAlpha = .14;
  fitCover(assets.palms, 0, h * .08, w, h * .78, 1.08);
  ctx.restore();

  const mist = ctx.createLinearGradient(0, h * .40, 0, h * .69);
  mist.addColorStop(0, 'rgba(111,92,167,0)');
  mist.addColorStop(.52, 'rgba(98,90,149,.16)');
  mist.addColorStop(1, 'rgba(7,10,24,0)');
  ctx.fillStyle = mist;
  ctx.fillRect(0, h * .38, w, h * .34);

  drawChartGhost(w, h, stateShift);
}

function drawProceduralCity(w, h, baseFrac, parallax, spacing, depth, stateName) {
  const base = h * baseFrac;
  const shift = ({ flat: 0, climb: 14, air: 27, compression: 34 }[stateName] || 0) * parallax;
  const count = Math.ceil(w / spacing) + 3;
  for (let i = -1; i < count; i++) {
    const r = seeded(i + 20, depth === 'far' ? 2 : 7);
    const rw = seeded(i + 70, 4);
    const bh = h * (depth === 'far' ? lerp(.07, .18, r) : lerp(.10, .24, r));
    const bw = spacing * lerp(.48, .82, rw);
    const x = i * spacing - shift + (spacing - bw) * .5;
    const y = base - bh;
    const g = ctx.createLinearGradient(x, y, x + bw, y);
    if (depth === 'far') {
      g.addColorStop(0, 'rgba(51,48,91,.42)');
      g.addColorStop(1, 'rgba(24,27,64,.34)');
    } else {
      g.addColorStop(0, 'rgba(29,27,61,.78)');
      g.addColorStop(1, 'rgba(9,13,35,.88)');
    }
    ctx.fillStyle = g;
    ctx.fillRect(x, y, bw, bh);
    ctx.fillStyle = depth === 'far' ? 'rgba(109,129,184,.15)' : 'rgba(79,105,165,.20)';
    ctx.fillRect(x, y, bw, 1);
    if (depth === 'mid') {
      for (let yy = y + 9; yy < base - 6; yy += 13) {
        for (let xx = x + 5; xx < x + bw - 4; xx += 9) {
          if (seeded(xx + yy, i) > .83) {
            ctx.fillStyle = seeded(xx, yy) > .55 ? 'rgba(255,173,118,.30)' : 'rgba(76,167,217,.26)';
            ctx.fillRect(xx, yy, 2, 3);
          }
        }
      }
    }
  }
}

function drawChartGhost(w, h, shift) {
  ctx.save();
  ctx.globalAlpha = .16;
  ctx.strokeStyle = '#69a8d0';
  ctx.lineWidth = 1;
  ctx.beginPath();
  for (let x = -16; x <= w + 16; x += 18) {
    const y = h * .39 - Math.sin((x + shift) * .026) * h * .018 - Math.sin((x + shift) * .009) * h * .028;
    x === -16 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
  }
  ctx.stroke();
  ctx.restore();
}

function collectRuns(w, h, stateName) {
  const runs = [];
  let current = [];
  for (let x = -8; x <= w + 8; x += 5) {
    if (sideIsGap(stateName, x, w)) {
      if (current.length) runs.push(current);
      current = [];
    } else current.push([x, pathProfile('sideplus', stateName, x, w, h)]);
  }
  if (current.length) runs.push(current);
  return runs;
}

function drawSideRoad(w, h, stateName) {
  const runs = collectRuns(w, h, stateName);
  for (const points of runs) {
    const first = points[0], last = points[points.length - 1];
    ctx.beginPath();
    ctx.moveTo(first[0], first[1]);
    points.forEach(p => ctx.lineTo(p[0], p[1]));
    ctx.lineTo(last[0], h * .80);
    ctx.lineTo(first[0], h * .80);
    ctx.closePath();
    const body = ctx.createLinearGradient(0, h * .48, 0, h * .78);
    body.addColorStop(0, '#242840');
    body.addColorStop(.22, '#15192c');
    body.addColorStop(1, '#070a16');
    ctx.fillStyle = body;
    ctx.fill();

    ctx.beginPath();
    ctx.moveTo(first[0], first[1]);
    points.forEach(p => ctx.lineTo(p[0], p[1]));
    for (let i = points.length - 1; i >= 0; i--) ctx.lineTo(points[i][0], points[i][1] + 8);
    ctx.closePath();
    const cap = ctx.createLinearGradient(0, first[1], 0, first[1] + 10);
    cap.addColorStop(0, 'rgba(110,118,150,.88)');
    cap.addColorStop(1, 'rgba(39,45,70,.96)');
    ctx.fillStyle = cap;
    ctx.fill();

    ctx.strokeStyle = 'rgba(25,29,51,.74)';
    ctx.lineWidth = 1;
    for (let i = 4; i < points.length; i += 7) {
      const p = points[i];
      ctx.beginPath();
      ctx.moveTo(p[0] - 2, p[1] + 8);
      ctx.lineTo(p[0] + 5, p[1] + 24);
      ctx.stroke();
    }

    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.strokeStyle = 'rgba(80,89,125,.66)';
    ctx.lineWidth = 4;
    ctx.beginPath();
    points.forEach((p, i) => i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]));
    ctx.stroke();
    ctx.strokeStyle = 'rgba(184,202,226,.82)';
    ctx.lineWidth = 1.35;
    ctx.beginPath();
    points.forEach((p, i) => i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]));
    ctx.stroke();
    ctx.lineCap = 'butt';
  }

  if (stateName === 'air') drawGapCaps(w, h);
}

function drawGapCaps(w, h) {
  const edges = [.335, .535];
  edges.forEach((t, idx) => {
    const x = w * t;
    const y = pathProfile('sideplus', 'air', x + (idx ? 3 : -3), w, h);
    ctx.fillStyle = idx ? 'rgba(94,231,183,.72)' : 'rgba(255,141,98,.75)';
    ctx.fillRect(x - 2, y - 1, 4, 23);
  });
}

function perspectiveGap(stateName, t) {
  return stateName === 'air' && t > .22 && t < .43;
}

function perspectiveNode(t, w, h, stateName) {
  const e = 1 - Math.pow(1 - t, 1.38);
  const s = lerp(1, .075, e);
  const cx = lerp(w * .225, w * .77, e);
  const baseY = lerp(h * .625, h * .385, e);
  const relief = pathProfile('perspective', stateName, t * w, w, h) * s;
  const half = lerp(w * .285, w * .012, e);
  const lateralY = half * .22;
  return { t, s, cx, y: baseY + relief, Lx: cx - half, Ly: baseY + relief + lateralY, Rx: cx + half, Ry: baseY + relief - lateralY };
}

function drawPerspectiveRoad(w, h, stateName) {
  const nodes = [];
  const n = 31;
  for (let i = 0; i < n; i++) nodes.push(perspectiveNode(i / (n - 1), w, h, stateName));

  for (let i = nodes.length - 2; i >= 0; i--) {
    const a = nodes[i], b = nodes[i + 1];
    if (perspectiveGap(stateName, (a.t + b.t) * .5)) continue;
    const near = (a.s + b.s) * .5;
    const g = ctx.createLinearGradient(a.Lx, a.Ly, a.Rx, a.Ry);
    g.addColorStop(0, `rgba(18,22,39,${.88 + near * .1})`);
    g.addColorStop(.52, `rgba(${Math.round(33 + 24 * near)},${Math.round(31 + 20 * near)},${Math.round(54 + 27 * near)},.98)`);
    g.addColorStop(1, `rgba(11,15,29,${.9 + near * .08})`);
    ctx.fillStyle = g;
    ctx.beginPath();
    ctx.moveTo(a.Lx, a.Ly);
    ctx.lineTo(a.Rx, a.Ry);
    ctx.lineTo(b.Rx, b.Ry);
    ctx.lineTo(b.Lx, b.Ly);
    ctx.closePath();
    ctx.fill();

    if (i % 2 === 0) {
      ctx.strokeStyle = `rgba(194,205,228,${.05 + near * .16})`;
      ctx.lineWidth = Math.max(.55, 1.6 * near);
      ctx.beginPath();
      ctx.moveTo(lerp(a.Lx, a.Rx, .48), lerp(a.Ly, a.Ry, .48));
      ctx.lineTo(lerp(b.Lx, b.Rx, .48), lerp(b.Ly, b.Ry, .48));
      ctx.stroke();
    }
  }

  for (const side of ['L', 'R']) {
    for (let pass = 0; pass < 2; pass++) {
      ctx.strokeStyle = pass ? 'rgba(130,159,198,.78)' : 'rgba(61,87,132,.35)';
      ctx.lineWidth = pass ? 1.15 : 4.5;
      ctx.beginPath();
      let drawing = false;
      nodes.forEach(p => {
        if (perspectiveGap(stateName, p.t)) { drawing = false; return; }
        const x = p[`${side}x`], y = p[`${side}y`];
        if (!drawing) { ctx.moveTo(x, y); drawing = true; } else ctx.lineTo(x, y);
      });
      ctx.stroke();
    }
  }

  for (let i = 4; i < nodes.length; i += 5) {
    const p = nodes[i];
    if (perspectiveGap(stateName, p.t)) continue;
    const r = Math.max(1.5, 8 * p.s);
    ctx.fillStyle = `rgba(255,180,112,${.12 + p.s * .26})`;
    ctx.beginPath();
    ctx.arc(p.Rx, p.Ry - r * 1.3, r * .22, 0, TAU);
    ctx.fill();
  }

  if (stateName === 'air') {
    const target = perspectiveNode(.49, w, h, stateName);
    drawLandingTarget(target.cx, target.y, 34 * target.s + 10, 'perspective');
  }
}

function heroPlacement(kind, stateName, w, h) {
  const x = w * (stateName === 'climb' && kind === 'sideplus' ? .28 : .25);
  if (kind === 'perspective') {
    const contact = h * .625 + pathProfile('perspective', stateName, 0, w, h);
    return { x, y: stateName === 'air' ? contact - h * .105 : contact, angle: stateName === 'climb' ? -.10 : stateName === 'air' ? -.14 : 0 };
  }
  const gx = x;
  const gy = pathProfile('sideplus', stateName, gx, w, h);
  const dy = pathProfile('sideplus', stateName, gx + 8, w, h) - pathProfile('sideplus', stateName, gx - 8, w, h);
  let angle = Math.atan2(dy, 16);
  if (stateName === 'air') return { x, y: h * .475, angle: -.12 };
  if (stateName === 'climb') angle = Math.max(angle, -.68);
  if (stateName === 'compression') angle += .025;
  return { x, y: gy, angle };
}

function drawContactShadow(kind, stateName, w, h, hero) {
  if (stateName === 'air') {
    if (kind === 'sideplus') {
      const x = w * .62;
      const y = pathProfile('sideplus', 'air', x, w, h);
      drawLandingTarget(x, y, 28, kind);
    }
    return;
  }
  ctx.save();
  ctx.translate(hero.x, hero.y + 2);
  ctx.rotate(hero.angle);
  const grad = ctx.createRadialGradient(0, 0, 0, 0, 0, 44);
  grad.addColorStop(0, 'rgba(0,0,0,.45)');
  grad.addColorStop(1, 'rgba(0,0,0,0)');
  ctx.fillStyle = grad;
  ctx.scale(1, .2);
  ctx.beginPath();
  ctx.arc(0, 0, 44, 0, TAU);
  ctx.fill();
  ctx.restore();
}

function drawLandingTarget(x, y, radius, kind) {
  ctx.save();
  ctx.translate(x, y - 2);
  ctx.scale(1, .22);
  const g = ctx.createRadialGradient(0, 0, 2, 0, 0, radius);
  g.addColorStop(0, 'rgba(0,0,0,.25)');
  g.addColorStop(1, 'rgba(0,0,0,0)');
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.arc(0, 0, radius, 0, TAU);
  ctx.fill();
  ctx.restore();
  ctx.strokeStyle = 'rgba(119,236,189,.82)';
  ctx.lineWidth = 1.4;
  ctx.setLineDash([5, 5]);
  ctx.beginPath();
  ctx.ellipse(x, y - 2, radius * .65, Math.max(3, radius * .13), -.08, 0, TAU);
  ctx.stroke();
  ctx.setLineDash([]);
  ctx.fillStyle = 'rgba(190,239,222,.82)';
  ctx.font = '700 9px ui-monospace, monospace';
  ctx.letterSpacing = '1px';
  ctx.fillText(kind === 'perspective' ? 'LANDING VECTOR' : 'ЗОНА ПОСАДКИ', x - radius, y + 18);
}

function drawHero(kind, stateName, w, h) {
  const hero = heroPlacement(kind, stateName, w, h);
  drawContactShadow(kind, stateName, w, h, hero);
  const [col, row] = poseFrame[stateName];
  const cellW = 445, cellH = 444;
  const box = Math.min(w * .58, h * (h < 650 ? .27 : .27));
  ctx.save();
  ctx.translate(hero.x, hero.y);
  ctx.rotate(hero.angle);
  ctx.shadowColor = 'rgba(255,65,120,.54)';
  ctx.shadowBlur = Math.max(5, box * .045);
  ctx.drawImage(assets.bike, col * cellW, row * cellH, cellW, cellH, -box * .46, -box * .88, box, box);
  ctx.shadowBlur = 0;
  ctx.drawImage(assets.bike, col * cellW, row * cellH, cellW, cellH, -box * .46, -box * .88, box, box);
  ctx.restore();
  return { ...hero, box };
}

function drawHUD(w, h, stateName, kind) {
  const meta = stateMeta[stateName];
  const safeTop = clamp(h * .045, 24, 44);
  const x = 18, y = safeTop + 8, pause = 56;
  const maxBar = Math.max(132, w - 118);
  ctx.fillStyle = 'rgba(6,8,22,.62)';
  roundedRect(x - 7, y - 9, maxBar + 21, 62, 13);
  ctx.fill();

  ctx.fillStyle = 'rgba(166,180,215,.56)';
  ctx.font = '700 9px ui-monospace, monospace';
  ctx.fillText(meta.title, x, y + 3);
  ctx.fillStyle = 'rgba(52,62,92,.92)';
  roundedRect(x, y + 12, maxBar, 5, 3);
  ctx.fill();
  const bar = ctx.createLinearGradient(x, 0, x + maxBar, 0);
  bar.addColorStop(0, '#6ee7d2');
  bar.addColorStop(1, '#ff5d91');
  ctx.fillStyle = bar;
  roundedRect(x, y + 12, maxBar * meta.progress, 5, 3);
  ctx.fill();
  ctx.fillStyle = '#e8efff';
  ctx.font = '800 12px ui-monospace, monospace';
  ctx.fillText(meta.time, x, y + 37);
  ctx.fillStyle = '#8797ba';
  ctx.font = '700 10px ui-monospace, monospace';
  ctx.fillText(meta.cp, x + 84, y + 37);

  const px = w - pause - 14, py = safeTop;
  ctx.fillStyle = 'rgba(7,10,28,.74)';
  ctx.strokeStyle = 'rgba(168,184,222,.34)';
  ctx.lineWidth = 1;
  roundedRect(px, py, pause, pause, 15);
  ctx.fill();
  ctx.stroke();
  ctx.fillStyle = '#d9e3f7';
  ctx.fillRect(px + 21, py + 18, 4, 20);
  ctx.fillRect(px + 31, py + 18, 4, 20);

  ctx.fillStyle = kind === 'sideplus' ? 'rgba(110,231,210,.78)' : 'rgba(255,170,112,.82)';
  ctx.font = '800 9px ui-monospace, monospace';
  ctx.fillText(kind === 'sideplus' ? 'HONEST SIDE 2D+' : 'PERSPECTIVE CANDIDATE', 18, y + 54);
}

function drawTouchButton(x, y, w, h, label, type, active) {
  const fill = ctx.createLinearGradient(x, y, x, y + h);
  if (active) {
    fill.addColorStop(0, type === 'gas' ? 'rgba(72,213,166,.72)' : 'rgba(255,91,134,.66)');
    fill.addColorStop(1, type === 'gas' ? 'rgba(29,111,94,.78)' : 'rgba(85,39,73,.82)');
  } else {
    fill.addColorStop(0, 'rgba(28,34,57,.82)');
    fill.addColorStop(1, 'rgba(13,18,35,.88)');
  }
  ctx.fillStyle = fill;
  ctx.strokeStyle = active ? (type === 'gas' ? 'rgba(108,255,205,.96)' : 'rgba(255,124,164,.92)') : 'rgba(126,145,182,.46)';
  ctx.lineWidth = active ? 2 : 1.2;
  roundedRect(x, y, w, h, 17);
  ctx.fill();
  ctx.stroke();

  ctx.save();
  ctx.translate(x + w * .5, y + h * .40);
  ctx.strokeStyle = active ? '#fff' : '#a9b9d5';
  ctx.fillStyle = active ? '#fff' : '#a9b9d5';
  ctx.lineWidth = 2.2;
  ctx.lineCap = 'round';
  if (type === 'gas') {
    for (let i = -1; i <= 1; i++) {
      ctx.beginPath();
      ctx.moveTo(-12 + i * 9, -5);
      ctx.lineTo(-5 + i * 9, 0);
      ctx.lineTo(-12 + i * 9, 5);
      ctx.stroke();
    }
  } else if (type === 'brake') {
    ctx.fillRect(-10, -7, 4, 14);
    ctx.fillRect(-2, -7, 4, 14);
    ctx.beginPath(); ctx.arc(9, 0, 5, 0, TAU); ctx.stroke();
  } else {
    ctx.beginPath(); ctx.arc(-9, 4, 5, 0, TAU); ctx.arc(9, 4, 5, 0, TAU); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(-9, 4); ctx.lineTo(-1, -3); ctx.lineTo(9, 4); ctx.stroke();
    ctx.beginPath();
    if (type === 'back') { ctx.arc(0, -3, 12, -.1, -2.7, true); ctx.moveTo(-11, -3); ctx.lineTo(-7, -10); }
    else { ctx.arc(0, -3, 12, -3.05, -.45, false); ctx.moveTo(11, -3); ctx.lineTo(7, -10); }
    ctx.stroke();
  }
  ctx.restore();

  ctx.fillStyle = active ? '#fff' : '#afbdd6';
  ctx.textAlign = 'center';
  ctx.font = `800 ${w < 70 ? 7 : 8}px ui-monospace, monospace`;
  ctx.fillText(label, x + w * .5, y + h - 11);
  ctx.textAlign = 'left';
}

function drawTouchUI(w, h, stateName) {
  const meta = stateMeta[stateName];
  const pressed = new Set(meta.pressed);
  const safeBottom = clamp(h * .025, 14, 24);
  const bh = clamp(h * .072, 58, 68);
  const rightW = clamp(w * .225, 76, 94);
  const rightX = w - rightW - (w < 350 ? 12 : 18);
  const gasY = h - safeBottom - bh;
  const brakeY = gasY - bh - 10;
  const leftX = w < 350 ? 12 : 18;
  const gap = 8;
  const leftAvailable = rightX - leftX - 26;
  const tiltW = clamp((leftAvailable - gap) * .5, 58, 82);
  const tiltY = gasY;

  const scrim = ctx.createLinearGradient(0, h - bh * 2.8, 0, h);
  scrim.addColorStop(0, 'rgba(3,5,15,0)');
  scrim.addColorStop(.38, 'rgba(3,5,15,.64)');
  scrim.addColorStop(1, 'rgba(3,5,15,.94)');
  ctx.fillStyle = scrim;
  ctx.fillRect(0, h - bh * 2.8, w, bh * 2.8);

  drawTouchButton(leftX, tiltY, tiltW, bh, 'ВЕС НАЗАД', 'back', pressed.has('back'));
  drawTouchButton(leftX + tiltW + gap, tiltY, tiltW, bh, 'ВЕС ВПЕРЁД', 'forward', pressed.has('forward'));
  drawTouchButton(rightX, brakeY, rightW, bh, 'ТОРМОЗ', 'brake', pressed.has('brake'));
  drawTouchButton(rightX, gasY, rightW, bh, 'ГАЗ', 'gas', pressed.has('gas'));
}

function drawDebug(w, h, kind, stateName, hero) {
  if (!debug) return;
  ctx.save();
  ctx.strokeStyle = 'rgba(255,239,105,.72)';
  ctx.lineWidth = 1;
  ctx.strokeRect(w * .18, h * .30, w * .78, h * .45);
  ctx.beginPath(); ctx.moveTo(hero.x, 0); ctx.lineTo(hero.x, h); ctx.stroke();
  ctx.fillStyle = 'rgba(2,4,12,.78)';
  ctx.fillRect(12, h * .22, 188, 64);
  ctx.fillStyle = '#ffe874';
  ctx.font = '10px ui-monospace, monospace';
  ctx.fillText(`viewport ${Math.round(w)}×${Math.round(h)}`, 20, h * .22 + 17);
  ctx.fillText(`hero x ${(hero.x / w * 100).toFixed(1)}%`, 20, h * .22 + 33);
  ctx.fillText(`variant ${kind} / ${stateName}`, 20, h * .22 + 49);
  ctx.restore();
}

function renderFrame(target, w, h, kind, stateName) {
  const old = ctx;
  if (target !== ctx) throw new Error('Visual V0 uses one Canvas 2D context by design');
  drawBackground(w, h, stateName);
  if (kind === 'sideplus') drawSideRoad(w, h, stateName);
  else drawPerspectiveRoad(w, h, stateName);
  const hero = drawHero(kind, stateName, w, h);
  drawHUD(w, h, stateName, kind);
  drawTouchUI(w, h, stateName);
  drawDebug(w, h, kind, stateName, hero);
}

function renderSingle() {
  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  const w = forcedW || Math.max(280, window.innerWidth);
  const h = forcedH || Math.max(500, window.innerHeight);
  if (forcedW && forcedH) {
    document.documentElement.style.width = `${w}px`;
    document.documentElement.style.height = `${h}px`;
    document.body.style.width = `${w}px`;
    document.body.style.height = `${h}px`;
    document.body.style.minHeight = `${h}px`;
    document.body.style.overflow = 'hidden';
  }
  canvas.style.width = `${w}px`;
  canvas.style.height = `${h}px`;
  canvas.width = Math.round(w * dpr);
  canvas.height = Math.round(h * dpr);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  renderFrame(ctx, w, h, variant, state);
}

function renderVariantsSheet() {
  const sourceW = 430, sourceH = 932, scale = .50;
  const cellW = sourceW * scale, cellH = sourceH * scale, gap = 16, header = 70;
  const states = ['flat', 'climb', 'air'];
  const variants = ['sideplus', 'perspective'];
  const totalW = cellW * states.length + gap * (states.length + 1);
  const totalH = header + cellH * variants.length + gap * (variants.length + 1);
  canvas.style.width = `${totalW}px`;
  canvas.style.height = `${totalH}px`;
  canvas.width = totalW;
  canvas.height = totalH;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.fillStyle = '#05050e';
  ctx.fillRect(0, 0, totalW, totalH);
  ctx.fillStyle = '#f0f4ff';
  ctx.font = '800 20px ui-monospace, monospace';
  ctx.fillText('CRYPTOBILL · VISUAL V0 · DIRECT CANVAS CAPTURES', gap, 30);
  ctx.fillStyle = '#8290ad';
  ctx.font = '700 11px ui-monospace, monospace';
  ctx.fillText('ROWS: A · SIDE 2D+ / B · PERSPECTIVE     COLS: FLAT / CLIMB / AIR', gap, 51);
  variants.forEach((kind, row) => {
    states.forEach((stateName, col) => {
      const x = gap + col * (cellW + gap), y = header + gap + row * (cellH + gap);
      ctx.save();
      ctx.translate(x, y);
      ctx.scale(scale, scale);
      renderFrame(ctx, sourceW, sourceH, kind, stateName);
      ctx.restore();
      ctx.strokeStyle = 'rgba(164,178,211,.36)';
      ctx.strokeRect(x + .5, y + .5, cellW - 1, cellH - 1);
      ctx.fillStyle = 'rgba(4,6,17,.82)';
      ctx.fillRect(x + 7, y + 7, 114, 19);
      ctx.fillStyle = '#dce7fb';
      ctx.font = '800 9px ui-monospace, monospace';
      ctx.fillText(`${kind === 'sideplus' ? 'A' : 'B'} · ${stateName.toUpperCase()}`, x + 12, y + 20);
    });
  });
}

function renderComparison() {
  const cellW = 430, cellH = 932, gap = 12, header = 62;
  const totalW = cellW * 3 + gap * 4;
  const totalH = header + cellH + gap * 2;
  canvas.style.width = `${totalW}px`;
  canvas.style.height = `${totalH}px`;
  canvas.width = totalW;
  canvas.height = totalH;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.fillStyle = '#05050e';
  ctx.fillRect(0, 0, totalW, totalH);
  ctx.fillStyle = '#edf2ff';
  ctx.font = '800 18px ui-monospace, monospace';
  ctx.fillText('CURRENT MOBILE → V0-A → V0-B', gap, 27);
  ctx.fillStyle = '#8796b4';
  ctx.font = '700 10px ui-monospace, monospace';
  ['CURRENT · VIDEO FRAME', 'V0-A · SIDE 2D+', 'V0-B · PERSPECTIVE'].forEach((label, i) => ctx.fillText(label, gap + i * (cellW + gap), 48));
  const y = header + gap;
  ctx.drawImage(assets.current, gap, y, cellW, cellH);
  ctx.save(); ctx.translate(gap * 2 + cellW, y); renderFrame(ctx, cellW, cellH, 'sideplus', 'climb'); ctx.restore();
  ctx.save(); ctx.translate(gap * 3 + cellW * 2, y); renderFrame(ctx, cellW, cellH, 'perspective', 'climb'); ctx.restore();
}

function updateControls() {
  document.querySelectorAll('[data-variant]').forEach(b => b.classList.toggle('active', b.dataset.variant === variant));
  document.querySelectorAll('[data-state]').forEach(b => b.classList.toggle('active', b.dataset.state === state));
}

function setQuery(nextVariant, nextState) {
  variant = nextVariant || variant;
  state = nextState || state;
  const next = new URL(location.href);
  next.searchParams.set('variant', variant);
  next.searchParams.set('state', state);
  history.replaceState(null, '', next);
  updateControls();
  renderSingle();
}

document.querySelectorAll('[data-variant]').forEach(button => button.addEventListener('click', () => setQuery(button.dataset.variant, state)));
document.querySelectorAll('[data-state]').forEach(button => button.addEventListener('click', () => setQuery(variant, button.dataset.state)));
window.addEventListener('keydown', event => {
  if (event.key === 'a' || event.key === 'A') setQuery('sideplus', state);
  if (event.key === 'b' || event.key === 'B') setQuery('perspective', state);
  const idx = Number(event.key) - 1;
  if (idx >= 0 && idx < VALID_STATES.length) setQuery(variant, VALID_STATES[idx]);
});
window.addEventListener('resize', () => { if (!sheet && assetsReady) renderSingle(); });

window.__visualV0 = {
  setVariant: v => VALID_VARIANTS.includes(v) && setQuery(v, state),
  setState: s => VALID_STATES.includes(s) && setQuery(variant, s),
  getState: () => ({ variant, state, ready: !!window.__visualV0Ready }),
  metrics: () => ({
    viewport: [window.innerWidth, window.innerHeight],
    heroAnchorPct: [state === 'climb' && variant === 'sideplus' ? 28 : 25, state === 'air' ? 47.5 : null],
    forwardSpacePct: 55,
    touchTargetMinCssPx: 58,
    renderer: 'CanvasRenderingContext2D',
    productionStateWrites: 0,
    localStorageWrites: 0,
  }),
};

async function start() {
  try {
    await assetPromise;
    assetsReady = true;
    if (sheet === 'comparison') {
      await loadImage('current', '../docs/visual-v0/reference/current-game-mobile.png');
      renderComparison();
    } else if (sheet === 'variants') renderVariantsSheet();
    else renderSingle();
    updateControls();
    requestAnimationFrame(() => requestAnimationFrame(() => {
      document.body.classList.add('ready');
      window.__visualV0Ready = true;
      document.dispatchEvent(new Event('visual-v0-ready'));
    }));
  } catch (error) {
    document.querySelector('#assetStatus').textContent = error.message;
    console.error(error);
  }
}

start();
