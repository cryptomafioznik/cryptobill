using System.Collections.Generic;
using UnityEngine;

namespace ChartRunner.Track
{
    /// <summary>
    /// Строит коллизию трассы из <see cref="TrackProfile"/> — код, не префаб и не руками
    /// собранная сцена (хендофф §4: YAML вручную писать нельзя, ошибки не поймать).
    ///
    /// РАЗРЫВЫ. По умолчанию разрыв НЕ режет коллизию — ровно как в исходнике, где
    /// `terrainAt` флаг `.gap` не читает и поверхность под разрывом непрерывна. Первая
    /// редакция резала всегда, «потому что физичнее», и это сделало дизайн-трассу
    /// непроходимой: 9.61 м дыры требуют 15.9 м/с при верхней скорости 6.27 м/с.
    /// Настоящие дыры включаются флагом `TrackProfile.gapsCutCollision` — он понадобится,
    /// когда появятся реальные пропасти, спроектированные под перелёт.
    /// </summary>
    public static class TrackBuilder
    {
        /// <summary>Материал по умолчанию для поверхности: трение задаётся профилем байка.</summary>
        public static PhysicsMaterial2D CreateSurfaceMaterial(float friction)
        {
            return new PhysicsMaterial2D("TrackSurface")
            {
                friction = friction,
                bounciness = 0f
            };
        }

        /// <summary>
        /// Создаёт объект трассы с одним или несколькими EdgeCollider2D.
        /// Возвращает корневой GameObject.
        /// </summary>
        public static GameObject Build(TrackProfile profile, float surfaceFriction, string name = "Track")
        {
            var root = new GameObject(name);
            var mat = CreateSurfaceMaterial(surfaceFriction);
            var runs = SplitIntoRuns(profile);

            for (var r = 0; r < runs.Count; r++)
            {
                var child = new GameObject("Run" + r);
                child.transform.SetParent(root.transform, false);
                var edge = child.AddComponent<EdgeCollider2D>();
                edge.points = runs[r].ToArray();
                edge.sharedMaterial = mat;
                // Замкнутых участков нет: это открытая линия рельефа.
                edge.edgeRadius = 0f;
            }

            return root;
        }

        /// <summary>
        /// Полилиния, разрезанная по разрывам. Каждый элемент — непрерывный участок
        /// в МЕТРАХ. Участки короче двух точек отбрасываются.
        /// </summary>
        public static List<List<Vector2>> SplitIntoRuns(TrackProfile profile, float paddingPx = 400f)
        {
            var runs = new List<List<Vector2>>();
            var cur = new List<Vector2>();
            var k = Tuning.UnitsContract.PxToM;

            for (var xPx = 0f; xPx <= profile.endPx + paddingPx; xPx += profile.nodeStepPx)
            {
                if (profile.gapsCutCollision && profile.IsGap(xPx))
                {
                    if (cur.Count >= 2) runs.Add(cur);
                    cur = new List<Vector2>();
                    continue;
                }
                cur.Add(new Vector2(xPx * k, profile.HeightPx(xPx) * k));
            }

            if (cur.Count >= 2) runs.Add(cur);
            return runs;
        }

        /// <summary>
        /// Плоская тестовая площадка — для проверок, которым не нужна вся трасса.
        /// Уклон в градусах, длина в метрах.
        /// </summary>
        public static GameObject BuildRamp(float slopeDeg, float lengthM, float friction,
            float flatRunUpM = 0f, string name = "Ramp")
        {
            var root = new GameObject(name);
            var edge = root.AddComponent<EdgeCollider2D>();
            edge.sharedMaterial = CreateSurfaceMaterial(friction);

            var pts = new List<Vector2>();
            // Небольшой запас позади старта, чтобы байк не свисал с края.
            pts.Add(new Vector2(-5f, 0f));
            pts.Add(new Vector2(0f, 0f));
            if (flatRunUpM > 0f) pts.Add(new Vector2(flatRunUpM, 0f));

            var x0 = Mathf.Max(0f, flatRunUpM);
            var tan = Mathf.Tan(slopeDeg * Mathf.Deg2Rad);
            pts.Add(new Vector2(x0 + lengthM, tan * lengthM));
            // Площадка сверху, чтобы было куда выехать.
            pts.Add(new Vector2(x0 + lengthM + 10f, tan * lengthM));

            edge.points = pts.ToArray();
            return root;
        }
    }
}
