using System.Collections.Generic;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// ТРАССА-ЛЕНТА — порт drawTrackReal + drawCandleBodies из toys/chartrider.html.
    ///
    /// Дорога = приподнятая дека («лента»), под ней виден мир, с деки СВИСАЮТ полупрозрачные
    /// свечи-колонны данных. Система исходника (b203): «дорога стоит на свечах».
    ///
    /// Язык элементов (b208): структура трассы — нейтральная СТАЛЬ во всех эпохах;
    /// ЦВЕТ остаётся только у данных — зелёный подъём / красный спуск на верхней кромке.
    /// Верх кромки = сама поверхность коллизии (правило, оплаченное багом в CandleView).
    ///
    /// Геометрия косоугольная: нижняя кромка = верхняя + константный вектор (TRV_X, TDECK),
    /// все рёбра параллельны — «смотрим чуть сверху-справа», как в Gravity Defied.
    /// </summary>
    public static class TrackDeckView
    {
        private const float Px = 0.0272f; // UnitsContract.PxToM — px исходника уже метричны

        private static readonly Vector2 Oblique = new Vector2(-9f * Px, -34f * Px); // (TRV_X, TDECK)

        public static GameObject Build(TrackProfile profile, TerrainSampler terrain, Transform parent,
            IReadOnlyList<CandleTrackGenerator.Candle> candles = null,
            IReadOnlyList<CandleTrackGenerator.EventSpan> events = null)
        {
            var root = new GameObject("TrackDeck");
            if (parent != null) root.transform.SetParent(parent, false);

            var pts = profile.SamplePolylineM();
            if (pts.Count < 2) return root;

            BuildPriceGrid(root.transform, pts);
            BuildShadow(root.transform, pts);
            BuildBody(root.transform, pts);
            BuildStructure(root.transform, profile, pts);
            BuildDataLine(root.transform, pts);
            if (candles != null) BuildCandleColumns(root.transform, profile, terrain, candles, events);

            return root;
        }

        /// <summary>Терминальная сетка: едва заметные горизонтальные ценовые уровни.</summary>
        private static void BuildPriceGrid(Transform parent, List<Vector2> pts)
        {
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            var maxX = 0f;
            foreach (var p in pts)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
                maxX = Mathf.Max(maxX, p.x);
            }

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var col = new Color(120 / 255f, 150 / 255f, 200 / 255f, 0.06f);
            var step = 70f * Px;
            for (var y = Mathf.Floor((minY - 8f) / step) * step; y < maxY + 10f; y += step)
                Shapes.AddBar(v, c, t, new Vector2(-10f, y), new Vector2(maxX + 10f, y), 0.03f, col);

            Shapes.Create("PriceGrid", parent, Shapes.Build("PriceGrid", v, c, t), -34);
        }

        /// <summary>Мягкая тень-глубина под лентой: дорога приподнята над миром.</summary>
        private static void BuildShadow(Transform parent, List<Vector2> pts)
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var s0 = new Color(WorldPalette.DeckShadow.r, WorldPalette.DeckShadow.g,
                WorldPalette.DeckShadow.b, 0.5f);
            var s1 = new Color(s0.r, s0.g, s0.b, 0f);
            var drop = 90f * Px;

            for (var i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i] + Oblique;
                var b = pts[i + 1] + Oblique;
                var i0 = v.Count;
                v.Add(new Vector3(a.x, a.y, 0f)); c.Add(Shapes.V(s0));
                v.Add(new Vector3(b.x, b.y, 0f)); c.Add(Shapes.V(s0));
                v.Add(new Vector3(b.x, b.y - drop, 0f)); c.Add(Shapes.V(s1));
                v.Add(new Vector3(a.x, a.y - drop, 0f)); c.Add(Shapes.V(s1));
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
            }
            Shapes.Create("DeckShadow", parent, Shapes.Build("DeckShadow", v, c, t), -30);
        }

        /// <summary>Тело деки: непрозрачный графит, верх чуть светлее низа.</summary>
        private static void BuildBody(Transform parent, List<Vector2> pts)
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var top = new Color(WorldPalette.DeckTop.r, WorldPalette.DeckTop.g,
                WorldPalette.DeckTop.b, 0.97f);
            var bot = new Color(WorldPalette.DeckBottom.r, WorldPalette.DeckBottom.g,
                WorldPalette.DeckBottom.b, 0.97f);

            for (var i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var a2 = a + Oblique;
                var b2 = b + Oblique;
                var i0 = v.Count;
                v.Add(new Vector3(a.x, a.y, 0f)); c.Add(Shapes.V(top));
                v.Add(new Vector3(b.x, b.y, 0f)); c.Add(Shapes.V(top));
                v.Add(new Vector3(b2.x, b2.y, 0f)); c.Add(Shapes.V(bot));
                v.Add(new Vector3(a2.x, a2.y, 0f)); c.Add(Shapes.V(bot));
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
            }
            Shapes.Create("DeckBody", parent, Shapes.Build("DeckBody", v, c, t), -28);
        }

        /// <summary>Сталь структуры: рёбра-шпалы по узлам + нижняя кромка.</summary>
        private static void BuildStructure(Transform parent, TrackProfile profile, List<Vector2> pts)
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var steel = WorldPalette.Steel;
            var rib = new Color(steel.r, steel.g, steel.b, 0.38f);
            var edge = new Color(steel.r, steel.g, steel.b, 0.66f);

            // Рёбра по узлам (~26 px исходника = шаг узла профиля).
            var stepM = profile.nodeStepPx * Px;
            var endX = pts[pts.Count - 1].x;
            var sampler = new TerrainSampler(profile);
            for (var x = 0f; x <= endX; x += stepM)
            {
                var y = sampler.HeightAt(x);
                var a = new Vector2(x, y);
                Shapes.AddBar(v, c, t, a, a + Oblique, 1.9f * Px, rib);
            }

            // Нижняя стальная кромка.
            for (var i = 0; i < pts.Count - 1; i++)
                Shapes.AddBar(v, c, t, pts[i] + Oblique, pts[i + 1] + Oblique, 2.4f * Px, edge);

            Shapes.Create("DeckSteel", parent, Shapes.Build("DeckSteel", v, c, t), -27);
        }

        /// <summary>
        /// ВЕРХНЯЯ КРОМКА — ДАННЫЕ. Зелёный подъём / красный спуск / сталь на плоском.
        /// Два прохода, как в исходнике: широкий свет (α 0.20), ложащийся на полотно,
        /// и яркое ядро. Ядро — Emissive: это единственное, что светится у трассы.
        /// </summary>
        private static void BuildDataLine(Transform parent, List<Vector2> pts)
        {
            var gv = new List<Vector3>();
            var gc = new List<Color>();
            var gt = new List<int>();
            var cv = new List<Vector3>();
            var cc = new List<Color>();
            var ct = new List<int>();
            var steel = WorldPalette.Steel;
            var flatThreshold = 1f * Px; // порог исходника: ±1 px на узел

            for (var i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var climb = b.y > a.y + flatThreshold;
                var drop = b.y < a.y - flatThreshold;
                var col = climb ? WorldPalette.DataUp : drop ? WorldPalette.DataDown : steel;

                if (climb || drop)
                {
                    // Свет данных ложится на полотно — смещён внутрь деки.
                    Shapes.AddBar(gv, gc, gt, a + new Vector2(0f, -3.6f * Px),
                        b + new Vector2(0f, -3.6f * Px), 10.5f * Px,
                        new Color(col.r, col.g, col.b, 0.20f));
                }
                Shapes.AddBar(cv, cc, ct, a, b, 2.9f * Px,
                    new Color(col.r, col.g, col.b, climb || drop ? 0.9f : 0.78f));
            }

            Shapes.Create("DataGlow", parent, Shapes.Build("DataGlow", gv, gc, gt), -26);
            Shapes.Create("DataLine", parent, Shapes.Build("DataLine", cv, cc, ct), -25,
                Shapes.Emissive(1.9f));
        }

        /// <summary>
        /// СВЕЧИ-КОЛОННЫ данных, свисающие с низа деки (b203). Полупрозрачные, с верхней
        /// гранью-фаской (объём), фитилём вверх сквозь деку и точкой на пике.
        /// Рисуются ПОВЕРХ деки — урок исходника: дека перекрывала их, и на тихих
        /// участках график исчезал из кадра.
        /// </summary>
        private static void BuildCandleColumns(Transform parent, TrackProfile profile,
            TerrainSampler terrain, IReadOnlyList<CandleTrackGenerator.Candle> candles,
            IReadOnlyList<CandleTrackGenerator.EventSpan> events)
        {
            var evAt = new Dictionary<int, CandleTrackGenerator.MarketEvent>();
            if (events != null)
            {
                foreach (var e in events)
                    for (var n = e.FromNode; n <= e.ToNode; n++) evAt[n] = e.Type;
            }

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var tv = new List<Vector3>(); // светящиеся точки пиков — отдельный меш
            var tc = new List<Color>();
            var tt = new List<int>();

            var stepM = profile.nodeStepPx * Px;
            var cw = stepM * 0.66f;
            var dx = 3f * Px;
            var dy = 2.6f * Px;
            const float CA = 1.28f; // worldNew: свечи чуть ярче — герой в глубоком мире

            for (var i = 0; i < candles.Count; i++)
            {
                var cd = candles[i];
                var x = (i + 0.5f) * stepM;
                var surfY = terrain.HeightAt(x);
                var col = cd.Up ? WorldPalette.DataUp : WorldPalette.DataDown;
                if (evAt.TryGetValue(i, out var ev))
                    col = ev == CandleTrackGenerator.MarketEvent.Rally
                        ? WorldPalette.DataUp : WorldPalette.DataDown;

                // Колонна: top = низ деки + 2 px, высота = |close − open|.
                var bodyH = Mathf.Max(2f * Px, Mathf.Abs(cd.ClosePx - cd.OpenPx) * Px);
                var by = surfY + Oblique.y - 2f * Px;
                var bo = by - bodyH;

                // Фронт-тело (α 0.20).
                QuadFlat(v, c, t, x - cw / 2f, x + cw / 2f, bo, by,
                    new Color(col.r, col.g, col.b, 0.20f * CA));
                // Верхняя грань-фаска (α 0.38) — косой параллелограмм.
                Rhomb(v, c, t, new Vector2(x - cw / 2f, by), new Vector2(x + cw / 2f, by),
                    new Vector2(dx, dy), new Color(col.r, col.g, col.b, 0.38f * CA));
                // Боковая грань (α 0.10).
                Rhomb(v, c, t, new Vector2(x + cw / 2f, bo), new Vector2(x + cw / 2f, by),
                    new Vector2(dx, dy), new Color(col.r, col.g, col.b, 0.10f * CA));
                // Hairline-блик слева.
                Shapes.AddBar(v, c, t, new Vector2(x - cw / 2f + 0.5f * Px, bo),
                    new Vector2(x - cw / 2f + 0.5f * Px, by), 1f * Px,
                    new Color(235 / 255f, 1f, 248 / 255f, 0.10f * CA));

                // Фитиль ВВЕРХ: от пика тени до поверхности. Тонкий — читается данными,
                // а не препятствием (проверено годами в исходнике).
                var wickTop = surfY + Mathf.Max(0f, (Mathf.Max(cd.OpenPx, cd.ClosePx) < cd.HighPx
                    ? (cd.HighPx - Mathf.Max(cd.OpenPx, cd.ClosePx)) * Px : 0f));
                if (wickTop > surfY + 0.02f)
                {
                    Shapes.AddBar(v, c, t, new Vector2(x, surfY), new Vector2(x, wickTop),
                        1f * Px, new Color(col.r, col.g, col.b, 0.30f * CA));
                    Shapes.AddDisc(tv, tc, tt, new Vector2(x, wickTop), 1.3f * Px,
                        new Color(col.r, col.g, col.b, 0.45f), 6);
                }
                // Фитиль ВНИЗ приглушён.
                var wickLow = (Mathf.Min(cd.OpenPx, cd.ClosePx) - cd.LowPx) * Px;
                if (wickLow > 0.02f)
                {
                    Shapes.AddBar(v, c, t, new Vector2(x, bo), new Vector2(x, bo - wickLow),
                        1f * Px, new Color(col.r, col.g, col.b, 0.30f * CA * 0.55f));
                }
            }

            Shapes.Create("CandleColumns", parent, Shapes.Build("CandleColumns", v, c, t), -24);
            Shapes.Create("CandleTips", parent, Shapes.Build("CandleTips", tv, tc, tt), -23,
                Shapes.Emissive(1.6f));
        }

        private static void QuadFlat(List<Vector3> v, List<Color> c, List<int> t,
            float x0, float x1, float y0, float y1, Color col)
        {
            var i0 = v.Count;
            v.Add(new Vector3(x0, y0, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(x1, y0, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(x1, y1, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(x0, y1, 0f)); c.Add(Shapes.V(col));
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
            t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
        }

        /// <summary>Параллелограмм от ребра (a→b) со сдвигом d — грани псевдо-3D свечи.</summary>
        private static void Rhomb(List<Vector3> v, List<Color> c, List<int> t,
            Vector2 a, Vector2 b, Vector2 d, Color col)
        {
            var i0 = v.Count;
            v.Add(new Vector3(a.x, a.y, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(b.x, b.y, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(b.x + d.x, b.y + d.y, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(a.x + d.x, a.y + d.y, 0f)); c.Add(Shapes.V(col));
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
            t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
        }
    }
}
