using System;
using System.Collections.Generic;
using System.Linq;
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
    /// ПРЕПЯТСТВИЯ. Перенесены ЧЕТЫРЕ, и ровно те, что включены в тюнинге исходника:
    /// гэп (`gapEvery 300`), кикер (`kickEvery 270`), крутой подъём (`climbEvery 420`) и
    /// вупсы (`whoopEvery 380`). Уступы и обрывы там стоят с `Every: 0`, то есть автором
    /// выключены — переносить их значило бы добавить в игру то, чего он в ней не хотел.
    /// Мега-рампа только в режиме «Отрыв», которого здесь пока нет. Грязь требует
    /// `mudRollResistance`, а он не реализован.
    ///
    /// Каждое препятствие — машина состояний, потребляющая узлы, и у КАЖДОГО есть разгон
    /// перед фичей. Это то же правило структуры, что и принудительный flat: игрок обязан
    /// иметь возможность подготовиться, иначе сложность превращается в лотерею.
    ///
    /// СОБЫТИЯ РЫНКА. Перенесены ДВА из трёх: `rally` (памп-ралли — затяжной зелёный
    /// подъём) и `flash` (флеш-крах — пол уходит из-под колёс обрывом). Третье, `whale`,
    /// НЕ перенесено намеренно: в исходнике его рельеф — обычная земля, а всю драму делает
    /// догоняющая волна, механики которой здесь нет. Событие, которое ничего не меняет,
    /// хуже отсутствующего: оно обещает игроку то, чего не произойдёт.
    ///
    /// События ПЕРЕБИВАЮТ и рельеф, и препятствия — как в исходнике (`if(!isEv)` вокруг
    /// машин фич). Смысл в том, что событие — это сет-пьеса, а не добавка поверх шума.
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

            /// <summary>
            /// Индексы узлов, лежащих внутри ПРОВАЛА. Нужны анализу честности: стенка
            /// провала — это не подъём, который надо заехать, а край, который перелетают.
            /// Без этого списка гейт крутизны меряет вертикальную стенку ямы как склон
            /// и справедливо, но бессмысленно ругается на 87°.
            /// </summary>
            public List<int> GapNodes = new List<int>();
            /// <summary>Узлы-липы (кромка кикера / рампы гэпа / мега-рампы).</summary>
            public List<int> KickNodes = new List<int>();

            /// <summary>Участки событий рынка: где начинается и кончается сет-пьеса.</summary>
            public List<EventSpan> Events = new List<EventSpan>();

            /// <summary>
            /// Какая ветка генератора поставила каждый узел. Нужен только диагностике:
            /// когда гейт честности ловит выброс крутизны, вопрос «откуда он» решается
            /// чтением этого списка, а не рассуждением о вероятных причинах.
            /// </summary>
            public List<string> NodeSource = new List<string>();
        }

        /// <summary>Тип рыночного события.</summary>
        public enum MarketEvent
        {
            None,
            /// <summary>Памп-ралли: затяжной зелёный подъём.</summary>
            Rally,
            /// <summary>Флеш-крах: пол уходит из-под колёс обрывом.</summary>
            Flash
        }

        public struct EventSpan
        {
            public MarketEvent Type;
            public int FromNode;
            public int ToNode;

            public string Title => Type == MarketEvent.Rally ? "ПАМП-РАЛЛИ" : "ФЛЕШ-КРАХ";
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

        public static Result Generate(CandleTerrainProfile tune, int seed, int nodeCount, bool endless = false)
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

            // ---- состояние машин препятствий (имена как в исходнике) ----
            float gapRunup = 0, gapRampLeft = 0, gapLeft = 0, gapLandFlat = 0;
            float gapPrevY = 0, gapRampBot = 0, gapLandTopY = 0, gapBaseY = 0, gapStepNow = 0;
            float kickRunup = 0, kickRampLeft = 0, kickLandLeft = 0;
            float kickBaseY = 0, kickRampBot = 0, kickLipY = 0, kickLandTopY = 0;
            float climbRunup = 0, climbLeft = 0, climbBaseY = 0, climbBotY = 0;
            float whoopRunup = 0, whoopLeft = 0, whoopBaseY = 0, whoopPhase = 0;
            float nextGapX = 1800f, nextKickX = 1700f, nextClimbX = 2400f, nextWhoopX = 2200f;
            // b234/b246 МЕГА-РАМПА — только в «Отрыве» (TUNE.megaEvery 560, runup 7×20, рампа 7 узлов
            // 104 px curve 2.6, gapDrop 44, посадка 16 узлов по 12): кинематографичный полёт ∝ скорости.
            float megaRunup = 0, megaRampLeft = 0, megaLandLeft = 0;
            float megaBaseY = 0, megaRampBot = 0, megaLipY = 0, megaLandTopY = 0;
            var nextMegaX = 3000f;
            var sharp = new List<int>();

            // ---- события рынка ----
            var evType = MarketEvent.None;
            var evLeft = 0;
            var evTotal = 0;
            var nextEvX = 2600f;
            var evStartNode = 0;

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

                // Идёт ли сейчас препятствие. Вычисляется ДО планирования события,
                // потому что событие не имеет права начаться посреди фичи.
                var featureBusy = gapRunup > 0 || gapRampLeft > 0 || gapLeft > 0 || gapLandFlat > 0
                                  || kickRunup > 0 || kickRampLeft > 0 || kickLandLeft > 0
                                  || climbRunup > 0 || climbLeft > 0
                                  || whoopRunup > 0 || whoopLeft > 0
                                  || megaRunup > 0 || megaRampLeft > 0 || megaLandLeft > 0;

                // ---- планирование события ----
                // Событие начинается по расписанию и перебивает всё остальное на свою
                // длину. Зона выбирается по пройденному пути: каждые 700 м «настроение»
                // рынка меняется, поэтому эйфория и медвежий цикл идут волнами, а не
                // ровной лотереей.
                // Условие `!featureBusy` — не косметика, а починка унаследованного дефекта.
                // Машины препятствий пропускаются во время события (как и в исходнике,
                // где они стоят внутри `if(!isEv)`), поэтому событие, начавшееся посреди
                // фичи, ЗАМОРАЖИВАЕТ её счётчики, а после события фича продолжается от
                // базовой высоты, снятой давно и на другой высоте. Замерено гейтом:
                // подъём 81.4° одним узлом ровно на границе flash → whoop, одинаково на
                // четырёх семенах из четырёх.
                if (evLeft <= 0 && !featureBusy && i > 40 && genX >= nextEvX)
                {
                    var zone = Mathf.FloorToInt(genX / 7000f) % 5;
                    evType = PickEvent(zone, ref rng);
                    evTotal = evType == MarketEvent.Rally ? 17 : 13;
                    evLeft = evTotal;
                    evStartNode = i;
                }

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

                var tag = "natural";

                // ================= СОБЫТИЕ =================
                //
                // Перебивает и рельеф, и препятствия: сет-пьеса, а не добавка поверх шума.
                if (evLeft > 0)
                {
                    var kk = evTotal - evLeft;
                    if (evType == MarketEvent.Rally)
                    {
                        // Бодрый, но заезжаемый зелёный подъём (~22°): вкатываешься на
                        // мощности, у вершины приходится дозировать.
                        close = Mathf.Clamp(prevC - (8f + rng.Next() * 5f), 78f, H * 0.92f);
                    }
                    else
                    {
                        // ФЛЕШ-КРАХ: на втором узле пол уходит обрывом, дальше дрожь.
                        close = kk == 1
                            ? Mathf.Clamp(prevC + (96f + rng.Next() * 40f), 90f, H * 0.95f)
                            : Mathf.Clamp(prevC + (rng.Next() - 0.5f) * 14f, 90f, H * 0.95f);
                        if (kk == 1) sharp.Add(i);   // кромка обрыва не сглаживается
                    }
                    walk = close - baseY;
                    evLeft--;
                    if (evLeft <= 0)
                    {
                        res.Events.Add(new EventSpan
                        {
                            Type = evType,
                            FromNode = evStartNode,
                            ToNode = i
                        });
                        nextEvX = genX + (evType == MarketEvent.Rally ? 2100f : 2900f)
                                  + rng.Next() * 900f;
                        evType = MarketEvent.None;
                    }
                    closes.Add(close);
                    res.NodeSource.Add(evType == MarketEvent.Rally ? "rally" : "flash");
                    AddCandle(res, prevC, close, baseY, regimeVol, regime, ref rng);
                    continue;
                }

                // ================= ПРЕПЯТСТВИЯ =================
                //
                // Порядок веток и все числа — из исходника (стр. 202-236). Ветки взаимно
                // исключающие: пока идёт одна фича, следующая не планируется, иначе они
                // накладываются и рельеф становится непроходимым.
                var densK = 1f - Mathf.Clamp01(genX / 12500f) * 0.42f;
                var busy = featureBusy;

                if (gapRunup > 0)
                {
                    tag = "gapRunup";
                    // РАЗГОН-СПУСК: набрать скорость перед рампой.
                    var kk = 5 - gapRunup;
                    close = Mathf.Clamp(gapPrevY + kk * 13f, 70f, H * 0.82f);
                    walk = close - baseY;
                    gapRunup--;
                    if (gapRunup <= 0) { gapRampBot = close; gapRampLeft = 5; }
                }
                else if (gapRampLeft > 0)
                {
                    tag = "gapRamp";
                    // ВОГНУТЫЙ ЛИП: смещение растёт как степень 2.0 — гладкая база к кромке,
                    // поэтому вылет ЭМЕРДЖЕНТНЫЙ (∝ скорости), а не скриптовый пуск.
                    var kk = 5 - gapRampLeft;
                    var off = 80f * Mathf.Pow((kk + 1f) / 5f, 2.0f);
                    close = Mathf.Clamp(gapRampBot - off, 70f, H * 0.9f);
                    walk = close - baseY;
                    if (gapRampLeft <= 1) { sharp.Add(i); res.KickNodes.Add(i); }   // кромка липа не сглаживается; лип = импульс вращения
                    gapRampLeft--;
                    if (gapRampLeft <= 0)
                    {
                        gapLeft = gapStepNow > 0 ? 1 : 2;
                        gapLandTopY = Mathf.Clamp(gapPrevY - gapStepNow, 70f, H * 0.9f);
                        gapBaseY = gapLandTopY;
                    }
                }
                else if (gapLeft > 0)
                {
                    tag = "gapPit";
                    // ПРОВАЛ. Обычный — глубокий (медленно = не долетел). Step-up — мелкий
                    // дип, чтобы недолёт не был мгновенной смертью о стену.
                    close = gapStepNow > 0
                        ? Mathf.Clamp(gapLandTopY + 50f, 70f, H * 0.95f)
                        : H * 0.99f;
                    sharp.Add(i);
                    // Узлы ямы и по одному с каждой стороны: стенки принадлежат провалу,
                    // а не рельефу, и мерить их крутизну как склон бессмысленно.
                    res.GapNodes.Add(i - 1);
                    res.GapNodes.Add(i);
                    res.GapNodes.Add(i + 1);
                    gapLeft--;
                    if (gapLeft <= 0) { walk = gapBaseY - baseY; gapLandFlat = 10; }
                }
                else if (gapLandFlat > 0)
                {
                    tag = "gapLand";
                    // ВСТРЕЧНЫЙ ДОВНСЛОП: ловит падающую дугу по касательной, а не плоским
                    // ударом. Щедрый — прощает разброс скорости.
                    var kk = 10 - gapLandFlat;
                    close = Mathf.Clamp(gapLandTopY + kk * 10f, 70f, H * 0.9f);
                    walk = close - baseY;
                    gapLandFlat--;
                }
                else if (kickRunup > 0)
                {
                    tag = "kickRunup";
                    var kk = 6 - kickRunup;
                    close = Mathf.Clamp(kickBaseY + kk * 12f, 70f, H * 0.88f);
                    walk = close - baseY;
                    kickRunup--;
                    if (kickRunup <= 0) { kickRampBot = close; kickRampLeft = 5; }
                }
                else if (kickRampLeft > 0)
                {
                    tag = "kickRamp";
                    var kk = 5 - kickRampLeft;
                    var off = 62f * Mathf.Pow((kk + 1f) / 5f, 2.2f);
                    close = Mathf.Clamp(kickRampBot - off, 70f, H * 0.95f);
                    walk = close - baseY;
                    if (kickRampLeft <= 1) { sharp.Add(i); kickLipY = close; res.KickNodes.Add(i); }
                    kickRampLeft--;
                    if (kickRampLeft <= 0)
                    {
                        kickLandLeft = 9;
                        kickLandTopY = Mathf.Clamp(kickLipY + 20f, 70f, H * 0.9f);
                    }
                }
                else if (kickLandLeft > 0)
                {
                    tag = "kickLand";
                    var kk = 9 - kickLandLeft;
                    close = Mathf.Clamp(kickLandTopY + kk * 8f, 70f, H * 0.9f);
                    walk = close - baseY;
                    kickLandLeft--;
                }
                else if (climbRunup > 0)
                {
                    tag = "climbRunup";
                    var kk = 6 - climbRunup;
                    close = Mathf.Clamp(climbBaseY + kk * 13f, 70f, H * 0.86f);
                    walk = close - baseY;
                    climbRunup--;
                    if (climbRunup <= 0) { climbBotY = close; climbLeft = 6; }
                }
                else if (climbLeft > 0)
                {
                    tag = "climb";
                    // ЕСТЕСТВЕННЫЙ ХОЛМ по S-кривой: пологий вход, крутая середина, пологий
                    // гребень. Линейный подъём давал угловатые изломы низа и верха.
                    var pp = (6f - climbLeft + 1f) / 6f;
                    var e = pp * pp * (3f - 2f * pp);
                    close = Mathf.Clamp(climbBotY - e * 6f * 15f, 70f, H * 0.95f);
                    walk = close - baseY;
                    climbLeft--;
                }
                else if (megaRunup > 0)
                {
                    tag = "megaRunup";
                    close = Mathf.Clamp(megaBaseY + (7f - megaRunup) * 20f, 70f, H * 0.92f);
                    walk = close - baseY;
                    megaRunup--;
                    if (megaRunup <= 0) { megaRampBot = close; megaRampLeft = 7; }
                }
                else if (megaRampLeft > 0)
                {
                    tag = "megaRamp";
                    var kk = 7 - megaRampLeft;
                    var off = 104f * Mathf.Pow((kk + 1f) / 7f, 2.6f);
                    close = Mathf.Clamp(megaRampBot - off, 70f, H * 0.95f);
                    walk = close - baseY;
                    if (megaRampLeft <= 1) { sharp.Add(i); megaLipY = close; res.KickNodes.Add(i); }
                    megaRampLeft--;
                    if (megaRampLeft <= 0)
                    {
                        megaLandLeft = 16;
                        megaLandTopY = Mathf.Clamp(megaLipY + 44f, 70f, H * 0.95f);
                    }
                }
                else if (megaLandLeft > 0)
                {
                    tag = "megaLand";
                    var kk = 16 - megaLandLeft;
                    close = Mathf.Clamp(megaLandTopY + kk * 12f, 70f, H * 0.95f);
                    walk = close - baseY;
                    megaLandLeft--;
                }
                else if (whoopRunup > 0)
                {
                    tag = "whoopRunup";
                    close = Mathf.Clamp(whoopBaseY, 70f, H * 0.86f);
                    walk = close - baseY;
                    whoopRunup--;
                    if (whoopRunup <= 0) { whoopLeft = 7; whoopPhase = 0f; }
                }
                else if (whoopLeft > 0)
                {
                    tag = "whoop";
                    // РИТМ-СЕКЦИЯ: на скорости срезаешь гребни и ловишь мелкий воздух,
                    // медленно — переваливаешься. Ямы нет, значит и смерти нет: динамика.
                    whoopPhase += 2.3f;
                    close = Mathf.Clamp(whoopBaseY - Mathf.Sin(whoopPhase) * 15f, 70f, H * 0.88f);
                    walk = close - baseY;
                    whoopLeft--;
                }
                // ПРОВЕРКА МЕСТА СНИЗУ, выведенная арифметикой, а не подобранная.
                //
                // У кикера и подъёма в исходнике есть условие `close > H*0.5` — «нужно
                // место СВЕРХУ под рампу». Симметричного условия снизу там нет, и оно
                // необходимо: у каждого разгона-спуска свой кламп, и запуск фичи слишком
                // глубоко мгновенно ДЁРГАЕТ землю вверх до этого клампа. Гейт честности
                // ловил это дважды: сначала 68.4° у гэпа, потом 55.8° у кикера.
                //
                // Условие: close + (глубина разгона) ≤ (кламп разгона).
                //   гэп:    5 узлов × 13 = 52,  кламп H·0.82 = 656 → close ≤ 604 = H·0.755
                //   кикер:  6 узлов × 12 = 60,  кламп H·0.88 = 704 → close ≤ 644 = H·0.805
                //   подъём: 6 узлов × 13 = 65,  кламп H·0.86 = 688 → close ≤ 623 = H·0.779
                //   вупсы:  разгон плоский,     кламп H·0.86 = 688 → close ≤ 688 = H·0.86
                else if (!busy && i > 20 && genX >= nextGapX && genX > 2600f
                         && close < H * 0.75f
                         && regime != CandleTerrainProfile.Regime.Pump
                         && regime != CandleTerrainProfile.Regime.Crash)
                {
                    gapPrevY = close;
                    gapRunup = 5;
                    gapStepNow = rng.Next() < 0.4f ? 20f : 0f;
                    nextGapX = genX + 3000f + rng.Next() * 1400f;
                }
                else if (!busy && i > 16 && genX >= nextKickX && genX > 1700f
                         && close > H * 0.5f && close < H * 0.80f
                         && regime != CandleTerrainProfile.Regime.Pump
                         && regime != CandleTerrainProfile.Regime.Crash)
                {
                    kickBaseY = close;
                    kickRunup = 6;
                    nextKickX = genX + 2700f * densK + rng.Next() * 1000f * densK;
                }
                else if (!busy && i > 16 && genX >= nextClimbX && genX > 3600f
                         && close > H * 0.52f && close < H * 0.77f
                         && regime != CandleTerrainProfile.Regime.Pump
                         && regime != CandleTerrainProfile.Regime.Crash)
                {
                    climbBaseY = close;
                    climbRunup = 6;
                    nextClimbX = genX + 4200f * densK + rng.Next() * 1200f * densK;
                }
                else if (endless && !busy && i > 16 && genX >= nextMegaX && genX > 2800f
                         && close > H * 0.44f
                         && regime != CandleTerrainProfile.Regime.Pump
                         && regime != CandleTerrainProfile.Regime.Crash
                         && lastRegime != CandleTerrainProfile.Regime.Bull)
                {
                    megaBaseY = close;
                    megaRunup = 7;
                    nextMegaX = genX + 5600f + rng.Next() * 1800f;
                }
                else if (!busy && i > 16 && genX >= nextWhoopX && genX > 1900f
                         && close < H * 0.86f
                         && regime != CandleTerrainProfile.Regime.Pump
                         && regime != CandleTerrainProfile.Regime.Crash)
                {
                    whoopBaseY = close;
                    whoopRunup = 3;
                    nextWhoopX = genX + 3800f * densK + rng.Next() * 900f * densK;
                }

                closes.Add(close);
                res.NodeSource.Add(tag);

                AddCandle(res, prevC, close, baseY, regimeVol, regime, ref rng);
            }

            // Перевод в профиль трассы: у нас высота вверх положительная, у исходника вниз.
            var nodes = new Vector2[closes.Count];
            // b68 СГЛАЖИВАНИЕ ЛИНИИ ЕЗДЫ (chartrider.html:249): причинный 5-тап [0.10 0.22 0.36 0.22 0.10]
            // по узлу n−3 при добавлении узла n, пропуская окна с гэпом/подъёмом/мега-рампой и сам лип.
            // Без него вупсы (±15 px через узел) — пила периодом 26 px, в которой байк с базой 52 px
            // клинит намертво (замер -proc -trace: остановка на 2375 px); в исходнике после блюра
            // это лёгкая рябь ~2 px.
            var kickSet = new HashSet<int>(res.KickNodes);
            bool Skip(int k) { var t = res.NodeSource[k]; return t == "gapPit" || t == "climb" || t == "megaRamp"; }
            for (var n = 5; n <= closes.Count; n++)
            {
                var w2 = n - 3;
                var skip = kickSet.Contains(w2);
                for (var k = n - 5; k < n && !skip; k++) skip = Skip(k);
                if (skip) continue;
                closes[w2] = closes[n - 5] * 0.10f + closes[n - 4] * 0.22f + closes[w2] * 0.36f + closes[n - 2] * 0.22f + closes[n - 1] * 0.10f;
            }
            for (var i = 0; i < closes.Count; i++)
                nodes[i] = new Vector2(i * StepPx, -(closes[i] - baseY));

            var profile = ScriptableObject.CreateInstance<TrackProfile>();
            profile.name = "CandleTrack_" + seed;
            profile.nodesPx = nodes;
            // Провалы гэпов: диапазоны по узлам pit (в исходнике gap:true → падение ниже кромки на 64 px = смерть).
            var gaps = new List<TrackProfile.Gap>();
            var gs = res.GapNodes.Distinct().OrderBy(v => v).ToList();
            for (var gi = 0; gi < gs.Count;)
            {
                var a = gs[gi]; var b = a;
                while (gi + 1 < gs.Count && gs[gi + 1] == b + 1) { gi++; b = gs[gi]; }
                gaps.Add(new TrackProfile.Gap { fromPx = a * StepPx, toPx = b * StepPx });
                gi++;
            }
            profile.gapsPx = gaps.ToArray();
            profile.kickNodeIndices = res.KickNodes.Distinct().OrderBy(v => v).ToArray();
            profile.proc = true;
            profile.endPx = (closes.Count - 1) * StepPx;
            profile.nodeStepPx = StepPx;
            profile.checkpoints = BuildCheckpoints(profile.endPx);
            // Кромки липов и края провалов НЕ сглаживаются: монотонная кубика гасит
            // касательные на локальных максимумах, и острый лип превратился бы в бугор,
            // а провал — в пологую ямку. Фича исчезла бы, оставшись в коде.
            profile.sharpNodeIndices = sharp.ToArray();
            profile.flowBoostEnabled = false;
            res.Profile = profile;
            return res;
        }

        /// <summary>
        /// Свеча узла. Фитили — выброс за тело, пропорциональный пиле режима: на
        /// волатильном рынке тени длиннее, и это видно глазом.
        /// </summary>
        private static void AddCandle(Result res, float prevC, float close, float baseY,
            float regimeVol, CandleTerrainProfile.Regime regime, ref Rng rng)
        {
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

        /// <summary>
        /// Выбор события по зоне пути. Порт `pickEvent`: в зоне эйфории чаще памп,
        /// в медвежьей — чаще обвал. `whale` заменён на `flash`, потому что его рельеф
        /// в исходнике обычный, а драму делает догоняющая волна, которой здесь нет.
        /// </summary>
        private static MarketEvent PickEvent(int zone, ref Rng rng)
        {
            var r = rng.Next();
            if (zone == 2) return r < 0.72f ? MarketEvent.Rally : MarketEvent.Flash;
            if (zone == 4) return r < 0.70f ? MarketEvent.Flash : MarketEvent.Rally;
            return r < 0.5f ? MarketEvent.Rally : MarketEvent.Flash;
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
