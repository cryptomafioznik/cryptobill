using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChartRunner.Meta
{
    /// <summary>
    /// ПУТЬ ТРЕЙДЕРА — порт b141/b142: кампания из 20 ИСТОРИЧЕСКИХ трасс (реальные свечи
    /// с фиксированной даты, калм → экстрим, боссы-крахи вплавлены вехами), последовательный
    /// анлок, медали 🥉🥈🥇 по профиту за круг. Плюс ВЫЗОВ ДНЯ (b125): детерминирован датой,
    /// награда раз в день. Плюс дейли «Рынок сегодня» (b161/b178): трасса дня у всех, 3 попытки,
    /// стрик с бонусом.
    /// </summary>
    public static class Campaign
    {
        public struct Track
        {
            public string Id, Coin, Symbol, Name, Short, Sub; public long T; public int[] Med; public int Rew; public bool Boss;
        }

        public static readonly Track[] Tracks =
        {
            Tr("c01","btc","BTCUSDT",1567296000000,Loc.T("Тихий вторник"),Loc.T("Тихий"),Loc.T("BTC · сен 2019"),80,200,450,40),
            Tr("c02","eth","ETHUSDT",1684800000000,Loc.T("Штиль"),Loc.T("Штиль"),Loc.T("ETH · май 2023"),110,280,600,60),
            Tr("c03","doge","DOGEUSDT",1707350400000,Loc.T("Прогрев"),Loc.T("Прогрев"),Loc.T("DOGE · фев 2024"),150,360,780,80),
            Tr("c04","btc","BTCUSDT",1640995200000,Loc.T("Дрейф"),Loc.T("Дрейф"),Loc.T("BTC · янв 2022"),220,540,1150,110),
            Tr("c05","sol","SOLUSDT",1693526400000,Loc.T("Раскачка"),Loc.T("Раскачка"),Loc.T("SOL · сен 2023"),300,750,1600,140),
            Tr("c06","eth","ETHUSDT",1659312000000,Loc.T("Сползание"),Loc.T("Сполз."),Loc.T("ETH · авг 2022"),380,950,2000,170),
            Tr("c07","btc","BTCUSDT",1731628800000,Loc.T("Ралли"),Loc.T("Ралли"),Loc.T("BTC · ноя 2024"),450,1100,2400,200),
            Tr("c08","sol","SOLUSDT",1636934400000,Loc.T("Импульс"),Loc.T("Импульс"),Loc.T("SOL · ноя 2021"),540,1350,2900,230),
            Tr("c09","xrp","XRPUSDT",1640131200000,Loc.T("Боковик"),Loc.T("Боковик"),Loc.T("XRP · дек 2021"),620,1500,3200,260),
            Tr("c10","link","LINKUSDT",1628812800000,Loc.T("Памп +11%"),Loc.T("Памп+11"),Loc.T("LINK · авг 2021"),720,1800,3800,300),
            Tr("b1","btc","BTCUSDT",1667952000000,Loc.T("КРАХ FTX"),"FTX −13%","BTC −13% · 11.2022",800,2000,4300,340,true),
            Tr("c11","bnb","BNBUSDT",1622505600000,Loc.T("Качели"),Loc.T("Качели"),Loc.T("BNB · июн 2021"),850,2100,4500,360),
            Tr("c12","avax","AVAXUSDT",1637280000000,Loc.T("Памп +9%"),Loc.T("Памп+9"),Loc.T("AVAX · ноя 2021"),950,2400,5000,400),
            Tr("c13","wif","WIFUSDT",1711238400000,"WIF +15%","WIF+15",Loc.T("WIF · мар 2024"),1050,2700,5800,440),
            Tr("c14","eth","ETHUSDT",1620864000000,Loc.T("Бычий шторм"),Loc.T("Шторм"),Loc.T("ETH · май 2021"),1200,3000,6500,490),
            Tr("b2","btc","BTCUSDT",1722816000000,Loc.T("АВГУСТ-ОБВАЛ"),Loc.T("АВГ −16%"),"BTC −16% · 08.2024",1300,3300,7000,540,true),
            Tr("c15","doge","DOGEUSDT",1620432000000,Loc.T("DOGE-безумие"),"DOGE",Loc.T("DOGE · май 2021"),1450,3600,7800,560),
            Tr("b3","btc","BTCUSDT",1583971200000,"BLACK THURSDAY","BLACK −28%","BTC −28% · 03.2020",1600,4000,8800,640,true),
            Tr("c16","pepe","PEPEUSDT",1683849600000,Loc.T("PEPE: ланч"),"PEPE+8",Loc.T("PEPE · май 2023"),1800,4500,10000,720),
            Tr("c17","bonk","BONKUSDT",1701907200000,Loc.T("BONK обвал"),"BONK−19","BONK −19% · 12.2023",2100,5200,11500,850),
        };
        private static Track Tr(string id, string coin, string sym, long t, string name, string sh, string sub, int m0, int m1, int m2, int rew, bool boss = false)
            => new Track { Id = id, Coin = coin, Symbol = sym, T = t, Name = name, Short = sh, Sub = sub, Med = new[] { m0, m1, m2 }, Rew = rew, Boss = boss };

        /// <summary>b141: длина = ось сложности, ~110 свечей → ~250.</summary>
        public static int Len(int idx) => 110 + Mathf.RoundToInt(idx / (float)Mathf.Max(1, Tracks.Length - 1) * 140f);

        // ---- активный вызов текущего заезда ----
        public sealed class Challenge
        {
            public string Id, Name, Goal; public int[] Med; public int Rew; public bool Daily, Camp;
            /// <summary>Итог: медаль −1/0/1/2 и награда, заполняется Finish.</summary>
            public int Medal = -1; public int Paid; public bool Resolved;
        }
        public static Challenge Active;

        // ---- сохранённые медали {id: лучший медал-индекс} ----
        private static readonly Dictionary<string, int> Done = new Dictionary<string, int>();
        private static string _dailyDoneDay = "";

        public static bool Beaten(int i) => i >= 0 && i < Tracks.Length && Done.ContainsKey("cmp_" + Tracks[i].Id);
        public static bool Unlocked(int i) => i == 0 || Beaten(i - 1);
        public static int Medal(string id) => Done.TryGetValue(id, out var m) ? m : -1;

        static Campaign() => Load();

        public static Challenge ForTrack(int i)
        {
            var t = Tracks[i];
            return new Challenge { Id = "cmp_" + t.Id, Name = t.Name, Goal = "campaign", Med = t.Med, Rew = t.Rew, Camp = true };
        }

        /// <summary>b125 ВЫЗОВ ДНЯ: детерминирован датой, одинаков весь день.</summary>
        public static Challenge DailyChallenge(out Tickers.Ticker coin)
        {
            var ds = DateTime.Now.ToString("ddd MMM dd yyyy");
            uint h = 0; foreach (var ch in ds) h = unchecked(h * 31 + ch);
            coin = Tickers.All[(int)(h % (uint)Tickers.All.Length)];
            var isBank = ((h >> 4) & 1) == 0;
            return new Challenge
            {
                Id = "daily", Daily = true, Rew = 200, Goal = isBank ? "bank" : "finish",
                Name = isBank ? Loc.T("Заработать на ") + coin.Name : Loc.T("Доехать: ") + coin.Name,
                Med = isBank ? new[] { 400, 1000, 2200 } : null
            };
        }
        public static bool DailyChallengeDone => _dailyDoneDay == DateTime.Now.ToString("ddd MMM dd yyyy");

        /// <summary>b1 finishChallenge: медаль по цели, награда, сохранение лучшего.</summary>
        public static void Finish(int distB, int finishDistB, int runGems)
        {
            var ch = Active; if (ch == null || ch.Resolved) return;
            ch.Resolved = true;
            var medal = -1;
            if (ch.Goal == "finish") { if (distB >= finishDistB) medal = 2; }
            else if (ch.Goal == "bank") { var m = ch.Med; medal = runGems >= m[2] ? 2 : runGems >= m[1] ? 1 : runGems >= m[0] ? 0 : -1; }
            else if (ch.Goal == "campaign") { var m = ch.Med; if (distB >= finishDistB) medal = runGems >= m[2] ? 2 : runGems >= m[1] ? 1 : 0; }
            ch.Medal = medal;
            if (medal < 0) return;
            if (ch.Daily)
            {
                var td = DateTime.Now.ToString("ddd MMM dd yyyy");
                if (_dailyDoneDay != td) { _dailyDoneDay = td; ch.Paid = ch.Rew; Economy.Bank += ch.Rew; }
            }
            else
            {
                var prev = Medal(ch.Id);
                if (medal > prev) Done[ch.Id] = medal;
                if (prev < 0) { ch.Paid = ch.Rew; Economy.Bank += ch.Rew; }
            }
            Save(); Economy.Save();
        }

        // ---- дейли «Рынок сегодня» (b161/b178) ----
        public static string DayUtc() { var d = DateTime.UtcNow; return d.Year + "-" + d.Month + "-" + d.Day; }
        private static string DayUtcShift(int n) { var d = DateTime.UtcNow.AddDays(-n); return d.Year + "-" + d.Month + "-" + d.Day; }
        public static string DailyDay = ""; public static int DailyTries, DailyBest, DailyStreak;
        public static int TriesToday => DailyDay == DayUtc() ? DailyTries : 0;
        public static int BestToday => DailyDay == DayUtc() ? DailyBest : 0;
        public static int StreakAlive => (DailyDay == DayUtc() || DailyDay == DayUtcShift(1)) ? DailyStreak : 0;
        public static Tickers.Ticker DailyCoin => Tickers.All[(int)((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 86400000L) % Tickers.All.Length)];

        /// <summary>Итог попытки дня. Возвращает стрик-бонус $ (первая попытка дня), иначе 0.</summary>
        public static int DailyResolve(int runGems)
        {
            var ds = DayUtc(); var bonus = 0;
            if (DailyDay != ds)
            {
                var keep = DailyDay == DayUtcShift(1);
                DailyDay = ds; DailyTries = 0; DailyBest = 0; DailyStreak = keep ? DailyStreak + 1 : 1;
                bonus = Mathf.RoundToInt(25f * Mathf.Min(DailyStreak, 14));
                Economy.Bank += bonus;
            }
            DailyTries++; DailyBest = Mathf.Max(DailyBest, runGems);
            Save(); Economy.Save();
            return bonus;
        }

        // ---- persistence ----
        private const string P = "cr_camp_";
        private static void Save()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in Done) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            PlayerPrefs.SetString(P + "done", sb.ToString());
            PlayerPrefs.SetString(P + "dailydone", _dailyDoneDay);
            PlayerPrefs.SetString(P + "dd", DailyDay); PlayerPrefs.SetInt(P + "dt", DailyTries);
            PlayerPrefs.SetInt(P + "db", DailyBest); PlayerPrefs.SetInt(P + "ds", DailyStreak);
            PlayerPrefs.Save();
        }
        private static void Load()
        {
            Done.Clear();
            foreach (var rec in PlayerPrefs.GetString(P + "done", "").Split(';'))
            { var a = rec.Split('='); if (a.Length == 2 && int.TryParse(a[1], out var m)) Done[a[0]] = m; }
            _dailyDoneDay = PlayerPrefs.GetString(P + "dailydone", "");
            DailyDay = PlayerPrefs.GetString(P + "dd", ""); DailyTries = PlayerPrefs.GetInt(P + "dt", 0);
            DailyBest = PlayerPrefs.GetInt(P + "db", 0); DailyStreak = PlayerPrefs.GetInt(P + "ds", 0);
        }
        public static void ResetAll() { Done.Clear(); _dailyDoneDay = ""; DailyDay = ""; DailyTries = DailyBest = DailyStreak = 0; Save(); }
    }
}
