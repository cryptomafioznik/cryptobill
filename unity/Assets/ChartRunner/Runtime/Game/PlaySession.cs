using ChartRunner.Bike;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Играбельная сессия: трасса, байк, камера, ввод живого игрока, рестарт, HUD.
    ///
    /// ЗАЧЕМ ЭТОТ КЛАСС СУЩЕСТВУЕТ. За ночь переноса физику прогнали 31 автотестом и ни разу
    /// не взяли в руки. Автотест умеет сказать «не сломано» и не умеет сказать «интересно»
    /// — измерено: скриптовый пилот «только газ» проходит 41.6 % трассы, живой человек 22 %.
    /// Поэтому играбельная сборка — не финальная витрина, а ИНСТРУМЕНТ, и появляется первой.
    ///
    /// Сцена содержит один этот объект. Всё остальное строится кодом в Start, как и полигон:
    /// префабов в проекте нет намеренно, их нельзя собрать и проверить в batchmode.
    /// </summary>
    public class PlaySession : MonoBehaviour
    {
        /// <summary>Какая трасса под ногами. Переключается в игре клавишей T / четырьмя пальцами.</summary>
        public enum TrackChoice
        {
            /// <summary>График из свечей — то, ради чего игра называется CHART RUNNER.</summary>
            Chart,
            /// <summary>Рукотворный отрезок ~30 с: разгон, подъём, кикер, крукс, спуск.</summary>
            Crux30,
            /// <summary>Перенесённая 1:1 дизайн-трасса VS, 315 м. Содержит стену на 271 м.</summary>
            VerticalSlice
        }

        /// <summary>
        /// Выбор трассы переживает перезагрузку сцены — иначе переключение сбрасывало бы
        /// само себя. Статика здесь оправдана: это единственное состояние, которое обязано
        /// жить дольше сцены, и оно принадлежит сессии игрока, а не объекту.
        /// </summary>
        public static TrackChoice Selected = TrackChoice.Chart;

        [Header("Данные (проставляются сборщиком сцены, чтобы попасть в билд)")]
        public BikeTuningProfile BikeProfile;
        public LevelPhysicsOverride LevelProfile;

        [Tooltip("Ассет трассы VS. Отрезок Crux30 строится кодом и ассета не требует.")]
        public TrackProfile Track;

        [Header("Рестарт")]
        [Tooltip("Пауза перед авто-рестартом после отказа, секунды. Ручной рестарт — тап/клавиша R.")]
        public float RestartDelaySeconds = 1.1f;

        [Tooltip("Возрождать на последнем пройденном чекпоинте, а не в начале трассы.")]
        public bool RestartAtCheckpoint = true;

        [Header("График")]
        [Tooltip("Профиль свечей. Без него трасса-график построиться не может.")]
        public CandleTerrainProfile CandleProfile;

        [Tooltip("Семя генератора. Одно и то же семя = побитово та же трасса.")]
        public int ChartSeed = 20260806;

        [Tooltip("Сколько свечей в трассе. 700 × 26 px ≈ 515 м ≈ полторы минуты.")]
        public int ChartCandles = 700;

        [Header("Композиция")]
        public float HeroScreenFraction = ChaseCamera.DefaultHeroFraction;

        private BikeController _controller;
        private ChaseCamera _chase;
        private TerrainSampler _sampler;
        private PlayInput _input;
        private Camera _camera;

        private float _deadFor = -1f;
        private float _bestDistanceM;
        private float _runStartedAt;
        private float _spawnXM;
        private int _attempts;
        private bool _switchLatch;
        private bool _inputCompiledOut;
        private GUIStyle _hud;
        private GUIStyle _big;
        private GUIStyle _zone;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
#if UNITY_IOS || UNITY_ANDROID
            Screen.orientation = ScreenOrientation.Portrait;
#endif
            // Гравитация проекта — из контракта единиц, а не из настроек проекта: если кто-то
            // поменяет Physics2D в инспекторе, физика разъедется с тестами молча.
            Physics2D.gravity = new Vector2(0f, UnitsContract.GravityMPerS2);
        }

        /// <summary>Трасса, действующая сейчас. Собирается по выбору, а не по полю сцены.</summary>
        private TrackProfile _track;
        private System.Collections.Generic.List<CandleTrackGenerator.Candle> _candles;

        private void Start()
        {
            // Трасса выбирается ДО всего остального: от неё зависят и коллизия, и вид,
            // и камера. Свечи хранятся отдельно — по ним рисуется земля.
            _candles = null;
            if (Selected == TrackChoice.Chart && CandleProfile != null)
            {
                var gen = CandleTrackGenerator.Generate(CandleProfile, ChartSeed, ChartCandles);
                _track = gen.Profile;
                _candles = gen.Candles;
            }
            else if (Selected == TrackChoice.Crux30)
            {
                _track = CruxSliceTrack.CreateProfile();
            }
            else
            {
                _track = Track != null ? Track : VerticalSliceTrack.CreateProfile();
            }
            if (BikeProfile == null)
            {
                Debug.LogError("PlaySession: не задан профиль байка — играть нечем.");
                enabled = false;
                return;
            }
            if (LevelProfile == null) LevelProfile = LevelPhysicsOverride.CreateStock();

            _sampler = new TerrainSampler(_track);

            // ---- мир ----
            //
            // Палитра подчинена ОДНОМУ источнику: низкое солнце позади игрока (SkyView).
            // Поэтому рельеф почти чёрный — он в тени, — а весь цвет уходит в горячую кромку
            // по верхней линии. Это даёт то, чего не было в первом кадре: разницу светлот
            // между героем, землёй и фоном, на которой силуэт вообще может читаться.
            var world = new GameObject("World").transform;
            TrackBuilder.Build(_track, BikeProfile.tyreFriction).transform.SetParent(world, true);
            if (_candles != null)
            {
                // Земля СОСТОИТ из свечей. Тела рисуются между теми же узлами, по которым
                // построена коллизия, поэтому игрок едет ровно по тому, что видит.
                CandleView.Build(_track, _candles, world);
            }
            else
            {
                TerrainView.Build(_track, world,
                    new Color(0.085f, 0.075f, 0.105f, 1f),   // гребень: чуть светлее подножия
                    new Color(0.028f, 0.026f, 0.042f, 1f),   // подножие: почти чёрное
                    new Color(1f, 0.68f, 0.34f, 1f),         // горячая кромка от солнца
                    0.11f);
            }
            TerrainView.BuildMarkers(_track, world, new Color(1f, 0.80f, 0.42f, 1f));

            // ---- байк ----
            _input = new PlayInput();
            _spawnXM = 1.2f;
            var rear = BikeFactory.RestingRearAxle(_sampler, BikeProfile, _spawnXM);
            _controller = BikeFactory.Spawn(BikeProfile, LevelProfile, _sampler, _input, rear);
            BikeView.Attach(_controller);

            // Оверлей телеметрии по умолчанию СПРЯТАН: он для разбора, а не для игры,
            // и закрывает собой ровно ту часть кадра, по которой судят композицию.
            var overlay = _controller.GetComponent<Telemetry.TelemetryOverlay>();
            if (overlay != null) overlay.Visible = false;

            // ---- камера ----
            var camGo = new GameObject("GameCamera");
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            // Фон камеры виден только если небо почему-то не построилось: держим его
            // близким к верху градиента, чтобы такая поломка не выглядела «задумкой».
            _camera.backgroundColor = SkyView.SkyTop;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 100f;
            _chase = camGo.AddComponent<ChaseCamera>();
            _chase.HeroScreenFraction = HeroScreenFraction;
            _chase.TopSpeedMPerS = BikeProfile.topSpeedMPerS;
            _chase.Bind(_camera, _controller.transform);

            // Небо и дальние планы приколачиваются к камере, поэтому создаются после Bind:
            // размер квада берётся из уже настроенного orthographicSize.
            SkyView.Attach(_camera, _track.EndM);
            ContactShadow.Attach(_controller, _sampler, world);
            WheelDust.Attach(_controller, _sampler, world);

            _runStartedAt = Time.time;
            _attempts = 1;

            // Режим съёмки кадров для приёмки композиции. В обычном запуске не включается.
            if (ScreenshotProbe.Requested) ScreenshotProbe.Attach(this, _controller, _chase);

            // Если ветка ввода вырезана препроцессором, игра запустится и будет выглядеть
            // рабочей, оставаясь неуправляемой. Молчать об этом нельзя — говорим в лог И в кадр.
#if !ENABLE_LEGACY_INPUT_MANAGER
            _inputCompiledOut = true;
            Debug.LogError("PLAY: ВВОД ВЫРЕЗАН ПРЕПРОЦЕССОРОМ (ENABLE_LEGACY_INPUT_MANAGER не "
                           + "определён). Игра неуправляема. ProjectSettings → activeInputHandler: 2");
#endif

            Debug.Log("PLAY: композиция — герой " + (_chase.MeasuredHeroFraction * 100f).ToString("0.0")
                      + " % высоты, обзор впереди на верхней скорости "
                      + _chase.AheadAtSpeed(BikeProfile.topSpeedMPerS).ToString("0.0") + " м = "
                      + _chase.AheadSecondsAt(BikeProfile.topSpeedMPerS).ToString("0.00") + " с");
        }

        private void Update()
        {
            if (_controller == null) return;

            var st = _controller.State;
            var travelled = st.PositionXM;
            if (travelled > _bestDistanceM) _bestDistanceM = travelled;

#if ENABLE_LEGACY_INPUT_MANAGER
            if (UnityEngine.Input.GetKeyDown(KeyCode.R)) Restart(true);
            if (UnityEngine.Input.GetKeyDown(KeyCode.T)) SwitchTrack();

            // Четыре пальца — переключение трассы. Один и два заняты управлением, три —
            // оверлеем телеметрии, поэтому конфликта жестов нет.
            var touches = UnityEngine.Input.touchCount;
            if (touches >= 4 && !_switchLatch) { _switchLatch = true; SwitchTrack(); }
            else if (touches < 4) _switchLatch = false;
#endif

            if (_controller.Halted)
            {
                if (_deadFor < 0f) _deadFor = 0f;
                _deadFor += Time.deltaTime;
                if (_deadFor >= RestartDelaySeconds) Restart(false);
            }
            else
            {
                _deadFor = -1f;
            }
        }

        /// <summary>
        /// Смена трассы перезагрузкой сцены. Пересобирать мир на месте было бы дешевле по
        /// кадрам и дороже по правильности: остались бы старые коллайдеры, тени и меши,
        /// и разница между трассами читалась бы как разница между «чисто» и «после смены».
        /// </summary>
        private void SwitchTrack()
        {
            Selected = Selected == TrackChoice.Chart ? TrackChoice.Crux30
                : Selected == TrackChoice.Crux30 ? TrackChoice.VerticalSlice
                : TrackChoice.Chart;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        private void Restart(bool fromStart)
        {
            var x = _spawnXM;
            if (!fromStart && RestartAtCheckpoint) x = LastCheckpointBefore(_controller.State.PositionXM);

            _controller.ResetTo(BikeFactory.RestingRearAxle(_sampler, BikeProfile, x));
            _chase.Snap();
            _deadFor = -1f;
            _runStartedAt = Time.time;
            _attempts++;
        }

        /// <summary>
        /// Последний чекпоинт ПЕРЕД точкой смерти. Рестарт с начала на трассе длиной 315 м
        /// превращает проверку фила в проверку терпения: до интересного места надо заново
        /// доехать, и большая часть попытки уходит на уже освоенное.
        /// </summary>
        private float LastCheckpointBefore(float xM)
        {
            var best = _spawnXM;
            for (var i = 0; i < _track.checkpoints.Length; i++)
            {
                var cx = _track.checkpoints[i].xPx * UnitsContract.PxToM;
                if (cx < xM - 2f && cx > best) best = cx;
            }
            return best;
        }

        // ================= HUD =================

        private void OnGUI()
        {
            if (_controller == null) return;

            // GUI живёт в ПИКСЕЛЯХ, а на телефоне их втрое больше, чем точек: без масштаба
            // шрифт 15 превращается в нечитаемые пять точек высотой.
            var scale = Screen.width / 430f;
            var m = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            EnsureStyles();
            var st = _controller.State;
            var pct = Mathf.Clamp01(st.PositionXM / Mathf.Max(1f, _track.EndM)) * 100f;

            GUI.Label(new Rect(14f, 12f, 220f, 22f),
                pct.ToString("0") + " %   " + (st.SpeedMPerS * 3.6f).ToString("0") + " км/ч", _hud);
            GUI.Label(new Rect(14f, 32f, 260f, 22f),
                "попытка " + _attempts + "   " + (Time.time - _runStartedAt).ToString("0.0") + " с"
                + "   " + (Selected == TrackChoice.Chart ? "график"
                    : Selected == TrackChoice.Crux30 ? "крукс-30" : "VS-315"), _hud);

            // Индикатор переноса веса: игрок обязан видеть, что он реально приложил,
            // иначе «я же наклонял» и «наклон приложился» неразличимы, и учиться не на чем.
            var barW = 150f;
            var cx = 14f + barW * 0.5f;
            var y = 60f;
            GUI.color = new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(new Rect(14f, y, barW, 5f), Texture2D.whiteTexture);
            GUI.color = st.WeightShift < 0f
                ? new Color(0.42f, 0.90f, 0.80f, 0.95f)
                : new Color(0.98f, 0.72f, 0.25f, 0.95f);
            var w = Mathf.Abs(st.WeightShift) * barW * 0.5f;
            GUI.DrawTexture(new Rect(st.WeightShift < 0f ? cx - w : cx, y, w, 5f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(14f, y + 8f, 220f, 20f),
                st.WeightShift < -0.05f ? "вес НАЗАД" : st.WeightShift > 0.05f ? "вес ВПЕРЁД" : "", _hud);

            DrawTouchZones(st);

            if (_inputCompiledOut)
            {
                GUI.Label(new Rect(0f, 932f * 0.44f, 430f, 60f),
                    "ВВОД НЕ СОБРАН\nactiveInputHandler: 2", _big);
            }

            if (_controller.Halted)
            {
                var reason = st.Failure == BikeFailure.Loop ? "ОПРОКИД НАЗАД"
                    : st.Failure == BikeFailure.Endo ? "КЛЕВОК ВПЕРЁД"
                    : st.Failure == BikeFailure.Crash ? "ЖЁСТКАЯ ПОСАДКА"
                    : "ПАДЕНИЕ";
                var r = new Rect(0f, 932f * 0.34f, 430f, 40f);
                GUI.Label(r, reason, _big);
            }

            GUI.matrix = m;
        }

        /// <summary>
        /// ВИДИМЫЕ ЗОНЫ УПРАВЛЕНИЯ. Отдельная ошибка, которую надо было чинить вместе с вводом:
        /// раскладка «правая половина газ, левая тормоз» существовала только в моей голове и
        /// в комментарии к коду. Игрок, впервые открывший приложение, не знает, куда нажимать,
        /// и неотличимо от сломанного ввода получает «я жму, ничего не происходит».
        ///
        /// Зоны подсвечиваются В МОМЕНТ нажатия по ФАКТИЧЕСКИ приложенной команде из
        /// BikeState, а не по факту касания экрана. Это делает панель ещё и щупом: если
        /// подсветка не загорается при нажатии, значит команда до физики не дошла.
        /// </summary>
        private void DrawTouchZones(BikeState st)
        {
            const float h = 932f;
            const float w = 430f;
            // Полоса зон занимает нижнюю пятую кадра, а не треть: в первой редакции
            // полупрозрачная заливка на 38 % высоты выбеливала игровое поле и спорила
            // со свечами. Зоны обязаны быть понятны и не обязаны быть заметны.
            var zoneTop = h * 0.80f;

            var brakeOn = st.BrakeApplied > 0.01f;
            var gasOn = st.ThrottleApplied > 0.01f;

            // В ПОКОЕ ЗАЛИВКИ НЕТ. Даже 3.5 % белого поверх почти чёрного грунта читались
            // светлой плашкой на пятой части кадра — панель забирала себе низ композиции.
            // Подсветка появляется только на нажатии, и тогда она несёт информацию.
            if (brakeOn)
            {
                GUI.color = new Color(0.62f, 0.80f, 1f, 0.15f);
                GUI.DrawTexture(new Rect(0f, zoneTop, w * 0.5f, h - zoneTop), Texture2D.whiteTexture);
            }
            if (gasOn)
            {
                GUI.color = new Color(1f, 0.78f, 0.34f, 0.15f);
                GUI.DrawTexture(new Rect(w * 0.5f, zoneTop, w * 0.5f, h - zoneTop), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;

            EnsureStyles();
            _zone.normal.textColor = new Color(0.86f, 0.93f, 0.98f, brakeOn ? 0.95f : 0.45f);
            GUI.Label(new Rect(0f, h - 74f, w * 0.5f, 30f), "ТОРМОЗ", _zone);
            _zone.normal.textColor = new Color(0.98f, 0.80f, 0.42f, gasOn ? 0.95f : 0.45f);
            GUI.Label(new Rect(w * 0.5f, h - 74f, w * 0.5f, 30f), "ГАЗ", _zone);

            _zone.normal.textColor = new Color(0.86f, 0.93f, 0.98f, 0.40f);
            GUI.Label(new Rect(0f, h - 44f, w, 26f), "палец вверх/вниз — перенос веса", _zone);

            // Разделитель половин — тонкая линия вместо заливки: границу зон надо ПОКАЗАТЬ,
            // а не занять ею кадр.
            GUI.color = new Color(1f, 1f, 1f, 0.14f);
            GUI.DrawTexture(new Rect(w * 0.5f - 0.5f, h - 96f, 1f, 72f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void EnsureStyles()
        {
            if (_hud != null) return;
            _hud = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperLeft };
            _hud.normal.textColor = new Color(0.86f, 0.93f, 0.98f, 0.92f);
            _big = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            _big.normal.textColor = new Color(1f, 0.44f, 0.38f, 0.95f);
            _zone = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        }
    }
}
