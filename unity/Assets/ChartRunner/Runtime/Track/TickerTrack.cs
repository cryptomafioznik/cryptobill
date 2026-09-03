using System.Collections.Generic;
using ChartRunner.Meta;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Track
{
    /// <summary>
    /// РЕАЛЬНАЯ СВЕЧА → УЗЕЛ ТРАССЫ. Порт buildTickerTrack (b96-b182), логика доказана на
    /// реальных данных годами. Каждая свеча = 3 подузла (SUB), ride-линия интерполируется
    /// внутри свечи, тело (open/hi/lo) живёт на HEAD-узле.
    ///
    /// Масштаб — из диапазона цен × bandFrac (∝ волатильности, b111/b182). Кап крутизны
    /// подъёма (b146: ни одна свеча не должна быть непролазной стеной — иначе смерть
    /// бинарна и прокачка косметична) и анти-клин (b149: V-ловушка уже колёсной базы).
    ///
    /// Рампы на пампах здесь НЕ лепятся: решение пользователя ph4 «Забег чистый» —
    /// фейк-трамплины на реальный график не ставим.
    ///
    /// Единицы: узлы в px исходника (кадр 430×932, STEP=26, baseY=H*0.6). Профиль
    /// хранит высоты «вверх = +», поэтому y исходника переворачивается: h = baseY − y.
    /// </summary>
    public sealed class TickerTrack
    {
        public const int Sub = 3;
        public const float StepPx = 26f;
        public const float ClimbCap = 0.9f; // TICKER_CLIMBCAP

        private const float W = 430f, H = 932f;
        private const float BaseY = H * 0.6f;

        public TrackProfile Profile;
        public List<CandleTrackGenerator.Candle> DeckCandles;
        /// <summary>Узлы с монетой (индекс узла профиля), b283: 30 % head-узлов.</summary>
        public List<int> CoinNodes;
        public float VolMul, Vol;
        public string VolLabel;
        public bool Short;
        /// <summary>Процедурный «Отрыв»: цены нет, PriceAt = 1.</summary>
        public bool IsProc;

        // Нормализатор Y↔цена (b359): tkK, tkMid, tkFlipP.
        private float _k, _mid, _flipP;
        private readonly List<float> _ys = new List<float>(); // y исходника по узлам (вниз +)

        /// <summary>Реальная цена монеты в точке трассы (b887 tkPrice).</summary>
        public float PriceAt(float xM)
        {
            if (IsProc || _ys.Count < 2) return 1f;
            var xPx = xM / UnitsContract.PxToM;
            var i = Mathf.Clamp(Mathf.FloorToInt(xPx / StepPx), 0, _ys.Count - 2);
            var t = Mathf.Clamp01(xPx / StepPx - i);
            var y = Mathf.Lerp(_ys[i], _ys[i + 1], t);
            var g2 = _mid - (y - BaseY) / _k;
            return _flipP != 0f ? _flipP - g2 : g2;
        }

        /// <summary>Режим «Отрыв» исходника (trackSource='proc'): трасса из генератора рельефа, монеты
        /// на 20 % узлов вне гэпов (chartrider.html:246), волатильность 1, без цены и плеча.</summary>
        public static TickerTrack FromProc(CandleTrackGenerator.Result res, int seed)
        {
            var tt = new TickerTrack { IsProc = true, Profile = res.Profile, DeckCandles = res.Candles, VolMul = 1f, Vol = 0f, VolLabel = "ОТРЫВ", Short = false };
            var rng = new System.Random(seed);
            var gapSet = new HashSet<int>(res.GapNodes);
            tt.CoinNodes = new List<int>();
            for (var i = 12; i < res.Profile.nodesPx.Length - 4; i++)
                if (!gapSet.Contains(i) && rng.NextDouble() < 0.2) tt.CoinNodes.Add(i);
            return tt;
        }

        public static TickerTrack Build(List<Tickers.Candle> src, bool shortRun, int seed)
        {
            var tt = new TickerTrack { Short = shortRun };
            var candles = new List<Tickers.Candle>(src);

            // b177 ШОРТ: зеркалим свечи относительно середины диапазона (hi↔lo).
            if (shortRun)
            {
                float fmn = float.MaxValue, fmx = float.MinValue;
                foreach (var c in candles) { fmn = Mathf.Min(fmn, c.L); fmx = Mathf.Max(fmx, c.H); }
                tt._flipP = fmn + fmx;
                for (var i = 0; i < candles.Count; i++)
                {
                    var c = candles[i];
                    candles[i] = new Tickers.Candle { O = tt._flipP - c.O, H = tt._flipP - c.L, L = tt._flipP - c.H, C = tt._flipP - c.C };
                }
            }

            float pMin = float.MaxValue, pMax = float.MinValue;
            foreach (var c in candles) { pMin = Mathf.Min(pMin, c.L); pMax = Mathf.Max(pMax, c.H); }

            tt.Vol = Tickers.Volatility(candles);
            tt.VolMul = Tickers.VolMulOf(tt.Vol);
            tt.VolLabel = Tickers.VolTier(tt.Vol);
            var bandFrac = Mathf.Clamp(0.40f + 0.34f * (tt.VolMul - 0.85f), 0.40f, 0.76f);
            tt._mid = (pMin + pMax) * 0.5f;
            tt._k = H * bandFrac / Mathf.Max(1e-9f, pMax - pMin);
            float Yc(float p) => Mathf.Clamp(BaseY + (tt._mid - p) * tt._k, 56f, H * 0.94f);

            var rng = new System.Random(seed);
            var ys = tt._ys;
            var opens = new List<float>(); var his = new List<float>(); var los = new List<float>();
            var heads = new List<bool>(); var ups = new List<bool>();
            tt.CoinNodes = new List<int>();

            var prevC = candles.Count > 0 ? candles[0].O : tt._mid;
            for (var i = 0; i < candles.Count; i++)
            {
                var c = candles[i];
                for (var s = 1; s <= Sub; s++)
                {
                    var t = s / (float)Sub; var head = s == Sub;
                    var cI = prevC + (c.C - prevC) * t;
                    var y = Yc(cI);
                    ys.Add(y);
                    opens.Add(head ? Yc(c.O) : y); his.Add(head ? Yc(c.H) : y); los.Add(head ? Yc(c.L) : y);
                    heads.Add(head); ups.Add(c.C > c.O);
                    if (head && rng.NextDouble() < 0.30) tt.CoinNodes.Add(ys.Count - 1);
                }
                prevC = c.C;
            }

            // b146 кап крутизны подъёма (climb = y уменьшается).
            var maxRise = StepPx * ClimbCap;
            for (var i = 1; i < ys.Count; i++)
                if (ys[i] < ys[i - 1] - maxRise) ys[i] = ys[i - 1] - maxRise;

            // b149 анти-клин: спуск → сразу две кап-крутые ступени = V-ловушка уже базы.
            for (var i = 2; i < ys.Count - 3; i++)
            {
                var dn = ys[i] - ys[i - 1]; var r1 = ys[i] - ys[i + 1]; var r2 = ys[i + 1] - ys[i + 2];
                if (dn > 6f && r1 > maxRise * 0.72f && r2 > maxRise * 0.72f)
                {
                    ys[i + 1] = ys[i];
                    for (var j = i + 2; j < Mathf.Min(i + 14, ys.Count); j++)
                    {
                        if (ys[j] < ys[j - 1] - maxRise) ys[j] = ys[j - 1] - maxRise; else break;
                    }
                }
            }

            // ---- в TrackProfile: высоты вверх = + (baseY − y) ----
            var profile = ScriptableObject.CreateInstance<TrackProfile>();
            profile.nodeStepPx = StepPx;
            var nodes = new Vector2[ys.Count];
            for (var i = 0; i < ys.Count; i++) nodes[i] = new Vector2(i * StepPx, BaseY - ys[i]);
            profile.nodesPx = nodes;
            profile.gapsPx = new TrackProfile.Gap[0];
            profile.endPx = (ys.Count - 1) * StepPx;
            profile.sharpNodeIndices = new int[0];
            // Чекпоинты каждые ~100 узлов — как в исходнике рестарт с последнего пройденного.
            var cps = new List<TrackProfile.Checkpoint>();
            for (var i = 90; i < ys.Count - 30; i += 90) cps.Add(new TrackProfile.Checkpoint { xPx = i * StepPx, name = "чекпоинт" });
            profile.checkpoints = cps.ToArray();
            tt.Profile = profile;

            // ---- свечи для деки: тела на head-узлах, «вверх = +» ----
            tt.DeckCandles = new List<CandleTrackGenerator.Candle>(ys.Count);
            for (var i = 0; i < ys.Count; i++)
            {
                var y = BaseY - ys[i];
                tt.DeckCandles.Add(heads[i]
                    ? new CandleTrackGenerator.Candle { OpenPx = BaseY - opens[i], ClosePx = y, HighPx = BaseY - his[i], LowPx = BaseY - los[i] }
                    : new CandleTrackGenerator.Candle { OpenPx = y, ClosePx = y, HighPx = y, LowPx = y });
            }
            return tt;
        }
    }
}
