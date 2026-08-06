using System.Collections.Generic;
using ChartRunner.Track;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Видимая трасса: заливка под линией рельефа плюс яркая кромка.
    ///
    /// Строится ИЗ ТОГО ЖЕ профиля, что и коллизия, тем же шагом. Это не украшение, а
    /// требование честности: если рисунок и коллизия расходятся, игрок бьётся о невидимое,
    /// и любое суждение о филе становится суждением о рассинхроне.
    ///
    /// Кромка рисуется отдельной полосой ПОВЕРХ заливки, потому что именно кромку игрок
    /// читает как «где земля» — на тёмной заливке контур несёт всю информацию о рельефе.
    /// </summary>
    public static class TerrainView
    {
        /// <summary>Насколько глубоко вниз уходит заливка, метры.</summary>
        public const float SkirtDepthM = 60f;

        public static GameObject Build(TrackProfile profile, Transform parent,
            Color fillTop, Color fillBottom, Color edge, float edgeWidthM = 0.14f)
        {
            var pts = profile.SamplePolylineM();
            if (pts.Count < 2) return new GameObject("TerrainView");

            var minY = float.MaxValue;
            for (var i = 0; i < pts.Count; i++) if (pts[i].y < minY) minY = pts[i].y;
            var bottom = minY - SkirtDepthM;

            // ---- заливка ----
            var v = new List<Vector3>(pts.Count * 2);
            var c = new List<Color>(pts.Count * 2);
            var t = new List<int>((pts.Count - 1) * 6);

            for (var i = 0; i < pts.Count; i++)
            {
                v.Add(new Vector3(pts[i].x, pts[i].y, 0f));
                v.Add(new Vector3(pts[i].x, bottom, 0f));
                c.Add(Shapes.V(fillTop));
                c.Add(Shapes.V(fillBottom));
            }
            for (var i = 0; i < pts.Count - 1; i++)
            {
                var a = i * 2;
                t.Add(a); t.Add(a + 2); t.Add(a + 1);
                t.Add(a + 1); t.Add(a + 2); t.Add(a + 3);
            }

            var root = Shapes.Create("TerrainFill", parent, Shapes.Build("TerrainFill", v, c, t), -20);

            // ---- слои породы ----
            //
            // Нижняя треть кадра без них — чёрная пустота: снятый кадр показал, что земля
            // читается провалом, а не поверхностью. Слои идут ПАРАЛЛЕЛЬНО рельефу, а не
            // горизонтально: так залегает осадочная порода на склоне, и заодно они
            // подчёркивают форму рельефа вместо того, чтобы спорить с ней.
            //
            // Контраст намеренно на пределе различимости: задача — дать поверхности
            // материал, а не рисунок. Всё, что ярче, начнёт конкурировать с силуэтом героя.
            var sv = new List<Vector3>();
            var sc = new List<Color>();
            var stt = new List<int>();
            var strata = new[] { 0.55f, 1.35f, 2.40f, 3.80f, 5.60f };
            for (var s = 0; s < strata.Length; s++)
            {
                var depth = strata[s];
                var fade = 1f - s / (float)strata.Length;
                var band = new Color(fillTop.r * (1f + 0.55f * fade),
                    fillTop.g * (1f + 0.45f * fade), fillTop.b * (1f + 0.40f * fade), 1f);
                for (var i = 0; i < pts.Count - 1; i++)
                {
                    Shapes.AddBar(sv, sc, stt,
                        pts[i] + new Vector2(0f, -depth),
                        pts[i + 1] + new Vector2(0f, -depth),
                        0.07f + s * 0.02f, band);
                }
            }
            var strataGo = Shapes.Create("TerrainStrata", root.transform,
                Shapes.Build("TerrainStrata", sv, sc, stt), -19);
            strataGo.transform.localPosition = new Vector3(0f, 0f, -0.005f);

            // ---- кромка ----
            var ev = new List<Vector3>();
            var ec = new List<Color>();
            var et = new List<int>();
            for (var i = 0; i < pts.Count - 1; i++)
                Shapes.AddBar(ev, ec, et, pts[i], pts[i + 1], edgeWidthM, edge);

            var edgeGo = Shapes.Create("TerrainEdge", root.transform,
                Shapes.Build("TerrainEdge", ev, ec, et), -19);
            edgeGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);

            return root;
        }

        /// <summary>
        /// Маркеры: старт, чекпоинты, финиш. Нужны, чтобы игрок понимал, где он на трассе —
        /// без этого «интересно» неотличимо от «непонятно».
        /// </summary>
        public static GameObject BuildMarkers(TrackProfile profile, Transform parent, Color color)
        {
            var sampler = new TerrainSampler(profile);
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            for (var i = 0; i < profile.checkpoints.Length; i++)
                AddFlag(v, c, t, sampler, profile.checkpoints[i].xPx * Tuning.UnitsContract.PxToM, color);

            AddFlag(v, c, t, sampler, profile.EndM, new Color(1f, 0.86f, 0.35f, 1f));

            return Shapes.Create("TrackMarkers", parent, Shapes.Build("TrackMarkers", v, c, t), -18);
        }

        private static void AddFlag(List<Vector3> v, List<Color> c, List<int> t,
            TerrainSampler sampler, float xM, Color color)
        {
            var y = sampler.HeightAt(xM);
            var top = y + 3.2f;
            Shapes.AddBar(v, c, t, new Vector2(xM, y), new Vector2(xM, top), 0.10f, color);
            Shapes.AddFan(v, c, t, new[]
            {
                new Vector2(xM, top),
                new Vector2(xM + 1.25f, top - 0.42f),
                new Vector2(xM, top - 0.84f)
            }, color);
        }
    }
}
