using System;
using UnityEngine;

namespace ChartRunner.Track
{
    /// <summary>
    /// Данные «свеча → рельеф» — порт `CANDLE_TUNE` и таблицы режимов `pickRegime`
    /// из toys/chartrider.html (стр. 110-137). ДАННЫЕ, не генератор: сам генератор
    /// (`genSeg`) переносится в отдельном пункте, здесь только числа и структура.
    ///
    /// НЕРЕШЁННАЯ ПРОБЛЕМА, КОТОРАЯ ПЕРЕЕЗЖАЕТ ВМЕСТЕ С ЭТИМИ ЧИСЛАМИ.
    /// Исходник сам это признаёт в комментарии на стр. 258: нормализация каждой монеты
    /// «в экран» СТИРАЕТ абсолютную амплитуду, поэтому DOGE выходит даже положе BTC.
    /// Следствие: игрок физически не может отличить одну монету от другой по рельефу —
    /// то есть УТП проекта не реализовано, и Unity этого не решает. Это дизайн, не порт;
    /// см. docs/PROJECT_AUDIT.md §3.1. Числа здесь верны, вывод из них — нет.
    /// </summary>
    [CreateAssetMenu(menuName = "ChartRunner/Candle Terrain Profile", fileName = "CandleTerrainProfile")]
    public class CandleTerrainProfile : ScriptableObject
    {
        /// <summary>Рыночный режим = «сценарий» рельефа. Порядок соответствует исходнику.</summary>
        public enum Regime
        {
            Flat,
            Rocks,
            Bull,
            Bear,
            Pump,
            Crash
        }

        [Serializable]
        public struct RegimeRow
        {
            public Regime regime;

            [Tooltip("Базовый дрейф высоты за узел, авторские px. Отрицательный = вверх (цена растёт).")]
            public float driftMin;

            public float driftMax;

            [Tooltip("Базовая «пила» (волатильность) за узел, авторские px.")]
            public float volatility;

            [Tooltip("Длительность режима в узлах, минимум и максимум.")]
            public int lengthMin;

            public int lengthMax;

            [Tooltip("Вес при случайном выборе внутри своей ветки (см. комментарий класса).")]
            public float pickWeight;
        }

        [Header("CANDLE_TUNE — свеча правит крутизну рельефа")]
        [Tooltip("C_PUMP_STEEP: множитель крутизны ЗЕЛЁНОГО подъёма.")]
        public float pumpSteep = 1.6f;

        [Tooltip("C_DUMP_STEEP: множитель крутизны КРАСНОГО спуска.")]
        public float dumpSteep = 1.6f;

        [Tooltip("C_VOL_JAG: множитель «пилы» на волатильных режимах.")]
        public float volatilityJag = 1.6f;

        [Tooltip("C_CALM_FLAT: насколько боковик пологий (передышка).")]
        public float calmFlat = 0.5f;

        [Tooltip("C_CLIMB_CAP: кэп крутизны подъёма, рад ≈ 34°.")]
        public float climbCapRad = 0.6f;

        [Header("Рампа сложности по дистанции")]
        [Tooltip("За сколько метров volK выходит на максимум (в исходнике 1400).")]
        public float difficultyRampM = 1400f;

        [Tooltip("Насколько растёт волатильность на полной рампе: volK = 1 + d * это (0.65).")]
        public float volatilityRampGain = 0.65f;

        [Tooltip("TUNE.steepEarly = 0.85 — множитель крутизны на старте.")]
        public float steepEarly = 0.85f;

        [Tooltip("TUNE.terrainSteep = 0.85 — полная крутизна.")]
        public float terrainSteep = 0.85f;

        [Tooltip("TUNE.steepRamp = 1100 м — за сколько крутизна растёт steepEarly → terrainSteep.")]
        public float steepRampM = 1100f;

        [Tooltip("TUNE.mountain = 0.80 — размах затяжных подъёмов/спусков.")]
        public float mountain = 0.80f;

        [Header("Таблица режимов")]
        [Tooltip("ВАЖНО: после любого «тяжёлого» режима (bull/bear/pump/crash/rocks) исходник " +
                 "ПРИНУДИТЕЛЬНО ставит flat — ровный разгон для подготовки. Это правило " +
                 "структуры, а не вес в лотерее, и его нельзя потерять при переносе генератора.")]
        public RegimeRow[] regimes = Array.Empty<RegimeRow>();

        [Tooltip("Доля rock-garden в лотерее (TUNE.rockGarden = 0.10).")]
        public float rockGardenShare = 0.10f;

        /// <summary>Данные ровно как в исходнике, стр. 126-135.</summary>
        public static RegimeRow[] SourceRegimes()
        {
            return new[]
            {
                // flat: дрейф ±1.5 (=(rand−0.5)*3), пила 4, длина 6..11
                new RegimeRow { regime = Regime.Flat,  driftMin = -1.5f, driftMax = 1.5f,  volatility = 4f, lengthMin = 6,  lengthMax = 11, pickWeight = 0f },
                // rocks: дрейф ±2.5 (=(rand−0.5)*5), пила 4, длина 9..16
                new RegimeRow { regime = Regime.Rocks, driftMin = -2.5f, driftMax = 2.5f,  volatility = 4f, lengthMin = 9,  lengthMax = 16, pickWeight = 0.10f },
                // bull: дрейф −(16..29) вверх, пила 5, длина 11..22, вес 0.42
                new RegimeRow { regime = Regime.Bull,  driftMin = -29f,  driftMax = -16f,  volatility = 5f, lengthMin = 11, lengthMax = 22, pickWeight = 0.42f },
                // bear: дрейф +(15..27) вниз, пила 5, длина 10..21, вес 0.30
                new RegimeRow { regime = Regime.Bear,  driftMin = 15f,   driftMax = 27f,   volatility = 5f, lengthMin = 10, lengthMax = 21, pickWeight = 0.30f },
                // pump: дрейф −(24..34) крутая гора, пила 5, длина 6..11, вес 0.16
                new RegimeRow { regime = Regime.Pump,  driftMin = -34f,  driftMax = -24f,  volatility = 5f, lengthMin = 6,  lengthMax = 11, pickWeight = 0.16f },
                // crash: дрейф +(22..32) обрыв, пила 8, длина 6..11, вес 0.12
                new RegimeRow { regime = Regime.Crash, driftMin = 22f,   driftMax = 32f,   volatility = 8f, lengthMin = 6,  lengthMax = 11, pickWeight = 0.12f }
            };
        }
    }
}
