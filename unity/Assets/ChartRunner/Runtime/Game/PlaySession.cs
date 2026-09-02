using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ChartRunner.Bike;
using ChartRunner.Input;
using ChartRunner.Meta;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// ИГРА ЦЕЛИКОМ — порт потока экранов toys/chartrider.html: howto → title → setup
    /// (терминал) → tickerload → play → dead → garage/settings. Решение пользователя:
    /// «вернуть как всё было на сайте, улучшив физику и графику движком». Ничего
    /// не сочиняется; каждый экран и каждое число — из исходника.
    ///
    /// Сцена содержит один этот объект; мир строится кодом в Start. Смена экрана, требующая
    /// новой трассы (старт заезда, рестарт), идёт через перезагрузку сцены — статики
    /// (Flow, PendingCandles) переживают её и говорят Start, что строить.
    /// </summary>
    public class PlaySession : MonoBehaviour
    {
        public enum Screen { Howto, Title, Setup, TickerLoad, Play, Dead, Paused, Garage, Bikes, Settings }

        // ---- состояние потока, переживающее перезагрузку сцены ----
        public static Screen Flow = Screen.Title;
        public static List<Tickers.Candle> PendingCandles;
        public static int PendingTicker;
        public static bool PendingFallback;
        public static Feel SelectedFeel = Feel.Balance;
        private static bool _flowInit;
        private static int _setupSel;

        [Header("Данные (проставляются сборщиком сцены)")]
        public BikeTuningProfile BikeProfile;
        public LevelPhysicsOverride LevelProfile;
        public TrackProfile Track;
        public float RestartDelaySeconds = 1.1f;
        public bool RestartAtCheckpoint = true;
        public CandleTerrainProfile CandleProfile;
        public int ChartSeed = 20260806;
        public int ChartCandles = 700;
        [Header("Композиция")]
        public float HeroScreenFraction = ChaseCamera.DefaultHeroFraction;

        // ---- мир ----
        private BikeController _controller;
        private ChaseCamera _chase;
        private TerrainSampler _sampler;
        private PlayInput _input;
        private Camera _camera;
        private LiquidationWave _wave;
        private CoinField _coinField;
        private TickerTrack _tt;
        private Position _position;
        private readonly PumpBoost _pump = new PumpBoost();
        private float _prevVerticalSpeed;
        private bool _prevAirborne;
        private bool _switchLatch;
        private float _feelBannerUntil;

        // ---- заезд ----
        private int _distB;            // браузерные метры (px/10)
        private int _finishDistB;
        private int _attempts = 1;
        private float _spawnXM = 1.2f;
        private bool _cashedOut;
        private bool _committed;
        private int _runGems, _stakeIncome, _liqPen, _missionRew;
        private string _deathBy = "";
        private float _maxKmh;
        private float _deadFor = -1f;
        private string _pop = ""; private float _popUntil;
        private bool _rankedUp; private Economy.Rank _newRank; private int _rankBonus;
        private string _onbMsg = "";
        private float _airT;

        // ---- стили ----
        private GUIStyle _hud, _big, _zone, _warn, _small;
        private Font _mono;

        private const float W = 430f, H = 932f;

        // ================= запуск =================

        private void Awake()
        {
            Application.targetFrameRate = 60;
            UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;
#if UNITY_IOS || UNITY_ANDROID
            UnityEngine.Screen.orientation = ScreenOrientation.Portrait;
#endif
            Physics2D.gravity = new Vector2(0f, UnitsContract.GravityMPerS2);
        }

        private void Start()
        {
            GameAudio.Ensure();
            if (!_flowInit)
            {
                _flowInit = true;
                Flow = Economy.SeenHowto ? Screen.Title : Screen.Howto;
                _setupSel = Mathf.Clamp(Economy.LastTicker, 0, Tickers.All.Length - 1);
            }
            if (PendingCandles == null)
            {
                // Мир существует всегда, даже без сети: фолбэк BTC исходника (300 свечей).
                PendingCandles = Tickers.Fallback();
                PendingTicker = Mathf.Clamp(Economy.LastTicker, 0, Tickers.All.Length - 1);
                PendingFallback = true;
            }
            if (BikeProfile == null) { Debug.LogError("PlaySession: нет профиля байка"); enabled = false; return; }
            if (LevelProfile == null) LevelProfile = LevelPhysicsOverride.CreateStock();
            LevelProfile = Instantiate(LevelProfile);
            // Копия профиля байка: тир и апгрейды применяются к КОПИИ, ассет и тесты не трогаются.
            BikeProfile = Instantiate(BikeProfile);
            UpgradeEffects.Apply(BikeProfile);
            FeelPreset.Apply(LevelProfile, SelectedFeel);

            // ---- трасса из реальных свечей ----
            var shortRun = Economy.ShortMode;
            _tt = TickerTrack.Build(PendingCandles, shortRun, ChartSeed + PendingTicker);
            _sampler = new TerrainSampler(_tt.Profile);
            _finishDistB = Mathf.FloorToInt((_tt.Profile.nodesPx.Length - 1) * TickerTrack.StepPx / 10f);

            var world = new GameObject("World").transform;
            TrackBuilder.Build(_tt.Profile, BikeProfile.tyreFriction).transform.SetParent(world, true);
            TrackDeckView.Build(_tt.Profile, _sampler, world, _tt.DeckCandles, null);
            TerrainView.BuildMarkers(_tt.Profile, world, new Color(1f, 0.80f, 0.42f, 1f));

            // ---- байк ----
            _input = new PlayInput();
            var rear = BikeFactory.RestingRearAxle(_sampler, BikeProfile, _spawnXM);
            _controller = BikeFactory.Spawn(BikeProfile, LevelProfile, _sampler, _input, rear);
            BikeView.Attach(_controller);
            var overlay = _controller.GetComponent<Telemetry.TelemetryOverlay>();
            if (overlay != null) overlay.Visible = false;

            // ---- камера ----
            var camGo = new GameObject("GameCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = WorldPalette.Eras[0].Sky0;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 100f;
            _chase = camGo.AddComponent<ChaseCamera>();
            _chase.HeroScreenFraction = HeroScreenFraction;
            _chase.TopSpeedMPerS = BikeProfile.topSpeedMPerS;
            _chase.Bind(_camera, _controller.transform);
            PostFX.Attach(_camera);
            WorldView.Attach(_camera, _tt.Profile.EndM, PendingTicker % WorldPalette.Eras.Length);

            // ---- игровой слой ----
            _wave = LiquidationWave.Attach(_controller, _camera, world);
            _wave.DifficultyMul = _tt.VolMul * (1f - 0.07f * Economy.UpgLvl("wave"))
                                  * (1f + Mathf.Max(0, Economy.Leverage - Economy.SafeLev()) * 0.02f);
            _coinField = CoinField.Attach(_tt, _sampler, _controller, world);
            _position = new Position(_tt);
            Missions.Roll(ChartSeed + PendingTicker + _attempts);
            ContactShadow.Attach(_controller, _sampler, world);
            WheelDust.Attach(_controller, _sampler, world);
            AirMotes.Attach(_camera);

            if (Flow != Screen.Play) Freeze();
            if (Flow != Screen.Play) StartCoroutine(Tickers.LoadAllPreviews());

            // Сообщение о трассе — только в заезде: титульный мир строится из фолбэка
            // по замыслу, и «нет связи» там было бы ложью.
            if (Flow == Screen.Play)
                Pop(Tickers.All[PendingTicker].Name + " · " + (shortRun ? "▼ ШОРТ · " : "") + "ВОЛАТИЛЬНОСТЬ " + _tt.VolLabel
                    + (PendingFallback ? " (нет связи — BTC)" : ""), 3.5f);

            if (ScreenshotProbe.Requested) { Flow = Screen.Play; Unfreeze(); ScreenshotProbe.Attach(this, _controller, _chase); }
        }

        private void Freeze() { _controller.SetInput(ScriptedBikeInput.HoldThrottle()); _controller.SetInput(new ScriptedBikeInput(() => BikeInputState.Neutral)); _wave.enabled = false; }
        private void Unfreeze() { _controller.SetInput(_input); _wave.enabled = true; }

        // ================= переходы =================

        private void StartTicker(int idx)
        {
            Economy.LastTicker = idx; Economy.Save();
            Flow = Screen.TickerLoad;
            StartCoroutine(Tickers.Load(Tickers.All[idx], (d, fb) =>
            {
                PendingCandles = d; PendingTicker = idx; PendingFallback = fb;
                Reload(Screen.Play);
            }));
        }

        private static void Reload(Screen next)
        {
            Flow = next;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        private void Pop(string s, float sec = 1.8f) { _pop = s; _popUntil = Time.time + sec; }

        // ================= цикл заезда =================

        private void FixedUpdate()
        {
            if (Flow == Screen.Play && _controller != null) _pump.FixedTick(_controller);
        }

        private void Update()
        {
            if (_controller == null) return;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetKeyDown(KeyCode.P))
            {
                if (Flow == Screen.Play) { Flow = Screen.Paused; Freeze(); }
                else if (Flow == Screen.Paused) { Flow = Screen.Play; Unfreeze(); }
            }
            if (Flow == Screen.Play)
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.F)) SwitchFeel();
                if (UnityEngine.Input.GetKeyDown(KeyCode.Space)) TryPump();
                var touches = UnityEngine.Input.touchCount;
                if (touches == 3 && !_switchLatch) { _switchLatch = true; SwitchFeel(); }
                else if (touches < 3) _switchLatch = false;
            }
