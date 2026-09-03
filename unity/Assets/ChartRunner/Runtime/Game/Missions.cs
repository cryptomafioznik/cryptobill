using System;
using System.Collections.Generic;
using ChartRunner.Meta;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// ЦЕЛИ ЗАЕЗДА — порт MISSION_POOL / rollMissions / checkMissions исходника:
    /// три случайные цели на заезд, выполнение платит ◆ в позицию сразу.
    /// Заменяет придуманную мной ранее модель «трёх целей Alto's»: правило сессии —
    /// переносить, а не сочинять.
    /// </summary>
    public sealed class Mission
    {
        public string Id, Text; public int Target, Reward; public bool Done;
        public Func<RunStats, float> Val;
    }

    public struct RunStats { public float Dist, Coins, MaxMult, Flips, Kmh; public int Leverage; }

    public static class Missions
    {
        private struct Def { public string Id; public Func<int, string> Txt; public Func<System.Random, int> Gen; public Func<RunStats, float> Val; public int Rew; }

        private static readonly Def[] Pool =
        {
            new Def { Id = "dist", Txt = v => Loc.T("Проехать ") + v + "m", Gen = r => 300 + r.Next(6) * 100, Val = s => s.Dist, Rew = 40 },
            new Def { Id = "gems", Txt = v => Loc.T("Собрать $") + v, Gen = r => 15 + r.Next(5) * 5, Val = s => s.Coins, Rew = 35 },
            new Def { Id = "spd", Txt = v => Loc.T("Разгон до ") + v + Loc.T(" км/ч"), Gen = r => 150 + r.Next(5) * 20, Val = s => s.Kmh, Rew = 40 },
            new Def { Id = "pos", Txt = v => Loc.T("Позиция $") + v, Gen = r => 40 + r.Next(5) * 20,
                Val = s => Mathf.Round(s.Coins * Meta.Economy.CoinGem * s.Leverage), Rew = 70 },
        };

        public static readonly List<Mission> Active = new List<Mission>();

        public static void Roll(int seed)
        {
            var rng = new System.Random(seed);
            var pool = new List<Def>(Pool);
            Active.Clear();
            for (var n = 0; n < 3 && pool.Count > 0; n++)
            {
                var d = pool[rng.Next(pool.Count)]; pool.Remove(d);
                var tgt = d.Gen(rng);
                Active.Add(new Mission { Id = d.Id, Target = tgt, Text = d.Txt(tgt), Val = d.Val, Reward = d.Rew });
            }
        }

        /// <summary>Возвращает суммарную награду ◆ за только что закрытые цели.</summary>
        public static int Check(RunStats s, out Mission justDone)
        {
            justDone = null; var rew = 0;
            foreach (var m in Active)
            {
                if (m.Done || m.Val(s) < m.Target) continue;
                m.Done = true; rew += m.Reward; justDone = m;
            }
            return rew;
        }
    }
}
