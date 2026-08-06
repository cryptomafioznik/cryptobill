using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChartRunner.Track
{
    /// <summary>
    /// Генератор рельефа ИЗ СВЕЧЕЙ — порт ядра `genSeg` + `pickRegime` из
    /// toys/chartrider.html (стр. 121-200).
    ///
    /// ЗАЧЕМ ЭТО ВООБЩЕ ПОНАДОБИЛОСЬ ОТДЕЛЬНО. `CandleTerrainProfile` перенёс ДАННЫЕ
    /// (режимы, крутизны, веса), а генератора не было, и в рантайме профиль не читался
    /// нигде. Следствие: игра под названием CHART RUNNER ехала по рукотворным холмам —
    /// то есть в ней не было графика, единственной вещи, которая делает её этой игрой.
    ///
    /// ЧТО ПЕРЕНЕСЕНО ТОЧНО: лотерея режимов с ПРИНУДИТЕЛЬНЫМ flat после каждого тяжёлого
    /// (это правило структуры — «после фичи всегда ровный разгон, чтобы можно было
    /// подготовиться», а не вес в лотерее), случайное блуждание с дрейфом и пилой режима,
    /// двухоктавные холмы, дребезг рок-гардена двумя синусами, пологий стартовый заезд
    /// и КЭП КРУТИЗНЫ ПОДЪЁМА с пересчётом walk — без последнего генератор «копит» долг
    /// и выдаёт его стеной через несколько узлов.
    ///
    /// ЧТО НЕ ПЕРЕНЕСЕНО: отдельные системы препятствий (гэпы, уступы, кикеры, мега-рампа,
    /// вупсы, грязь, обрывы) и рыночные события. Это ещё десяток машин состояний; они
    /// накладываются поверх этого же ядра и переносятся отдельно.
    ///
    /// ДВА ОТСТУПЛЕНИЯ ОТ ИСХОДНИКА, ОБА НАМЕРЕННЫЕ:
    ///
    /// 1. ВЫСОТА МИРА ФИКСИРОВАНА. В исходнике `H` — это высота ХОЛСТА, поэтому рельеф
    ///    зависел от размера окна: на другом экране получалась другая трасса. Здесь
    ///    <see cref="WorldHeightPx"/> = 800 авторских px и от экрана не зависит. Это
    ///    частичное лечение той же болезни, из-за которой не работает УТП: нормализация
    ///    «в экран» стирает амплитуду и делает DOGE неотличимым от BTC. Полное лечение —
    ///    отдельный разговор про то, чем монеты должны различаться.
    ///
    /// 2. СЛУЧАЙНОСТЬ С СЕМЕНЕМ. `Math.random()` заменён на детерминированный поток:
    ///    одно и то же семя даёт побитово ту же трассу. Без этого нельзя ни сравнить два
    ///    прогона, ни написать тест, ни воспроизвести жалобу игрока.
    /// </summary>
    public static class CandleTrackGenerator
    {
        /// <summary>Высота игрового мира в авторских px. Заменяет высоту холста исходника.</summary>
        public const float WorldHeightPx = 800f;

        /// <summary>Шаг узла, авторские px (STEP исходника).</summary>
        public const float StepPx = 26f;

        /// <summary>Тип свечи для отрисовки: направление и режим, в котором она родилась.</summary>
        public struct Candle
        {
            public float OpenPx;   // высота в начале узла, вверх = +
            public float ClosePx;  // высота в конце узла
            public float HighPx;   // фитиль вверх
            public float LowPx;    // фитиль вниз
            public CandleTerrainProfile.Regime Regime;
            public bool Up => ClosePx >= OpenPx;
        }

        public sealed class Result
        {
            public TrackProfile Profile;
            public List<Candle> Candles = new List<Candle>();
        }

        /// <summary>
        /// Детерминированный поток. Свой, а не System.Random: нужен воспроизводимый
        /// результат от семени НЕЗАВИСИМО от версии рантайма, иначе «та же трасса»
        /// перестанет быть той же при обновлении Unity.
        /// </summary>
        private struct Rng
        {
            private uint _s;
            public Rng(int seed) { _s = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed); }

            public float Next()
            {
                // xorshift32 — короткий, быстрый, с достаточным периодом для трассы.
                _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
                return (_s & 0xFFFFFF) / 16777216f;
            }

            public float Range(float a, float b) => a + (b - a) * Next();
        }

        public static Result Generate(CandleTerrainProfile tune, int seed, int nodeCount)
        {
            if (tune == null) throw new ArgumentNullException(nameof(tune));

            var rng = new Rng(seed);
            var res = new Result();
            var closes = new List<float>(nodeCount);

            const float H = WorldHeightPx;
            var baseY = H * 0.5f;

            // Состояние генератора — ровно те переменные, что в исходнике.
            var walk = 0f;
            var close = baseY;
            var regime = CandleTerrainProfile.Regime.Flat;
            var lastRegime = CandleTerrainProfile.Regime.Flat;
            var regimeDrift = 0f;
            var regimeVol = 0f;
            var regimeLeft = 0f;

            for (var i = 0; i < nodeCount; i++)
            {
                var genX = i * StepPx;
                // Рампа сложности по пройденному пути: глубже — волатильнее и круче.
                var d = Mathf.Clamp01(genX / Mathf.Max(1f, tune.difficultyRampM * 10f));
                var volK = 1f + d * tune.volatilityRampGain;

                if (regimeLeft <= 0f)
                {
                    PickRegime(tune, ref rng, volK, ref regime, ref lastRegime,
                        ref regimeDrift, ref regimeVol, ref regimeLeft);
                }
                regimeLeft--;

                var prevC = close;

                // Макро-тренд: блуждание с дрейфом режима плюс его пила.
                walk += regimeDrift * 0.5f + (rng.Next() - 0.5f) * regimeVol * 0.5f;
                walk = Mathf.Clamp(walk, -H * 0.40f, H * 0.34f);

                // Крутизна нарастает от начала трассы: пологий вход, полная крутизна вглубь.
                var steepNow = Mathf.Lerp(tune.steepEarly, tune.terrainSteep,
                    Mathf.Clamp01(genX / Mathf.Max(1f, tune.steepRampM * 10f)));

                // Двухоктавные холмы. Режим правит их амплитудой: боковик = передышка,
                // рок-гарден ниже основанием под дребезг, памп и обвал — резче.
                var regimeRoll =
                    regime == CandleTerrainProfile.Regime.Flat ? 0.22f :
                    regime == CandleTerrainProfile.Regime.Rocks ? 0.5f :
                    regime == CandleTerrainProfile.Regime.Pump ||
                    regime == CandleTerrainProfile.Regime.Crash ? 0.6f : 1f;

                const float hillHeight = 40f;
                var dd = Mathf.Clamp01(genX / 18000f);
                var roll = (Mathf.Sin(genX * 0.0042f) * hillHeight
                            + Mathf.Sin(genX * 0.0105f + 0.6f) * (hillHeight * 0.45f))
                           * (1f + dd * 0.25f) * regimeRoll * steepNow;

                close = Mathf.Clamp(baseY + walk + roll, 70f, H * 0.95f);

                // Рок-гарден: дребезг ДВУМЯ непрерывными синусами, а не случайными скачками —
                // так он трясёт баланс, оставаясь проезжаемым.
                if (regime == CandleTerrainProfile.Regime.Rocks)
                {
                    const float rockSize = 5f;
                    close = Mathf.Clamp(close + Mathf.Sin(genX * 0.085f) * rockSize
                                        + Mathf.Sin(genX * 0.20f + 1.3f) * rockSize * 0.5f,
                        70f, H * 0.95f);
                }

                // Пологий стартовый заезд: первые 16 узлов подтягиваются к базе.
                if (i < 16)
                {
                    var e = i / 16f;
                    close = Mathf.Lerp(baseY, close, e * e);
                    walk *= 0.7f;
                }

                // КЭП КРУТИЗНЫ ПОДЪЁМА. В экранных координатах подъём = close МЕНЬШЕ
                // предыдущего. walk пересчитывается обязательно: иначе генератор копит
                // непоказанный долг и выдаёт его стеной через несколько узлов.
                var maxRise = Mathf.Tan(tune.climbCapRad) * StepPx;
                if (prevC - close > maxRise)
                {
                    close = prevC - maxRise;
                    walk = close - baseY - roll;
                }

                closes.Add(close);

                // Свеча узла. Фитили — небольшой выброс за тело, пропорциональный пиле
                // режима: на волатильном рынке тени длиннее, и это видно глазом.
                var openUp = -(prevC - baseY);
                var closeUp = -(close - baseY);
                var wick = Mathf.Max(2f, regimeVol * 0.9f);
                res.Candles.Add(new Candle
                {
                    OpenPx = openUp,
                    ClosePx = closeUp,
                    HighPx = Mathf.Max(openUp, closeUp) + wick * rng.Range(0.2f, 1f),
                    LowPx = Mathf.Min(openUp, closeUp) - wick * rng.Range(0.2f, 1f),
                    Regime = regime
                });
            }

            // Перевод в профиль трассы: у нас высота вверх положительная, у исходника вниз.
            var nodes = new Vector2[closes.Count];
            for (var i = 0; i < closes.Count; i++)
                nodes[i] = new Vector2(i * StepPx, -(closes[i] - baseY));

            var profile = ScriptableObject.CreateInstance<TrackProfile>();
            profile.name = "CandleTrack_" + seed;
            profile.nodesPx = nodes;
            profile.gapsPx = new TrackProfile.Gap[0];
            profile.endPx = (closes.Count - 1) * StepPx;
            profile.nodeStepPx = StepPx;
            profile.checkpoints = BuildCheckpoints(profile.endPx);
            profile.flowBoostEnabled = false;
            res.Profile = profile;
            return res;
        }

        /// <summary>
        /// Лотерея режимов. Главное здесь — ПРИНУДИТЕЛЬНЫЙ flat после каждого тяжёлого
        /// режима. Это правило структуры трассы: после любой фичи идёт ровный разгон,
        /// на котором можно подготовиться. Потерять его при переносе значило бы получить
        /// цепочку обрывов подряд без передышки.
        /// </summary>
        private static void PickRegime(CandleTerrainProfile tune, ref Rng rng, float volK,
            ref CandleTerrainProfile.Regime regime, ref CandleTerrainProfile.Regime lastRegime,
            ref float drift, ref float vol, ref float left)
        {
            var hard = lastRegime == CandleTerrainProfile.Regime.Bull
                       || lastRegime == CandleTerrainProfile.Regime.Bear
                       || lastRegime == CandleTerrainProfile.Regime.Pump
                       || lastRegime == CandleTerrainProfile.Regime.Crash
                       || lastRegime == CandleTerrainProfile.Regime.Rocks;

            if (hard)
            {
                regime = CandleTerrainProfile.Regime.Flat;
                drift = (rng.Next() - 0.5f) * 3f * tune.calmFlat;
                vol = 4f * volK * tune.calmFlat;
                left = 6f + rng.Next() * 5f;
                lastRegime = regime;
                return;
            }

            var r = rng.Next();
            if (r < tune.rockGardenShare)
            {
                regime = CandleTerrainProfile.Regime.Rocks;
                drift = (rng.Next() - 0.5f) * 5f;
                vol = 4f * volK * tune.volatilityJag;
                left = 9f + rng.Next() * 7f;
            }
            else
            {
                var r2 = rng.Next();
                if (r2 < 0.42f)
                {
                    regime = CandleTerrainProfile.Regime.Bull;
                    drift = -(16f + rng.Next() * 13f) * tune.mountain * tune.pumpSteep;
                    vol = 5f * volK;
                    left = 11f + rng.Next() * 11f;
                }
                else if (r2 < 0.72f)
                {
                    regime = CandleTerrainProfile.Regime.Bear;
                    drift = (15f + rng.Next() * 12f) * tune.mountain * tune.dumpSteep;
                    vol = 5f * volK;
                    left = 10f + rng.Next() * 11f;
                }
                else if (r2 < 0.88f)
                {
                    regime = CandleTerrainProfile.Regime.Pump;
                    drift = -(24f + rng.Next() * 10f) * tune.mountain * tune.pumpSteep;
                    vol = 5f * volK * tune.volatilityJag;
                    left = 6f + rng.Next() * 5f;
                }
                else
                {
                    regime = CandleTerrainProfile.Regime.Crash;
                    drift = (22f + rng.Next() * 10f) * tune.dumpSteep;
                    vol = 8f * volK * tune.volatilityJag;
                    left = 6f + rng.Next() * 5f;
                }
            }
            lastRegime = regime;
        }

        private static TrackProfile.Checkpoint[] BuildCheckpoints(float endPx)
        {
            var list = new List<TrackProfile.Checkpoint>();
            // Чекпоинт примерно каждые 40 метров пути: рестарт не должен возвращать
            // к уже освоенному куску, иначе проверка фила превращается в проверку терпения.
            var stride = 40f / Tuning.UnitsContract.PxToM;
            for (var x = stride; x < endPx - stride * 0.5f; x += stride)
                list.Add(new TrackProfile.Checkpoint { xPx = x, name = "чекпоинт" });
            return list.ToArray();
        }
    }
}
