using System;
using System.Collections.Generic;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Track
{
    /// <summary>
    /// Профиль дизайн-трассы — порт данных `VS` из toys/chartrider.html (стр. 3570-3603).
    ///
    /// РЕШЕНИЕ О ЕДИНИЦАХ: узлы хранятся в АВТОРСКИХ ПИКСЕЛЯХ, ровно как заданы в исходнике.
    /// Перевод в метры — снаружи, через UnitsContract, один раз при построении коллайдера.
    /// Причина: данные остаются побайтово сверяемыми с исходником и читаемыми глазами, а
    /// масштаб живёт в одном месте. Если хранить метры, любая правка контракта единиц
    /// молча переписала бы саму трассу.
    ///
    /// Что НЕ переносится сюда: `VS.roadW/farLift/ribEvery` (визуальная косоугольная дорога —
    /// выброшена, docs/PROJECT_AUDIT.md §1.4) и `VS.cam*` (композиция под landscape,
    /// переписывается под portrait в P1).
    /// </summary>
    [CreateAssetMenu(menuName = "ChartRunner/Track Profile", fileName = "TrackProfile")]
    public class TrackProfile : ScriptableObject
    {
        [Serializable]
        public struct Gap
        {
            public float fromPx;
            public float toPx;
        }

        [Serializable]
        public struct Checkpoint
        {
            public float xPx;
            public string name;
        }

        [Serializable]
        public struct Section
        {
            public string name;
            public float fromPx;
            public float toPx;
            [Tooltip("Дефект, перенесённый вместе с данными; пусто если его нет.")]
            public string knownDefect;
        }

        [Header("Профиль: [x, высота] в авторских px, высота вверх = +")]
        public Vector2[] nodesPx = Array.Empty<Vector2>();

        [Header("Разрывы (пропасти) в авторских px")]
        public Gap[] gapsPx = Array.Empty<Gap>();

        [Tooltip("Режет ли разрыв коллизию НАСТОЯЩЕЙ дырой. ДЕФОЛТ false — как в исходнике.\n\n" +
                 "Проверено по коду: `terrainAt` флаг .gap НЕ ЧИТАЕТ, в rigidStep слова gap нет " +
                 "вообще, а buildVSliceTerr ставит высоту на узлах разрыва обычным образом. То есть " +
                 "в исходной игре «GAP 6750→7090» — косметический маркер, поверхность под ним " +
                 "непрерывна, и скриптовые пилоты его просто переезжали.\n\n" +
                 "Первая редакция TrackBuilder делала из разрыва настоящую дыру «потому что " +
                 "физичнее» — и трасса стала непроходимой: разрыв 9.61 м требует 15.9 м/с, а " +
                 "верхняя скорость 6.27 м/с даёт дальность броска 1.49 м. Оба скриптовых пилота " +
                 "умирали в нём на 62 % трассы с одинаковым результатом, из-за чего тест окна " +
                 "навыка мерил положение разрыва, а не навык.")]
        public bool gapsCutCollision;

        [Header("Финиш, авторские px")]
        public float endPx;

        [Header("Чекпоинты")]
        public Checkpoint[] checkpoints = Array.Empty<Checkpoint>();

        [Header("Шаг дискретизации коллайдера, авторские px (STEP в исходнике)")]
        public float nodeStepPx = 26f;

        [Header("ОСТРЫЕ узлы: индексы, где кромка не скругляется")]
        [Tooltip("Сегменты рядом с этими узлами интерполируются линейно, поэтому угол сохраняется. " +
                 "Нужно для липов трамплинов: кубика Фрича–Карлсона гасит касательные в нуль на любом " +
                 "локальном максимуме, и острую кромку в этом формате иначе не выразить. Исходник делал " +
                 "то же — не сглаживал узлы фич (gap/drop/kick/step/climb/mega), стр. 248-249.")]
        public int[] sharpNodeIndices = Array.Empty<int>();

        private bool[] _sharpMask;

        /// <summary>Маска острых узлов, пересобирается при смене данных.</summary>
        public bool[] SharpMask
        {
            get
            {
                if (_sharpMask == null || _sharpMask.Length != nodesPx.Length)
                {
                    _sharpMask = new bool[nodesPx.Length];
                    for (var i = 0; i < sharpNodeIndices.Length; i++)
                    {
                        var k = sharpNodeIndices[i];
                        if (k >= 0 && k < _sharpMask.Length) _sharpMask[k] = true;
                    }
                }
                return _sharpMask;
            }
        }

        [Header("Секции — для отчётов и для разговора о кривой обучения")]
        public Section[] sections = Array.Empty<Section>();

        [Header("Игровые флаги уровня")]
        [Tooltip("flowBoost выключен КОНФИГОМ уровня, а не как побочка HUD (VS.gameplay).")]
        public bool flowBoostEnabled;

        // ---- производные величины ----

        public float EndM => endPx * UnitsContract.PxToM;
        public float NodeStepM => nodeStepPx * UnitsContract.PxToM;

        /// <summary>Высота в авторских px по монотонной кубической кривой.</summary>
        public float HeightPx(float xPx) => MonotoneCubic.Height(xPx, nodesPx, SharpMask);

        /// <summary>Разрыв ли в точке x (строго внутри интервала, как в исходном vsIsGap).</summary>
        public bool IsGap(float xPx)
        {
            for (var i = 0; i < gapsPx.Length; i++)
                if (xPx > gapsPx[i].fromPx && xPx < gapsPx[i].toPx) return true;
            return false;
        }

        /// <summary>
        /// Дискретизация в точки для EdgeCollider2D, в МЕТРАХ, шагом nodeStep.
        /// Разрывы не выкидываются — вызывающий сам решает, как их резать на отдельные
        /// коллайдеры: физика исходника тоже хранила их как флаг на узле, а не как дыру.
        /// </summary>
        public List<Vector2> SamplePolylineM(float paddingPx = 400f)
        {
            var pts = new List<Vector2>();
            if (nodesPx.Length == 0) return pts;
            var k = UnitsContract.PxToM;
            for (var x = 0f; x <= endPx + paddingPx; x += nodeStepPx)
                pts.Add(new Vector2(x * k, HeightPx(x) * k));
            return pts;
        }

        /// <summary>
        /// Уклон на сетке узлов — ровно как считал `terrainAt` в исходнике:
        /// atan2 разности высот соседних узлов сетки, а НЕ производная кривой.
        /// Физика обязана использовать именно это.
        /// </summary>
        public float GridSlopeRadAt(float xPx)
        {
            var i = Mathf.Floor(xPx / nodeStepPx);
            var x0 = i * nodeStepPx;
            var x1 = x0 + nodeStepPx;
            return Mathf.Atan2(HeightPx(x1) - HeightPx(x0), nodeStepPx);
        }

        /// <summary>Максимальный уклон на участке, градусы, по сетке узлов.</summary>
        public float MaxGridSlopeDeg(float fromPx, float toPx)
        {
            var best = 0f;
            for (var x = fromPx; x <= toPx; x += nodeStepPx)
            {
                var d = GridSlopeRadAt(x) * Mathf.Rad2Deg;
                if (Mathf.Abs(d) > Mathf.Abs(best)) best = d;
            }
            return best;
        }
    }
}
