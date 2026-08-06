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
    /// земля выглядела сглаженным холмом, этого отличия в кадре не было вообще, и игра
    /// читалась обобщённой. Свеча как форма рельефа возвращает смысл названию.
    ///
    /// Причинность соблюдена: тело свечи рисуется между её open и close, то есть между
    /// теми же узлами, по которым построена КОЛЛИЗИЯ. Игрок едет ровно по тому, что видит,
    /// а не по невидимой линии рядом со свечами.
    ///
    /// Цвет несёт данные, а не настроение: зелёная — узел вверх, красная — вниз. Это
    /// прямое следование правилу проекта «светятся только данные»: горит рынок, а не декор.
    /// </summary>
    public static class CandleView
    {
        // Палитра. Свечи — единственный источник цвета в мире, поэтому они заметно
        // светлее рельефа, но темнее неба: они всё ещё в контражуре.
        // СВЕТЛОТЫ ПОДОБРАНЫ ПО СНЯТОМУ КАДРУ, а не по палитре. Первая редакция дала
        // насыщенные зелёные тела на 40 % экрана — они перетянули на себя и контраст, и
        // внимание, и герой перестал быть главным объектом. Свечи стоят БЛИЖЕ солнца, чем
        // герой, значит по логике сцены они темнее его фона, но светлее самого героя:
        // ровно узкая полоса между небом и силуэтом.
        public static readonly Color UpBody = new Color(0.11f, 0.30f, 0.21f, 1f);
        public static readonly Color DownBody = new Color(0.32f, 0.11f, 0.16f, 1f);
        public static readonly Color UpEdge = new Color(0.38f, 0.86f, 0.58f, 1f);
        public static readonly Color DownEdge = new Color(0.94f, 0.36f, 0.40f, 1f);
        public static readonly Color Wick = new Color(0.42f, 0.42f, 0.52f, 1f);
        public static readonly Color Deep = new Color(0.030f, 0.028f, 0.048f, 1f);

        /// <summary>Насколько глубоко вниз уходят тела свечей, метры.</summary>
        public const float BodyDepthM = 26f;

        /// <summary>
        /// За сколько метров вниз тело свечи уходит в темноту. Это НЕ то же, что глубина
        /// тела: кадр показывает всего ~17 м по высоте, поэтому градиент, растянутый на
        /// всю глубину, в кадр не попадал и свечи выглядели равномерно яркими до нижнего
        /// края экрана. Затухание должно укладываться в кадр, а не в геометрию.
        /// </summary>
        public const float FadeDepthM = 4.5f;

        public static GameObject Build(TrackProfile profile,
            IReadOnlyList<CandleTrackGenerator.Candle> candles, Transform parent)
        {
            var root = new GameObject("CandleTerrain");
            if (parent != null) root.transform.SetParent(parent, false);

            var k = UnitsContract.PxToM;
            var step = profile.nodeStepPx * k;
            // Зазор между телами: без него свечи сливаются в сплошную стену и перестают
            // читаться свечами. Доля, а не абсолют — чтобы держаться при смене шага.
            var gap = step * 0.16f;
            var bodyW = step - gap;

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var wv = new List<Vector3>();
            var wc = new List<Color>();
            var wt = new List<int>();

            var deepBottom = float.MaxValue;
            for (var i = 0; i < candles.Count; i++)
                deepBottom = Mathf.Min(deepBottom, candles[i].LowPx * k);
            deepBottom -= BodyDepthM;

            for (var i = 0; i < candles.Count; i++)
            {
                var cd = candles[i];
                var x = i * step;
                var cx = x + step * 0.5f;

                var top = Mathf.Max(cd.OpenPx, cd.ClosePx) * k;
                var bot = Mathf.Min(cd.OpenPx, cd.ClosePx) * k;
                // Минимальная высота тела: узел без движения цены иначе исчезает,
                // и в ряду появляется дыра, которой в рельефе нет.
                if (top - bot < step * 0.28f)
                {
                    var mid = (top + bot) * 0.5f;
                    top = mid + step * 0.14f;
                    bot = mid - step * 0.14f;
                }

                var body = cd.Up ? UpBody : DownBody;
                var edge = cd.Up ? UpEdge : DownEdge;

                // ФИТИЛЬ — рисуется первым, чтобы тело его перекрыло по центру.
                Shapes.AddBar(wv, wc, wt,
                    new Vector2(cx, cd.LowPx * k), new Vector2(cx, cd.HighPx * k),
                    bodyW * 0.14f, Wick);

                // ТЕЛО в два яруса: верхний уходит в темноту за FadeDepthM (это влезает
                // в кадр), нижний — сплошная глубина. Один ярус на всю глубину давал
                // равномерно яркую стену до низа экрана.
                var fade = top - FadeDepthM;
                AddQuad(v, c, t,
                    new Vector2(cx - bodyW * 0.5f, top), new Vector2(cx + bodyW * 0.5f, top),
                    new Vector2(cx + bodyW * 0.5f, fade), new Vector2(cx - bodyW * 0.5f, fade),
                    body, Deep);
                AddQuad(v, c, t,
                    new Vector2(cx - bodyW * 0.5f, fade), new Vector2(cx + bodyW * 0.5f, fade),
                    new Vector2(cx + bodyW * 0.5f, deepBottom), new Vector2(cx - bodyW * 0.5f, deepBottom),
                    Deep, Deep);

                // Кромка по верхней грани тела — то, по чему глаз читает саму линию цены.
                Shapes.AddBar(v, c, t,
                    new Vector2(cx - bodyW * 0.5f, top), new Vector2(cx + bodyW * 0.5f, top),
                    step * 0.12f, edge);
            }

            var wicks = Shapes.Create("CandleWicks", root.transform,
                Shapes.Build("CandleWicks", wv, wc, wt), -21);
            wicks.transform.localPosition = new Vector3(0f, 0f, 0.02f);

            Shapes.Create("CandleBodies", root.transform, Shapes.Build("CandleBodies", v, c, t), -20);

            return root;
        }

        /// <summary>Четырёхугольник с вертикальным градиентом: верх — цвет свечи, низ — глубина.</summary>
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
