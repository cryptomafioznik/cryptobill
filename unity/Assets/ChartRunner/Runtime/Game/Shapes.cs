using System.Collections.Generic;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Генератор плоских мешей: круг, кольцо, полоса, полигон, скруглённый прямоугольник.
    ///
    /// ПОЧЕМУ МЕШИ, А НЕ СПРАЙТЫ. Правило проекта: сгенерированная картинка входит в игру
    /// только разобранной на риг, иначе по трассе едет изображение вместо байка. Меш —
    /// это геометрия с параметрами: силуэт можно менять числом, он масштабируется без
    /// потери резкости и на нём видно поворот колеса. Спрайты появятся тогда, когда будет
    /// что в них класть, чего геометрия не выражает.
    ///
    /// Все меши строятся в ЛОКАЛЬНЫХ метрах вокруг нуля объекта.
    /// </summary>
    public static class Shapes
    {
        private static Material _material;
        private static readonly Dictionary<int, Material> _emissive = new Dictionary<int, Material>();

        /// <summary>
        /// Перевод авторского цвета в тот, который шейдер выведет как задумано.
        ///
        /// НАЙДЕНО ЗАПУСКОМ, а не чтением настроек: первый снятый кадр показал заливку
        /// рельефа светло-серой там, где задан тёмно-синий 0.16. Проект работает в ЛИНЕЙНОМ
        /// пространстве, вершинный цвет попадает в шейдер без преобразования, и 0.16 линейных
        /// выводится как 0.43 sRGB — то есть вся палитра оказывалась почти втрое светлее
        /// написанной. Здесь цвета авторские (sRGB), перевод один и в одном месте.
        /// </summary>
        public static Color V(Color c)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;
        }

        /// <summary>
        /// Общий материал: цвет берётся из вершин, поэтому один материал на весь кадр
        /// и ни одного лишнего draw call на смену цвета.
        /// </summary>
        public static Material VertexColorMaterial
        {
            get
            {
                if (_material == null)
                {
                    _material = new Material(WorldShader) { name = "ChartRunnerVertexColor" };
                }
                return _material;
            }
        }

        /// <summary>
        /// Материал-источник свечения: вершинный цвет × intensity. Вершинный цвет меша
        /// 8-битный и не бывает больше 1.0, а bloom пост-обработки ловит только то, что
        /// ярче 1.0 в HDR-буфере — поэтому яркость живёт в материале. Кэш по десятым
        /// долям: сто разных интенсивностей = сто draw call, а глаз различает ~пять.
        /// </summary>
        public static Material Emissive(float intensity)
        {
            var key = Mathf.RoundToInt(intensity * 10f);
            if (!_emissive.TryGetValue(key, out var m) || m == null)
            {
                m = new Material(WorldShader) { name = "ChartRunnerEmissive_" + key };
                if (m.HasProperty(IntensityId)) m.SetFloat(IntensityId, key / 10f);
                _emissive[key] = m;
            }
            return m;
        }

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        private static Shader WorldShader
        {
            get
            {
                // Свой URP-шейдер лежит в Resources (иначе вырезается из билда).
                // Фолбэки ниже — страховка от «розового мира», а не рабочий путь.
                var sh = Shader.Find("ChartRunner/VertexColorHDR");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                return sh;
            }
        }

        /// <summary>Создаёт объект с мешем, готовый к показу. sortingOrder — порядок в слое.</summary>
        public static GameObject Create(string name, Transform parent, Mesh mesh, int sortingOrder)
        {
            return Create(name, parent, mesh, sortingOrder, VertexColorMaterial);
        }

        /// <summary>То же, но со своим материалом — для светящейся геометрии (Emissive).</summary>
        public static GameObject Create(string name, Transform parent, Mesh mesh, int sortingOrder,
            Material material)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = sortingOrder;
            return go;
        }

        public static Mesh Disc(float radius, Color color, int segments = 28)
        {
            var v = new List<Vector3>(segments + 1);
            var c = new List<Color>(segments + 1);
            var t = new List<int>(segments * 3);

            v.Add(Vector3.zero);
            c.Add(V(color));
            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
                c.Add(V(color));
            }
            for (var i = 0; i < segments; i++)
            {
                t.Add(0);
                t.Add(1 + i);
                t.Add(1 + (i + 1) % segments);
            }
            return Build("Disc", v, c, t);
        }

        public static Mesh Ring(float outerRadius, float innerRadius, Color color, int segments = 28)
        {
            var v = new List<Vector3>(segments * 2);
            var c = new List<Color>(segments * 2);
            var t = new List<int>(segments * 6);

            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                var dx = Mathf.Cos(a);
                var dy = Mathf.Sin(a);
                v.Add(new Vector3(dx * outerRadius, dy * outerRadius, 0f));
                v.Add(new Vector3(dx * innerRadius, dy * innerRadius, 0f));
                c.Add(V(color));
                c.Add(V(color));
            }
            for (var i = 0; i < segments; i++)
            {
                var o0 = i * 2;
                var i0 = i * 2 + 1;
                var o1 = (i * 2 + 2) % (segments * 2);
                var i1 = (i * 2 + 3) % (segments * 2);
                t.Add(o0); t.Add(o1); t.Add(i0);
                t.Add(i0); t.Add(o1); t.Add(i1);
            }
            return Build("Ring", v, c, t);
        }

        /// <summary>Отрезок заданной толщины — базовый элемент рамы, вилки, конечностей.</summary>
        public static void AddBar(List<Vector3> v, List<Color> c, List<int> t,
            Vector2 a, Vector2 b, float width, Color color)
        {
            var dir = b - a;
            var len = dir.magnitude;
            if (len < 1e-5f) return;
            var n = new Vector2(-dir.y, dir.x) / len * (width * 0.5f);

            var i0 = v.Count;
            v.Add(new Vector3(a.x + n.x, a.y + n.y, 0f));
            v.Add(new Vector3(b.x + n.x, b.y + n.y, 0f));
            v.Add(new Vector3(b.x - n.x, b.y - n.y, 0f));
            v.Add(new Vector3(a.x - n.x, a.y - n.y, 0f));
            for (var i = 0; i < 4; i++) c.Add(V(color));
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
        }

        /// <summary>Выпуклый полигон по точкам (веером от первой).</summary>
        public static void AddFan(List<Vector3> v, List<Color> c, List<int> t,
            IList<Vector2> pts, Color color)
        {
            if (pts.Count < 3) return;
            var i0 = v.Count;
            for (var i = 0; i < pts.Count; i++)
            {
                v.Add(new Vector3(pts[i].x, pts[i].y, 0f));
                c.Add(V(color));
            }
            for (var i = 1; i < pts.Count - 1; i++)
            {
                t.Add(i0); t.Add(i0 + i); t.Add(i0 + i + 1);
            }
        }

        /// <summary>Круглая «шапка» — сустав, голова, точка сочленения.</summary>
        public static void AddDisc(List<Vector3> v, List<Color> c, List<int> t,
            Vector2 center, float radius, Color color, int segments = 14)
        {
            var i0 = v.Count;
            v.Add(new Vector3(center.x, center.y, 0f));
            c.Add(V(color));
            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                v.Add(new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius, 0f));
                c.Add(V(color));
            }
            for (var i = 0; i < segments; i++)
            {
                t.Add(i0);
                t.Add(i0 + 1 + i);
                t.Add(i0 + 1 + (i + 1) % segments);
            }
        }

        public static Mesh Build(string name, List<Vector3> v, List<Color> c, List<int> t)
        {
            var m = new Mesh { name = name };
            m.indexFormat = v.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
