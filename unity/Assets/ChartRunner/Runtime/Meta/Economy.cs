using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChartRunner.Meta
{
    /// <summary>
    /// ЭКОНОМИКА — порт констант и формул toys/chartrider.html (b71-b231), один в один.
    /// Здесь нет ни одного придуманного числа: каждая константа снята со строки исходника,
    /// на которую указывает комментарий. Это спецификация игры, принятой пользователем.
    ///
    /// Модель (b76 «единая экономика — маховик»): bank = NET WORTH в $.
    ///   ЗАФИКСИЛ  → bank += стоимость позиции (DCA-доливка × убывающее плечо, b231).
    ///   ЛИКВИДАЦИЯ → bank −= min(bank×lev×LIQ_PEN, bank×LIQ_PEN_MAX) — реальная потеря.
    ///   career (◆) — накопленный заработок, растит РАНГ; ◆ даёт стейкинг 3 % за заезд.
    /// </summary>
    public static class Economy
    {
        // ---- b858: плечо ----
        public static readonly int[] Levs = { 2, 5, 10, 25, 50, 100 };
        public const float CoinGem = 0.1f;
        public const float LiqPen = 0.004f;
        public const float LiqPenMax = 0.18f;

        // ---- b875: маржа ----
        public const float StakeBase = 8f, StakePer = 0.008f, StakeCap = 30f;
        public const float StakeYield = 0.03f; // b114 стейкинг ◆ → $ за заезд

        // ---- b885: доливка ----
        public const float DcaM = 120f;       // браузерных метров на полный tkStake
        public const float DcaCoin = 2.5f;    // буст монетки
        public const float DcaPay = 0.55f;    // масштаб выплаты

        // ---- b855: ранги от накопленного ◆ ----
        public struct Rank { public string Name, Emoji; public int At, Reward; }
        public static readonly Rank[] Ranks =
        {
            new Rank { Name = "КРЕВЕТКА", Emoji = "①", At = 0, Reward = 0 },
            new Rank { Name = "КРАБ", Emoji = "②", At = 400, Reward = 80 },
            new Rank { Name = "РЫБА", Emoji = "③", At = 1500, Reward = 180 },
            new Rank { Name = "ДЕЛЬФИН", Emoji = "④", At = 5000, Reward = 400 },
            new Rank { Name = "АКУЛА", Emoji = "⑤", At = 16000, Reward = 800 },
            new Rank { Name = "КИТ", Emoji = "⑥", At = 45000, Reward = 1500 },
        };

        // ---- b844: апгрейды ----
        public struct Upgrade { public string Id, Name, Icon; public int Max; public Func<int, string> Eff; }
        public static readonly Upgrade[] Upgrades =
        {
            new Upgrade { Id = "eng", Name = "ДВИЖОК", Max = 5, Icon = "⚙", Eff = l => "+" + 8 * l + "% к скорости" },
            new Upgrade { Id = "grip", Name = "СЦЕПЛЕНИЕ", Max = 5, Icon = "◎", Eff = l => "+" + 15 * l + "% хватка (подъём+посадка)" },
            new Upgrade { Id = "susp", Name = "ПОДВЕСКА", Max = 4, Icon = "≡", Eff = l => "+" + 22 * l + "% мягкая посадка" },
            new Upgrade { Id = "pump", Name = "СИЛА PUMP", Max = 5, Icon = "⚡", Eff = l => "+" + 22 * l + "% длина рывка" },
            new Upgrade { Id = "wave", Name = "ЩИТ ОТ ВОЛНЫ", Max = 5, Icon = "◈", Eff = l => "−" + 7 * l + "% скорость дампа" },
            new Upgrade { Id = "air", Name = "ВОЗД.КОНТРОЛЬ", Max = 4, Icon = "✦", Eff = l => "+" + 14 * l + "% верчение" },
            new Upgrade { Id = "mag", Name = "МАГНИТ", Max = 4, Icon = "◆", Eff = l => "+" + 40 * l + "% радиус сбора" },
        };
        private const float UpgCurve = 2.6f; // b853
        private static readonly Dictionary<string, int> UpgBase = new Dictionary<string, int>
        {
            { "eng", 45 }, { "grip", 55 }, { "susp", 55 }, { "pump", 55 }, { "wave", 65 }, { "air", 50 }, { "mag", 40 }
        };
        public static int UpgCost(string id, int lvl)
        {
            var b = UpgBase.TryGetValue(id, out var v) ? v : 50;
            return Mathf.RoundToInt(b * Mathf.Pow(1 + lvl, UpgCurve));
        }

        // ---- b997: байки (цена, тир) — арт и физика подключаются срезом «гараж» ----
        public struct BikeDef { public string Name, Type, Frame, Tag; public int Cost, ReqRank; public float Accel, Grip; }
        public static readonly BikeDef[] Bikes =
        {
            new BikeDef { Name = "ВЕЛИК", Type = "bike", Cost = 0, Accel = 0.30f, Grip = 1.30f, Tag = "учебка · медленный, лёгкий" },
            new BikeDef { Name = "МОПЕД", Type = "moped", Cost = 150, Accel = 0.34f, Grip = 1.38f, Tag = "дворовый · цепкий, шустрее" },
            new BikeDef { Name = "СКУТЕР", Type = "scooter", Cost = 400, Accel = 0.38f, Grip = 1.50f, Tag = "стабильный · прощает посадки" },
            new BikeDef { Name = "ЭНДУРО 125", Type = "dirt", Frame = "enduro", Cost = 900, Accel = 0.42f, Grip = 1.46f, Tag = "резвый универсал" },
            new BikeDef { Name = "КРОСС 250", Type = "dirt", Frame = "cross", Cost = 1800, Accel = 0.46f, Grip = 1.54f, Tag = "сбалансированный зверь" },
            new BikeDef { Name = "МОТАРД 450", Type = "dirt", Frame = "motard", Cost = 3400, Accel = 0.49f, Grip = 1.44f, Tag = "тяга-монстр · нервный" },
            new BikeDef { Name = "СУПЕРБАЙК 650", Type = "sport", Cost = 6500, Accel = 0.52f, Grip = 1.60f, Tag = "быстрый + вкопанный", ReqRank = 3 },
        };

        // ---- состояние игрока (persist) ----
        public static int Bank;
        public static int Career;       // ◆ накопленный заработок
        public static int Leverage = 2;
        public static bool ShortMode;
        public static int SelBike;
        public static readonly List<int> Owned = new List<int> { 0 };
        public static int Best;         // рекорд дистанции (браузерные метры)
        public static int BestPnl;
        public static int LastTicker;
        public static bool SeenHowto;
        public static int OnbIdx;       // b180 онбординг-миссии
        public static bool Muted;
        private static readonly Dictionary<int, Dictionary<string, int>> AllUpg = new Dictionary<int, Dictionary<string, int>>();

        static Economy() => Load();

        public static int UpgLvl(string id)
        {
            return AllUpg.TryGetValue(SelBike, out var u) && u.TryGetValue(id, out var l) ? l : 0;
        }

        public static void BuyUpgrade(string id)
        {
            var lvl = UpgLvl(id);
            var max = 0;
            foreach (var u in Upgrades) if (u.Id == id) max = u.Max;
            var cost = UpgCost(id, lvl);
            if (lvl >= max || Bank < cost) return;
            Bank -= cost;
            if (!AllUpg.ContainsKey(SelBike)) AllUpg[SelBike] = new Dictionary<string, int>();
            AllUpg[SelBike][id] = lvl + 1;
            Save();
        }

        /// <summary>b873: безопасный потолок плеча — растят ЩИТ, ДВИЖОК и тир байка.</summary>
        public static int SafeLev() => Mathf.RoundToInt(4 + 4 * UpgLvl("wave") + 1.5f * UpgLvl("eng") + SelBike * 2);

        /// <summary>b876: маржа (размер ставки) растёт от $-стека, капана.</summary>
        public static float TkStake() => Mathf.Clamp(StakeBase + Bank * StakePer, StakeBase, StakeCap);

        public static int RankIdx(int c)
        {
            var i = 0;
            for (var k = 0; k < Ranks.Length; k++) if (c >= Ranks[k].At) i = k;
            return i;
        }

        /// <summary>b1895: вывод ◆ в холод — растит ранг, даёт бонус за новые ранги.</summary>
        public static int CommitCareer(int amt, out bool rankedUp, out Rank newRank)
        {
            var oi = RankIdx(Career);
            Career += amt;
            var ni = RankIdx(Career);
            var bonus = 0;
            rankedUp = ni > oi;
            newRank = Ranks[ni];
            if (rankedUp) for (var r = oi + 1; r <= ni; r++) bonus += Ranks[r].Reward;
            Bank += bonus;
            Save();
            return bonus;
        }

        /// <summary>b1867: ликвидация = реальная потеря ∝ плечу, капана, пол ≥ 0.</summary>
        public static int Liquidate()
        {
            var pen = Mathf.Min(Mathf.RoundToInt(Bank * Leverage * LiqPen), Mathf.RoundToInt(Bank * LiqPenMax));
            Bank = Mathf.Max(0, Bank - pen);
            Save();
            return pen;
        }

        /// <summary>b878: стейкинг — холодный ◆ даёт пассивный $ каждый заезд.</summary>
        public static int PayStaking()
        {
            var inc = Mathf.RoundToInt(Career * StakeYield);
            Bank += inc;
            return inc;
        }

        public static void CycleLeverage()
        {
            var i = Array.IndexOf(Levs, Leverage);
            Leverage = Levs[(i + 1) % Levs.Length];
            Save();
        }

        // ---- persistence ----
        private const string P = "cr_";

        public static void Save()
        {
            PlayerPrefs.SetInt(P + "bank", Bank);
            PlayerPrefs.SetInt(P + "career", Career);
            PlayerPrefs.SetInt(P + "lev", Leverage);
            PlayerPrefs.SetInt(P + "short", ShortMode ? 1 : 0);
            PlayerPrefs.SetInt(P + "bike", SelBike);
            PlayerPrefs.SetString(P + "owned", string.Join(",", Owned));
            PlayerPrefs.SetInt(P + "best", Best);
            PlayerPrefs.SetInt(P + "bestpnl", BestPnl);
            PlayerPrefs.SetInt(P + "lasttk", LastTicker);
            PlayerPrefs.SetInt(P + "howto", SeenHowto ? 1 : 0);
            PlayerPrefs.SetInt(P + "onb", OnbIdx);
            PlayerPrefs.SetInt(P + "muted", Muted ? 1 : 0);
            var sb = new System.Text.StringBuilder();
            foreach (var kv in AllUpg)
                foreach (var u in kv.Value) sb.Append(kv.Key).Append(':').Append(u.Key).Append('=').Append(u.Value).Append(';');
            PlayerPrefs.SetString(P + "upg", sb.ToString());
            PlayerPrefs.Save();
        }

        private static void Load()
        {
            Bank = PlayerPrefs.GetInt(P + "bank", 0);
            Career = PlayerPrefs.GetInt(P + "career", 0);
            Leverage = PlayerPrefs.GetInt(P + "lev", 2);
            if (Array.IndexOf(Levs, Leverage) < 0) Leverage = 2;
            ShortMode = PlayerPrefs.GetInt(P + "short", 0) == 1;
            SelBike = PlayerPrefs.GetInt(P + "bike", 0);
            Owned.Clear();
            foreach (var s in PlayerPrefs.GetString(P + "owned", "0").Split(','))
                if (int.TryParse(s, out var i)) Owned.Add(i);
            if (Owned.Count == 0) Owned.Add(0);
            Best = PlayerPrefs.GetInt(P + "best", 0);
            BestPnl = PlayerPrefs.GetInt(P + "bestpnl", 0);
            LastTicker = PlayerPrefs.GetInt(P + "lasttk", 0);
            SeenHowto = PlayerPrefs.GetInt(P + "howto", 0) == 1;
            OnbIdx = PlayerPrefs.GetInt(P + "onb", 0);
            Muted = PlayerPrefs.GetInt(P + "muted", 0) == 1;
            AllUpg.Clear();
            foreach (var rec in PlayerPrefs.GetString(P + "upg", "").Split(';'))
            {
                if (string.IsNullOrEmpty(rec)) continue;
                var a = rec.Split(':'); if (a.Length != 2) continue;
                var b = a[1].Split('='); if (b.Length != 2) continue;
                if (!int.TryParse(a[0], out var bike) || !int.TryParse(b[1], out var lvl)) continue;
                if (!AllUpg.ContainsKey(bike)) AllUpg[bike] = new Dictionary<string, int>();
                AllUpg[bike][b[0]] = lvl;
            }
        }

        /// <summary>b1228: чистый старт — стирает прогресс, сохраняет настройки.</summary>
        public static void ResetProgress()
        {
            Bank = 0; Career = 0; Leverage = 2; SelBike = 0; Best = 0; BestPnl = 0; OnbIdx = 0;
            Owned.Clear(); Owned.Add(0); AllUpg.Clear();
            Save();
        }
    }
}
