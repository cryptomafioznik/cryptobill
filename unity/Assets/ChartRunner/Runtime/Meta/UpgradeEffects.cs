using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Meta
{
    /// <summary>
    /// АПГРЕЙДЫ И ТИР БАЙКА → ФИЗИКА. Порт стр. 1509 исходника (b70/b88): сток = одобренный
    /// аркадный фил, апгрейды добавляют СВЕРХУ. Множители — исходника:
    ///   ДВИЖОК      тяга    × (accel/0.46) × (1 + 0.13·lvl)
    ///   СЦЕПЛЕНИЕ   µ       × (grip/1.54)  × (1 + 0.15·lvl)   + конус посадки +0.12·lvl
    ///   ПОДВЕСКА    демпфер × (1 + 0.22·lvl)                  + конус посадки +0.10·lvl
    ///   ВОЗД.КОНТР. наклон в полёте × (1 + 0.18·lvl)
    /// Базовый тир — КРОСС 250 (accel 0.46, grip 1.54): именно под него откалиброваны
    /// engineForceN и tyreFriction на полигоне (заезд на 45°).
    ///
    /// ПОЛ ПО ПРОХОДИМОСТИ. Трасса-график капнута на 42° (TICKER_CLIMBCAP), и исходник
    /// требует, чтобы её брал даже ВЕЛИК. Полигонная калибровка: 1.0× тяги = минимум
    /// для 45°, µ обязано быть > tan(42°)=0.90. Поэтому множитель тяги не опускается
    /// ниже 0.92, а µ — ниже 1.0: слабый байк медленнее, но не стена. Это единственное
    /// отступление от чисел исходника, и оно продиктовано другой физикой (Box2D).
    /// </summary>
    public static class UpgradeEffects
    {
        public static void Apply(BikeTuningProfile p)
        {
            var b = Economy.Bikes[Mathf.Clamp(Economy.SelBike, 0, Economy.Bikes.Length - 1)];
            var eng = Economy.UpgLvl("eng");
            var grip = Economy.UpgLvl("grip");
            var susp = Economy.UpgLvl("susp");
            var air = Economy.UpgLvl("air");

            var engMul = Mathf.Max(0.92f, b.Accel / 0.46f) * (1f + 0.13f * eng);
            p.engineForceN *= engMul;
            p.topSpeedMPerS *= 1f + 0.08f * eng;                 // «+8 % к скорости» из описания апгрейда

            var muMul = b.Grip / 1.54f * (1f + 0.15f * grip);
            p.tyreFriction = Mathf.Max(1.0f, p.tyreFriction * muMul);

            p.suspensionDamping *= 1f + 0.22f * susp;
            // Авторитет наклона в ПОЛЁТЕ — это leanYankAir (рывок телом без опоры), а не leanAir:
            // leanAir в этой физике не читается (гейт честности профиля это и поймал —
            // апгрейд был бы декоративной константой).
            p.leanYankAir *= 1f + 0.18f * air;
            p.crashAngleRad += 0.12f * grip + 0.10f * susp;
        }
    }
}
