using System.Collections.Generic;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// МИР — порт композиции drawCityWorld() из toys/chartrider.html, слой в слой.
    ///
    /// Почему порт, а не сочинение: вердикт живого теста — «вся стилистика не та», при
    /// том что картинку браузерной версии пользователь принимал годами. Значит референс
    /// уже существует и записан кодом; выдумывать взамен него — та же ошибка, что
    /// с выброшенной схемой управления btn4.
    ///
    /// Порядок слоёв исходника (стр. 2063+): солнце → золотая заливка → god-rays →
    /// глоу горизонта → дальний скайлайн → город (даль) → пальмы → город (близь) →
    /// отсвет набережной → вуаль-дымка → вода с отражением. Всё привязано к экрану
    /// по вертикали (горизонт всегда на 60 % высоты кадра), по горизонтали — параллакс.
    ///
    /// Координаты исходника — пиксели кадра 430×932; здесь один переводной множитель.
    /// </summary>
    public class WorldView : MonoBehaviour
    {
        private struct Layer
        {
            public Transform T;
            public float Parallax;
        }

        private readonly List<Layer> _layers = new List<Layer>();
        private Transform _cam;
        private Camera _camera;

        private float _halfH, _halfW;
        /// <summary>Пиксель исходника → метры кадра (одно число на обе оси: аспект тот же).</summary>
        private float _px;
        /// <summary>Горизонт: 60 % высоты кадра, в координатах камеры (центр = 0).</summary>
        private float _hz;
        private Vector2 _sunPos;
        private float _sunR;

        private WorldPalette.Era _era;

        // Анимируемые мелочи: лучи и маячки. Дёшево — меши в десятки вершин.
        private Mesh _raysMesh;
        private MeshFilter _raysFilter;
        private Mesh _beaconMesh;
        private MeshFilter _beaconFilter;
        private readonly List<Vector2> _beacons = new List<Vector2>();
        private float _time;

        public static WorldView Attach(Camera cam, float trackLengthM, int eraIndex = 0)
        {
            var go = new GameObject("World-Vice");
            var w = go.AddComponent<WorldView>();
            w._cam = cam.transform;
            w._camera = cam;
            w._halfH = cam.orthographicSize;
            w._halfW = w._halfH * cam.aspect;
            w._px = w._halfH * 2f / 932f;
            w._hz = -0.2f * w._halfH; // 60 % высоты кадра от верха
            w._era = WorldPalette.Eras[((eraIndex % WorldPalette.Eras.Length)
                                        + WorldPalette.Eras.Length) % WorldPalette.Eras.Length];
            w.BuildAll(trackLengthM);
            return w;
        }

        private void BuildAll(float trackLengthM)
        {
            BuildSky();
            BuildStars();
            BuildSun();
            BuildRays();
            BuildFarSkyline(trackLengthM);
            BuildCity(trackLengthM, 0.13f, false, -86);
            BuildPalms(trackLengthM, 0.18f, -82);
            BuildCity(trackLengthM, 0.26f, true, -80);
            BuildWater(trackLengthM);
            BuildVeil();
        }

        // ================= небо =================

        private void BuildSky()
        {
            var w = _halfW * 3f;
            var top = _halfH * 1.25f;
            var bottom = -_halfH * 1.25f;
            // Остановки исходника: 0 / 0.45 / 0.72 / 1.0 высоты кадра, сверху вниз.
            var y1 = _halfH * (1f - 2f * 0.45f);
            var y2 = _halfH * (1f - 2f * 0.72f);

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            // Полосы дробятся, интерполяция — в sRGB: Canvas исходника интерполирует
            // градиент в sRGB, а вершинный цвет в линейном проекте — в линейном
            // пространстве, и середины полос выходили темнее и грязнее референса.
            BandSrgb(v, c, t, -w, w, y1, top, _era.Sky1, _era.Sky0, 4);
            BandSrgb(v, c, t, -w, w, y2, y1, _era.Sky2, _era.Sky1, 4);
            BandSrgb(v, c, t, -w, w, bottom, y2, _era.Sky3, _era.Sky2, 4);

            Pin(Shapes.Create("Sky", transform, Shapes.Build("Sky", v, c, t), -100), 60f);
        }

        private void BuildStars()
        {
            // Звёзды в верхней трети: у исходника мир живёт в сумерках, и точки в небе —
            // часть его тишины. Детерминированы, чтобы кадры сравнивались между сборками.
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var st = 48271u;
            float Rand()
            {
                st ^= st << 13; st ^= st >> 17; st ^= st << 5;
                return (st & 0xFFFFFF) / 16777216f;
            }

            for (var i = 0; i < 45; i++)
            {
                var x = (Rand() * 2f - 1f) * _halfW * 1.1f;
                var y = Mathf.Lerp(_halfH * 0.45f, _halfH * 1.05f, Rand());
                var a = 0.20f + 0.45f * Rand();
                var r = _px * (0.7f + Rand() * 0.8f);
                Shapes.AddDisc(v, c, t, new Vector2(x, y),
                    r, new Color(0.92f, 0.95f, 1f, a), 6);
            }
            Pin(Shapes.Create("Stars", transform, Shapes.Build("Stars", v, c, t), -99), 59.8f);
        }

        // ================= солнце =================

        private void BuildSun()
        {
            // Фаза «закат» исходника (b212): KX=0.64, KH=0.62, KR=118.
            _sunR = 118f * _px;
            _sunPos = new Vector2((0.64f - 0.5f) * 2f * _halfW, _hz + _sunR * 0.62f);

            // Широкий тёплый ореол (radial 2.2r, α .18) + золотая заливка от солнца
            // (b221: radial на всю сцену, α .38→.16→0) — аддитивно, как 'lighter'.
            var gv = new List<Vector3>();
            var gc = new List<Color>();
            var gt = new List<int>();
            GlowDisc(gv, gc, gt, _sunPos, _sunR * 2.2f, new Color(1f, 0.75f, 0.5f, 0.18f), 36);
            // Золотая заливка от солнца — умеренная: первая редакция (α .30, радиус 2.3
            // полувысоты) коричневила всё небо, убивая розово-магентовую идентичность.
            GlowDisc(gv, gc, gt, new Vector2(_sunPos.x, _hz), _halfH * 1.5f,
                new Color(1f, 0.72f, 0.44f, 0.15f), 40);
            var glow = Shapes.Create("SunGlow", transform, Shapes.Build("SunGlow", gv, gc, gt),
                -98, Shapes.Additive);
            Pin(glow, 59.5f);

            // Диск горизонтальными полосами С ПРОПУСКАМИ — синтвейв-срезы нижней половины,
            // жанровая подпись исходника (sunSprite, destination-out полосы).
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var core = new Color(1f, 0.925f, 0.73f, 0.95f);
            var mid = new Color(_era.Sky2.r, _era.Sky2.g, _era.Sky2.b, 0.85f);

            var cuts = new List<Vector2>(); // (y от центра, полутолщина), вниз — отрицательное
            for (var k = 0; k < 6; k++)
            {
                var yy = _sunR * 0.10f + k * _sunR * 0.135f;
                if (yy > _sunR) break;
                cuts.Add(new Vector2(-yy, (1.6f + k * 1.25f) * _px * 0.5f));
            }

            var rows = 36;
            for (var i = 0; i < rows; i++)
            {
                var y0 = -_sunR + 2f * _sunR * (i / (float)rows);
                var y1 = -_sunR + 2f * _sunR * ((i + 1) / (float)rows);
                var mid0 = (y0 + y1) * 0.5f;
                var inCut = false;
                foreach (var cut in cuts)
                    if (mid0 > cut.x - cut.y && mid0 < cut.x + cut.y) { inCut = true; break; }
                if (inCut) continue;

                var hw0 = Mathf.Sqrt(Mathf.Max(0f, _sunR * _sunR - y0 * y0));
                var hw1 = Mathf.Sqrt(Mathf.Max(0f, _sunR * _sunR - y1 * y1));
                // Радиальный градиент, приближённый по вертикали: центр тёплый, край — тон неба.
                var k0 = Mathf.Abs(mid0) / _sunR;
                var col = Color.Lerp(core, mid, Mathf.SmoothStep(0.25f, 1f, k0));
                var i0 = v.Count;
                v.Add(new Vector3(_sunPos.x - hw0, _sunPos.y + y0, 0f)); c.Add(Shapes.V(mid));
                v.Add(new Vector3(_sunPos.x + hw0, _sunPos.y + y0, 0f)); c.Add(Shapes.V(mid));
                v.Add(new Vector3(_sunPos.x + hw1, _sunPos.y + y1, 0f)); c.Add(Shapes.V(col));
                v.Add(new Vector3(_sunPos.x - hw1, _sunPos.y + y1, 0f)); c.Add(Shapes.V(col));
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
                t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
            }
            var sun = Shapes.Create("Sun", transform, Shapes.Build("Sun", v, c, t), -97,
                Shapes.Emissive(1.9f));
            Pin(sun, 59f);

            // Глоу горизонта (BLINE α .15, полоса ±70..50 px вокруг hz) — «свет рынка».
            var hv = new List<Vector3>();
            var hc = new List<Color>();
            var ht = new List<int>();
            var line = _era.Line;
            var w = _halfW * 3f;
            Band(hv, hc, ht, -w, w, _hz, _hz + 70f * _px,
                new Color(line.r, line.g, line.b, 0.15f), new Color(line.r, line.g, line.b, 0f));
            Band(hv, hc, ht, -w, w, _hz - 50f * _px, _hz,
                new Color(line.r, line.g, line.b, 0f), new Color(line.r, line.g, line.b, 0.15f));
            var hg = Shapes.Create("HorizonGlow", transform, Shapes.Build("HorizonGlow", hv, hc, ht),
                -95, Shapes.Additive);
            Pin(hg, 58.5f);
        }

        private void BuildRays()
        {
            // 12 god-rays из позиции диска, живут в LateUpdate (ширина и альфа дышат).
            var go = new GameObject("GodRays");
            go.transform.SetParent(transform, false);
            _raysMesh = new Mesh { name = "GodRays" };
            _raysFilter = go.AddComponent<MeshFilter>();
            _raysFilter.sharedMesh = _raysMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Shapes.Additive;
            mr.sortingOrder = -96;
            Pin(go, 58.8f);
        }

        // ================= дальний скайлайн =================

        private void BuildFarSkyline(float trackLengthM)
        {
            const float par = 0.06f;
            var span = trackLengthM * (1f - par) + _halfW * 2f + 30f;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var col = new Color(_era.Bridge.r, _era.Bridge.g, _era.Bridge.b, 0.5f);
            var bottom = _hz - _halfH * 1.1f;

            var prev = -1;
            for (var x = -_halfW * 1.5f; x <= span; x += 10f * _px)
            {
                var wx = x * 0.006f / _px * 0.0187f / 0.0187f; // частота исходника в его же px
                var y = _hz - 30f * _px + Mathf.Sin(wx * 1.3f / _px * 0.01866f) * 22f * _px
                        + Mathf.Sin(wx * 0.5f / _px * 0.01866f) * 30f * _px;
                var i0 = v.Count;
                v.Add(new Vector3(x, y, 0f)); c.Add(Shapes.V(col));
                v.Add(new Vector3(x, bottom, 0f)); c.Add(Shapes.V(col));
                if (prev >= 0)
                {
                    t.Add(prev); t.Add(i0); t.Add(prev + 1);
                    t.Add(prev + 1); t.Add(i0); t.Add(i0 + 1);
                }
                prev = i0;
            }
            var go = Shapes.Create("FarSkyline", transform, Shapes.Build("FarSkyline", v, c, t), -92);
            AddParallax(go, par, 57f);
        }

        // ================= город =================

        /// <summary>
        /// Порт cityPass: ритм застройки (просветы, кластеры, доминанты со шпилями),
        /// тональная лепка свет→тень поперёк корпуса, тёплая кромка со стороны солнца,
        /// неон-кромка крыши, редкие окна, антенны. Маячки антенн мигают — их позиции
        /// копятся в _beacons и рисуются динамическим мешем.
        /// </summary>
        private void BuildCity(float trackLengthM, float par, bool near, int order)
        {
            var span = trackLengthM * (1f - par) + _halfW * 2f + 30f;
            var pitch = (near ? 52f : 34f) * _px;
            // Ось Y исходника — ВНИЗ: baseY = hz+8 значит 8 px НИЖЕ горизонта.
            var baseY = _hz - (near ? 8f : -4f) * _px;

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var sv = new List<Vector3>(); // отсвет набережной (аддитив, только близь)
            var sc = new List<Color>();
            var stt = new List<int>();

            var lit = near ? WorldPalette.CityLit : (Color)WorldPalette.CityFarLit;
            var mid = near ? WorldPalette.CityMid : (Color)WorldPalette.CityFarMid;
            var shade = near ? WorldPalette.CityShade : (Color)WorldPalette.CityFarShade;
            var winA = near ? 0.30f : 0.17f;

            var idx0 = Mathf.FloorToInt(-_halfW * 1.5f / pitch);
            var idx1 = Mathf.CeilToInt(span / pitch);
            for (var i = idx0; i <= idx1; i++)
            {
                var sx = i * pitch;
                var r = Hash(i * 12.9898f + (near ? 1.7f : 4.3f));
                var r2 = Hash(i * 45.77f);
                var rg = Hash(i * 7.13f);
                if (rg < 0.20f) continue; // просвет в застройке

                var clu = 0.78f + 0.5f * (0.5f + 0.5f * Mathf.Sin(i * 0.31f + (near ? 0f : 2.2f)));
                var land = r > 0.94f;
                var w = pitch * (0.5f + 0.32f * Frac(r * 3.7f)) * (land ? 0.74f : 1f);
                var x = sx + (pitch - w) * 0.5f;
                var th = ((near ? 58f + r * 160f : 36f + r * 112f) * clu * (land ? 1.6f : 1f)) * _px;
                var top = baseY + th;
                var glow = r2 > 0.5f ? WorldPalette.RoofTeal : WorldPalette.RoofPink;

                // Корпус: свет со стороны солнца → тень (b222). Здание СТОИТ на baseY —
                // тела до дна кадра давали стену брёвен, закрывавшую воду (снято кадром).
                var sunRight = _sunPos.x > x + w * 0.5f - 0f; // солнце экранное; здания едут — упрощение: свет справа
                var cl = sunRight ? shade : lit;
                var cr = sunRight ? lit : shade;
                QuadH(v, c, t, x, x + w, baseY, top, cl, mid, cr);

                // Юбка-примыкание: мягкое растворение под основанием.
                var skirt = new Color(WorldPalette.Silhouette.r, WorldPalette.Silhouette.g,
                    WorldPalette.Silhouette.b, 0.75f);
                Band(v, c, t, x, x + w, baseY - 13f * _px, baseY,
                    new Color(skirt.r, skirt.g, skirt.b, 0f), skirt);

                // Тёплая кромка со стороны солнца.
                var ex = sunRight ? x + w - 2.2f * _px : x;
                QuadFlat(v, c, t, ex, ex + 2.2f * _px, baseY, top,
                    new Color(WorldPalette.SunEdge.r, WorldPalette.SunEdge.g, WorldPalette.SunEdge.b,
                        near ? 0.55f : 0.30f));

                // Неон-кромка крыши — «светятся только данные» город не нарушает:
                // это рыночные цвета города-биржи, приглушённые.
                QuadFlat(v, c, t, x, x + w, top, top + 1.6f * _px,
                    new Color(glow.r, glow.g, glow.b, near ? 0.5f : 0.3f));

                // Шпиль доминанты.
                if (land)
                {
                    var cx = x + w * 0.5f;
                    var i0 = v.Count;
                    v.Add(new Vector3(cx - 4f * _px, top, 0f)); c.Add(Shapes.V(mid));
                    v.Add(new Vector3(cx, top + 16f * _px, 0f)); c.Add(Shapes.V(mid));
                    v.Add(new Vector3(cx + 4f * _px, top, 0f)); c.Add(Shapes.V(mid));
                    t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
                }

                // Окна: редкая сетка, тёплые и рыночные.
                for (var wy = top - 6f * _px; wy > baseY + 4f * _px; wy -= (near ? 10f : 8f) * _px)
                {
                    for (var wx = x + 3f * _px; wx < x + w - 3f * _px; wx += (near ? 7f : 5.6f) * _px)
                    {
                        var rw = Hash(i * 3.1f + wy / _px * 0.7f + wx / _px * 0.31f);
                        if (rw <= 0.74f) continue;
                        var wc = rw > 0.915f
                            ? new Color(WorldPalette.WindowWarm.r, WorldPalette.WindowWarm.g,
                                WorldPalette.WindowWarm.b, winA)
                            : new Color(glow.r, glow.g, glow.b, winA * 0.85f);
                        QuadFlat(v, c, t, wx, wx + (near ? 2.4f : 1.8f) * _px,
                            wy, wy + (near ? 3f : 2.2f) * _px, wc);
                    }
                }

                // Антенна + маячок (мигает динамическим мешем).
                if (r > 0.64f || land)
                {
                    var ax = x + w * 0.5f;
                    var ah = ((land ? 16f : 8f) + r * 15f) * _px;
                    var atop = land ? top + 16f * _px : top;
                    QuadFlat(v, c, t, ax - 0.6f * _px, ax + 0.6f * _px, atop, atop + ah,
                        new Color(120 / 255f, 130 / 255f, 175 / 255f, near ? 0.5f : 0.32f));
                    if (near) _beacons.Add(new Vector2(ax, atop + ah + 1f * _px));
                }

                // Отсвет набережной (только близь): вертикальный смаз цвета здания под hz.
                if (near)
                {
                    var hh = (30f + r2 * 28f) * _px;
                    var scol = new Color(glow.r, glow.g, glow.b, 0.09f);
                    var szero = new Color(glow.r, glow.g, glow.b, 0f);
                    Band(sv, sc, stt, sx, sx + pitch * 0.6f, baseY - hh, baseY, szero, scol);
                }
            }

            var go = Shapes.Create(near ? "CityNear" : "CityFar", transform,
                Shapes.Build("City", v, c, t), order);
            AddParallax(go, par, near ? 54f : 56f);

            if (near)
            {
                var sheen = Shapes.Create("Sheen", go.transform,
                    Shapes.Build("Sheen", sv, sc, stt), -79, Shapes.Additive);
                sheen.transform.localPosition = new Vector3(0f, 0f, -0.1f);

                // Отражение города в воде: тот же ритм, сплюснутый силуэт, тише вдвое.
                BuildCityReflection(go.transform, trackLengthM, par, pitch, baseY);

                // Маячки — динамический меш.
                var bgo = new GameObject("Beacons");
                bgo.transform.SetParent(go.transform, false);
                _beaconMesh = new Mesh { name = "Beacons" };
                _beaconFilter = bgo.AddComponent<MeshFilter>();
                _beaconFilter.sharedMesh = _beaconMesh;
                var bmr = bgo.AddComponent<MeshRenderer>();
                bmr.sharedMaterial = Shapes.Emissive(1.5f);
                bmr.sortingOrder = order + 1;
            }
        }

        private void BuildCityReflection(Transform parent, float trackLengthM, float par,
            float pitch, float baseY)
        {
            var span = trackLengthM * (1f - par) + _halfW * 2f + 30f;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var col = new Color(WorldPalette.Silhouette.r, WorldPalette.Silhouette.g,
                WorldPalette.Silhouette.b, 0.20f);

            var idx0 = Mathf.FloorToInt(-_halfW * 1.5f / pitch);
            var idx1 = Mathf.CeilToInt(span / pitch);
            for (var i = idx0; i <= idx1; i++)
            {
                var sx = i * pitch;
                var r = Hash(i * 12.9898f + 1.7f);
                var rg = Hash(i * 7.13f);
                if (rg < 0.20f) continue;
                var clu = 0.78f + 0.5f * (0.5f + 0.5f * Mathf.Sin(i * 0.31f));
                var land = r > 0.94f;
                var w = pitch * (0.5f + 0.32f * Frac(r * 3.7f)) * (land ? 0.74f : 1f);
                var x = sx + (pitch - w) * 0.5f;
                var th = (58f + r * 160f) * clu * (land ? 1.6f : 1f) * _px * 0.72f; // сплюснуто
                QuadFlat(v, c, t, x, x + w, baseY - th, baseY, col);
            }
            var go = Shapes.Create("CityReflection", parent, Shapes.Build("CityReflection", v, c, t), -77);
            go.transform.localPosition = new Vector3(0f, 0f, -0.2f);
        }

        // ================= пальмы =================

        private void BuildPalms(float trackLengthM, float par, int order)
        {
            var span = trackLengthM * (1f - par) + _halfW * 2f + 30f;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var col = new Color(WorldPalette.Silhouette.r, WorldPalette.Silhouette.g,
                WorldPalette.Silhouette.b, 0.66f);
            var baseY = _hz - 4f * _px;
            var sp = 310f * _px;

            var i0p = Mathf.FloorToInt(-_halfW * 1.5f / sp);
            var i1p = Mathf.CeilToInt(span / sp);
            for (var i = i0p; i <= i1p; i++)
            {
                var r = Hash(i * 91.17f);
                if (r < 0.34f) continue;
                var variant = Mathf.FloorToInt(r * 97f) % 3;
                var s = (0.72f + Frac(r * 7.7f) * 0.5f) * 0.62f * _px;
                var x = i * sp + (Frac(r * 13.3f) - 0.5f) * 130f * _px;
                var flip = Frac(r * 29.7f) > 0.5f;
                AddPalm(v, c, t, x, baseY, s, variant, flip, col);
            }
            var go = Shapes.Create("Palms", transform, Shapes.Build("Palms", v, c, t), order);
            AddParallax(go, par, 55f);
        }

        /// <summary>
        /// Пальма — порт palmSprites: ствол-дуга + 9 листьев-дуг с бахромой. Кривые Безье
        /// сэмплируются в полосы. Спрайт исходника 150×210, точки в его координатах.
        /// </summary>
        private void AddPalm(List<Vector3> v, List<Color> c, List<int> t,
            float x, float baseY, float s, int variant, bool flip, Color col)
        {
            var cx = 75f;
            var ty = 52f + variant * 7f;
            float FX(float px) => x + (flip ? cx - px : px - cx) * s;
            float FY(float py) => baseY + (210f - py) * s;

            // Ствол: quadratic (cx-9+v*6,210) → (cx-1,ty+5), ctrl (cx-15+v*9,126).
            Bez(v, c, t, FX(cx - 9 + variant * 6), FY(210),
                FX(cx - 15 + variant * 9), FY(126),
                FX(cx - 1), FY(ty + 5), (6.5f - variant * 1.1f) * s, col, 8);

            // Листья: 9 дуг от кроны, каждая с бахромой из коротких штрихов.
            for (var k = 0; k < 9; k++)
            {
                var a = -Mathf.PI / 2f + (k - 4) * 0.42f + (variant - 1) * 0.06f;
                var L = 50f + (k == 4 ? 11f : 0f) + (k % 3) * 5f;
                var mx = cx + Mathf.Cos(a) * L * 0.52f;
                var my = ty + Mathf.Sin(a) * L * 0.30f;
                var tx = cx + Mathf.Cos(a) * L;
                var tyy = ty + Mathf.Sin(a) * L * 0.62f + 15f;
                Bez(v, c, t, FX(cx), FY(ty), FX(mx), FY(my), FX(tx), FY(tyy), 4.6f * s, col, 7);
                for (var f = 0.34f; f <= 0.94f; f += 0.15f)
                {
                    var px0 = cx + (tx - cx) * f;
                    var py0 = ty + (tyy - ty) * f - (1f - f) * 7f;
                    Shapes.AddBar(v, c, t,
                        new Vector2(FX(px0), FY(py0)),
                        new Vector2(FX(px0 + Mathf.Cos(a + 0.95f) * 7f),
                            FY(py0 + Mathf.Sin(a + 0.95f) * 7f + 5f)),
                        1.5f * s, col);
                }
            }
            Shapes.AddDisc(v, c, t, new Vector2(FX(cx), FY(ty)), 5f * s, col, 8);
        }

        // ================= вода =================

        private void BuildWater(float trackLengthM)
        {
            var w = _halfW * 3f;
            var wy = _hz - 26f * _px; // вода на 26 px ниже горизонта (hz+26 в экранных)

            // Тон глубины: прозрачно у кромки → почти чёрное вниз (порт dg).
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var deep = WorldPalette.DeckShadow;
            var y0 = _hz - 20f * _px;
            var yMid = Mathf.Lerp(y0, -_halfH * 1.3f, 0.38f);
            Band(v, c, t, -w, w, yMid, y0,
                new Color(deep.r, deep.g, deep.b, 0.58f), new Color(deep.r, deep.g, deep.b, 0f));
            Band(v, c, t, -w, w, -_halfH * 1.3f, yMid,
                new Color(deep.r, deep.g, deep.b, 0.92f), new Color(deep.r, deep.g, deep.b, 0.58f));
            Pin(Shapes.Create("WaterDepth", transform, Shapes.Build("WaterDepth", v, c, t), -78), 53f);

            // Дорожка света от солнца (аддитив, 46 px шириной, 150 px вниз).
            var sv = new List<Vector3>();
            var sc = new List<Color>();
            var st = new List<int>();
            var pc = new Color(1f, 170 / 255f, 110 / 255f, 1f);
            Band(sv, sc, st, _sunPos.x - 23f * _px, _sunPos.x + 23f * _px,
                wy - 150f * _px, wy,
                new Color(pc.r, pc.g, pc.b, 0f), new Color(pc.r, pc.g, pc.b, 0.15f));
            // Рябь: тонкие штрихи в дорожке, детерминированные.
            for (var i = 0; i < 14; i++)
            {
                var rr = Hash(i * 17.31f);
                var ry = wy - (10f + rr * 120f) * _px;
                var rw2 = (10f + Frac(rr * 9.7f) * 26f) * _px;
                var rx = _sunPos.x + (Frac(rr * 5.3f) - 0.5f) * 70f * _px;
                Shapes.AddBar(sv, sc, st, new Vector2(rx - rw2 * 0.5f, ry),
                    new Vector2(rx + rw2 * 0.5f, ry), 1.4f * _px,
                    new Color(pc.r, pc.g, pc.b, 0.10f + 0.08f * Frac(rr * 3.3f)));
            }
            var sun = Shapes.Create("SunPath", transform, Shapes.Build("SunPath", sv, sc, st),
                -76, Shapes.Additive);
            Pin(sun, 52.5f);
        }

        private void BuildVeil()
        {
            // Вуаль-дымка над ВСЕМ задним планом (b209): светлее/мягче = уходит назад.
            var w = _halfW * 3f;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            Band(v, c, t, -w, w, _hz - 36f * _px, _halfH * 1.25f,
                WorldPalette.Veil, WorldPalette.Veil);
            // Холодная тень в переднем плане (низ кадра) — контраст к тёплому свету.
            var cold = new Color(14 / 255f, 10 / 255f, 40 / 255f, 1f);
            Band(v, c, t, -w, w, -_halfH * 1.25f, -_halfH * 0.44f,
                new Color(cold.r, cold.g, cold.b, 0.5f), new Color(cold.r, cold.g, cold.b, 0f));
            Pin(Shapes.Create("Veil", transform, Shapes.Build("Veil", v, c, t), -60), 50f);
        }

        // ================= анимация =================

        private void LateUpdate()
        {
            if (_cam == null) return;
            _time += Time.deltaTime;
            var camX = _cam.position.x;
            var camY = _cam.position.y;

            // Мир приклеен к камере по вертикали (горизонт всегда на 60 % кадра, как в
            // исходнике), по горизонтали слои едут со своим параллаксом.
            for (var i = 0; i < _layers.Count; i++)
            {
                var l = _layers[i];
                l.T.position = new Vector3(camX * l.Parallax, camY, l.T.position.z);
            }

            AnimateRays(camX, camY);
            AnimateBeacons();
        }

        private void AnimateRays(float camX, float camY)
        {
            if (_raysMesh == null) return;
            var v = new List<Vector3>(48);
            var c = new List<Color>(48);
            var t = new List<int>(72);
            var line = _era.Line;
            var len = _halfH * 2f * 0.9f;
            var origin = new Vector2(_sunPos.x, _hz);
            // t исходника — кадры при 60 fps: t*0.004 → *0.24 в секундах.
            var tt = _time * 60f;

            for (var k = 0; k < 12; k++)
            {
                var a0 = -1.15f + k / 11f * 2.3f + Mathf.Sin(tt * 0.004f + k) * 0.04f;
                var w = (4f + 4f * Mathf.Abs(Mathf.Sin(tt * 0.009f + k * 1.7f))) * _px;
                var al = 0.02f + 0.018f * Mathf.Abs(Mathf.Sin(tt * 0.011f + k));
                var dir = new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)); // вверх, наклон a0
                var n = new Vector2(-dir.y, dir.x);
                var c0 = new Color(line.r, line.g, line.b, al);
                var c1 = new Color(line.r, line.g, line.b, 0f);

                var i0 = v.Count;
                v.Add(origin - n * w); c.Add(Shapes.V(c0));
                v.Add(origin + n * w); c.Add(Shapes.V(c0));
                v.Add(origin + (Vector2)(dir * len) + n * w * 2.2f); c.Add(Shapes.V(c1));
                v.Add(origin + (Vector2)(dir * len) - n * w * 2.2f); c.Add(Shapes.V(c1));
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
                t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
            }
            _raysMesh.Clear();
            _raysMesh.SetVertices(v);
            _raysMesh.SetColors(c);
            _raysMesh.SetTriangles(t, 0);
            _raysMesh.RecalculateBounds();
            _raysFilter.sharedMesh = _raysMesh;
        }

        private void AnimateBeacons()
        {
            if (_beaconMesh == null || _beacons.Count == 0) return;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            var tt = _time * 60f;
            for (var i = 0; i < _beacons.Count; i++)
            {
                var bl = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(tt * 0.05f + i * 2.1f));
                Shapes.AddDisc(v, c, t, _beacons[i], 1.5f * _px,
                    new Color(1f, 84 / 255f, 96 / 255f, bl * 0.75f), 6);
            }
            _beaconMesh.Clear();
            _beaconMesh.SetVertices(v);
            _beaconMesh.SetColors(c);
            _beaconMesh.SetTriangles(t, 0);
            _beaconMesh.RecalculateBounds();
            _beaconFilter.sharedMesh = _beaconMesh;
        }

        // ================= помощники =================

        /// <summary>Слой, приклеенный к камере целиком (небо, солнце, вода, вуаль).</summary>
        private void Pin(GameObject go, float z)
        {
            go.transform.SetParent(_cam, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
        }

        /// <summary>Слой с параллаксом по X (город, пальмы, скайлайн). Y — за камерой.</summary>
        private void AddParallax(GameObject go, float par, float z)
        {
            go.transform.SetParent(transform, true);
            go.transform.position = new Vector3(0f, 0f, z);
            _layers.Add(new Layer { T = go.transform, Parallax = par });
        }

        /// <summary>Полоса, разбитая на подполосы с ОСТАНОВКАМИ, слерпнутыми в sRGB.</summary>
        private static void BandSrgb(List<Vector3> v, List<Color> c, List<int> t,
            float x0, float x1, float y0, float y1, Color bottom, Color top, int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                var k0 = i / (float)steps;
                var k1 = (i + 1) / (float)steps;
                Band(v, c, t, x0, x1,
                    Mathf.Lerp(y0, y1, k0), Mathf.Lerp(y0, y1, k1),
                    Color.Lerp(bottom, top, k0), Color.Lerp(bottom, top, k1));
            }
        }

        private static void Band(List<Vector3> v, List<Color> c, List<int> t,
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

        /// <summary>Квад с горизонтальной трёхцветной лепкой (свет→середина→тень).</summary>
        private static void QuadH(List<Vector3> v, List<Color> c, List<int> t,
            float x0, float x1, float y0, float y1, Color left, Color mid, Color right)
        {
            var xm = (x0 + x1) * 0.5f;
            var i0 = v.Count;
            v.Add(new Vector3(x0, y0, 0f)); c.Add(Shapes.V(left));
            v.Add(new Vector3(x0, y1, 0f)); c.Add(Shapes.V(left));
            v.Add(new Vector3(xm, y1, 0f)); c.Add(Shapes.V(mid));
            v.Add(new Vector3(xm, y0, 0f)); c.Add(Shapes.V(mid));
            v.Add(new Vector3(x1, y0, 0f)); c.Add(Shapes.V(right));
            v.Add(new Vector3(x1, y1, 0f)); c.Add(Shapes.V(right));
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
            t.Add(i0 + 3); t.Add(i0 + 2); t.Add(i0 + 5);
            t.Add(i0 + 3); t.Add(i0 + 5); t.Add(i0 + 4);
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

        private static void GlowDisc(List<Vector3> v, List<Color> c, List<int> t,
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

        /// <summary>Квадратичная Безье полосой заданной толщины.</summary>
        private static void Bez(List<Vector3> v, List<Color> c, List<int> t,
            float x0, float y0, float cx, float cy, float x1, float y1,
            float width, Color col, int segments)
        {
            var prev = new Vector2(x0, y0);
            for (var i = 1; i <= segments; i++)
            {
                var k = i / (float)segments;
                var a = Vector2.Lerp(new Vector2(x0, y0), new Vector2(cx, cy), k);
                var b = Vector2.Lerp(new Vector2(cx, cy), new Vector2(x1, y1), k);
                var p = Vector2.Lerp(a, b, k);
                Shapes.AddBar(v, c, t, prev, p, width, col);
                prev = p;
            }
        }

        private static float Hash(float x)
        {
            var r = (Mathf.Sin(x) * 43758.5453f) % 1f;
            return r < 0f ? r + 1f : r;
        }

        private static float Frac(float x) => x - Mathf.Floor(x);
    }
}
