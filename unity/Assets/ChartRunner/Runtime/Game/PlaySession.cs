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

        /// <summary>Вариант фила управления. Как и трасса, переживает перезагрузку сцены.</summary>
        public static Feel SelectedFeel = Feel.Balance;

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

        /// <summary>Минимум, сколько держится экран итогов: иначе тап смерти его проглотит.</summary>
        private const float SummaryMinSeconds = 0.55f;

        /// <summary>Через сколько итог уходит сам, если игрок не тапнул.</summary>
        private const float SummaryAutoSeconds = 6f;

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
        private float _feelBannerUntil;
        private bool _prevAirborne;
        private float _prevVerticalSpeed;
        private LiquidationWave _wave;
        private CoinField _coinField;
        private float _airAccum;
        private float _wheelieAccum;
        private float _bestLeadM;
        private float _runBestDistM;
        private bool _runCommitted;
        private bool _levelUp;
        private int _runCoins;
        private GUIStyle _hud;
        private GUIStyle _big;
        private GUIStyle _zone;
        private GUIStyle _warn;

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
        private System.Collections.Generic.List<CandleTrackGenerator.EventSpan> _events;

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
                _events = gen.Events;
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

            // РАБОТАЕМ С КОПИЕЙ профиля уровня. Пресет фила меняет его поля, а LevelProfile —
            // это ссылка на АССЕТ: правка на лету записалась бы в файл на диске и тихо
            // изменила бы то, что меряют тесты. Копия делает переключение обратимым.
            LevelProfile = Instantiate(LevelProfile);
            FeelPreset.Apply(LevelProfile, SelectedFeel);

            _sampler = new TerrainSampler(_track);

            // ---- мир ----
            //
            // Идентичность браузерной версии (вердикт живого теста: моё контражурное
            // направление — «вся стилистика не та», а картинку исходника пользователь
            // принимал годами). Мир — порт drawCityWorld: закат Vice, город, вода.
            // Трасса — приподнятая дека, с которой свисают свечи-колонны данных.
            var world = new GameObject("World").transform;
            TrackBuilder.Build(_track, BikeProfile.tyreFriction).transform.SetParent(world, true);
            TrackDeckView.Build(_track, _sampler, world, _candles, _events);
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
            _camera.backgroundColor = WorldPalette.Eras[0].Sky0;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 100f;
            _chase = camGo.AddComponent<ChaseCamera>();
            _chase.HeroScreenFraction = HeroScreenFraction;
            _chase.TopSpeedMPerS = BikeProfile.topSpeedMPerS;
            _chase.Bind(_camera, _controller.transform);

            // Пост-обработка: HDR + bloom + виньетка + цветокоррекция. Включение движка.
            PostFX.Attach(_camera);

            // Мир приколачивается к камере, поэтому создаётся после Bind:
            // размеры берутся из уже настроенного orthographicSize.
            WorldView.Attach(_camera, _track.EndM);

            // ---- игровой слой ----
            //
            // Волна ликвидации — СТАВКА заезда. Разбор топов жанра дал один общий
            // знаменатель: без давления заезд перестаёт быть заездом. У Hill Climb это
            // топливо, у Alto's — погоня, у нас ставка уже была придумана и обкатана
            // годами в браузерной версии, и в перенос она не попала. Именно поэтому
            // сборка на движке проигрывала браузерной при лучшей физике: ехать было не за чем.
            _wave = LiquidationWave.Attach(_controller, _camera, world);
            _coinField = CoinField.Attach(_track, _sampler, _controller, world, ChartSeed);

            ContactShadow.Attach(_controller, _sampler, world);
            WheelDust.Attach(_controller, _sampler, world);
            AirMotes.Attach(_camera);

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

            // УДАР: переход «в воздухе → на земле». Берём вертикальную скорость ПРОШЛОГО
            // кадра: в кадре касания решатель её уже погасил, и по ней удар не измерить.
            // Это тот же класс ловушки, что и «пик величины до обработки ≠ после».
            if (st.IsGrounded && _prevAirborne) _chase.Impact(_prevVerticalSpeed);
            _prevAirborne = !st.IsGrounded;
            _prevVerticalSpeed = st.VerticalSpeedMPerS;

            var travelled = st.PositionXM;
            if (travelled > _bestDistanceM) _bestDistanceM = travelled;

            AccumulateGoals(st);

