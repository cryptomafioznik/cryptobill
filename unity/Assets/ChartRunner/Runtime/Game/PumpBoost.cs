using ChartRunner.Bike;
using ChartRunner.Meta;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// PUMP — порт boost/activateBoost (TUNE.pumpForce 0.09 px/кадр, pumpDur 120 кадров,
    /// ×(1+0.22·СИЛА PUMP)). Заряд копится от игры: монета +0.04, вилли +0.006/кадр,
    /// большой полёт +0.3, события +0.25. Полный заряд → тап → 2 с мягкого разгона.
    /// </summary>
    public sealed class PumpBoost
    {
        private const float PumpForcePxPerFrame = 0.09f;
        private const int PumpDurFrames = 120;

        public float Charge { get; private set; }
        public int BoostingFrames { get; private set; }
        public bool Ready => Charge >= 1f;
        public bool Active => BoostingFrames > 0;

        public void Add(float v) => Charge = Mathf.Min(1f, Charge + v);

        public bool Activate()
        {
            if (Charge < 1f || BoostingFrames > 0) return false;
            Charge = 0f;
            BoostingFrames = Mathf.RoundToInt(PumpDurFrames * (1f + 0.22f * Economy.UpgLvl("pump")));
            return true;
        }

        public void FixedTick(BikeController bike)
        {
            if (BoostingFrames <= 0) return;
            BoostingFrames--;
            bike.PushForward(PumpForcePxPerFrame * UnitsContract.PxPerFrameToMPerS);
        }

        public void Reset() { Charge = 0f; BoostingFrames = 0; }
    }
}
