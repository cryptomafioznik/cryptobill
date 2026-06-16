// Shareable run recap. Auto-generates a square card (PNG) and a tweet-ready
// line. The recap IS the growth engine: a loss becomes content, not a churn.

function compact(value) {
  const v = Math.max(0, value);
  if (v >= 1e6) return `$${(v / 1e6).toFixed(2)}M`;
  if (v >= 1e3) return `$${(v / 1e3).toFixed(1)}k`;
  return `$${Math.round(v)}`;
}

export function resultText(data) {
  const { worth, rank, seedLabel, week, maxWeeks, epitaph } = data;
  return (
    `I turned $1,000 into ${compact(worth)} as a ${rank} in CryptoBill ` +
    `(seed ${seedLabel}, week ${week}/${maxWeeks}). ${epitaph} Beat my run.`
  );
}

export function buildRecapCanvas(data) {
  const { worth, rank, seedLabel, week, maxWeeks, epitaph, history, win } = data;
  const size = 1080;
  const canvas = document.createElement("canvas");
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext("2d");

  const ink = "#171716";
  const muted = "#66645f";
  const paper = "#f7f7f3";
  const green = "#188958";
  const red = "#c93f32";
  const accent = win ? green : red;

  // Paper background with the signature faint grid.
  ctx.fillStyle = paper;
  ctx.fillRect(0, 0, size, size);
  ctx.strokeStyle = "rgba(23,23,22,0.05)";
  ctx.lineWidth = 1;
  for (let x = 0; x <= size; x += 60) {
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x, size);
    ctx.stroke();
  }
  for (let y = 0; y <= size; y += 60) {
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(size, y);
    ctx.stroke();
  }

  const pad = 84;

  // Header
  ctx.fillStyle = ink;
  ctx.font = "800 40px Inter, system-ui, sans-serif";
  ctx.textBaseline = "top";
  ctx.fillText("CRYPTOBILL", pad, pad);
  ctx.fillStyle = muted;
  ctx.font = "600 26px Inter, system-ui, sans-serif";
  ctx.fillText("Virtual crypto career run", pad, pad + 50);

  // Net worth headline
  ctx.fillStyle = muted;
  ctx.font = "700 28px Inter, system-ui, sans-serif";
  ctx.fillText("FINAL NET WORTH", pad, pad + 150);
  ctx.fillStyle = accent;
  ctx.font = "800 150px Inter, system-ui, sans-serif";
  ctx.fillText(compact(worth), pad, pad + 184);

  // Rank
  ctx.fillStyle = ink;
  ctx.font = "800 56px Inter, system-ui, sans-serif";
  ctx.fillText(rank, pad, pad + 360);

  // Sparkline
  const chartTop = pad + 470;
  const chartH = 230;
  const chartW = size - pad * 2;
  ctx.strokeStyle = "#d8d7d0";
  ctx.lineWidth = 2;
  ctx.strokeRect(pad, chartTop, chartW, chartH);
  const values = history && history.length > 1 ? history : [1000, worth];
  const max = Math.max(...values);
  const min = Math.min(...values);
  ctx.strokeStyle = accent;
  ctx.lineWidth = 6;
  ctx.lineJoin = "round";
  ctx.lineCap = "round";
  ctx.beginPath();
  values.forEach((value, i) => {
    const x = pad + 16 + ((chartW - 32) / Math.max(values.length - 1, 1)) * i;
    const y =
      chartTop + chartH - 16 - ((value - min) / Math.max(max - min, 1)) * (chartH - 32);
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  });
  ctx.stroke();

  // Empire flex (home + ride)
  if (data.empire) {
    ctx.fillStyle = muted;
    ctx.font = "700 30px Inter, system-ui, sans-serif";
    ctx.fillText(data.empire, pad, chartTop + chartH + 40);
  }

  // Epitaph
  ctx.fillStyle = ink;
  ctx.font = "600 30px Inter, system-ui, sans-serif";
  wrapText(ctx, epitaph, pad, chartTop + chartH + 90, chartW, 40);

  // Footer
  ctx.fillStyle = muted;
  ctx.font = "700 24px Inter, system-ui, sans-serif";
  ctx.fillText(`SEED ${seedLabel}   ·   WEEK ${week}/${maxWeeks}`, pad, size - pad - 6);

  return canvas;
}

function wrapText(ctx, text, x, y, maxWidth, lineHeight) {
  const words = String(text).split(" ");
  let line = "";
  let cy = y;
  for (const word of words) {
    const test = line ? `${line} ${word}` : word;
    if (ctx.measureText(test).width > maxWidth && line) {
      ctx.fillText(line, x, cy);
      line = word;
      cy += lineHeight;
    } else {
      line = test;
    }
  }
  if (line) ctx.fillText(line, x, cy);
}
