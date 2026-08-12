using System.Collections.Generic;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Земля, СОСТОЯЩАЯ ИЗ СВЕЧЕЙ. Не украшение поверх рельефа, а сам рельеф.
    ///
    /// Почему это важнее любой другой визуальной работы: игра называется CHART RUNNER,
    /// и её единственное отличие от любого другого trials — что едешь по графику. Пока
    /// земля выглядела сглаженным холмом, этого отличия в кадре не было вообще.
    ///
    /// ГЛАВНОЕ ПРАВИЛО ЭТОГО ФАЙЛА, ОПЛАЧЕННОЕ БАГОМ: ВЕРХ ЗАЛИВКИ = САМА ПОВЕРХНОСТЬ,
    /// по которой построена коллизия. Не уровень узла, не max(open, close), а поверхность.
    ///
    /// Первая редакция рисовала тело свечи прямоугольником с ГОРИЗОНТАЛЬНЫМ верхом на
    /// уровне max(open, close). На спуске open выше close, значит верх прямоугольника —
    /// это уровень ПРЕДЫДУЩЕГО узла, протянутый через всю ячейку, а коллизия за это время
    /// уходит вниз по кривой. Байк честно ехал по кривой и оказывался НИЖЕ нарисованных
    /// свечей — со стороны выглядело, будто он проваливается внутрь графика. Расхождение
    /// рисунка и коллизии — тот самый дефект, из-за которого нельзя судить о филе: игрок
    /// видит не то, по чему едет.
    ///
    /// Поэтому заливка строится подстолбцами по РЕАЛЬНОЙ поверхности, а «свечность» несут
    /// цвет ячейки, тёмные разделители на границах узлов и фитили над линией.
    ///
    /// Цвет несёт данные, а не настроение: зелёная — узел вверх, красная — вниз.
    /// </summary>
    public static class CandleView
    {
        // Светлоты подобраны по снятому кадру: насыщенные тела на 40 % экрана перетягивали
        // контраст с героя. Свечи темнее неба и светлее силуэта — узкая полоса между ними.
        // Пересняты после включения bloom и цветокоррекции: рядом со светящейся кромкой
        // прежние тела проваливались в черноту, и графика под линией не было видно.
        public static readonly Color UpBody = new Color(0.14f, 0.38f, 0.26f, 1f);
        public static readonly Color DownBody = new Color(0.40f, 0.14f, 0.19f, 1f);
        public static readonly Color UpEdge = new Color(0.38f, 0.86f, 0.58f, 1f);
        public static readonly Color DownEdge = new Color(0.94f, 0.36f, 0.40f, 1f);
        public static readonly Color Wick = new Color(0.30f, 0.30f, 0.40f, 1f);
        public static readonly Color Deep = new Color(0.042f, 0.038f, 0.066f, 1f);
        public static readonly Color Separator = new Color(0.015f, 0.014f, 0.028f, 1f);

        /// <summary>Насколько глубоко вниз уходит масса графика, метры.</summary>
        public const float DepthM = 60f;

        /// <summary>
        /// За сколько метров вниз цвет свечи уходит в темноту. Кадр показывает ~17 м по
        /// высоте, поэтому затухание должно укладываться в кадр, а не в геометрию: градиент,
        /// растянутый на всю глубину, давал равномерно яркую стену до низа экрана.
        /// </summary>
        public const float FadeDepthM = 4.5f;

        /// <summary>Подстолбцов на свечу. Верх заливки идёт по поверхности этими шагами.</summary>
        private const int SubColumns = 4;

        /// <summary>Свечи внутри события горят ярче: сет-пьеса обязана быть видна издалека.</summary>
        public static readonly Color RallyTint = new Color(0.20f, 0.62f, 0.36f, 1f);
        public static readonly Color FlashTint = new Color(0.58f, 0.20f, 0.16f, 1f);

        public static GameObject Build(TrackProfile profile,
            IReadOnlyList<CandleTrackGenerator.Candle> candles, TerrainSampler terrain,
            Transform parent, IReadOnlyList<CandleTrackGenerator.EventSpan> events = null)
        {
            // Разметка событий по узлам: внутри сет-пьесы свечи окрашены её цветом,
            // поэтому игрок видит участок ЗАРАНЕЕ, а не узнаёт о нём, влетев в него.
            var evAt = new Dictionary<int, CandleTrackGenerator.MarketEvent>();
            if (events != null)
            {
                foreach (var e in events)
                    for (var n = e.FromNode; n <= e.ToNode; n++) evAt[n] = e.Type;
            }

            var root = new GameObject("CandleTerrain");
            if (parent != null) root.transform.SetParent(parent, false);

            var k = UnitsContract.PxToM;
            var step = profile.nodeStepPx * k;

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var ov = new List<Vector3>();
            var oc = new List<Color>();
            var ot = new List<int>();

            var bottom = float.MaxValue;
            for (var i = 0; i < candles.Count; i++)
                bottom = Mathf.Min(bottom, candles[i].LowPx * k);
            bottom -= DepthM;

            for (var i = 0; i < candles.Count; i++)
            {
                var cd = candles[i];
                var x0 = i * step;
                var body = cd.Up ? UpBody : DownBody;
                var edge = cd.Up ? UpEdge : DownEdge;
                if (evAt.TryGetValue(i, out var ev))
                {
                    body = ev == CandleTrackGenerator.MarketEvent.Rally ? RallyTint : FlashTint;
                    edge = ev == CandleTrackGenerator.MarketEvent.Rally ? UpEdge : DownEdge;
                }

                // ---- ТЕЛО СВЕЧИ ----
                //
                // Глубина тела пропорциональна размаху узла |close − open|: крупное движение
                // рынка = высокий цветной блок, мелкое = тонкий. Это и возвращает свечность,
                // потерянную после починки провала: первая правка сделала верх заливки
                // поверхностью и получила сплошную зелёную массу с тёмными полосами —
                // читалось забором, а не графиком.
                //
                // Тело висит ПОД линией цены, поэтому перекрыть байка не может по построению.
                var span = Mathf.Abs(cd.ClosePx - cd.OpenPx) * k;
                var bodyDepth = Mathf.Clamp(span * 1.35f, step * 2.4f, FadeDepthM * 1.6f);

                for (var s = 0; s < SubColumns; s++)
                {
                    var ax = x0 + step * (s / (float)SubColumns);
                    var bx = x0 + step * ((s + 1) / (float)SubColumns);
                    var ay = terrain.HeightAt(ax);
                    var by = terrain.HeightAt(bx);

                    // Тело: от поверхности вниз на свою глубину, с уходом в темноту.
                    AddQuad(v, c, t,
                        new Vector2(ax, ay), new Vector2(bx, by),
                        new Vector2(bx, by - bodyDepth), new Vector2(ax, ay - bodyDepth),
                        body, Deep);
                    // Ниже тела — глубина: масса графика, по которой нельзя прочитать свечу.
                    AddQuad(v, c, t,
                        new Vector2(ax, ay - bodyDepth), new Vector2(bx, by - bodyDepth),
                        new Vector2(bx, bottom), new Vector2(ax, bottom),
                        Deep, Deep);

                    // Кромка по поверхности — линия цены, по которой едет игрок.
                    Shapes.AddBar(ov, oc, ot, new Vector2(ax, ay), new Vector2(bx, by),
                        step * 0.14f, edge);
                }

                // ---- разделитель на границе узлов ----
                // Короткий НАСЕЧКОЙ, а не во всю глубину: длинные полосы читались забором.
                var sy = terrain.HeightAt(x0);
                Shapes.AddBar(ov, oc, ot,
                    new Vector2(x0, sy + 0.04f), new Vector2(x0, sy - step * 0.75f),
                    step * 0.09f, Separator);

                // ---- фитиль ----
                //
                // Рисуется ПОД поверхностью, а не над ней. Причина не эстетическая:
                // всё, что нарисовано выше линии езды, глаз читает как препятствие, а
                // фитиль проезжается насквозь. Такое расхождение рисунка и проходимости —
                // тот же класс дефекта, что и провал внутрь свечей, только наоборот.
                //
                // Длина внутрь массы пропорциональна тени свечи, поэтому волатильность
                // по-прежнему видна: на дёрганом рынке штрихи длиннее.
                var cx = x0 + step * 0.5f;
                var surf = terrain.HeightAt(cx);
                var shadow = (cd.HighPx - cd.LowPx) * k;
                Shapes.AddBar(ov, oc, ot,
                    new Vector2(cx, surf - bodyDepth * 0.15f),
                    new Vector2(cx, surf - bodyDepth * 0.15f - Mathf.Clamp(shadow * 0.9f, step * 0.5f, 6f)),
                    step * 0.10f, Wick);
            }

            // ---- СЛОИ В МАССЕ ГРАФИКА ----
            //
            // Без них всё ниже полосы свечей — чёрный провал: снятый кадр показал, что низ
            // кадра пустой, и земля не читается землёй. Слои идут ПАРАЛЛЕЛЬНО поверхности
            // (так залегает порода на склоне) и подчёркивают форму рельефа, а не спорят
            // с ней. Контраст на пределе различимости намеренно: всё ярче начнёт
            // конкурировать и со свечами, и с силуэтом героя.
            var sv = new List<Vector3>();
            var sc = new List<Color>();
            var stt = new List<int>();
            var depths = new[] { 6.5f, 9.5f, 13.5f, 18.5f, 25f };
            var last = candles.Count * step;
            for (var d = 0; d < depths.Length; d++)
            {
                var fade = 1f - d / (float)depths.Length;
                var band = new Color(0.10f * (1f + fade), 0.10f * (1f + fade), 0.15f * (1f + fade), 1f);
                for (var x = 0f; x < last - step; x += step)
                {
                    Shapes.AddBar(sv, sc, stt,
                        new Vector2(x, terrain.HeightAt(x) - depths[d]),
                        new Vector2(x + step, terrain.HeightAt(x + step) - depths[d]),
                        0.09f + d * 0.03f, band);
                }
            }
            var strata = Shapes.Create("CandleStrata", root.transform,
                Shapes.Build("CandleStrata", sv, sc, stt), -20);
            strata.transform.localPosition = new Vector3(0f, 0f, -0.005f);

            Shapes.Create("CandleBodies", root.transform, Shapes.Build("CandleBodies", v, c, t), -20);
            // Кромка — ЛИНИЯ ЦЕНЫ, единственные данные в кадре — светится через bloom.
            // Правило «светятся только данные» выполняется конструкцией: Emissive-материал
            // стоит на кромке и ни на чём другом в земле.
            var over = Shapes.Create("CandleEdges", root.transform,
                Shapes.Build("CandleEdges", ov, oc, ot), -19, Shapes.Emissive(2.1f));
            over.transform.localPosition = new Vector3(0f, 0f, -0.01f);

            return root;
        }

        private static void AddQuad(List<Vector3> v, List<Color> c, List<int> t,
            Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl, Color top, Color bottom)
        {
            var i0 = v.Count;
            v.Add(new Vector3(tl.x, tl.y, 0f)); c.Add(Shapes.V(top));
            v.Add(new Vector3(tr.x, tr.y, 0f)); c.Add(Shapes.V(top));
            v.Add(new Vector3(br.x, br.y, 0f)); c.Add(Shapes.V(bottom));
            v.Add(new Vector3(bl.x, bl.y, 0f)); c.Add(Shapes.V(bottom));
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
        }
    }
}