#endif
            if (Flow != Screen.Play && Flow != Screen.Dead) { GameAudio.I.SetRun(false, 0f, false, false, false, 0f, 0f, 0f, null); return; }

            var st = _controller.State;
            GameAudio.I.SetRun(Flow == Screen.Play && !_controller.Halted, st.SpeedMPerS, st.ThrottleApplied > 0.3f,
                _pump.Active, !st.IsGrounded, Mathf.Clamp01(_airT / 1.2f), _wave != null ? _wave.Danger : 0f,
                _pump.Active ? 1f : 0f, Economy.Bikes[Economy.SelBike].Type);
            if (st.IsGrounded && _prevAirborne) _chase.Impact(_prevVerticalSpeed);
            _prevAirborne = !st.IsGrounded; _prevVerticalSpeed = st.VerticalSpeedMPerS;

            if (Flow == Screen.Play && !_controller.Halted && !_cashedOut)
            {
                _distB = Mathf.Max(_distB, Mathf.FloorToInt(st.PositionXM / UnitsContract.PxToM / 10f));
                _maxKmh = Mathf.Max(_maxKmh, st.SpeedMPerS * 3.6f);
                _position.Tick(st.PositionXM, _distB, _coinField.Collected);
                if (_coinField.JustCollected > 0) { _pump.Add(0.04f * _coinField.JustCollected); GameAudio.I.Coin(); }
                if (st.IsGrounded && st.PitchRelRad > 0.45f) _pump.Add(0.006f * 0.5f);
                if (!st.IsGrounded) _airT += Time.deltaTime; else { if (_airT > 0.47f) _pump.Add(0.3f); _airT = 0f; }

                var rew = Missions.Check(new RunStats
                {
                    Dist = _distB, Coins = _coinField.Collected, Kmh = _maxKmh, Leverage = Economy.Leverage
                }, out var done);
                if (done != null) { _missionRew += rew; Pop("ЦЕЛЬ ✓  +◆" + rew); GameAudio.I.Lev(); }

                // b1760: доехал до конца графика = финиш → фиксация по текущей цене.
                if (_distB >= _finishDistB)
                {
                    Pop("⚑ ГРАФИК " + Tickers.All[PendingTicker].Name + " ПРОЙДЕН");
                    if (_position.Value(st.PositionXM) > 0) CashOut(); else Die("cashout");
                }
            }

            if (_controller.Halted && !_committed && !_cashedOut)
            {
                var f = st.Failure;
                Die(f == BikeFailure.Liquidated ? "wave" : f == BikeFailure.Loop ? "loop"
                    : f == BikeFailure.Endo ? "endo" : f == BikeFailure.Void ? "gap" : "crash");
            }
            if (Flow == Screen.Dead) _deadFor += Time.deltaTime;
        }

        /// <summary>b1870 cashOut: TAKE PROFIT — решение «жадность ↔ безопасность».</summary>
        private void CashOut()
        {
            if (Flow != Screen.Play || _cashedOut) return;
            var val = _position.Value(_controller.State.PositionXM);
            if (val <= 0) return;
            _cashedOut = true; _committed = true; _deathBy = "cashout";
            GameAudio.I.Lev();
            _runGems = val; Economy.Bank += _runGems;
            _stakeIncome = Economy.PayStaking();
            if (_runGems > Economy.BestPnl) Economy.BestPnl = _runGems;
            if (_distB > Economy.Best) Economy.Best = _distB;
            OnbRunEnd();
            Economy.Save();
            Pop("$ ЗАФИКСИРОВАНО  +$" + _runGems.ToString("N0", CultureInfo.InvariantCulture), 2.5f);
            Freeze();
            Flow = Screen.Dead; _deadFor = 0f;
        }

        /// <summary>b1864 wipeout: в Забеге любая смерть = ликвидация (реальная потеря ∝ плечу).</summary>
        private void Die(string reason)
        {
            if (_committed) return;
            _committed = true; _deathBy = reason;
            GameAudio.I.Crash();
            _liqPen = Economy.Liquidate(); _runGems = -_liqPen;
            if (_distB > Economy.Best) Economy.Best = _distB;
            OnbRunEnd();
            Economy.Save();
            _wave.enabled = false;
            Flow = Screen.Dead; _deadFor = 0f;
        }

        /// <summary>b180 онбординг-миссии: направленная лестница первых заездов.</summary>
        private void OnbRunEnd()
        {
            var onb = new (string txt, System.Func<bool> chk, int rew)[]
            {
                ("проедь 300м", () => _distB >= 300, 60),
                ("ЗАФИКСИРУЙ профит кнопкой $", () => _cashedOut, 80),
                ("раскачай позицию до $40 — просто едь дальше", () => _position.MaxValue >= 40, 120),
            };
            if (Economy.OnbIdx >= onb.Length) return;
            var m = onb[Economy.OnbIdx];
            if (!m.chk()) return;
            Economy.Bank += m.rew; Economy.OnbIdx++;
            _onbMsg = "★ МИССИЯ ✓ «" + m.txt + "» +$" + m.rew;
        }

        private void TryPump() { if (_pump.Activate()) { Pop("⚡ PUMP!"); GameAudio.I.Lev(); } }

        private void SwitchFeel()
        {
            SelectedFeel = FeelPreset.Next(SelectedFeel);
            FeelPreset.Apply(LevelProfile, SelectedFeel);
            _feelBannerUntil = Time.time + 1.6f;
        }

        /// <summary>b1895 diamondHand: вывод $ в холод ◆ — растит ранг, $ обнуляется.</summary>
        private void DiamondHand()
        {
            var amt = Economy.Bank; if (amt <= 0) return;
            Economy.Bank = 0;
            _rankBonus = Economy.CommitCareer(amt, out _rankedUp, out _newRank);
            if (_rankedUp) GameAudio.I.RankUp(); else GameAudio.I.Lev();
            Pop("◆ ЗАКРЕПЛЕНО ◆+" + amt + " (навсегда)" + (_rankedUp ? "  " + _newRank.Emoji + " НОВЫЙ РАНГ: " + _newRank.Name : ""), 2.6f);
        }

        // ================= GUI =================

        private void OnGUI()
        {
            if (_controller == null) return;
            var scale = UnityEngine.Screen.width / W;
            var m = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            EnsureStyles();

            switch (Flow)
            {
                case Screen.Howto: DrawHowto(); break;
                case Screen.Title: DrawTitle(); break;
                case Screen.Setup: DrawSetup(); break;
                case Screen.TickerLoad: DrawLoad(); break;
                case Screen.Play: DrawPlay(); break;
                case Screen.Dead: DrawPlay(); DrawDead(); break;
                case Screen.Paused: DrawPlay(); DrawPaused(); break;
                case Screen.Garage: DrawGarage(); break;
                case Screen.Bikes: DrawBikes(); break;
                case Screen.Settings: DrawSettings(); break;
            }
            GUI.matrix = m;
        }

        // ---- helpers ----

        private static readonly Color Plate = new Color(9 / 255f, 10 / 255f, 24 / 255f, 0.78f);

        private void Panel(Rect r, Color border, Color fill, float radius = 11f, float bw = 1.4f)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, fill, Vector4.zero, Vector4.one * radius);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, border, Vector4.one * bw, Vector4.one * radius);
        }

        private bool Btn(Rect r, string label, Color col, int fs = 15, float fillA = 0.55f, string sub = null)
        {
            Panel(r, new Color(col.r, col.g, col.b, 0.9f), new Color(col.r * 0.25f, col.g * 0.25f, col.b * 0.25f, fillA));
            var st = new GUIStyle(_zone) { fontSize = fs };
            st.normal.textColor = col;
            if (sub != null)
            {
                GUI.Label(new Rect(r.x, r.y + 4f, r.width, r.height * 0.55f), label, st);
                var ss = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter };
                ss.normal.textColor = new Color(col.r, col.g, col.b, 0.7f);
                GUI.Label(new Rect(r.x, r.y + r.height * 0.55f, r.width, r.height * 0.4f), sub, ss);
            }
            else GUI.Label(r, label, st);
            var hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (hit) GameAudio.I.Click();
            return hit;
        }

        private void Label(float x, float y, float w, string s, Color c, int fs, TextAnchor a = TextAnchor.UpperLeft, bool bold = true)
        {
            var st = new GUIStyle(_hud) { fontSize = fs, alignment = a, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal };
            st.normal.textColor = c;
            GUI.Label(new Rect(x, y, w, fs + 8f), s, st);
        }

        private void Dim(float a) { GUI.color = new Color(0.03f, 0.02f, 0.07f, a); GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture); GUI.color = Color.white; }

        private static readonly Color Mint = new Color(0.62f, 0.94f, 0.78f), Gold = new Color(1f, 0.84f, 0.47f),
            Rose = new Color(1f, 0.48f, 0.54f), Ice = new Color(0.81f, 0.88f, 1f), Dimc = new Color(0.48f, 0.55f, 0.69f);

        // ---- HOWTO (b108, 4 карточки) ----
        private void DrawHowto()
        {
            Dim(0.96f);
            Label(0, H * 0.14f, W, "КАК ИГРАТЬ", Ice, 26, TextAnchor.MiddleCenter);
            var steps = new[]
            {
                ("1", "ОСЕДЛАЙ РЕАЛЬНЫЙ ГРАФИК", "едешь по живому графику крипто-монеты", Mint),
                ("2", "ПЛЕЧО МНОЖИТ ДВИЖЕНИЕ", "цена вверх × плечо = позиция растёт", Gold),
                ("3", "ЗАФИКСЬ ДО ВОЛНЫ", "фиксь на пампе, пока волна не догнала", new Color(1f, 0.6f, 0.48f)),
                ("4", "УПРАВЛЕНИЕ", "держи ГАЗ · НОС↑ = вилли · НОС↓ = прижать", new Color(0.6f, 0.8f, 1f)),
            };
            var y = H * 0.20f;
            foreach (var s in steps)
            {
                var r = new Rect(30f, y, W - 60f, 66f);
                Panel(r, new Color(0.35f, 0.42f, 0.6f, 0.5f), new Color(0.08f, 0.09f, 0.17f, 0.9f));
                Label(r.x + 14f, r.y + 14f, 40f, s.Item1, s.Item4, 28, TextAnchor.MiddleLeft);
                Label(r.x + 54f, r.y + 12f, r.width - 60f, s.Item2, Ice, 15);
                Label(r.x + 54f, r.y + 36f, r.width - 60f, s.Item3, Dimc, 11, TextAnchor.UpperLeft, false);
                y += 78f;
            }
            Label(0, y + 6f, W, "теряешь — только от волны/краша, не от цены", Dimc, 11, TextAnchor.MiddleCenter, false);
            Label(0, y + 24f, W, "$ — деньги заезда (риск)   ·   ◆ — гемы навсегда (ранг)", Dimc, 10, TextAnchor.MiddleCenter, false);
            if (Btn(new Rect(60f, y + 60f, W - 120f, 50f), "▶ ВЫБРАТЬ МОНЕТУ", Mint, 18))
            {
                Economy.SeenHowto = true; Economy.Save(); Flow = Screen.Setup; StartCoroutine(Tickers.LoadAllPreviews());
            }
            if (Btn(new Rect(W / 2f - 50f, y + 120f, 100f, 30f), "позже", Dimc, 12, 0.2f))
            {
                Economy.SeenHowto = true; Economy.Save(); Flow = Screen.Title;
            }
        }

        // ---- TITLE (b108/b252 минимал-Главная) ----
        private void DrawTitle()
        {
            Dim(0.55f);
            // Верхняя панель: ⚙ · $ ◆ ранг · ▣ ГАРАЖ
            if (Btn(new Rect(16f, 16f, 46f, 38f), "⚙", Ice, 18, 0.35f)) Flow = Screen.Settings;
            var rk = Economy.Ranks[Economy.RankIdx(Economy.Career)];
            var chip = new Rect(66f, 18f, W - 184f, 34f);
            Panel(chip, new Color(0.47f, 0.55f, 0.7f, 0.35f), new Color(0.09f, 0.11f, 0.18f, 0.7f));
            Label(chip.x, chip.y + 7f, chip.width * 0.5f, "$" + Economy.Bank, Gold, 13, TextAnchor.MiddleCenter);
            Label(chip.x + chip.width * 0.45f, chip.y + 7f, chip.width * 0.42f, "◆" + Economy.Career, new Color(0.62f, 0.88f, 1f), 13, TextAnchor.MiddleCenter);
            Label(chip.x + chip.width * 0.84f, chip.y + 7f, chip.width * 0.16f, rk.Emoji, Ice, 13, TextAnchor.MiddleCenter);
            if (GUI.Button(chip, GUIContent.none, GUIStyle.none)) Flow = Screen.Garage;
            if (Btn(new Rect(W - 112f, 16f, 96f, 38f), "▣ ГАРАЖ", Mint, 13, 0.5f)) Flow = Screen.Garage;

            Label(0, H * 0.085f + 14f, W, "CHART RUNNER", Ice, 33, TextAnchor.MiddleCenter);
            Label(0, H * 0.085f + 52f, W, "гоняй по РЕАЛЬНОМУ крипто-графику", Dimc, 11, TextAnchor.MiddleCenter, false);

            // ▶ ИГРАТЬ — доминирующая карточка: тап = сразу заезд на последней монете.
            var lc = Tickers.All[Mathf.Clamp(Economy.LastTicker, 0, Tickers.All.Length - 1)];
            var my = H * 0.085f + H * 0.205f + 14f;
            var play = new Rect(18f, my, W - 36f, 80f);
            var pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 4.2f);
            Panel(play, new Color(0.47f, 1f, 0.71f, 0.55f + 0.4f * pulse), new Color(0.06f, 0.23f, 0.16f, 0.85f), 14f, 2.6f);
            Label(play.x + 16f, play.y + 12f, 200f, "▶ ИГРАТЬ", Mint, 22);
            Tickers.Previews.TryGetValue(lc.Key, out var pv);
            Label(play.x + 16f, play.y + 46f, 300f, "ЗАБЕГ · " + lc.Name + (pv.Spark != null ? "  " + Tickers.FmtPrice(pv.Price) + "  " + (pv.Up ? "+" : "") + (pv.Chg * 100f).ToString("0.0") + "%" : "")
                + "  ×" + Economy.Leverage, Dimc, 11, TextAnchor.UpperLeft, false);
            if (GUI.Button(play, GUIContent.none, GUIStyle.none)) StartTicker(Economy.LastTicker);
            my += 88f;
            if (Btn(new Rect(18f, my, W - 36f, 44f), "≣ ТЕРМИНАЛ — монета · плечо · лонг/шорт", Ice, 13, 0.35f)) Flow = Screen.Setup;
            my += 54f;
            if (Btn(new Rect(18f, my, (W - 44f) / 2f, 54f), "◆ ЗАКРЕПИТЬ", new Color(0.62f, 0.88f, 1f), 14, 0.4f, "$" + Economy.Bank + " → ◆ навсегда"))
                DiamondHand();
            if (Btn(new Rect(18f + (W - 44f) / 2f + 8f, my, (W - 44f) / 2f, 54f), "? КАК ИГРАТЬ", Dimc, 14, 0.3f)) Flow = Screen.Howto;
            my += 66f;
            Label(0, my, W, "рекорд " + Economy.Best + " м   ·   лучший фикс $" + Economy.BestPnl, Dimc, 11, TextAnchor.MiddleCenter, false);

            DrawPop();
        }

        // ---- SETUP (b107 терминал) ----
        private void DrawSetup()
        {
            Dim(0.94f);
            Label(0, 26f, W, "ВЫБЕРИ МОНЕТУ", Ice, 22, TextAnchor.MiddleCenter);
            Label(0, 54f, W, "спарклайн = реальный график = превью трассы", Dimc, 10, TextAnchor.MiddleCenter, false);
            if (Btn(new Rect(14f, 16f, 78f, 32f), "‹ НАЗАД", Ice, 13, 0.35f)) Flow = Screen.Title;

            const float gap = 6f, x0 = 16f, w0 = W - 32f, y0 = 78f;
            var rowH = Mathf.Clamp(Mathf.Floor((H - y0 - 64f) / Tickers.All.Length) - gap, 38f, 50f);
            for (var i = 0; i < Tickers.All.Length; i++)
            {
                var tk = Tickers.All[i]; var ry = y0 + i * (rowH + gap); var sel = i == _setupSel;
                var r = new Rect(x0, ry, w0, rowH);
                Panel(r, sel ? new Color(1f, 0.48f, 0.29f, 0.95f) : new Color(0.47f, 0.55f, 0.7f, 0.3f),
                    sel ? new Color(0.16f, 0.08f, 0.06f, 0.85f) : new Color(0.08f, 0.09f, 0.15f, 0.6f), 11f, sel ? 2.4f : 1.2f);
                Label(x0 + 14f, ry + 6f, 90f, tk.Name + (sel ? " ✓" : ""), Ice, 15);
                if (Tickers.Previews.TryGetValue(tk.Key, out var pv))
                {
                    Label(x0 + 14f, ry + rowH - 20f, 90f, Tickers.FmtPrice(pv.Price), Dimc, 10, TextAnchor.UpperLeft, false);
                    DrawSpark(pv.Spark, x0 + 96f, ry + 10f, w0 - 208f, rowH - 20f, pv.Up ? new Color(0.5f, 0.9f, 0.66f) : new Color(1f, 0.48f, 0.42f));
                    var tc = pv.VolLabel == "НИЗК" ? Mint : pv.VolLabel == "СРЕД" ? Gold : pv.VolLabel == "ВЫС" ? new Color(1f, 0.69f, 0.44f) : new Color(1f, 0.48f, 0.42f);
                    Label(x0 + w0 - 90f, ry + 6f, 76f, (pv.Up ? "+" : "") + (pv.Chg * 100f).ToString("0.0") + "%", pv.Up ? new Color(0.5f, 0.9f, 0.66f) : new Color(1f, 0.48f, 0.42f), 11, TextAnchor.UpperRight);
                    Label(x0 + w0 - 90f, ry + rowH - 20f, 76f, pv.VolLabel, tc, 9, TextAnchor.UpperRight);
                }
                else Label(x0 + w0 - 100f, ry + rowH / 2f - 8f, 86f, "загрузка…", Dimc, 10, TextAnchor.UpperRight, false);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) _setupSel = i;
            }
            var lb = Mathf.Min(y0 + Tickers.All.Length * (rowH + gap) + 6f, H - 56f);
            var lw = Mathf.Round(W * 0.33f); var sw2 = Mathf.Round(W * 0.24f);
            if (Btn(new Rect(x0, lb, lw, 48f), "⚡ ×" + Economy.Leverage, Gold, 15, 0.7f, "плечо · тап")) Economy.CycleLeverage();
            if (Btn(new Rect(x0 + lw + 8f, lb, sw2, 48f), Economy.ShortMode ? "▼ ШОРТ" : "▲ ЛОНГ",
                    Economy.ShortMode ? Rose : Mint, 14, 0.7f, Economy.ShortMode ? "профит на дампе" : "профит на пампе"))
            { Economy.ShortMode = !Economy.ShortMode; Economy.Save(); }
            if (Btn(new Rect(x0 + lw + sw2 + 16f, lb, w0 - lw - sw2 - 16f, 48f), "▶ ГОНКА", Mint, 15, 0.55f, Tickers.All[_setupSel].Name + " ×" + Economy.Leverage))
                StartTicker(_setupSel);
        }

        private void DrawSpark(float[] arr, float sx, float sy, float sw, float sh, Color col)
        {
            if (arr == null || arr.Length < 2) return;
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var v in arr) { mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v); }
            var rng = Mathf.Max(1e-12f, mx - mn);
            GUI.color = col;
            for (var i = 0; i < arr.Length; i++)
            {
                var x = sx + sw * i / (arr.Length - 1);
                var y = sy + sh - (arr[i] - mn) / rng * sh;
                GUI.DrawTexture(new Rect(x - 1f, y - 1.2f, 2.4f, 2.4f), Texture2D.whiteTexture);
                if (i > 0)
                {
                    var py = sy + sh - (arr[i - 1] - mn) / rng * sh; var px = sx + sw * (i - 1) / (arr.Length - 1);
                    for (var k = 1; k < 4; k++)
                    {
                        var t = k / 4f;
                        GUI.DrawTexture(new Rect(Mathf.Lerp(px, x, t) - 0.8f, Mathf.Lerp(py, y, t) - 0.8f, 1.6f, 1.6f), Texture2D.whiteTexture);
                    }
                }
            }
            GUI.color = Color.white;
        }

        private void DrawLoad()
        {
            Dim(0.8f);
            var name = Tickers.All[Mathf.Clamp(Economy.LastTicker, 0, Tickers.All.Length - 1)].Name;
            Label(0, H * 0.44f, W, "ЗАГРУЖАЮ " + name + "…", Ice, 20, TextAnchor.MiddleCenter);
            Label(0, H * 0.44f + 30f, W, "реальные свечи · 5 минут · Binance", Dimc, 11, TextAnchor.MiddleCenter, false);
        }

        // ---- PLAY HUD ----
        private void DrawPlay()
        {
            var st = _controller.State;
            var kmh = st.SpeedMPerS * 3.6f;
            // Строка тикера: монета · цена · PnL позиции.
            var price = _tt.PriceAt(st.PositionXM);
            var pnl = _position.Pnl(st.PositionXM);
            Label(14f, 10f, 300f, Tickers.All[PendingTicker].Name + "  " + Tickers.FmtPrice(price), Ice, 14);
            Label(14f, 30f, 300f, _distB + " м   " + kmh.ToString("0") + " км/ч", Ice, 13);
            var pc = pnl >= 0 ? Mint : Rose;
            Label(W - 160f, 10f, 146f, "$" + Economy.Bank, Gold, 14, TextAnchor.UpperRight);
            Label(W - 160f, 30f, 146f, (pnl >= 0 ? "+" : "") + (pnl * 100f).ToString("0.0") + "%  ×" + Economy.Leverage, pc, 13, TextAnchor.UpperRight);

            // Цели (3 миссии).
            var y = 56f;
            foreach (var mi in Missions.Active)
            {
                Label(14f, y, 260f, (mi.Done ? "✓ " : "· ") + mi.Text + "  +◆" + mi.Reward, mi.Done ? Mint : new Color(0.7f, 0.76f, 0.88f, 0.85f), 10, TextAnchor.UpperLeft, false);
                y += 14f;
            }

            // Тревога волны (порт: красная виньетка слева ∝ близости).
            if (_wave != null && Flow == Screen.Play)
            {
                var d = _wave.Danger;
                if (d > 0.02f)
                {
                    var pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * (4f + 10f * d));
                    GUI.color = new Color(1f, 0.18f, 0.28f, d * d * 0.55f * pulse);
                    GUI.DrawTexture(new Rect(0f, 0f, 26f + 60f * d, H), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
            }

            // b71 TAKE-PROFIT: кнопка «ЗАФИКСИТЬ $X ×lev» под КОМБО, когда позиция ≥ 3.
            if (Flow == Screen.Play && !_cashedOut)
            {
                var val = _position.Value(st.PositionXM);
                if (val >= 3)
                {
                    var peak = pnl > 0.006f;
                    var urge = _wave.Danger > 0.4f;
                    var pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (3f + 8f * _wave.Danger));
                    var col = peak ? new Color(1f, 0.9f, 0.42f) : urge ? new Color(1f, 0.82f, 0.66f) : Mint;
                    var r = new Rect(W / 2f - 94f, 124f, 188f, 38f);
                    Panel(r, new Color(col.r, col.g, col.b, 0.5f + 0.5f * pulse), urge ? new Color(0.2f, 0.09f, 0.07f, 0.9f) : new Color(0.06f, 0.16f, 0.11f, 0.86f), 10f, urge ? 2.6f : 2f);
                    Label(r.x, r.y + 10f, r.width, (peak ? "▲ ПИК! ЗАБЕРИ $" : urge ? "$ ЗАБЕРИ $" : "$ ЗАФИКСИТЬ $") + val + "  ×" + Economy.Leverage, col, 14, TextAnchor.MiddleCenter);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) CashOut();
                }
            }

            // PUMP (btn4: слева над парой наклона, 174×48).
            if (Flow == Screen.Play)
            {
                var r = new Rect(14f, H - 178f, 174f, 48f);
                var ready = _pump.Ready;
                var col = ready ? Gold : new Color(0.7f, 0.62f, 0.4f);
                Panel(r, new Color(col.r, col.g, col.b, ready ? 0.95f : 0.4f), new Color(0.16f, 0.13f, 0.06f, 0.75f), 10f, ready ? 2.4f : 1.4f);
                GUI.DrawTexture(new Rect(r.x + 4f, r.yMax - 8f, (r.width - 8f) * _pump.Charge, 4f), Texture2D.whiteTexture,
                    ScaleMode.StretchToFill, true, 0f, new Color(col.r, col.g, col.b, 0.9f), Vector4.zero, Vector4.one * 2f);
                Label(r.x, r.y + 8f, r.width, _pump.Active ? "⚡ РЫВОК" : ready ? "⚡ PUMP!" : "PUMP", col, 15, TextAnchor.MiddleCenter);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) TryPump();

                if (Btn(new Rect(W - 58f, 62f, 44f, 34f), "II", Dimc, 14, 0.3f)) { Flow = Screen.Paused; Freeze(); }
            }

            if (Time.time < _feelBannerUntil)
                DrawChip("ФИЛ: " + FeelPreset.Name(SelectedFeel), W / 2f, H * 0.30f, new Color(0.59f, 0.8f, 1f), _warn);

            DrawButtons();
            DrawPop();
        }

        private void DrawPop()
        {
            if (Time.time >= _popUntil) return;
            var a = Mathf.Clamp01((_popUntil - Time.time) * 2f);
            var st = new GUIStyle(_warn) { fontSize = 16, wordWrap = true };
            st.normal.textColor = new Color(Gold.r, Gold.g, Gold.b, a);
            var py = Flow == Screen.Play || Flow == Screen.Dead ? H * 0.36f : H * 0.80f;
            GUI.Label(new Rect(20f, py, W - 40f, 60f), _pop, st);
        }

        // ---- DEAD (b132 деклаттер: A = заголовок, B = деньги, C = дистанция/рекорд) ----
        private void DrawDead()
        {
            Dim(0.66f);
            var title = _cashedOut ? "$ ПРОФИТ ЗАФИКСИРОВАН" : _deathBy == "wave" ? "ЛИКВИДИРОВАН ДАМПОМ" : _deathBy == "loop" ? "ОПРОКИНУЛСЯ"
                : _deathBy == "endo" ? "ЧЕРЕЗ РУЛЬ" : _deathBy == "gap" ? "УПАЛ В ПРОПАСТЬ" : "РАЗБИЛСЯ";
            Label(0, H * 0.24f, W, title, _cashedOut ? Mint : Rose, 24, TextAnchor.MiddleCenter);
            if (!_cashedOut)
            {
                var taunt = Economy.Leverage >= 50 ? "×" + Economy.Leverage + " — дерзко, рынок не прощает"
                    : _deathBy == "wave" ? "волна дампа догнала — газуй раньше"
                    : _deathBy == "crash" || _deathBy == "loop" || _deathBy == "endo" ? "разбился — мягче приземляйся" : "надо было ЗАФИКСИТЬ";
                Label(0, H * 0.24f + 30f, W, taunt, new Color(1f, 0.69f, 0.63f), 12, TextAnchor.MiddleCenter, false);
            }
            var gain = _runGems;
            var isBest = _cashedOut && gain >= Economy.BestPnl && gain > 0;
            var gf = _cashedOut ? Mathf.Min(58f, 42f + Mathf.Sqrt(Mathf.Max(0, gain)) * 0.6f) : 42f;
            Label(0, H * 0.34f + 8f, W, (_cashedOut ? "$ +" : "$ ") + gain.ToString("N0", CultureInfo.InvariantCulture),
                _cashedOut ? (isBest ? Gold : Mint) : Rose, Mathf.RoundToInt(gf), TextAnchor.MiddleCenter);
            Label(0, H * 0.34f + 74f, W, _distB + "м  ·  рекорд " + Economy.Best + "м", new Color(0.8f, 0.75f, 0.94f), 15, TextAnchor.MiddleCenter);
            Label(0, H * 0.34f + 96f, W, (_cashedOut ? "$ " + Economy.Bank + "  ·  ◆ " + Economy.Career : "осталось  $ " + Economy.Bank)
                + (_stakeIncome > 0 ? "  ◆+$" + _stakeIncome : ""), _cashedOut ? Mint : new Color(1f, 0.69f, 0.63f), 15, TextAnchor.MiddleCenter);
            if (_missionRew > 0) Label(0, H * 0.34f + 118f, W, "цели заезда  +◆" + _missionRew, Mint, 12, TextAnchor.MiddleCenter);
            if (!string.IsNullOrEmpty(_onbMsg)) Label(20f, H * 0.34f + 140f, W - 40f, _onbMsg, Gold, 12, TextAnchor.MiddleCenter);
            var tip = _deathBy == "wave" ? "◈ качай ЩИТ ОТ ВОЛНЫ — оторвёшься от дампа"
                : _deathBy == "loop" || _deathBy == "endo" ? "◎ качай СЦЕПЛЕНИЕ — прощает кувырки"
                : _deathBy == "crash" ? "◎ качай СЦЕПЛЕНИЕ — мягче посадки" : "⚙ качай ДВИЖОК — быстрее волны";
            if (!_cashedOut) Label(20f, H * 0.34f + 164f, W - 40f, tip, Dimc, 11, TextAnchor.MiddleCenter, false);

            var by = H * 0.62f;
            if (_deadFor > 0.5f)
            {
                if (Btn(new Rect(W / 2f - 100f, by, 200f, 50f), "↻ ЕЩЁ РАЗ", Mint, 18)) Reload(Screen.Play);
                if (Btn(new Rect(24f, by + 60f, (W - 56f) / 2f, 44f), "▣ ГАРАЖ", new Color(0.75f, 0.88f, 1f), 14, 0.4f)) Flow = Screen.Garage;
                if (Btn(new Rect(24f + (W - 56f) / 2f + 8f, by + 60f, (W - 56f) / 2f, 44f), "≡ МЕНЮ", Dimc, 14, 0.3f)) Reload(Screen.Title);
            }
        }

        private void DrawPaused()
        {
            Dim(0.6f);
            Label(0, H * 0.36f, W, "ПАУЗА", Ice, 26, TextAnchor.MiddleCenter);
            if (Btn(new Rect(W / 2f - 100f, H * 0.46f, 200f, 48f), "▶ ПРОДОЛЖИТЬ", Mint, 16)) { Flow = Screen.Play; Unfreeze(); }
            if (Btn(new Rect(W / 2f - 100f, H * 0.46f + 60f, 200f, 44f), "≡ МЕНЮ", Dimc, 14, 0.3f)) Reload(Screen.Title);
        }

        // ---- GARAGE (b88/b143: 7 апгрейдов, прокачка у каждого байка отдельная) ----
        private void DrawGarage()
        {
            Dim(0.94f);
            Label(0, 28f, W, "▣ ГАРАЖ", Ice, 24, TextAnchor.MiddleCenter);
            Label(0, 60f, W, "$ " + Economy.Bank, Gold, 20, TextAnchor.MiddleCenter);
            if (Btn(new Rect(W - 110f, 18f, 94f, 34f), "▣ БАЙКИ", new Color(0.75f, 0.88f, 1f), 13, 0.4f)) Flow = Screen.Bikes;
            var rk = Economy.Ranks[Economy.RankIdx(Economy.Career)];
            var ni = Economy.RankIdx(Economy.Career) + 1;
            var nr = ni < Economy.Ranks.Length ? "  до " + Economy.Ranks[ni].Emoji + " ◆" + Mathf.Max(0, Economy.Ranks[ni].At - Economy.Career) : "  ВЕРШИНА";
            Label(0, 86f, W, rk.Emoji + " " + rk.Name + nr, new Color(0.62f, 0.88f, 0.78f), 11, TextAnchor.MiddleCenter);
            Label(0, 106f, W, "▣ качаешь: " + Economy.Bikes[Economy.SelBike].Name, new Color(0.59f, 0.86f, 1f), 13, TextAnchor.MiddleCenter);

            const float rowH = 60f, gap = 6f, x0 = 18f, w0 = W - 36f, y0 = 136f;
            for (var i = 0; i < Economy.Upgrades.Length; i++)
            {
                var u = Economy.Upgrades[i]; var lvl = Economy.UpgLvl(u.Id); var maxed = lvl >= u.Max;
                var cost = Economy.UpgCost(u.Id, lvl); var afford = !maxed && Economy.Bank >= cost;
                var r = new Rect(x0, y0 + i * (rowH + gap), w0, rowH);
                Panel(r, afford ? new Color(0.47f, 1f, 0.71f, 0.6f) : maxed ? new Color(1f, 0.84f, 0.47f, 0.5f) : new Color(0.47f, 0.55f, 0.7f, 0.28f),
                    afford ? new Color(0.11f, 0.19f, 0.16f, 0.72f) : new Color(0.08f, 0.09f, 0.15f, 0.62f), 12f, 1.4f);
                Label(r.x + 8f, r.y + 16f, 40f, u.Icon, Color.white, 22, TextAnchor.MiddleCenter);
                Label(r.x + 52f, r.y + 8f, 220f, u.Name, Ice, 14);
                Label(r.x + 52f, r.y + 28f, 240f, lvl > 0 ? u.Eff(lvl) : "пока без бонуса", new Color(0.49f, 0.88f, 0.69f), 11, TextAnchor.UpperLeft, false);
                if (!maxed) Label(r.x + 52f, r.y + 44f, 240f, "след. ур: " + u.Eff(lvl + 1), Dimc, 9, TextAnchor.UpperLeft, false);
                for (var p = 0; p < u.Max; p++)
                {
                    GUI.color = p < lvl ? new Color(0.47f, 1f, 0.71f, 0.95f) : new Color(0.47f, 0.55f, 0.7f, 0.28f);
                    GUI.DrawTexture(new Rect(r.xMax - 148f + p * 14f - 4f, r.y + 12f, 8f, 8f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, GUI.color, Vector4.zero, Vector4.one * 4f);
                }
                GUI.color = Color.white;
                if (maxed) Label(r.xMax - 118f, r.y + 30f, 100f, "МАКС", Gold, 15, TextAnchor.UpperRight);
                else
                {
                    Label(r.xMax - 118f, r.y + 26f, 100f, "$ " + cost, afford ? Mint : new Color(0.48f, 0.43f, 0.6f), 17, TextAnchor.UpperRight);
                    Label(r.xMax - 118f, r.y + 46f, 100f, afford ? "▸ КУПИТЬ" : "мало $", afford ? new Color(0.59f, 1f, 0.78f, 0.8f) : Dimc, 9, TextAnchor.UpperRight);
                }
                if (afford && GUI.Button(r, GUIContent.none, GUIStyle.none)) Economy.BuyUpgrade(u.Id);
            }
            if (Btn(new Rect(W / 2f - 90f, H - 74f, 180f, 46f), "←  НАЗАД", Ice, 15, 0.35f))
                Flow = _controller.Halted || _cashedOut ? Screen.Dead : Screen.Title;
        }

        /// <summary>
        /// БАЙКИ — порт экрана b108/b115/b176: превью-спрайт, ‹ › перебор, полоски
        /// СКОР/ХВАТ (нормировка исходника: accel 0.28..0.62, grip 1.06..1.78), покупка за $,
        /// замок по рангу ◆, ряд скинов (сток + 4 палитры). Смена байка/скина перестраивает
        /// героя при следующем заезде (спрайты грузятся по выбору в BikeView).
        /// </summary>
        private static Texture2D _bikePreview; private static string _bikePreviewTag = "";
        private void DrawBikes()
        {
            Dim(0.94f);
            Label(0, 28f, W, "▣ БАЙКИ", Ice, 24, TextAnchor.MiddleCenter);
            Label(0, 60f, W, "$ " + Economy.Bank, Gold, 16, TextAnchor.MiddleCenter);
            if (Btn(new Rect(14f, 18f, 78f, 34f), "‹ НАЗАД", Ice, 13, 0.35f)) Flow = Screen.Garage;

            var i = Economy.SelBike; var b = Economy.Bikes[i];
            var own = Economy.Owned.Contains(i);
            var locked = b.ReqRank > 0 && Economy.RankIdx(Economy.Career) < b.ReqRank;

            // Превью: спрайт корпуса выбранного байка и скина.
            var tag = i + (string.IsNullOrEmpty(Economy.CurrentSkin) ? "" : "-" + Economy.CurrentSkin);
            if (_bikePreviewTag != tag) { var sp = Resources.Load<Sprite>("Art/bike-" + tag + "-body"); _bikePreview = sp != null ? sp.texture : null; _bikePreviewTag = tag; }
            if (_bikePreview != null)
            {
                var pw = 300f; var ph = pw * _bikePreview.height / _bikePreview.width;
                GUI.DrawTexture(new Rect(W / 2f - pw / 2f, H * 0.27f - ph / 2f, pw, ph), _bikePreview, ScaleMode.ScaleToFit, true);
            }

            var ay = H * 0.40f;
            if (Btn(new Rect(W / 2f - 150f, ay - 30f, 50f, 64f), "‹", Ice, 30, 0.2f)) { Economy.SelBike = (i + Economy.Bikes.Length - 1) % Economy.Bikes.Length; Economy.Save(); }
            if (Btn(new Rect(W / 2f + 100f, ay - 30f, 50f, 64f), "›", Ice, 30, 0.2f)) { Economy.SelBike = (i + 1) % Economy.Bikes.Length; Economy.Save(); }
            Label(0, ay - 14f, W, b.Name, Ice, 26, TextAnchor.MiddleCenter);

            var sp0 = Mathf.Clamp01((b.Accel - 0.28f) / (0.62f - 0.28f));
            var gr0 = Mathf.Clamp01((b.Grip - 1.06f) / (1.78f - 1.06f));
            StatBar("СКОР", sp0, new Color(0.47f, 1f, 0.71f), ay + 26f);
            StatBar("ХВАТ", gr0, new Color(0.47f, 0.78f, 1f), ay + 42f);
            Label(0, ay + 62f, W, b.Tag, new Color(0.65f, 0.73f, 0.84f), 11, TextAnchor.MiddleCenter, false);
            if (own)
            {
                var lv = 0; var mx = 0;
                foreach (var u in Economy.Upgrades) { lv += Economy.UpgLvl(u.Id); mx += u.Max; }
                Label(0, ay + 80f, W, "⚙ прокачка этого байка: " + lv + "/" + mx + (lv > 0 ? " · качай в ГАРАЖЕ" : ""), new Color(0.47f, 1f, 0.71f, 0.7f), 9, TextAnchor.MiddleCenter, false);
            }

            var act = new Rect(W / 2f - 110f, ay + 96f, 220f, 50f);
            var actCol = own ? Mint : locked ? new Color(0.73f, 0.78f, 0.93f) : Economy.Bank >= b.Cost ? Gold : new Color(0.56f, 0.57f, 0.65f);
            var actTxt = own ? "✓ ВЫБРАН" : locked ? "⊘ РАНГ " + Economy.Ranks[b.ReqRank].Emoji + " " + Economy.Ranks[b.ReqRank].Name : "РАЗБЛОКИРОВАТЬ  $" + b.Cost;
            if (Btn(act, actTxt, actCol, 15, 0.3f) && !own && !locked) Economy.BuyBike(i);
            if (locked) Label(0, act.yMax + 2f, W, "закрепляй $→◆ до ранга " + Economy.Ranks[b.ReqRank].Name + " (◆" + Economy.Ranks[b.ReqRank].At + ")", new Color(0.59f, 0.71f, 0.9f, 0.7f), 9, TextAnchor.MiddleCenter, false);

            // Ряд скинов: сток + 4 палитры (b176).
            var sy = ay + 172f; var n = Economy.Skins.Length + 1; var spx = 52f; var x00 = W / 2f - (n - 1) * spx / 2f;
            for (var k = 0; k < n; k++)
            {
                var x = x00 + k * spx;
                var id = k > 0 ? Economy.Skins[k - 1].Id : "";
                var cur = Economy.CurrentSkin == id;
                var r = new Rect(x - 22f, sy - 22f, 44f, 56f);
                var owned = k == 0 || Economy.SkinOwned(i, id);
                var col = k == 0 ? Ice : Economy.Skins[k - 1].Accent.Split(',') is var pc
                    ? new Color(int.Parse(pc[0]) / 255f, int.Parse(pc[1]) / 255f, int.Parse(pc[2]) / 255f) : Ice;
                Panel(r, new Color(col.r, col.g, col.b, cur ? 1f : 0.4f), new Color(col.r * 0.2f, col.g * 0.2f, col.b * 0.2f, 0.6f), 9f, cur ? 2.4f : 1.2f);
                Label(r.x, r.y + 4f, r.width, k == 0 ? "СТОК" : Economy.Skins[k - 1].Name, col, 8, TextAnchor.MiddleCenter);
                Label(r.x, r.y + 30f, r.width, k == 0 ? "" : owned ? (cur ? "✓" : "надеть") : "$" + Economy.Skins[k - 1].Cost, owned ? col : Dimc, 8, TextAnchor.MiddleCenter, false);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    GameAudio.I.Click();
                    if (k == 0) { Economy.SkinSel.Remove(i); Economy.Save(); }
                    else if (owned) { Economy.SkinSel[i] = id; Economy.Save(); }
                    else if (!Economy.BuySkin(i, id)) Pop(own ? "мало $" : "сначала разблокируй байк");
                }
            }
        }

        private void StatBar(string lab, float val, Color col, float yy)
        {
            const float bw = 120f; var bx = W / 2f - 30f;
            Label(bx - 96f, yy - 4f, 88f, lab, new Color(0.75f, 0.8f, 0.92f, 0.8f), 10, TextAnchor.UpperRight);
            GUI.DrawTexture(new Rect(bx, yy, bw, 7f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.12f, 0.16f, 0.24f, 0.7f), Vector4.zero, Vector4.one * 3f);
            GUI.DrawTexture(new Rect(bx, yy, bw * val, 7f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(col.r, col.g, col.b, 0.92f), Vector4.zero, Vector4.one * 3f);
        }

        private void DrawSettings()
        {
            Dim(0.96f);
            Label(0, H * 0.065f, W, "⚙ НАСТРОЙКИ", Ice, 23, TextAnchor.MiddleCenter);
            var y = H * 0.14f;
            if (Btn(new Rect(W / 2f - 146f, y, 292f, 56f), Economy.Muted ? "ЗВУК: ВЫКЛ" : "ЗВУК: ВКЛ", Ice, 15, 0.35f)) { Economy.Muted = !Economy.Muted; Economy.Save(); }
            y += 70f;
            if (Btn(new Rect(W / 2f - 146f, y, 292f, 56f), "ФИЛ: " + FeelPreset.Name(SelectedFeel), Ice, 15, 0.35f, "тап — переключить")) SwitchFeel();
            y += 70f;
            if (Btn(new Rect(W / 2f - 146f, y, 292f, 56f), "✕ СБРОСИТЬ ПРОГРЕСС", Rose, 14, 0.3f)) { Economy.ResetProgress(); Pop("✅ ПРОГРЕСС ОБНУЛЁН"); }
            if (Btn(new Rect(W / 2f - 90f, H - 74f, 180f, 46f), "←  НАЗАД", Ice, 15, 0.35f)) Flow = Screen.Title;
            DrawPop();
        }

        // ---- кнопки управления btn4 (порт ctlBtn) ----
        private void DrawButtons()
        {
            var b = _input.Buttons;
            DrawButton(b.Gas, b.Gas.Active ? new Color(120 / 255f, 1f, 180 / 255f) : new Color(120 / 255f, 205 / 255f, 160 / 255f), 18);
            DrawButton(b.Brake, b.Brake.Active ? new Color(1f, 175 / 255f, 120 / 255f) : new Color(200 / 255f, 155 / 255f, 135 / 255f), 14);
            DrawButton(b.NoseUp, new Color(150 / 255f, 205 / 255f, 1f), 14);
            DrawButton(b.NoseDown, new Color(150 / 255f, 205 / 255f, 1f), 14);
        }

        private void DrawButton(TouchButtons.Zone z, Color col, int fontSize)
        {
            var r = z.R; var radius = Vector4.one * 10f;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(col.r, col.g, col.b, z.Active ? 0.34f : 0.12f), Vector4.zero, radius);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(col.r, col.g, col.b, z.Active ? 1f : 0.5f), Vector4.one * (z.Active ? 2.6f : 1.4f), radius);
            _zone.fontSize = fontSize; _zone.normal.textColor = new Color(col.r, col.g, col.b, z.Active ? 1f : 0.82f);
            GUI.Label(r, z.Label, _zone);
        }

        private void DrawChip(string txt, float cx, float cy, Color col, GUIStyle style, float a = 1f)
        {
            var content = new GUIContent(txt); var size = style.CalcSize(content);
            var r = new Rect(cx - size.x / 2f - 8f, cy - size.y / 2f - 4f, size.x + 16f, size.y + 8f);
            Panel(r, new Color(col.r, col.g, col.b, 0.85f * a), new Color(Plate.r, Plate.g, Plate.b, Plate.a * a), 6f);
            style.normal.textColor = new Color(236 / 255f, 243 / 255f, 253 / 255f, 0.95f * a);
            GUI.Label(r, content, style);
        }

        private void EnsureStyles()
        {
            if (_hud != null) return;
            _mono = Font.CreateDynamicFontFromOSFont(new[] { "Menlo", "Menlo-Regular", "Courier", "Courier New" }, 15);
            _hud = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold };
            if (_mono != null) _hud.font = _mono;
            _hud.normal.textColor = new Color(0.86f, 0.93f, 0.98f, 0.92f);
            _big = new GUIStyle(_hud) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            _zone = new GUIStyle(_hud) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            _warn = new GUIStyle(_hud) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            _small = new GUIStyle(_hud) { fontSize = 10, fontStyle = FontStyle.Normal };
        }
    }
}
