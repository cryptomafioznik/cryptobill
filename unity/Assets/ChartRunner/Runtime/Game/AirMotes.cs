using System.Collections.Generic;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Взвесь в воздухе: редкие тёплые пылинки, дрейфующие против света.
    ///
    /// Зачем: контражур работает, когда ВИДЕН сам воздух, через который бьёт свет.
    /// Небо и дымка дают его крупными формами, пылинки — микромасштабом. Без них кадр
    /// между героем и фоном стерильно пуст, и glow-у не на чем «висеть».
    ///
    /// Всё детерминировано от индекса частицы: одинаковые прогоны дают одинаковые кадры,
    /// иначе скриншоты нельзя сравнивать между сборками (правило приёмки по кадрам).
    /// Движение — медленный дрейф с синусоидальным блужданием, БЕЗ реакции на байк:
    /// это атмосфера, а не эффект. Частицы живут в боксе вокруг камеры и заворачиваются
    /// по краям, поэтому их ровно Count на любой длине трассы.
    /// </summary>
    public class AirMotes : MonoBehaviour
    {
        private const int Count = 44;

        private Transform _cam;
        private float _halfW;
        private float _halfH;
        private Mesh _mesh;
        private MeshFilter _filter;

        private static readonly Color Warm = new Color(1f, 0.80f, 0.50f, 1f);

        public static AirMotes Attach(Camera cam)
        {
            var go = new GameObject("AirMotes");
            var m = go.AddComponent<AirMotes>();
            m._cam = cam.transform;
            m._halfH = cam.orthographicSize * 1.15f;
            m._halfW = cam.orthographicSize * Mathf.Max(cam.aspect, 1f) * 1.15f;
            m._mesh = new Mesh { name = "AirMotes" };
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = m._mesh;
            var mr = go.AddComponent<MeshRenderer>();
            // Слегка ярче единицы: пылинка на просвет — крошечный источник, но bloom
            // на ней почти не виден — и не должен быть, иначе кадр зарастает светляками.
            mr.sharedMaterial = Shapes.Emissive(1.3f);
            mr.sortingOrder = 8;
            m._filter = mf;
            return m;
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            var t = Time.timeSinceLevelLoad;
            var cx = _cam.position.x;
            var cy = _cam.position.y;

            var v = new List<Vector3>(Count * 9);
            var c = new List<Color>(Count * 9);
            var tr = new List<int>(Count * 24);

            for (var i = 0; i < Count; i++)
            {
                // Детерминированные параметры частицы от её индекса.
                var h1 = Frac(i * 0.6180339887f);          // золотое сечение — равномерно
                var h2 = Frac(i * 0.7548776662f + 0.37f);
                var h3 = Frac(i * 0.5698402910f + 0.71f);

                // Базовый дрейф: против хода (свет сзади → пыль плывёт навстречу),
                // чуть вверх. Скорость своя у каждой.
                var speed = 0.16f + h1 * 0.30f;
                var x0 = h2 * _halfW * 2f - _halfW - t * speed;
                var y0 = h3 * _halfH * 2f - _halfH + Mathf.Sin(t * (0.25f + h1 * 0.4f) + i) * 0.35f;

                // Заворот в бокс камеры: частица, ушедшая за край, входит с другого.
                var x = Wrap(x0 - cx * 0.06f, _halfW) + cx;
                var y = Wrap(y0, _halfH) + cy;

                // Мерцание — пылинка ловит свет только под углом.
                var tw = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(t * (0.6f + h2) + i * 2.1f));
                var size = 0.020f + h1 * 0.030f;
                var a = (0.16f + 0.22f * h3) * tw;

                Shapes.AddDisc(v, c, tr, new Vector2(x, y), size,
                    new Color(Warm.r, Warm.g, Warm.b, a), 8);
            }

            _mesh.Clear();
            _mesh.SetVertices(v);
            _mesh.SetColors(c);
            _mesh.SetTriangles(tr, 0);
            _mesh.RecalculateBounds();
            _filter.sharedMesh = _mesh;
        }

        private static float Frac(float x) => x - Mathf.Floor(x);

        private static float Wrap(float x, float half)
        {
            var w = half * 2f;
            var r = (x + half) % w;
            if (r < 0f) r += w;
            return r - half;
        }
    }
}
