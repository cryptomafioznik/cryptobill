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
        // Четыре остановки, а не три: на телефоне градиент из трёх остановок читается
        // двумя плоскими заливками, тёплая половина неба требует своего перехода.
        public static readonly Color SkyTop = new Color(0.07f, 0.09f, 0.20f, 1f);
        public static readonly Color SkyMid = new Color(0.27f, 0.19f, 0.33f, 1f);
        public static readonly Color SkyWarm = new Color(0.62f, 0.35f, 0.35f, 1f);
        public static readonly Color SkyHorizon = new Color(0.98f, 0.62f, 0.31f, 1f);
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

            // Градиент по семи остановкам. Меньше — виден стык полос (полоса Маха):
            // на стыке скорость изменения цвета меняется скачком, и глаз рисует там
            // горизонтальную линию, которой нет в данных, — снято кадром.
            var ys = new[] { -1.00f, -0.55f, -0.10f, 0.10f, 0.30f, 0.62f, 1.00f };
            var cs = new[]
            {
                SkyHorizon,
                new Color(0.82f, 0.49f, 0.33f, 1f),
                SkyWarm,
                new Color(0.44f, 0.27f, 0.34f, 1f),
                SkyMid,
                new Color(0.16f, 0.13f, 0.26f, 1f),
                SkyTop
            };
            for (var i = 0; i < ys.Length - 1; i++)
                AddGradientBand(v, c, t, -w, w, h * ys[i], h * ys[i + 1], cs[i], cs[i + 1]);

            var go = Shapes.Create("SkyQuad", transform, Shapes.Build("SkyQuad", v, c, t), -100);
            go.transform.SetParent(_cam, false);
            go.transform.localPosition = new Vector3(0f, 0f, 60f);

            // Солнце у горизонта — источник, из которого следует вся остальная светотень.
            // Оно СЗАДИ игрока (слева по ходу), поэтому герой оказывается против света.
            // Высота выставлена ПО КАДРУ: при −0.16h диск целиком прятался за городом
            // и рельефом, и в кадре не было самого источника — остальная светотень
            // становилась беспричинной. Теперь диск висит у верхней кромки города,
            // частично перекрывается башнями — это и есть закат в городе.
            var sunPos = new Vector2(-halfW * 0.55f, halfH * 0.30f);
            var sunR = halfH * 0.14f;

            // Ореол: радиальное затухание вершинным цветом (центр тёплый, край прозрачный).
            // Это воздух вокруг источника; сам диск ниже светится через Emissive + bloom.
            var gv = new List<Vector3>();
            var gc = new List<Color>();
            var gt = new List<int>();
            AddGlowDisc(gv, gc, gt, sunPos, sunR * 5.2f,
                new Color(SkyHorizon.r, SkyHorizon.g, SkyHorizon.b, 0.55f), 36);
            AddGlowDisc(gv, gc, gt, sunPos, sunR * 2.3f,
                new Color(SunCore.r, SunCore.g, SunCore.b, 0.50f), 28);
            var glow = Shapes.Create("SunGlow", transform, Shapes.Build("SunGlow", gv, gc, gt), -99);
            glow.transform.SetParent(_cam, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 59.5f);

            var sv = new List<Vector3>();
            var sc = new List<Color>();
            var st = new List<int>();
            Shapes.AddDisc(sv, sc, st, sunPos, sunR, SunCore, 32);
            // Диск — источник для bloom: ярче диапазона обычных цветов, пост разольёт
            // его свет по соседним пикселям, как разливается низкое солнце в оптике.
            var sun = Shapes.Create("Sun", transform, Shapes.Build("Sun", sv, sc, st), -98,
                Shapes.Emissive(2.6f));
            sun.transform.SetParent(_cam, false);
            sun.transform.localPosition = new Vector3(0f, 0f, 59f);

        }

        /// <summary>Диск с радиальным затуханием: центр — цвет, кромка — прозрачный ноль.</summary>
        private static void AddGlowDisc(List<Vector3> v, List<Color> c, List<int> t,
            Vector2 center, float radius, Color core, int segments)
        {
            var i0 = v.Count;
            v.Add(new Vector3(center.x, center.y, 0f));
            c.Add(Shapes.V(core));
            var rim = new Color(core.r, core.g, core.b, 0f);
            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                v.Add(new Vector3(center.x + Mathf.Cos(a) * radius,
                    center.y + Mathf.Sin(a) * radius, 0f));
                c.Add(Shapes.V(rim));
            }
            for (var i = 0; i < segments; i++)
            {
                t.Add(i0);
                t.Add(i0 + 1 + i);
                t.Add(i0 + 1 + (i + 1) % segments);
            }
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
            // ГОРОД ИЗ СВЕЧЕЙ вместо абстрактных гряд. Это главный визуальный пробел
            // против старой игры: там за спиной стоял город, собранный из свечей, и он
            // делал мир этой игрой, а не обобщённым trials. Формы те же, что под колёсами,
            // но дальше, мельче и в дымке — так работает воздушная перспектива, и заодно
            // это связывает фон с механикой: весь мир сделан из рынка.
            //
            // Дальний план НЕ несёт данных — это силуэт, а не котировки. Поэтому цвет
            // у него приглушён до оттенка неба: правило «светятся только данные» остаётся
            // за свечами под колёсами, которыми игрок реально едет.
            // Высоты подрезаны по снятому кадру: первая редакция (3.4 и 5.2 м) заполняла
            // башнями весь верх экрана и не оставляла неба, а именно небо у горизонта —
            // самое светлое место кадра, на котором и читается контражурный силуэт героя.
            // Город обязан подпирать композицию, а не занимать её.
            // Цвета и плотность выставлены по кадру: первая редакция дала сплошной
            // бежевый частокол без неба в просветах — город обязан быть СИЛУЭТОМ на
            // тёплом небе, тёмным и редким, а не светлой стеной вплотную к герою.
            AddCandleCity("CityFar", 0.90f, 1.0f, 1.9f, 0.50f,
                new Color(0.34f, 0.22f, 0.28f, 1f), -62, trackLengthM, 7919);
            AddCandleCity("CityMid", 0.78f, -0.9f, 2.8f, 0.72f,
                new Color(0.20f, 0.13f, 0.20f, 1f), -52, trackLengthM, 104729);

            // ВЫСОТА И РАЗМЕР ИСПРАВЛЕНЫ ПО СНЯТОМУ КАДРУ. В первой редакции дальняя гряда
            // стояла ВЫШЕ ближних (base 9.5 против 3.0) и была самой большой формой в кадре —
            // то есть перспектива работала наоборот, и самым светлым и крупным пятном
            // оказывался фон, а земля читалась чёрным провалом.
            //
            // Как правильно: далёкое собирается У ГОРИЗОНТА и мелкое, близкое — НИЖЕ горизонта
            // и крупнее. Поэтому смещения идут от + к −, амплитуды растут к зрителю, а вся
            // группа держится узкой полосой у линии глаз: гряды обязаны подпирать силуэт
            // рельефа, а не спорить с ним за кадр.
            // Ближняя гряда осталась грядой: город на трёх планах превращается в частокол,
            // а земле нужен сплошной тёмный подпор под силуэтом героя.
            AddRidge("RidgeNear", 0.58f, -1.8f, 3.1f, 0.115f, 5.3f,
                new Color(0.17f, 0.13f, 0.20f, 1f), -42, trackLengthM);

            // Дымка ПЕРЕД каждым планом: воздушная перспектива не только осветляет цвет
            // слоя (это уже делают его вершинные цвета), но и кладёт слой тёплого воздуха
            // МЕЖДУ планами. Чем дальше план, тем больше воздуха перед ним — поэтому
            // перед дальним городом дымка плотнее, перед ближней грядой едва заметна.
            AddHaze("HazeFar", -58, 0.24f);
            AddHaze("HazeMid", -48, 0.13f);
            AddHaze("HazeNear", -38, 0.06f);
        }

        /// <summary>
        /// Слой тёплого воздуха на весь кадр: плотный у горизонта, растворяется кверху.
        /// Ниже линии рельефа его закрывает непрозрачная земля, поэтому низ не важен.
        /// </summary>
        private void AddHaze(string name, int order, float alpha)
        {
            var cam = _cam.GetComponent<Camera>();
            var halfH = cam.orthographicSize;
            var halfW = halfH * Mathf.Max(cam.aspect, 1f);
            var w = halfW * 3f;

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var warm = SkyHorizon;
            var lo = new Color(warm.r, warm.g, warm.b, alpha);
            var mid = new Color(warm.r, warm.g, warm.b, alpha * 0.45f);
            var none = new Color(warm.r, warm.g, warm.b, 0f);

            AddGradientBand(v, c, t, -w, w, -halfH * 1.4f, -halfH * 0.10f, lo, mid);
            AddGradientBand(v, c, t, -w, w, -halfH * 0.10f, halfH * 0.55f, mid, none);

            var go = Shapes.Create(name, transform, Shapes.Build(name, v, c, t), order);
            go.transform.SetParent(_cam, false);
            go.transform.localPosition = new Vector3(0f, 0f, 45f + order * -0.01f);
        }

        /// <summary>
        /// Ряд башен-свечей с фитилями. Высоты детерминированы от семени слоя: один и тот
        /// же кадр в каждом прогоне, иначе по скриншотам нельзя сравнивать изменения.
        /// </summary>
        private void AddCandleCity(string name, float parallax, float baseY, float maxH,
            float width, Color color, int order, float trackLengthM, int seed)
        {
            var span = trackLengthM * (1f - parallax) + 140f;
            // Шаг 2.6 ширины и случайные пропуски: городу нужны ПРОСВЕТЫ неба между
            // башнями — сплошной ряд на шаге 1.75 читался стеной, а не горизонтом.
            var pitch = width * 2.6f;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var bottom = -80f;
            var st = (uint)seed;

            float Rand()
            {
                st ^= st << 13; st ^= st >> 17; st ^= st << 5;
                return (st & 0xFFFFFF) / 16777216f;
            }

            for (var x = -70f; x <= span; x += pitch)
            {
                // Высота башни: смесь низкочастотной волны (кварталы) и случая (дома).
                // Одна случайность дала бы шум, одна волна — гребёнку.
                var wave = 0.5f + 0.5f * Mathf.Sin(x * 0.055f + seed * 0.001f);
                var h = maxH * (0.22f + 0.78f * (0.55f * wave + 0.45f * Rand()));
                var w = width * (0.7f + 0.6f * Rand());
                if (Rand() > 0.80f) continue; // квартал с пустырём — просвет до неба
                var top = baseY + h;

                // Тело башни: к подножию темнеет, как и свечи под колёсами.
                var dark = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.55f, 1f);
                var i0 = v.Count;
                v.Add(new Vector3(x, top, 0f)); c.Add(Shapes.V(color));
                v.Add(new Vector3(x + w, top, 0f)); c.Add(Shapes.V(color));
                v.Add(new Vector3(x + w, bottom, 0f)); c.Add(Shapes.V(dark));
                v.Add(new Vector3(x, bottom, 0f)); c.Add(Shapes.V(dark));
                t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);

                // Фитиль: короткая антенна над телом. Без неё башни читаются домами,
                // а нужны СВЕЧИ — форма должна повторять ту, что под колёсами.
                if (Rand() > 0.35f)
                {
                    Shapes.AddBar(v, c, t,
                        new Vector2(x + w * 0.5f, top),
                        new Vector2(x + w * 0.5f, top + h * (0.12f + 0.22f * Rand())),
                        w * 0.16f, color);
                }
            }

            var go = Shapes.Create(name, transform, Shapes.Build(name, v, c, t), order);
            go.transform.position = new Vector3(0f, baseY, 0f);
            _layers.Add(new Layer { T = go.transform, Parallax = parallax, BaseY = baseY });
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
                // По вертикали гряды следуют за камерой ПОЧТИ полностью: иначе на подъёме
                // 40° камера уезжает вверх на десятки метров и весь дальний план выпадает
                // из кадра. Остаток (1 − 0.92) даёт лёгкий вертикальный параллакс.
                l.T.position = new Vector3(camX * l.Parallax, l.BaseY + camY * 0.92f, 0f);
            }
        }
    }
}
