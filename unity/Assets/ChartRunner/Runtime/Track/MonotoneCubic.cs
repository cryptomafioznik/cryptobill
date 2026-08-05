using System;
using UnityEngine;

namespace ChartRunner.Track
{
    /// <summary>
    /// Монотонная кусочно-кубическая интерполяция по узлам трассы — точный порт `vsHeight`
    /// из toys/chartrider.html (стр. 3609-3622). Схема касательных Фрича–Карлсона.
    ///
    /// Почему НЕ Catmull-Rom (это записано в исходнике и переносится вместе с кодом):
    /// обычный Catmull-Rom давал «горбы» на резких переходах вершины, то есть выброс за пределы
    /// значений соседних узлов. Гладкость нужна, чтобы на переломах не было ступенек, но БЕЗ
    /// перелётов — иначе на вершине появляется рельеф, которого нет в данных.
    ///
    /// Работает в АВТОРСКИХ ПИКСЕЛЯХ исходной игры, а не в метрах. Причина в
    /// TrackProfile: данные хранятся как были заданы, перевод в метры — один раз, снаружи.
    /// </summary>
    public static class MonotoneCubic
    {
        /// <summary>Наклон хорды сегмента i (px по вертикали на px по горизонтали).</summary>
        public static float SlopeAt(int i, Vector2[] p)
        {
            return (p[i + 1].y - p[i].y) / Mathf.Max(1e-6f, p[i + 1].x - p[i].x);
        }

        /// <summary>
        /// Высота в точке x. Вне диапазона узлов — константа крайнего узла (как в исходнике).
        /// </summary>
        public static float Height(float x, Vector2[] p)
        {
            if (p == null || p.Length == 0) return 0f;
            var n = p.Length;
            if (n == 1 || x <= p[0].x) return p[0].y;
            if (x >= p[n - 1].x) return p[n - 1].y;

            var i = 0;
            for (; i < n - 1; i++)
                if (x >= p[i].x && x <= p[i + 1].x) break;
            if (i > n - 2) i = n - 2;

            float x0 = p[i].x, y0 = p[i].y, x1 = p[i + 1].x, y1 = p[i + 1].y;
            var h = x1 - x0;
            var t = (x - x0) / h;

            var s = SlopeAt(i, p);
            var sPrev = i > 0 ? SlopeAt(i - 1, p) : s;
            var sNext = i < n - 2 ? SlopeAt(i + 1, p) : s;

            // Касательные гасятся в ноль на смене знака наклона → нет перелётов на переломах.
            var m0 = (i == 0) ? s : ((sPrev * s <= 0f) ? 0f : (sPrev + s) * 0.5f);
            var m1 = (i == n - 2) ? s : ((s * sNext <= 0f) ? 0f : (s + sNext) * 0.5f);

            var lim = 3f * Mathf.Abs(s);
            var c0 = Mathf.Clamp(m0, -lim, lim);
            var c1 = Mathf.Clamp(m1, -lim, lim);

            var t2 = t * t;
            var t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * y0
                   + (t3 - 2f * t2 + t) * h * c0
                   + (-2f * t3 + 3f * t2) * y1
                   + (t3 - t2) * h * c1;
        }

        /// <summary>
        /// Уклон поверхности в точке x, радианы, положительный = вверх по ходу движения.
        /// Считается центральной разностью по той же кривой, шагом eps в авторских пикселях.
        /// Исходная игра брала уклон иначе — как atan2 разности соседних УЗЛОВ сетки 26 px
        /// (`terrainAt`), поэтому для физики надо использовать сетку, а эту функцию — только
        /// для анализа кривой. Разница между двумя способами измерена: см. отчёт пункта 4.
        /// </summary>
        public static float SlopeRadAt(float x, Vector2[] p, float eps = 0.5f)
        {
            var yb = Height(x - eps, p);
            var ya = Height(x + eps, p);
            return Mathf.Atan2(ya - yb, 2f * eps);
        }
    }
}
