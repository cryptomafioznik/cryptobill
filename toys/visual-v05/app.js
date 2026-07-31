const canvas = document.querySelector('#scene');
const ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });
const params = new URLSearchParams(location.search);
const STATES = ['flat', 'climb', 'air', 'compression'];
let state = STATES.includes(params.get('state')) ? params.get('state') : 'flat';
let mode = params.get('mode') === 'loop' ? 'loop' : 'static';
let debug = params.get('debug') === '1';
const clean = params.get('clean') === '1';
const forcedW = Math.max(0, Math.min(2400, Number(params.get('width')) || 0));
const forcedH = Math.max(0, Math.min(4000, Number(params.get('height')) || 0));
const frameParam = params.get('frame');
const fixedFrameMs = frameParam === null ? null : Math.max(0, Math.min(9999, Number(frameParam) || 0));
if (clean) document.body.classList.add('clean');

const TAU = Math.PI * 2;
const CELL_W = 445;
const CELL_H = 444;
const LOOP_MS = 10000;
const WORLD_LENGTH = 1800;
const PARALLAX = Object.freeze({ sky: .02, far: .07, mid: .14, near: .28, track: 1 });
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const lerp = (a, b, t) => a + (b - a) * t;
const smooth = (a, b, v) => {
  const t = clamp((v - a) / (b - a), 0, 1);
  return t * t * (3 - 2 * t);
};

const poseFrame = {
  flat: [3, 0],
  climb: [1, 0],
  air: [0, 1],
  compression: [3, 1],
};

const stateMeta = {
  flat: { progress: .18, time: '00:08.42', cp: 'CP 0/3', pressed: ['gas'] },
  climb: { progress: .43, time: '00:19.07', cp: 'CP 1/3', pressed: ['gas', 'forward'] },
  air: { progress: .67, time: '00:28.64', cp: 'CP 2/3', pressed: ['back'] },
  compression: { progress: .71, time: '00:30.11', cp: 'CP 2/3', pressed: ['brake', 'forward'] },
};

const assets = {};
const alphaBounds = {};
let assetsReady = false;
let cssW = 430;
let cssH = 932;
let startMs = 0;
let rafId = 0;
let lastFrame = null;

function loadImage(key, src) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => { assets[key] = image; resolve(image); };
    image.onerror = () => reject(new Error(`Не удалось загрузить ${src}`));
    image.src = src;
  });
}

const assetPromise = Promise.all([
  loadImage('sky', 'assets/trials25d/bg_far_sky_chart.png'),
  loadImage('city', 'assets/trials25d/city_mid_rgba.png'),
  loadImage('palms', 'assets/trials25d/foreground_palms_rgba.png'),
  loadImage('bike', 'assets/trials25d/bike_atlas_4x2.png'),
]);