#if ENABLE_LEGACY_INPUT_MANAGER
            if (UnityEngine.Input.GetKeyDown(KeyCode.R)) Restart(true);
            if (UnityEngine.Input.GetKeyDown(KeyCode.T)) SwitchTrack();
            if (UnityEngine.Input.GetKeyDown(KeyCode.F)) SwitchFeel();

            // Четыре пальца — переключение трассы. Один и два заняты управлением, три —
            // оверлеем телеметрии, поэтому конфликта жестов нет.
            var touches = UnityEngine.Input.touchCount;
            if (touches >= 4 && !_switchLatch) { _switchLatch = true; SwitchTrack(); }
            else if (touches == 3 && !_switchLatch) { _switchLatch = true; SwitchFeel(); }
            else if (touches < 3) _switchLatch = false;
#endif

            if (_controller.Halted)
            {
                if (_deadFor < 0f) _deadFor = 0f;
                _deadFor += Time.deltaTime;

                // Итог заезда фиксируется ОДИН раз: прогресс целей переживает смерть,
                // и это главная причина начать следующую попытку.
                if (!_runCommitted)
                {
                    _runCommitted = true;
                    _runCoins = _coinField != null ? _coinField.Collected : 0;
                    _levelUp = RunGoals.CommitRun();
                }

                // Итог показывается, пока игрок его читает. Тап или R — сразу заново:
                // мгновенный рестарт держит петлю, длинная пауза её рвёт.
#if ENABLE_LEGACY_INPUT_MANAGER
                var tapped = UnityEngine.Input.GetMouseButtonDown(0)
                             || UnityEngine.Input.touchCount > 0;
#else
                var tapped = false;
#endif
                if (_deadFor >= SummaryMinSeconds && (tapped || _deadFor >= SummaryAutoSeconds))
                    Restart(false);
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
        /// <summary>
        /// Вклад текущего заезда в цели. Считается ПО ФАКТУ состояния байка, а не по
        /// нажатым кнопкам: цель «на заднем колесе» обязана засчитывать реальное вилли,
        /// иначе её можно закрыть, просто держа кнопку в воздухе.
        /// </summary>
        private void AccumulateGoals(BikeState st)
        {
            if (_controller.Halted) return;
            var dt = Time.deltaTime;

            // Дистанция: только ПРИРОСТ рекорда заезда — иначе катание взад-вперёд
            // накручивало бы цель.
            if (st.DistanceM > _runBestDistM)
            {
                RunGoals.AddDistance(st.DistanceM - _runBestDistM);
                _runBestDistM = st.DistanceM;
            }

            // Третья цель чередуется по уровню — считаем ту величину, которая нужна.
            switch (RunGoals.Level % 3)
            {
                case 0: // время в воздухе
                    if (!st.IsGrounded) { _airAccum += dt; RunGoals.AddSkill(dt); }
                    break;
                case 1: // время на заднем колесе
                    if (st.IsGrounded && st.PitchRelRad > 0.45f)
                    {
                        _wheelieAccum += dt;
                        RunGoals.AddSkill(dt);
                    }
                    break;
                default: // максимальный отрыв от волны
                    if (_wave != null && _wave.LeadM > _bestLeadM)
                    {
                        RunGoals.AddSkill(_wave.LeadM - _bestLeadM);
                        _bestLeadM = _wave.LeadM;
                    }
                    break;
            }
        }

        private void SwitchTrack()
        {
            Selected = Selected == TrackChoice.Chart ? TrackChoice.Crux30
                : Selected == TrackChoice.Crux30 ? TrackChoice.VerticalSlice
                : TrackChoice.Chart;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>
        /// Смена варианта фила. На месте, без перезагрузки сцены: профиль уровня — копия,
        /// контроллер читает его каждый кадр, поэтому разница чувствуется сразу и её можно
        /// сравнить на одном и том же куске трассы. Перезагрузка сцены сбрасывала бы
        /// положение и мешала сравнивать.
        /// </summary>
        private void SwitchFeel()
        {
            SelectedFeel = FeelPreset.Next(SelectedFeel);
            FeelPreset.Apply(LevelProfile, SelectedFeel);
            _feelBannerUntil = Time.time + 1.6f;
        }

        private void Restart(bool fromStart)
        {
            var x = _spawnXM;
            if (!fromStart && RestartAtCheckpoint) x = LastCheckpointBefore(_controller.State.PositionXM);

            _controller.ResetTo(BikeFactory.RestingRearAxle(_sampler, BikeProfile, x));
            _chase.Snap();
            if (_wave != null) _wave.Reset();
            if (_coinField != null) _coinField.ResetRun();
            _deadFor = -1f;
            _runStartedAt = Time.time;
            _attempts++;
            _airAccum = 0f;
            _wheelieAccum = 0f;
            _bestLeadM = 0f;
            _runBestDistM = 0f;
            _runCommitted = false;
            _levelUp = false;
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

            // ---- строка состояния: дистанция, скорость, деньги ----
            GUI.Label(new Rect(14f, 12f, 260f, 22f),
                Mathf.FloorToInt(st.DistanceM) + " м    "
                + (st.SpeedMPerS * 3.6f).ToString("0") + " км/ч", _hud);

            // Монеты — справа, золотом: единственное золото в кадре, и это деньги.
            if (_coinField != null)
            {
                var goldStyle = new GUIStyle(_hud) { alignment = TextAnchor.UpperRight };
                goldStyle.normal.textColor = new Color(1f, 0.84f, 0.36f, 0.95f);
                GUI.Label(new Rect(170f, 12f, 246f, 22f), "\u25C6 " + _coinField.Collected, goldStyle);
            }
            DrawGoals();
            DrawDanger();

            // Баннер при смене: без него непонятно, переключилось ли, и сравнение
            // превращается в угадывание.
            if (Time.time < _feelBannerUntil)
            {
                DrawChip("ФИЛ: " + FeelPreset.Name(SelectedFeel), 215f, 932f * 0.30f,
                    new Color(150 / 255f, 205 / 255f, 1f), _warn);
            }

            // Индикатор переноса веса: игрок обязан видеть, что он реально приложил,
            // иначе «я же наклонял» и «наклон приложился» неразличимы, и учиться не на чем.
            // Индикатор веса переехал ВНИЗ, к кнопкам наклона: обратная связь должна быть
            // там, куда смотрит палец в момент действия, а не в углу с целями.
            var barW = 174f;
            var cx = 14f + barW * 0.5f;
            var y = 932f - 130f;
            GUI.color = new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(new Rect(14f, y, barW, 5f), Texture2D.whiteTexture);
            GUI.color = st.WeightShift < 0f
                ? new Color(0.42f, 0.90f, 0.80f, 0.95f)
                : new Color(0.98f, 0.72f, 0.25f, 0.95f);
            var w = Mathf.Abs(st.WeightShift) * barW * 0.5f;
            GUI.DrawTexture(new Rect(st.WeightShift < 0f ? cx - w : cx, y, w, 5f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var wl = new GUIStyle(_hud) { fontSize = 11 };
            wl.normal.textColor = new Color(0.78f, 0.84f, 0.94f, 0.75f);
            GUI.Label(new Rect(14f, y - 15f, 220f, 14f),
                st.WeightShift < -0.05f ? "вес НАЗАД" : st.WeightShift > 0.05f ? "вес ВПЕРЁД" : "", wl);

            DrawEventWarning(st);
            DrawButtons();

            if (_inputCompiledOut)
            {
                GUI.Label(new Rect(0f, 932f * 0.44f, 430f, 60f),
                    "ВВОД НЕ СОБРАН\nactiveInputHandler: 2", _big);
            }

            if (_controller.Halted) DrawSummary(st);

            GUI.matrix = m;
        }

        /// <summary>
        /// ПРЕДУПРЕЖДЕНИЕ О СОБЫТИИ. Сет-пьеса обязана быть ТЕЛЕГРАФИРУЕМОЙ: игрок должен
        /// узнать о ней заранее и успеть подготовиться, иначе это не драма, а подстава.
        /// Ровно тем же принципом живут разгоны перед препятствиями.
        ///
        /// Порог 22 метра выведен, а не назначен: на верхней скорости 6.27 м/с это 3.5 с —
        /// заметно больше бюджета реакции 0.80 с, то есть времени хватает не только
        /// среагировать, но и выбрать, как заходить.
        /// </summary>
        private void DrawEventWarning(BikeState st)
        {
            if (_events == null || _events.Count == 0) return;
            EnsureStyles();

            var stepM = _track.nodeStepPx * UnitsContract.PxToM;
            for (var i = 0; i < _events.Count; i++)
            {
                var e = _events[i];
                var fromM = e.FromNode * stepM;
                var toM = e.ToNode * stepM;
                var dist = fromM - st.PositionXM;

                // Показываем на подлёте и пока едем внутри участка.
                if (dist > 22f || st.PositionXM > toM) continue;

                var rally = e.Type == CandleTrackGenerator.MarketEvent.Rally;
                var col = rally
                    ? new Color(0.42f, 0.94f, 0.62f, 1f)
                    : new Color(1f, 0.46f, 0.36f, 1f);
                // Внутри участка баннер тусклее: он уже сделал свою работу и не должен
                // перетягивать внимание с рельефа.
                var inside = dist <= 0f;
                DrawChip(e.Title, 215f, 932f * 0.16f, col, _warn, inside ? 0.55f : 0.95f);
                return;
            }
        }

        /// <summary>
        /// ЧЕТЫРЕ КНОПКИ схемы `btn4`. Рисуются ИЗ ТОГО ЖЕ объекта, который опрашивает
        /// ввод (<see cref="PlayInput.Buttons"/>), поэтому нарисованное и нажимаемое не
        /// могут разъехаться: это одна структура, а не две копии координат.
        ///
        /// Подсветка идёт по флагу, выставленному при ОПРОСЕ касания, а не по состоянию
        /// физики — так кнопка отвечает мгновенно, а не через рампу газа, и остаётся щупом:
        /// не загорелась при нажатии — палец не попал или ввод не дошёл.
        ///
        /// Стиль — порт ctlBtn исходника: скруглённая плашка, заливка цветом кнопки
        /// (α .34 нажата / .12 нет), рамка тем же цветом, подпись моноширинным жирным.
        /// Цвета кнопок — из раскладки btn4 (стр. 4453+).
        /// </summary>
        private void DrawButtons()
        {
            EnsureStyles();
            var b = _input.Buttons;
            DrawButton(b.Gas, b.Gas.Active
                ? new Color(120 / 255f, 1f, 180 / 255f)
                : new Color(120 / 255f, 205 / 255f, 160 / 255f), 18);
            DrawButton(b.Brake, b.Brake.Active
                ? new Color(1f, 175 / 255f, 120 / 255f)
                : new Color(200 / 255f, 155 / 255f, 135 / 255f), 14);
            DrawButton(b.NoseUp, new Color(150 / 255f, 205 / 255f, 1f), 14);
            DrawButton(b.NoseDown, new Color(150 / 255f, 205 / 255f, 1f), 14);
        }

        private void DrawButton(TouchButtons.Zone z, Color col, int fontSize)
        {
            var r = z.R;
            var radius = Vector4.one * 10f;

            // Заливка.
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(col.r, col.g, col.b, z.Active ? 0.34f : 0.12f), Vector4.zero, radius);
            // Рамка.
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(col.r, col.g, col.b, z.Active ? 1f : 0.5f),
                Vector4.one * (z.Active ? 2.6f : 1.4f), radius);

            _zone.fontSize = fontSize;
            _zone.normal.textColor = new Color(col.r, col.g, col.b, z.Active ? 1f : 0.82f);
            GUI.Label(r, z.Label, _zone);
        }

        /// <summary>
        /// Чип-плашка (порт chipText): тёмная подложка + цветная рамка + светлый текст.
        /// Единый стиль игровых меток — серый текст тонул на пёстром мире.
        /// </summary>
        private void DrawChip(string txt, float cx, float cy, Color col, GUIStyle style, float a = 1f)
        {
            var content = new GUIContent(txt);
            var size = style.CalcSize(content);
            var r = new Rect(cx - size.x / 2f - 8f, cy - size.y / 2f - 4f, size.x + 16f, size.y + 8f);
            var radius = Vector4.one * 6f;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(9 / 255f, 10 / 255f, 24 / 255f, 0.78f * a), Vector4.zero, radius);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(col.r, col.g, col.b, 0.85f * a), Vector4.one * 1.4f, radius);
            style.normal.textColor = new Color(236 / 255f, 243 / 255f, 253 / 255f, 0.95f * a);
            GUI.Label(r, content, style);
        }

        /// <summary>
        /// ТРИ ЦЕЛИ — постоянно на экране. Модель Alto's Odyssey: маленькие достижимые
        /// задачи, прогресс по которым переживает смерть. Именно они дают причину начать
        /// следующий заезд, пока в игре нет магазина и прогрессии.
        ///
        /// Полоска прогресса обязательна: цель без видимого приближения к ней не работает
        /// — игрок не может понять, стоит ли ещё один заезд.
        /// </summary>
        private void DrawGoals()
        {
            var y = 36f;
            for (var i = 0; i < RunGoals.Active.Length; i++)
            {
                var g = RunGoals.Active[i];
                var done = g.Done;
                var col = done
                    ? new Color(0.42f, 0.94f, 0.62f, 1f)
                    : new Color(0.78f, 0.84f, 0.94f, 1f);

                // Полоска-подложка во всю ширину плашки и заливка по прогрессу.
                var r = new Rect(14f, y, 190f, 15f);
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                    new Color(9 / 255f, 10 / 255f, 24 / 255f, 0.55f), Vector4.zero, Vector4.one * 4f);
                var fill = new Rect(r.x, r.y, r.width * g.Fraction, r.height);
                GUI.DrawTexture(fill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                    new Color(col.r, col.g, col.b, done ? 0.34f : 0.20f), Vector4.zero, Vector4.one * 4f);

                var st = new GUIStyle(_hud) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
                st.normal.textColor = new Color(col.r, col.g, col.b, 0.95f);
                GUI.Label(new Rect(r.x + 6f, r.y, r.width - 8f, r.height),
                    (done ? "\u2713 " : "") + g.Label, st);
                y += 18f;
            }

            // Уровень целей: длинная петля видна одним числом.
            var lv = new GUIStyle(_hud) { fontSize = 11 };
            lv.normal.textColor = new Color(0.62f, 0.68f, 0.80f, 0.75f);
            GUI.Label(new Rect(14f, y + 1f, 190f, 14f), "УРОВЕНЬ " + RunGoals.Level, lv);
        }

        /// <summary>
        /// ТРЕВОГА ОТ ВОЛНЫ. Красная виньетка по левому краю ∝ близости ликвидации.
        /// Смысл не в украшении: игрок смотрит вперёд, а смерть приходит сзади — без
        /// индикации он узнаёт о ней в момент смерти, и это читается нечестностью.
        /// </summary>
        private void DrawDanger()
        {
            if (_wave == null || _controller.Halted) return;
            var d = _wave.Danger;
            if (d < 0.02f) return;

            // Пульсация тем быстрее, чем ближе — темп сам сообщает степень опасности.
            var pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * (4f + 10f * d));
            var a = d * d * 0.55f * pulse;
            var w = 26f + 60f * d;
            GUI.color = new Color(1f, 0.18f, 0.28f, a);
            GUI.DrawTexture(new Rect(0f, 0f, w, 932f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (d > 0.55f)
            {
                var st = new GUIStyle(_warn) { fontSize = 15 };
                st.normal.textColor = new Color(1f, 0.42f, 0.42f, 0.55f + 0.45f * pulse);
                GUI.Label(new Rect(0f, 932f * 0.10f, 430f, 20f), "ЛИКВИДАЦИЯ НАСТИГАЕТ", st);
            }
        }

        /// <summary>
        /// ЭКРАН ИТОГОВ. Отвечает на три вопроса, которые игрок задаёт после смерти:
        /// что случилось, сколько я прошёл, приблизился ли я к чему-нибудь. Третий —
        /// главный: именно он превращает поражение в причину сыграть ещё раз.
        ///
        /// Тап рестартит сразу: длинная пауза после смерти рвёт петлю, и это единственная
        /// вещь, которую в разборах топов ругают чаще всего.
        /// </summary>
        private void DrawSummary(BikeState st)
        {
            // Затемнение кадра: итог обязан читаться поверх пёстрого мира.
            GUI.color = new Color(0.02f, 0.02f, 0.06f, 0.62f);
            GUI.DrawTexture(new Rect(0f, 0f, 430f, 932f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var reason = st.Failure == BikeFailure.Loop ? "ОПРОКИД НАЗАД"
                : st.Failure == BikeFailure.Endo ? "КЛЕВОК ВПЕРЁД"
                : st.Failure == BikeFailure.Crash ? "ЖЁСТКАЯ ПОСАДКА"
                : st.Failure == BikeFailure.Liquidated ? "ЛИКВИДИРОВАН"
                : "ПАДЕНИЕ";
            var accent = st.Failure == BikeFailure.Liquidated
                ? new Color(1f, 0.25f, 0.34f)
                : new Color(1f, 0.55f, 0.32f);

            DrawChip(reason, 215f, 932f * 0.26f, accent, _big);

            // Дистанция крупно — это счёт заезда.
            var dist = new GUIStyle(_big) { fontSize = 44 };
            dist.normal.textColor = new Color(0.94f, 0.97f, 1f, 0.96f);
            GUI.Label(new Rect(0f, 932f * 0.32f, 430f, 56f),
                Mathf.FloorToInt(_runBestDistM) + " м", dist);

            var sub = new GUIStyle(_hud) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            sub.normal.textColor = new Color(1f, 0.84f, 0.36f, 0.95f);
            GUI.Label(new Rect(0f, 932f * 0.32f + 56f, 430f, 22f), "\u25C6 " + _runCoins, sub);

            // Рекорд — вторая причина ехать ещё раз.
            var rec = new GUIStyle(_hud) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            rec.normal.textColor = new Color(0.68f, 0.74f, 0.86f, 0.8f);
            GUI.Label(new Rect(0f, 932f * 0.32f + 78f, 430f, 20f),
                "рекорд " + Mathf.FloorToInt(_bestDistanceM) + " м   ·   попытка " + _attempts, rec);

            // Цели с прогрессом — то, ради чего игрок нажмёт «ещё раз».
            var y = 932f * 0.47f;
            for (var i = 0; i < RunGoals.Active.Length; i++)
            {
                var g = RunGoals.Active[i];
                var col = g.Done
                    ? new Color(0.42f, 0.94f, 0.62f, 1f)
                    : new Color(0.80f, 0.86f, 0.96f, 1f);
                var r = new Rect(65f, y, 300f, 22f);
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                    new Color(9 / 255f, 10 / 255f, 24 / 255f, 0.7f), Vector4.zero, Vector4.one * 5f);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * g.Fraction, r.height),
                    Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                    new Color(col.r, col.g, col.b, g.Done ? 0.34f : 0.18f),
                    Vector4.zero, Vector4.one * 5f);
                var gs = new GUIStyle(_hud) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                gs.normal.textColor = col;
                GUI.Label(r, (g.Done ? "\u2713 " : "") + g.Label, gs);
                y += 27f;
            }

            if (_levelUp)
            {
                DrawChip("УРОВЕНЬ " + RunGoals.Level + " ОТКРЫТ",
                    215f, y + 22f, new Color(0.42f, 0.94f, 0.62f), _warn);
                y += 34f;
            }

            // Призыв к действию — пульсирует, чтобы читался как кнопка, а не как надпись.
            if (_deadFor >= SummaryMinSeconds)
            {
                var p = 0.6f + 0.4f * Mathf.Sin(Time.time * 4.5f);
                var cta = new GUIStyle(_big) { fontSize = 20 };
                cta.normal.textColor = new Color(0.47f, 1f, 0.71f, 0.55f + 0.45f * p);
                GUI.Label(new Rect(0f, 932f * 0.70f, 430f, 30f), "ТАП — ЕЩЁ РАЗ", cta);
            }
        }

        private void EnsureStyles()
        {
            if (_hud != null) return;
            // Моноширинный шрифт — голос исходника (ui-monospace). Menlo есть и на
            // macOS, и на iOS; Courier — страховка.
            var mono = Font.CreateDynamicFontFromOSFont(
                new[] { "Menlo", "Menlo-Regular", "Courier", "Courier New" }, 15);

            _hud = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold
            };
            if (mono != null) _hud.font = mono;
            _hud.normal.textColor = new Color(0.86f, 0.93f, 0.98f, 0.92f);

            _big = new GUIStyle(_hud)
            {
                fontSize = 26, alignment = TextAnchor.MiddleCenter
            };
            _big.normal.textColor = new Color(1f, 0.44f, 0.38f, 0.95f);

            _zone = new GUIStyle(_hud)
            {
                fontSize = 17, alignment = TextAnchor.MiddleCenter
            };

            _warn = new GUIStyle(_hud)
            {
                fontSize = 20, alignment = TextAnchor.MiddleCenter
            };
        }
    }
}
