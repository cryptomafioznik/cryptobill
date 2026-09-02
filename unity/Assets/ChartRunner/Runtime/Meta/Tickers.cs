using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

namespace ChartRunner.Meta
{
    /// <summary>
    /// РЕАЛЬНЫЕ ГРАФИКИ — порт b97/b102/b127: 12 монет, live-загрузка 5-минутных свечей
    /// с Binance (300 свечей ≈ сутки), кэш на час, офлайн-фолбэк — встроенный снапшот BTC
    /// (те же 300 свечей, что в исходнике). Игра ВСЕГДА играбельна, даже без сети.
    ///
    /// Волатильность монеты → множитель скорости волны (b98): тир/сложность считаются
    /// из реальных данных эмерджентно, без ручной настройки.
    /// </summary>
    public static class Tickers
    {
        public struct Ticker { public string Key, Symbol, Name; }

        public static readonly Ticker[] All =
        {
            T("btc", "BTCUSDT", "BTC"), T("eth", "ETHUSDT", "ETH"), T("bnb", "BNBUSDT", "BNB"),
            T("sol", "SOLUSDT", "SOL"), T("xrp", "XRPUSDT", "XRP"), T("link", "LINKUSDT", "LINK"),
            T("avax", "AVAXUSDT", "AVAX"), T("doge", "DOGEUSDT", "DOGE"), T("pepe", "PEPEUSDT", "PEPE"),
            T("bonk", "BONKUSDT", "BONK"), T("floki", "FLOKIUSDT", "FLOKI"), T("wif", "WIFUSDT", "WIF"),
        };
        private static Ticker T(string k, string s, string n) => new Ticker { Key = k, Symbol = s, Name = n };

        /// <summary>Свеча: open, high, low, close.</summary>
        public struct Candle { public float O, H, L, C; }

        public struct Preview
        {
            public float Price, Chg; public bool Up; public string VolLabel; public float[] Spark;
        }

        public static readonly Dictionary<string, Preview> Previews = new Dictionary<string, Preview>();
        private static readonly Dictionary<string, List<Candle>> Cache = new Dictionary<string, List<Candle>>();
        private static readonly Dictionary<string, DateTime> CacheAt = new Dictionary<string, DateTime>();
        private static List<Candle> _fallback;

        // ---- b494/b495: тир и множитель волатильности ----
        public static string VolTier(float v) => v < 0.0009f ? "НИЗК" : v < 0.0016f ? "СРЕД" : v < 0.0024f ? "ВЫС" : "ЭКСТРИМ";
        public static float VolMulOf(float v) => Mathf.Clamp(v / 0.0008f, 0.85f, 2.4f);

        /// <summary>b274: реальная волатильность = средний |Δclose| / close.</summary>
        public static float Volatility(List<Candle> d)
        {
            float vs = 0; var vn = 0;
            for (var i = 1; i < d.Count; i++)
                if (d[i - 1].C > 0) { vs += Mathf.Abs(d[i].C - d[i - 1].C) / d[i - 1].C; vn++; }
            return vn > 0 ? vs / vn : 0.0008f;
        }

        public static Preview PreviewStats(List<Candle> d)
        {
            var last = d[d.Count - 1].C; var first = d[0].C;
            var chg = first > 0 ? last / first - 1f : 0f;
            const int N = 26; var spark = new float[N];
            for (var i = 0; i < N; i++) spark[i] = d[Mathf.FloorToInt(i * (d.Count - 1) / (float)(N - 1))].C;
            return new Preview { Price = last, Chg = chg, Up = chg >= 0, VolLabel = VolTier(Volatility(d)), Spark = spark };
        }

        public static string FmtPrice(float p)
        {
            if (p >= 1000) return "$" + Mathf.RoundToInt(p).ToString("N0", CultureInfo.InvariantCulture);
            if (p >= 1) return "$" + p.ToString("0.00", CultureInfo.InvariantCulture);
            if (p >= 0.01f) return "$" + p.ToString("0.0000", CultureInfo.InvariantCulture);
            return "$" + p.ToString("0.00E+0", CultureInfo.InvariantCulture);
        }

