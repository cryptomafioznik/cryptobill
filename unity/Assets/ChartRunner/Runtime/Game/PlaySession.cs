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
        [Header("Данные (проставляются сборщиком сцены, чтобы попасть в билд)")]
        public BikeTuningProfile BikeProfile;
        public LevelPhysicsOverride LevelProfile;
        public TrackProfile Track;

        [Header("Рестарт")]
        [Tooltip("Пауза перед авто-рестартом после отказа, секунды. Ручной рестарт — тап/клавиша R.")]
        public float RestartDelaySeconds = 1.1f;

        [Tooltip("Возрождать на последнем пройденном чекпоинте, а не в начале трассы.")]
        public bool RestartAtCheckpoint = true;

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
        private GUIStyle _hud;
        private GUIStyle _big;

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

        private void Start()
        {
            if (Track == null) Track = VerticalSliceTrack.CreateProfile();
            if (BikeProfile == null)
            {
                Debug.LogError("PlaySession: не задан профиль байка — играть нечем.");
                enabled = false;
                return;
            }
            if (LevelProfile == null) LevelProfile = LevelPhysicsOverride.CreateStock();

            _sampler = new TerrainSampler(Track);

            // ---- мир ----
            //
            // Палитра подчинена ОДНОМУ источнику: низкое солнце позади игрока (SkyView).
            // Поэтому рельеф почти чёрный — он в тени, — а весь цвет уходит в горячую кромку
            // по верхней линии. Это даёт то, чего не было в первом кадре: разницу светлот
            // между героем, землёй и фоном, на которой силуэт вообще может читаться.
            var world = new GameObject("World").transform;
            TrackBuilder.Build(Track, BikeProfile.tyreFriction).transform.SetParent(world, true);
            TerrainView.Build(Track, world,
                new Color(0.085f, 0.075f, 0.105f, 1f),   // гребень: чуть светлее подножия
                new Color(0.028f, 0.026f, 0.042f, 1f),   // подножие: почти чёрное
                new Color(1f, 0.68f, 0.34f, 1f),         // горячая кромка от солнца
                0.11f);
            TerrainView.BuildMarkers(Track, world, new Color(1f, 0.80f, 0.42f, 1f));

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
            SkyView.Attach(_camera, Track.EndM);
            ContactShadow.Attach(_controller, _sampler, world);

            _runStartedAt = Time.time;
            _attempts = 1;

            // Режим съёмки кадров для приёмки композиции. В обычном запуске не включается.
            if (ScreenshotProbe.Requested) ScreenshotProbe.Attach(this, _controller, _chase);

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
            for (var i = 0; i < Track.checkpoints.Length; i++)
            {
                var cx = Track.checkpoints[i].xPx * UnitsContract.PxToM;
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
            var pct = Mathf.Clamp01(st.PositionXM / Mathf.Max(1f, Track.EndM)) * 100f;

            GUI.Label(new Rect(14f, 12f, 220f, 22f),
                pct.ToString("0") + " %   " + (st.SpeedMPerS * 3.6f).ToString("0") + " км/ч", _hud);
            GUI.Label(new Rect(14f, 32f, 220f, 22f),
                "попытка " + _attempts + "   " + (Time.time - _runStartedAt).ToString("0.0") + " с", _hud);

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

        private void EnsureStyles()
        {
            if (_hud != null) return;
            _hud = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperLeft };
            _hud.normal.textColor = new Color(0.86f, 0.93f, 0.98f, 0.92f);
            _big = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            _big.normal.textColor = new Color(1f, 0.44f, 0.38f, 0.95f);
        }
    }
}