function seeded(i, salt = 0) {
  const x = Math.sin(i * 127.1 + salt * 311.7) * 43758.5453;
  return x - Math.floor(x);
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

function measureAlphaBounds() {
  const probe = document.createElement('canvas');
  probe.width = CELL_W;
  probe.height = CELL_H;
  const pctx = probe.getContext('2d', { willReadFrequently: true });
  for (const stateName of STATES) {
    const [col, row] = poseFrame[stateName];
    pctx.clearRect(0, 0, CELL_W, CELL_H);
    pctx.drawImage(assets.bike, col * CELL_W, row * CELL_H, CELL_W, CELL_H, 0, 0, CELL_W, CELL_H);
    const data = pctx.getImageData(0, 0, CELL_W, CELL_H).data;
    let minX = CELL_W, minY = CELL_H, maxX = -1, maxY = -1;
    for (let y = 0; y < CELL_H; y++) {
      for (let x = 0; x < CELL_W; x++) {
        if (data[(y * CELL_W + x) * 4 + 3] <= 8) continue;
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
      }
    }
    alphaBounds[stateName] = { x: minX, y: minY, w: maxX - minX + 1, h: maxY - minY + 1 };
  }
  document.body.dataset.alphaBounds = JSON.stringify(alphaBounds);
}

function drawImageCover(image, x, y, w, h, scale = 1, shiftX = 0) {
  const ratio = Math.max(w / image.naturalWidth, h / image.naturalHeight) * scale;
  const dw = image.naturalWidth * ratio;
  const dh = image.naturalHeight * ratio;
  ctx.drawImage(image, x + (w - dw) * .5 - shiftX, y + h - dh, dw, dh);
}

function drawSky(w, h, scroll) {
  ctx.fillStyle = '#101124';
  ctx.fillRect(0, 0, w, h);
  ctx.save();
  ctx.globalAlpha = .62;
  drawImageCover(assets.sky, 0, 0, w, h, 1.04, (scroll * PARALLAX.sky) % (w * .08));
  ctx.restore();

  const topShade = ctx.createLinearGradient(0, 0, 0, h);
  topShade.addColorStop(0, 'rgba(8,12,39,.45)');
  topShade.addColorStop(.48, 'rgba(17,15,43,.14)');
  topShade.addColorStop(.78, 'rgba(8,12,29,.36)');
  topShade.addColorStop(1, 'rgba(5,8,19,.82)');
  ctx.fillStyle = topShade;
  ctx.fillRect(0, 0, w, h);

  ctx.save();
  ctx.globalCompositeOperation = 'screen';
  const light = ctx.createRadialGradient(w * .82, h * .25, 0, w * .82, h * .25, h * .62);
  light.addColorStop(0, 'rgba(255,192,121,.24)');
  light.addColorStop(.34, 'rgba(218,121,155,.11)');
  light.addColorStop(1, 'rgba(84,73,148,0)');
  ctx.fillStyle = light;
  ctx.fillRect(0, 0, w, h * .78);
  ctx.restore();
}

function drawCityAsset(w, h, scroll) {
  ctx.save();
  ctx.globalAlpha = .30;
  const shift = (scroll * PARALLAX.far) % (w * .10);
  drawImageCover(assets.city, -w * .04, h * .04, w * 1.12, h * .70, 1, shift);
  ctx.restore();
}

function drawCityLayer(w, h, scroll, config) {
  const { parallax, spacing, base, minH, maxH, far } = config;
  const camera = scroll * parallax;
  const offset = ((camera % spacing) + spacing) % spacing;
  const baseIndex = Math.floor(camera / spacing);
  const count = Math.ceil(w / spacing) + 3;
  for (let k = -1; k < count; k++) {
    const i = baseIndex + k;
    const r = seeded(i, far ? 5 : 11);
    if (seeded(i, 23) < (far ? .10 : .18)) continue;
    const bw = spacing * lerp(.52, .84, seeded(i, 3));
    const bh = h * lerp(minH, maxH, r);
    const x = k * spacing - offset + (spacing - bw) * .5;
    const y = h * base - bh;
    const g = ctx.createLinearGradient(x, y, x + bw, y);
    if (far) {
      g.addColorStop(0, 'rgba(89,82,123,.48)');
      g.addColorStop(1, 'rgba(43,49,86,.42)');
    } else {
      g.addColorStop(0, 'rgba(46,43,72,.88)');
      g.addColorStop(1, 'rgba(15,23,45,.92)');
    }
    ctx.fillStyle = g;
    ctx.fillRect(x, y, bw, bh);
    ctx.fillStyle = far ? 'rgba(182,155,187,.20)' : 'rgba(164,130,155,.24)';
    ctx.fillRect(x, y, bw, 1.2);
    const windowAlpha = far ? .18 : .28;
    for (let wy = y + 9; wy < h * base - 5; wy += far ? 14 : 12) {
      for (let wx = x + 5; wx < x + bw - 4; wx += far ? 10 : 9) {
        if (seeded(wx + i * 3, wy) > .82) {
          ctx.fillStyle = seeded(wx, i) > .52 ? `rgba(255,189,125,${windowAlpha})` : `rgba(86,183,206,${windowAlpha})`;
          ctx.fillRect(wx, wy, 2, far ? 2 : 3);
        }
      }
    }
  }
}

function drawNearLayer(w, h, scroll) {
  const shift = ((scroll * PARALLAX.near) % w + w) % w;
  ctx.save();
  ctx.globalAlpha = .23;
  drawImageCover(assets.palms, -shift, h * .10, w, h * .68, 1.04);
  drawImageCover(assets.palms, w - shift, h * .10, w, h * .68, 1.04);
  ctx.restore();

  const spacing = 190;
  const camera = scroll * PARALLAX.near;
  const offset = ((camera % spacing) + spacing) % spacing;
  ctx.strokeStyle = 'rgba(24,34,55,.52)';
  ctx.lineWidth = 8;
  for (let x = -offset - 60; x < w + 80; x += spacing) {
    ctx.beginPath();
    ctx.moveTo(x, h * .74);
    ctx.lineTo(x + 58, h * .51);
    ctx.lineTo(x + 94, h * .51);
    ctx.stroke();
  }
}

function drawAtmosphere(w, h) {
  const haze = ctx.createLinearGradient(0, h * .36, 0, h * .76);
  haze.addColorStop(0, 'rgba(104,99,148,0)');
  haze.addColorStop(.48, 'rgba(116,106,145,.14)');
  haze.addColorStop(1, 'rgba(9,13,27,0)');
  ctx.fillStyle = haze;
  ctx.fillRect(0, h * .32, w, h * .48);

  const calm = ctx.createRadialGradient(w * .26, h * .54, 0, w * .26, h * .54, w * .28);
  calm.addColorStop(0, 'rgba(7,11,25,.60)');
  calm.addColorStop(.58, 'rgba(7,11,25,.26)');
  calm.addColorStop(1, 'rgba(7,11,25,0)');
  ctx.fillStyle = calm;
  ctx.fillRect(0, h * .31, w * .62, h * .43);
}

function drawBackground(w, h, scroll) {
  drawSky(w, h, scroll);
  drawCityAsset(w, h, scroll);
  drawCityLayer(w, h, scroll, { parallax: PARALLAX.far, spacing: 34, base: .52, minH: .07, maxH: .16, far: true });
  drawCityLayer(w, h, scroll, { parallax: PARALLAX.mid, spacing: 52, base: .64, minH: .11, maxH: .25, far: false });
  drawNearLayer(w, h, scroll);
  drawAtmosphere(w, h);
}

function staticProfile(stateName, x, w, h) {
  const t = clamp(x / w, 0, 1);
  if (stateName === 'flat') return h * (.585 + Math.sin(t * 8.2) * .006 + Math.sin(t * 2.1) * .006);
  if (stateName === 'climb') return h * (.675 - smooth(.07, .68, t) * .205 + smooth(.76, 1, t) * .028 + Math.sin(t * 16) * .003);
  if (stateName === 'air') {
    if (t < .34) return h * (.645 - smooth(.06, .32, t) * .105);
    return h * (.545 + smooth(.54, .80, t) * .12 - smooth(.82, 1, t) * .022);
  }
  return h * (.58 + smooth(.06, .34, t) * .055 - smooth(.34, .68, t) * .078 + smooth(.72, 1, t) * .024);
}

function staticGap(stateName, x, w) {
  const t = x / w;
  return stateName === 'air' && t > .335 && t < .535;
}

function worldHeight(worldX) {
  let x = ((worldX % WORLD_LENGTH) + WORLD_LENGTH) % WORLD_LENGTH;
  if (x < 280) return Math.sin(x * .018) * 3;
  if (x < 690) return smooth(280, 690, x) * 165;
  if (x < 800) return 165 - smooth(690, 800, x) * 18;
  if (x < 1040) return lerp(147, 92, smooth(800, 1040, x));
  if (x < 1270) return 92 - smooth(1040, 1270, x) * 92;
  if (x < 1450) return -Math.sin((x - 1270) / 180 * Math.PI) * 20;
  return Math.sin((x - 1450) * .018) * 3;
}

function worldGap(worldX) {
  const x = ((worldX % WORLD_LENGTH) + WORLD_LENGTH) % WORLD_LENGTH;
  return x > 800 && x < 1040;
}

function worldState(worldX) {
  const x = ((worldX % WORLD_LENGTH) + WORLD_LENGTH) % WORLD_LENGTH;
  if (x >= 300 && x < 720) return 'climb';
  if (x >= 800 && x < 1040) return 'air';
  if (x >= 1040 && x < 1260) return 'compression';
  return 'flat';
}

function createTrackPoints(w, h, frame) {
  const points = [];
  const baseY = h * .585;
  if (!frame.loop) {
    for (let x = -12; x <= w + 12; x += 4) {
      points.push({ x, y: staticProfile(frame.state, x, w, h), gap: staticGap(frame.state, x, w), world: x });
    }
    return points;
  }
  const heroX = w * .26;
  const heroWorld = frame.scroll + heroX;
  const ref = worldHeight(heroWorld);
  for (let x = -16; x <= w + 16; x += 4) {
    const world = frame.scroll + x;
    points.push({ x, y: baseY - (worldHeight(world) - ref) * .72, gap: worldGap(world), world });
  }
  return points;
}

function splitRuns(points) {
  const runs = [];
  let current = [];
  for (const point of points) {
    if (point.gap) {
      if (current.length > 1) runs.push(current);
      current = [];
    } else current.push(point);
  }
  if (current.length > 1) runs.push(current);
  return runs;
}

function yAt(points, x) {
  let closest = points[0];
  for (const p of points) if (Math.abs(p.x - x) < Math.abs(closest.x - x)) closest = p;
  return closest;
}

function drawBeam(x1, y1, x2, y2, width = 4) {
  ctx.strokeStyle = 'rgba(9,15,28,.95)';
  ctx.lineWidth = width + 2;
  ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();
  ctx.strokeStyle = 'rgba(80,95,121,.64)';
  ctx.lineWidth = 1;
  ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();
}

function drawStructure(points, w, h) {
  const runs = splitRuns(points);
  const supportBase = h * .755;
  for (const run of runs) {
    const first = run[0], last = run[run.length - 1];

    ctx.strokeStyle = 'rgba(8,14,27,.98)';
    ctx.lineWidth = 7;
    ctx.beginPath();
    run.forEach((p, i) => i ? ctx.lineTo(p.x, p.y + 35) : ctx.moveTo(p.x, p.y + 35));
    ctx.stroke();
    ctx.strokeStyle = 'rgba(83,98,126,.56)';
    ctx.lineWidth = 1.2;
    ctx.beginPath();
    run.forEach((p, i) => i ? ctx.lineTo(p.x, p.y + 35) : ctx.moveTo(p.x, p.y + 35));
    ctx.stroke();

    for (let i = 3; i < run.length - 4; i += 10) {
      const a = run[i];
      const b = run[Math.min(run.length - 1, i + 10)];
      if ((Math.floor(a.world / 40) & 1) === 0) drawBeam(a.x, a.y + 10, b.x, b.y + 35, 3.5);
      else drawBeam(a.x, a.y + 35, b.x, b.y + 10, 3.5);
    }

    const supportSpacing = 145;
    let lastSupportBucket = null;
    for (const p of run) {
      const bucket = Math.floor(p.world / supportSpacing);
      if (bucket === lastSupportBucket) continue;
      lastSupportBucket = bucket;
      if (p.x < -10 || p.x > w + 10 || supportBase - p.y < 42) continue;
      drawBeam(p.x, p.y + 34, p.x, supportBase, 6);
      drawBeam(p.x - 16, supportBase, p.x + 16, supportBase, 5);
      if (supportBase - p.y > 95) {
        drawBeam(p.x, p.y + 54, p.x + 34, supportBase, 3);
        drawBeam(p.x, p.y + 54, p.x - 28, supportBase, 3);
      }
    }

    ctx.beginPath();
    ctx.moveTo(first.x, first.y);
    run.forEach(p => ctx.lineTo(p.x, p.y));
    for (let i = run.length - 1; i >= 0; i--) ctx.lineTo(run[i].x, run[i].y + 11);
    ctx.closePath();
    const deck = ctx.createLinearGradient(0, first.y, 0, first.y + 13);
    deck.addColorStop(0, '#697389');
    deck.addColorStop(.22, '#3a4358');
    deck.addColorStop(1, '#151c2c');
    ctx.fillStyle = deck;
    ctx.fill();

    ctx.strokeStyle = 'rgba(17,24,38,.94)';
    ctx.lineWidth = 5;
    ctx.beginPath();
    run.forEach((p, i) => i ? ctx.lineTo(p.x, p.y + 12) : ctx.moveTo(p.x, p.y + 12));
    ctx.stroke();
    ctx.strokeStyle = 'rgba(119,139,166,.55)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    run.forEach((p, i) => i ? ctx.lineTo(p.x, p.y + 10) : ctx.moveTo(p.x, p.y + 10));
    ctx.stroke();

    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.strokeStyle = 'rgba(180,204,217,.80)';
    ctx.lineWidth = 1.45;
    ctx.beginPath();
    run.forEach((p, i) => i ? ctx.lineTo(p.x, p.y) : ctx.moveTo(p.x, p.y));
    ctx.stroke();
    ctx.lineCap = 'butt';

    drawDataMarkers(run);
    if (debug) drawStructureDebug(run);

    if (first.x > 0) drawGapCap(first, true);
    if (last.x < w) drawGapCap(last, false);
  }
}

function drawDataMarkers(run) {
  let lastBucket = null;
  for (const p of run) {
    const bucket = Math.floor((p.world + 35) / 210);
    if (bucket === lastBucket) continue;
    lastBucket = bucket;
    if (p.x < 20 || p.x > cssW - 18) continue;
    const up = seeded(bucket, 17) > .44;
    const height = 9 + seeded(bucket, 8) * 8;
    ctx.strokeStyle = up ? 'rgba(89,231,199,.56)' : 'rgba(255,111,144,.54)';
    ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(p.x, p.y - height - 4); ctx.lineTo(p.x, p.y - 3); ctx.stroke();
    ctx.fillStyle = up ? 'rgba(89,231,199,.62)' : 'rgba(255,111,144,.58)';
    ctx.fillRect(p.x - 2, p.y - height, 4, Math.max(4, height * .58));
  }
}

function drawGapCap(point, leftEdge) {
  ctx.fillStyle = 'rgba(15,20,34,.98)';
  ctx.fillRect(point.x - (leftEdge ? 0 : 4), point.y, 4, 47);
  ctx.strokeStyle = leftEdge ? 'rgba(89,231,199,.72)' : 'rgba(255,137,102,.72)';
  ctx.lineWidth = 2;
  ctx.beginPath(); ctx.moveTo(point.x, point.y); ctx.lineTo(point.x, point.y + 12); ctx.stroke();
}

function drawStructureDebug(run) {
  ctx.strokeStyle = 'rgba(255,72,89,.92)';
  ctx.lineWidth = 1.2;
  ctx.beginPath();
  run.forEach((p, i) => i ? ctx.lineTo(p.x, p.y) : ctx.moveTo(p.x, p.y));
  ctx.stroke();
  ctx.strokeStyle = 'rgba(72,229,255,.78)';
  ctx.beginPath();
  run.forEach((p, i) => i ? ctx.lineTo(p.x, p.y + 11) : ctx.moveTo(p.x, p.y + 11));
  ctx.stroke();
}

function currentHeroState(frame, w) {
  if (!frame.loop) return frame.state;
  return worldState(frame.scroll + w * .26);
}

function heroPlacement(points, frame, w, h, heroState) {
  const x = w * (heroState === 'climb' ? .28 : .26);
  const p = yAt(points, x);
  const p0 = yAt(points, x - 8);
  const p1 = yAt(points, x + 8);
  let angle = Math.atan2(p1.y - p0.y, 16);
  if (heroState === 'climb') angle = Math.max(angle, -.66);
  if (heroState === 'air') {
    if (frame.loop) {
      const world = ((frame.scroll + x) % WORLD_LENGTH + WORLD_LENGTH) % WORLD_LENGTH;
      const progress = clamp((world - 800) / 240, 0, 1);
      return { x, y: h * .585 - 26 - Math.sin(progress * Math.PI) * h * .095, angle: lerp(-.12, .09, progress), ground: p };
    }
    return { x, y: h * .455, angle: -.10, ground: p };
  }
  return { x, y: p.y, angle, ground: p };
}

function drawContactShadow(hero, heroState, points, w, h, frame) {
  if (heroState === 'air') {
    let targetX;
    if (frame.loop) {
      const heroWorld = frame.scroll + hero.x;
      targetX = hero.x + (1040 - (((heroWorld % WORLD_LENGTH) + WORLD_LENGTH) % WORLD_LENGTH));
    } else targetX = w * .61;
    targetX = clamp(targetX, w * .42, w * .78);
    const target = yAt(points.filter(p => !p.gap), targetX);
    const distance = clamp(Math.abs(target.y - hero.y) / (h * .25), 0, 1);
    ctx.save();
    ctx.globalAlpha = lerp(.26, .08, distance);
    ctx.fillStyle = '#02050c';
    ctx.beginPath();
    ctx.ellipse(targetX, target.y + 1, lerp(30, 20, distance), lerp(5, 3, distance), 0, 0, TAU);
    ctx.fill();
    ctx.restore();
    ctx.strokeStyle = 'rgba(99,231,195,.72)';
    ctx.lineWidth = 1.2;
    for (let i = -1; i <= 1; i++) {
      ctx.beginPath();
      ctx.moveTo(targetX - 13 + i * 10, target.y - 8);
      ctx.lineTo(targetX - 7 + i * 10, target.y - 3);
      ctx.lineTo(targetX - 13 + i * 10, target.y + 2);
      ctx.stroke();
    }
    return;
  }
  ctx.save();
  ctx.translate(hero.x, hero.y + 2);
  ctx.rotate(hero.angle);
  const shadow = ctx.createRadialGradient(0, 0, 0, 0, 0, 42);
  shadow.addColorStop(0, 'rgba(0,0,0,.46)');
  shadow.addColorStop(1, 'rgba(0,0,0,0)');
  ctx.fillStyle = shadow;
  ctx.scale(1, .17);
  ctx.beginPath(); ctx.arc(0, 0, 42, 0, TAU); ctx.fill();
  ctx.restore();
}

function drawHero(points, frame, w, h) {
  const heroState = currentHeroState(frame, w);
  const hero = heroPlacement(points, frame, w, h, heroState);
  drawContactShadow(hero, heroState, points, w, h, frame);
  const [col, row] = poseFrame[heroState];
  const bounds = alphaBounds[heroState];
  const safeTop = clamp(h * .045, 24, 44);
  const safeBottom = clamp(h * .025, 14, 24);
  const usefulH = h - safeTop - safeBottom;
  const targetVisibleH = usefulH * .17;
  const scale = targetVisibleH / bounds.h;
  const destW = CELL_W * scale;
  const destH = CELL_H * scale;
  const left = -(bounds.x + bounds.w * .5) * scale;
  const top = -(bounds.y + bounds.h) * scale;

  ctx.save();
  ctx.translate(hero.x, hero.y);
  ctx.rotate(hero.angle);
  ctx.shadowColor = 'rgba(255,71,119,.48)';
  ctx.shadowBlur = Math.max(4, targetVisibleH * .035);
  ctx.drawImage(assets.bike, col * CELL_W, row * CELL_H, CELL_W, CELL_H, left, top, destW, destH);
  ctx.shadowBlur = 0;
  ctx.drawImage(assets.bike, col * CELL_W, row * CELL_H, CELL_W, CELL_H, left, top, destW, destH);
  if (debug) {
    ctx.strokeStyle = 'rgba(255,226,94,.88)';
    ctx.lineWidth = 1;
    ctx.strokeRect(-bounds.w * scale * .5, -bounds.h * scale, bounds.w * scale, bounds.h * scale);
  }
  ctx.restore();
  return { ...hero, state: heroState, bounds, visibleH: targetVisibleH, usefulH, scale };
}

function drawHUD(w, h, meta) {
  const safeTop = clamp(h * .045, 24, 44);
  const x = 18, y = safeTop + 7, pause = 56;
  const maxBar = Math.max(132, w - 118);
  ctx.fillStyle = 'rgba(7,10,24,.62)';
  roundedRect(x - 7, y - 9, maxBar + 21, 55, 12);
  ctx.fill();
  ctx.fillStyle = 'rgba(53,64,91,.88)';
  roundedRect(x, y + 7, maxBar, 5, 3);
  ctx.fill();
  const bar = ctx.createLinearGradient(x, 0, x + maxBar, 0);
  bar.addColorStop(0, '#69e5ca');
  bar.addColorStop(1, '#ff6b95');
  ctx.fillStyle = bar;
  roundedRect(x, y + 7, maxBar * meta.progress, 5, 3);
  ctx.fill();
  ctx.fillStyle = '#edf4ff';
  ctx.font = '800 12px ui-monospace, monospace';
  ctx.fillText(meta.time, x, y + 32);
  ctx.fillStyle = '#9aabc5';
  ctx.font = '800 10px ui-monospace, monospace';
  ctx.fillText(meta.cp, x + 84, y + 32);

  const px = w - pause - 14, py = safeTop;
  ctx.fillStyle = 'rgba(8,12,29,.76)';
  ctx.strokeStyle = 'rgba(173,192,221,.42)';
  ctx.lineWidth = 1;
  roundedRect(px, py, pause, pause, 15);
  ctx.fill(); ctx.stroke();
  ctx.fillStyle = '#e1eaf8';
  ctx.fillRect(px + 21, py + 18, 4, 20);
  ctx.fillRect(px + 31, py + 18, 4, 20);
}

function drawTouchButton(x, y, w, h, label, type, active) {
  const fill = ctx.createLinearGradient(x, y, x, y + h);
  if (active) {
    fill.addColorStop(0, type === 'gas' ? 'rgba(60,203,164,.78)' : 'rgba(224,78,121,.74)');
    fill.addColorStop(1, type === 'gas' ? 'rgba(24,102,84,.88)' : 'rgba(82,33,66,.9)');
  } else {
    fill.addColorStop(0, 'rgba(34,42,64,.88)');
    fill.addColorStop(1, 'rgba(14,20,35,.92)');
  }
  ctx.fillStyle = fill;
  ctx.strokeStyle = active ? (type === 'gas' ? 'rgba(112,255,207,.98)' : 'rgba(255,132,170,.95)') : 'rgba(142,164,194,.56)';
  ctx.lineWidth = active ? 2 : 1.3;
  roundedRect(x, y, w, h, 17);
  ctx.fill(); ctx.stroke();

  ctx.save();
  ctx.translate(x + w * .5, y + h * .37);
  ctx.strokeStyle = active ? '#fff' : '#c2cfe1';
  ctx.fillStyle = active ? '#fff' : '#c2cfe1';
  ctx.lineWidth = 2.2;
  ctx.lineCap = 'round';
  if (type === 'gas') {
    for (let i = -1; i <= 1; i++) {
      ctx.beginPath(); ctx.moveTo(-12 + i * 9, -5); ctx.lineTo(-5 + i * 9, 0); ctx.lineTo(-12 + i * 9, 5); ctx.stroke();
    }
  } else if (type === 'brake') {
    ctx.fillRect(-10, -7, 4, 14); ctx.fillRect(-2, -7, 4, 14);
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
  ctx.fillStyle = active ? '#fff' : '#c4d0e2';
  ctx.textAlign = 'center';
  ctx.font = `800 ${w < 70 ? 8.5 : 9.5}px ui-monospace, monospace`;
  ctx.fillText(label, x + w * .5, y + h - 10);
  ctx.textAlign = 'left';
}

function drawTouchUI(w, h, meta) {
  const pressed = new Set(meta.pressed);
  const safeBottom = clamp(h * .025, 14, 24);
  const bh = clamp(h * .074, 58, 68);
  const rightW = clamp(w * .225, 78, 94);
  const margin = w < 350 ? 12 : 18;
  const rightX = w - rightW - margin;
  const gasY = h - safeBottom - bh;
  const brakeY = gasY - bh - 10;
  const leftX = margin;
  const gap = 8;
  const available = rightX - leftX - 24;
  const tiltW = clamp((available - gap) * .5, 60, 82);

  const scrim = ctx.createLinearGradient(0, h - bh * 2.65, 0, h);
  scrim.addColorStop(0, 'rgba(5,8,18,0)');
  scrim.addColorStop(.4, 'rgba(5,8,18,.54)');
  scrim.addColorStop(1, 'rgba(5,8,18,.92)');
  ctx.fillStyle = scrim;
  ctx.fillRect(0, h - bh * 2.65, w, bh * 2.65);

  drawTouchButton(leftX, gasY, tiltW, bh, 'ВЕС НАЗАД', 'back', pressed.has('back'));
  drawTouchButton(leftX + tiltW + gap, gasY, tiltW, bh, 'ВЕС ВПЕРЁД', 'forward', pressed.has('forward'));
  drawTouchButton(rightX, brakeY, rightW, bh, 'ТОРМОЗ', 'brake', pressed.has('brake'));
  drawTouchButton(rightX, gasY, rightW, bh, 'ГАЗ', 'gas', pressed.has('gas'));
}

function drawDebugOverlay(w, h, hero, frame) {
  if (!debug) return;
  const y = clamp(h * .14, 92, 132);
  ctx.fillStyle = 'rgba(5,8,18,.84)';
  roundedRect(12, y, Math.min(292, w - 24), 89, 10);
  ctx.fill();
  ctx.font = '800 9px ui-monospace, monospace';
  ctx.fillStyle = '#ff5968'; ctx.fillText('COLLISION LINE', 22, y + 18);
  ctx.fillStyle = '#48e5ff'; ctx.fillText('DECK BOTTOM', 130, y + 18);
  ctx.fillStyle = '#ffb96b'; ctx.fillText('SUPPORTS / TRUSS', 22, y + 36);
  ctx.fillStyle = '#ffe25e'; ctx.fillText(`ALPHA ${hero.bounds.w}×${hero.bounds.h}px`, 150, y + 36);
  ctx.fillStyle = '#b9c8dd';
  ctx.fillText(`VISIBLE ${(hero.visibleH / hero.usefulH * 100).toFixed(1)}% useful H`, 22, y + 56);
  ctx.fillText(`PARALLAX .02 / .07 / .14 / .28 / 1.0`, 22, y + 74);
  if (frame.loop) ctx.fillText(`LOOP ${(frame.timeMs / 1000).toFixed(2)}s`, 208, y + 56);
}

function render(frame) {
  const w = cssW, h = cssH;
  drawBackground(w, h, frame.scroll);
  const points = createTrackPoints(w, h, frame);
  drawStructure(points, w, h);
  const hero = drawHero(points, frame, w, h);
  const meta = frame.loop ? stateMeta[hero.state] : stateMeta[frame.state];
  drawHUD(w, h, meta);
  drawTouchUI(w, h, meta);
  drawDebugOverlay(w, h, hero, frame);
  lastFrame = { ...frame, hero, parallax: PARALLAX, viewport: [w, h] };
}

function staticFrame() {
  return { loop: false, state, scroll: ({ flat: 0, climb: 180, air: 370, compression: 520 }[state]), timeMs: 0 };
}

function loopTick(now) {
  if (!startMs) startMs = now;
  const timeMs = (now - startMs) % LOOP_MS;
  const scroll = timeMs / LOOP_MS * WORLD_LENGTH;
  render({ loop: true, state: worldState(scroll + cssW * .26), scroll, timeMs });
  rafId = requestAnimationFrame(loopTick);
}

function startRender() {
  cancelAnimationFrame(rafId);
  startMs = 0;
  if (fixedFrameMs !== null) {
    const scroll = fixedFrameMs / LOOP_MS * WORLD_LENGTH;
    render({ loop: true, state: worldState(scroll + cssW * .26), scroll, timeMs: fixedFrameMs });
  } else if (mode === 'loop') rafId = requestAnimationFrame(loopTick);
  else render(staticFrame());
}

function resize() {
  cssW = forcedW || Math.max(280, window.innerWidth);
  cssH = forcedH || Math.max(500, window.innerHeight);
  // Fixed review exports are authored at an exact 1:1 CSS-pixel raster.
  // The unconstrained interactive lab keeps device-resolution rendering.
  const dpr = forcedW && forcedH ? 1 : Math.min(window.devicePixelRatio || 1, 2);
  if (forcedW && forcedH) {
    document.documentElement.style.width = `${cssW}px`;
    document.documentElement.style.height = `${cssH}px`;
    document.body.style.width = `${cssW}px`;
    document.body.style.height = `${cssH}px`;
    document.body.style.minHeight = `${cssH}px`;
  }
  canvas.style.width = `${cssW}px`;
  canvas.style.height = `${cssH}px`;
  canvas.width = Math.round(cssW * dpr);
  canvas.height = Math.round(cssH * dpr);
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  if (assetsReady) startRender();
}

function replaceQuery() {
  const next = new URL(location.href);
  next.searchParams.set('mode', mode);
  next.searchParams.set('state', state);
  debug ? next.searchParams.set('debug', '1') : next.searchParams.delete('debug');
  history.replaceState(null, '', next);
}

function updateControls() {
  document.querySelectorAll('[data-mode]').forEach(b => b.classList.toggle('active', b.dataset.mode === mode));
  document.querySelectorAll('[data-state]').forEach(b => b.classList.toggle('active', b.dataset.state === state));
  document.querySelector('[data-debug]').classList.toggle('active', debug);
}

function setMode(next) {
  mode = next;
  replaceQuery(); updateControls(); startRender();
}
function setState(next) {
  state = next;
  mode = 'static';
  replaceQuery(); updateControls(); startRender();
}

document.querySelectorAll('[data-mode]').forEach(button => button.addEventListener('click', () => setMode(button.dataset.mode)));
document.querySelectorAll('[data-state]').forEach(button => button.addEventListener('click', () => setState(button.dataset.state)));
document.querySelector('[data-debug]').addEventListener('click', () => { debug = !debug; replaceQuery(); updateControls(); startRender(); });
window.addEventListener('resize', resize);
window.addEventListener('keydown', event => {
  const idx = Number(event.key) - 1;
  if (idx >= 0 && idx < STATES.length) setState(STATES[idx]);
  if (event.key.toLowerCase() === 'l') setMode(mode === 'loop' ? 'static' : 'loop');
  if (event.key.toLowerCase() === 'd') { debug = !debug; replaceQuery(); updateControls(); startRender(); }
});

window.__visualV05 = {
  getState: () => ({ mode, state, debug, ready: assetsReady, frame: lastFrame }),
  setMode,
  setState,
  parallax: PARALLAX,
  alphaBounds: () => JSON.parse(JSON.stringify(alphaBounds)),
  renderAt: timeMs => {
    cancelAnimationFrame(rafId);
    mode = 'loop';
    const t = ((timeMs % LOOP_MS) + LOOP_MS) % LOOP_MS;
    render({ loop: true, state: worldState(t / LOOP_MS * WORLD_LENGTH + cssW * .26), scroll: t / LOOP_MS * WORLD_LENGTH, timeMs: t });
  },
};

async function start() {
  try {
    await assetPromise;
    measureAlphaBounds();
    assetsReady = true;
    resize();
    updateControls();
    requestAnimationFrame(() => requestAnimationFrame(() => {
      document.body.classList.add('ready');
      window.__visualV05Ready = true;
      document.dispatchEvent(new Event('visual-v05-ready'));
    }));
  } catch (error) {
    document.querySelector('#assetStatus').textContent = error.message;
    console.error(error);
  }
}

start();