        /// <summary>Встроенный снапшот BTC (Resources/Data/btc-fallback.csv) — офлайн всегда играбельно.</summary>
        public static List<Candle> Fallback()
        {
            if (_fallback != null) return _fallback;
            _fallback = new List<Candle>();
            var ta = Resources.Load<TextAsset>("Data/btc-fallback");
            if (ta != null)
            {
                foreach (var line in ta.text.Split('\n'))
                {
                    var p = line.Trim().Split(',');
                    if (p.Length != 4) continue;
                    _fallback.Add(new Candle
                    {
                        O = float.Parse(p[0], CultureInfo.InvariantCulture), H = float.Parse(p[1], CultureInfo.InvariantCulture),
                        L = float.Parse(p[2], CultureInfo.InvariantCulture), C = float.Parse(p[3], CultureInfo.InvariantCulture)
                    });
                }
            }
            if (_fallback.Count < 20) Debug.LogError("Tickers: фолбэк BTC не загрузился — Resources/Data/btc-fallback.csv");
            return _fallback;
        }

        /// <summary>
        /// Загрузка свечей: кэш 1 ч → Binance → фолбэк BTC. Коллбэк получает данные и флаг
        /// «это фолбэк» (в исходнике показывалось «(нет связи — BTC)»).
        /// </summary>
        public static IEnumerator Load(Ticker t, Action<List<Candle>, bool> done)
        {
            if (Cache.TryGetValue(t.Key, out var cached) && (DateTime.UtcNow - CacheAt[t.Key]).TotalHours < 1)
            {
                done(cached, false); yield break;
            }
            var url = "https://api.binance.com/api/v3/klines?symbol=" + t.Symbol + "&interval=5m&limit=300";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var d = ParseKlines(req.downloadHandler.text);
                    if (d != null && d.Count >= 20)
                    {
                        Cache[t.Key] = d; CacheAt[t.Key] = DateTime.UtcNow;
                        done(d, false); yield break;
                    }
                }
            }
            done(Fallback(), true);
        }

        /// <summary>
        /// Свечи с параметрами: исторический отрезок кампании (startTime+limit, b141) или
        /// «вчерашние 288» для дейли (endTime = полночь UTC, b161). Кэш по ключу — история
        /// неизменна, поэтому навсегда (PlayerPrefs, компактный CSV).
        /// </summary>
        public static IEnumerator LoadRange(string symbol, string cacheKey, string query, Action<List<Candle>, bool> done)
        {
            var cached = PlayerPrefs.GetString("cr_kl_" + cacheKey, "");
            if (cached.Length > 0)
            {
                var d = ParseCsv(cached);
                if (d.Count >= 20) { done(d, false); yield break; }
            }
            var url = "https://api.binance.com/api/v3/klines?symbol=" + symbol + "&interval=5m&" + query;
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var d = ParseKlines(req.downloadHandler.text);
                    if (d != null && d.Count >= 20)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var c in d) sb.Append(c.O.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(c.H.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                            .Append(c.L.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(c.C.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                        PlayerPrefs.SetString("cr_kl_" + cacheKey, sb.ToString()); PlayerPrefs.Save();
                        done(d, false); yield break;
                    }
                }
            }
            done(Fallback(), true);
        }

        private static List<Candle> ParseCsv(string csv)
        {
            var list = new List<Candle>();
            foreach (var line in csv.Split('\n'))
            {
                var p = line.Split(','); if (p.Length != 4) continue;
                if (TryNum(p[0], out var o) && TryNum(p[1], out var h) && TryNum(p[2], out var l) && TryNum(p[3], out var c))
                    list.Add(new Candle { O = o, H = h, L = l, C = c });
            }
            return list;
        }

        /// <summary>Фан-аут превью для терминала (b108): строки заполняются по мере прихода.</summary>
        public static IEnumerator LoadAllPreviews()
        {
            foreach (var t in All)
            {
                if (Previews.ContainsKey(t.Key)) continue;
                var tk = t;
                yield return Load(tk, (d, fb) => { if (!fb && d.Count >= 20) Previews[tk.Key] = PreviewStats(d); });
            }
        }

        /// <summary>Binance klines: [[t,o,h,l,c,...],...] — парсим без JSON-библиотеки, поля 1..4.</summary>
        private static List<Candle> ParseKlines(string json)
        {
            var list = new List<Candle>();
            var i = 0;
            while ((i = json.IndexOf('[', i + 1)) >= 0)
            {
                var j = json.IndexOf(']', i);
                if (j < 0) break;
                var parts = json.Substring(i + 1, j - i - 1).Split(',');
                if (parts.Length < 5) continue;
                if (TryNum(parts[1], out var o) && TryNum(parts[2], out var h) && TryNum(parts[3], out var l) && TryNum(parts[4], out var c)
                    && o > 0 && h > 0 && l > 0 && c > 0)
                    list.Add(new Candle { O = o, H = h, L = l, C = c });
                i = j;
            }
            return list;
        }

        private static bool TryNum(string s, out float v)
        {
            return float.TryParse(s.Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && float.IsFinite(v);
        }
    }
}
