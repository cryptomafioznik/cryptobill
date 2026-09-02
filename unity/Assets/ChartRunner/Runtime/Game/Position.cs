using ChartRunner.Meta;
using ChartRunner.Track;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// ПОЗИЦИЯ — порт b173 «ДОЛИВКА» (DCA): ты не входишь всей котлетой на старте —
    /// КАЖДЫЙ метр докупает позицию по ТЕКУЩЕЙ цене (∝ метрам + монеткам). Стоимость
    /// растёт по мере трассы, поэтому «10 м на плече и зафиксироваться» не работает.
    ///
    /// Выплата (b231): dcaCost + 2.2·√lev · PnL — плечо в выплате УБЫВАЮЩЕЕ
    /// (2x→3.1 · 25x→11 · 100x→22): риск ликвидации полный, а джекпота за заезд нет.
    /// Цена НЕ ликвидирует капитал (b104) — только волна и краш.
    /// </summary>
    public sealed class Position
    {
        private readonly TickerTrack _track;
        private float _dcaCost, _dcaQty, _dcaDist, _dcaCoins;

        public float EntryPrice { get; private set; }
        public float MaxValue { get; private set; }

        public Position(TickerTrack track)
        {
            _track = track;
            EntryPrice = track.PriceAt(60f * Tuning.UnitsContract.PxToM); // b1154: вход = цена на старте
        }

        /// <summary>b888 dcaTick: dist — браузерные метры, coins — собранные монеты.</summary>
        public void Tick(float xM, float distBrowserM, float coins)
        {
            var cur = Mathf.Max(1e-9f, _track.PriceAt(xM));
            var dd = Mathf.Max(0f, distBrowserM - _dcaDist); _dcaDist = distBrowserM;
            var dc = Mathf.Max(0f, coins - _dcaCoins); _dcaCoins = coins;
            var stake = Economy.TkStake();
            var spend = stake * (dd / Economy.DcaM) + stake * (1f / Economy.DcaM) * Economy.DcaCoin * dc;
            if (spend > 0f) { _dcaCost += spend; _dcaQty += spend / cur; }
            MaxValue = Mathf.Max(MaxValue, Value(xM));
        }

        public int Value(float xM)
        {
            if (_dcaCost <= 0f) return 0;
            var cur = Mathf.Max(1e-9f, _track.PriceAt(xM));
            var pl = _track.Short ? _dcaCost - _dcaQty * cur : _dcaQty * cur - _dcaCost;
            return Mathf.Max(0, Mathf.RoundToInt((_dcaCost + 2.2f * Mathf.Sqrt(Economy.Leverage) * pl) * Economy.DcaPay));
        }

        /// <summary>PnL % позиции (шорт = минус движения).</summary>
        public float Pnl(float xM)
        {
            if (_dcaCost <= 0f) return 0f;
            var cur = Mathf.Max(1e-9f, _track.PriceAt(xM));
            var raw = _dcaQty * cur / _dcaCost - 1f;
            return _track.Short ? -raw : raw;
        }

        /// <summary>b879: реальное изменение цены монеты от входа.</summary>
        public float Change(float xM) => EntryPrice > 0f ? _track.PriceAt(xM) / EntryPrice - 1f : 0f;
    }
}
