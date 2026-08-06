using System.Collections.Generic;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Небо и дальние планы — источник СВЕТЛОТЫ, на фоне которой читается герой.
    ///
    /// Зачем это здесь, а не «для красоты». Первый снятый кадр показал ровно одну проблему:
    /// байк, райдер и рельеф оказались одной тёмной массой на тёмном фоне. Силуэт не читается
    /// не потому, что он плохо построен, а потому что ему не на чем читаться — фон был
    /// однородно чёрным. Контраст нельзя добавить к герою, его можно только создать
    /// РАЗНИЦЕЙ между героем и фоном.
    ///
    /// Отсюда решение сцены: низкое солнце ПОЗАДИ игрока. Тогда горизонт — самое светлое
    /// место кадра, а всё, что ближе (гряды, рельеф, байк), тем темнее, чем ближе. Герой
    /// становится контражурным силуэтом, то есть максимально контрастным объектом, и это
    /// получается из логики света, а не из подкрутки цветов.
    ///
    /// Правило проекта «светятся только данные» не нарушено: небо не светится, оно окрашено.
    /// Источников неонового света в кадре нет.
    /// </summary>
    public class SkyView : MonoBehaviour
    {
        /// <summary>Слой дальнего плана: чем больше Parallax, тем дальше и медленнее.</summary>
        private struct Layer
        {
            public Transform T;
            public float Parallax;
            public float BaseY;
        }

        private readonly List<Layer> _layers = new List<Layer>();
        private Transform _cam;

        // ---- палитра golden hour ----
        // Небо: от глубокого сине-фиолетового вверху к тёплому у горизонта. Значения
        // подобраны так, чтобы САМОЕ СВЕТЛОЕ место кадра было у линии горизонта.
        public static readonly Color SkyTop = new Color(0.09f, 0.11f, 0.22f, 1f);
        public static readonly Color SkyMid = new Color(0.33f, 0.24f, 0.34f, 1f);
        public static readonly Color SkyHorizon = new Color(0.96f, 0.62f, 0.34f, 1f);
        public static readonly Color SunCore = new Color(1f, 0.86f, 0.58f, 1f);

        public static SkyView Attach(Camera cam, float trackLengthM)
        {
            var go = new GameObject("Sky");
            var sky = go.AddComponent<SkyView>();
            sky._cam = cam.transform;
            sky.BuildSky(cam);
            sky.BuildRidges(trackLengthM);
            return sky;
        }

        // ================= небо =================

        private void BuildSky(Camera cam)
        {
            // Квад приколочен к камере: он обязан закрывать кадр всегда, а не «обычно».
            var halfH = cam.orthographicSize;
            var halfW = halfH * Mathf.Max(cam.aspect, 1f);
            // Запас: орто-размер зависит от разрешения устройства, и кадр не должен
            // обнажить край неба на нестандартном аспекте.
            var w = halfW * 3f;
            var h = halfH * 2.4f;

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            // Градиент по трём остановкам: верх, середина, горизонт. Две остановки дают
            // плоскую заливку, четыре на этом размере уже неразличимы.
            AddGradientBand(v, c, t, -w, w, 0f, h, SkyMid, SkyTop);
            AddGradientBand(v, c, t, -w, w, -h, 0f, SkyHorizon, SkyMid);

            var go = Shapes.Create("SkyQuad", transform, Shapes.Build("SkyQuad", v, c, t), -100);
            go.transform.SetParent(_cam, false);
            go.transform.localPosition = new Vector3(0f, 0f, 60f);

            // Солнце у горизонта — источник, из которого следует вся остальная светотень.
            // Оно СЗАДИ игрока (слева по ходу), поэтому герой оказывается против света.
            var sv = new List<Vector3>();
            var sc = new List<Color>();
            var st = new List<int>();
            Shapes.AddDisc(sv, sc, st, new Vector2(-halfW * 0.55f, -h * 0.16f), halfH * 0.14f,
                SunCore, 24);
            var sun = Shapes.Create("Sun", transform, Shapes.Build("Sun", sv, sc, st), -99);
            sun.transform.SetParent(_cam, false);
            sun.transform.localPosition = new Vector3(0f, 0f, 59f);
        }

        private static void AddGradientBand(List<Vector3> v, List<Color> c, List<int> t,
            float x0, float x1, float y0, float y1, Color bottom, Color top)
        {
            var i0 = v.Count;
            v.Add(new Vector3(x0, y0, 0f)); c.Add(Shapes.V(bottom));
            v.Add(new Vector3(x1, y0, 0f)); c.Add(Shapes.V(bottom));
            v.Add(new Vector3(x1, y1, 0f)); c.Add(Shapes.V(top));
            v.Add(new Vector3(x0, y1, 0f)); c.Add(Shapes.V(top));
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
            t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
        }

        // ================= дальние гряды =================

        /// <summary>
        /// Три гряды с воздушной перспективой: дальше — светлее, ниже контраст, медленнее ход.
        /// Это единственный способ дать глубину без второго измерения, и он причинный:
        /// так работает рассеяние в воздухе, а не «эффект».
        /// </summary>
        private void BuildRidges(float trackLengthM)
        {
            AddRidge("RidgeFar", 0.86f, 9.5f, 3.2f, 0.055f, 1.7f,
                new Color(0.72f, 0.51f, 0.44f, 1f), -60, trackLengthM);
            AddRidge("RidgeMid", 0.70f, 6.5f, 4.6f, 0.085f, 3.1f,
                new Color(0.44f, 0.31f, 0.35f, 1f), -50, trackLengthM);
            AddRidge("RidgeNear", 0.48f, 3.0f, 5.8f, 0.130f, 5.3f,
                new Color(0.22f, 0.18f, 0.26f, 1f), -40, trackLengthM);
        }

        private void AddRidge(string name, float parallax, float baseY, float amp,
            float freq, float phase, Color color, int order, float trackLengthM)
        {
            // Слой движется медленнее камеры, поэтому ему нужна длина только на ту долю
            // пути, которую он реально проходит, плюс запас на ширину кадра.
            var span = trackLengthM * (1f - parallax) + 140f;
            var step = 1.6f;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            var bottom = -80f;
            var prevIdx = -1;
            for (var x = -70f; x <= span; x += step)
            {
                var y = Ridge(x, freq, phase) * amp;
                var i0 = v.Count;
                v.Add(new Vector3(x, y, 0f));
                v.Add(new Vector3(x, bottom, 0f));
                // Гряда светлее у кромки и темнее к подножию: свет приходит сверху-сзади.
                c.Add(Shapes.V(color));
                c.Add(Shapes.V(new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.62f, 1f)));
                if (prevIdx >= 0)
                {
                    t.Add(prevIdx); t.Add(i0); t.Add(prevIdx + 1);
                    t.Add(prevIdx + 1); t.Add(i0); t.Add(i0 + 1);
                }
                prevIdx = i0;
            }

            var go = Shapes.Create(name, transform, Shapes.Build(name, v, c, t), order);
            go.transform.position = new Vector3(0f, baseY, 0f);
            _layers.Add(new Layer { T = go.transform, Parallax = parallax, BaseY = baseY });
        }

        /// <summary>
        /// Профиль гряды: сумма синусов с несоизмеримыми частотами. Детерминирована —
        /// одна и та же трасса выглядит одинаково в каждом прогоне, поэтому по скриншотам
        /// можно сравнивать изменения, а не шум.
        /// </summary>
        private static float Ridge(float x, float freq, float phase)
        {
            return Mathf.Sin(x * freq + phase)
                   + 0.52f * Mathf.Sin(x * freq * 2.31f + phase * 1.7f)
                   + 0.27f * Mathf.Sin(x * freq * 4.77f + phase * 0.6f);
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            var camX = _cam.position.x;
            var camY = _cam.position.y;
            for (var i = 0; i < _layers.Count; i++)
            {
                var l = _layers[i];
                // Слой смещается на долю хода камеры: чем дальше, тем меньше собственный ход.
                l.T.position = new Vector3(camX * l.Parallax,
                    l.BaseY + camY * l.Parallax * 0.55f, 0f);
            }
        }
    }
}
