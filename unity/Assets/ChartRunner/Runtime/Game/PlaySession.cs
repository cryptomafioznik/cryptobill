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
            // Палитра подчинена ОДНОМУ источнику: низкое солнце позади игрока (SkyView).
            // Поэтому рельеф почти чёрный — он в тени, — а весь цвет уходит в горячую кромку
            // по верхней линии. Это даёт то, чего не было в первом кадре: разницу светлот
            // между героем, землёй и фоном, на которой силуэт вообще может читаться.
            var world = new GameObject("World").transform;
            TrackBuilder.Build(_track, BikeProfile.tyreFriction).transform.SetParent(world, true);
            if (_candles != null)
            {
                // Земля СОСТОИТ из свечей, и верх заливки идёт по САМОЙ поверхности
                // (сэмплер тот же, что у коллизии), поэтому игрок едет ровно по тому,
                // что видит, а не проваливается внутрь нарисованных свечей.
                CandleView.Build(_track, _candles, _sampler, world, _events);
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

            // Пост-обработка: HDR + bloom + виньетка + цветокоррекция. Включение движка.
            PostFX.Attach(_camera);

            // Небо и дальние планы приколачиваются к камере, поэтому создаются после Bind:
            // размер квада берётся из уже настроенного orthographicSize.
            SkyView.Attach(_camera, _track.EndM);
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
            // Две строки, а не одна: в одной строке шириной 260 название фила обрезалось,
            // и переключение выглядело неработающим — ровно та же беда, что с невидимым
            // управлением, только в отчёте о состоянии.
            GUI.Label(new Rect(14f, 32f, 300f, 22f),
                "попытка " + _attempts + "   " + (Time.time - _runStartedAt).ToString("0.0") + " с", _hud);
            GUI.Label(new Rect(14f, 52f, 300f, 22f),
                (Selected == TrackChoice.Chart ? "график"
                    : Selected == TrackChoice.Crux30 ? "крукс-30" : "VS-315")
                + "   фил: " + FeelPreset.Name(SelectedFeel), _hud);

            // Баннер при смене: без него непонятно, переключилось ли, и сравнение
            // превращается в угадывание.
            if (Time.time < _feelBannerUntil)
            {
                GUI.Label(new Rect(0f, 932f * 0.30f, 430f, 40f),
                    "ФИЛ: " + FeelPreset.Name(SelectedFeel), _big);
            }

            // Индикатор переноса веса: игрок обязан видеть, что он реально приложил,
            // иначе «я же наклонял» и «наклон приложился» неразличимы, и учиться не на чем.
            var barW = 150f;
            var cx = 14f + barW * 0.5f;
            var y = 78f;
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

            DrawEventWarning(st);
            DrawButtons();

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
                _warn.normal.textColor = new Color(col.r, col.g, col.b, inside ? 0.55f : 0.95f);
                GUI.Label(new Rect(0f, 932f * 0.16f, 430f, 34f), e.Title, _warn);
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
        /// </summary>
        private void DrawButtons()
        {
            EnsureStyles();
            var b = _input.Buttons;
            DrawButton(b.Gas, new Color(0.47f, 1f, 0.71f, 1f));
            DrawButton(b.Brake, new Color(1f, 0.69f, 0.47f, 1f));
            DrawButton(b.NoseUp, new Color(0.59f, 0.80f, 1f, 1f));
            DrawButton(b.NoseDown, new Color(0.59f, 0.80f, 1f, 1f));
        }

        private void DrawButton(TouchButtons.Zone z, Color tint)
        {
            var r = z.R;
            // Подложка: заметная, но не спорящая с игровым полем. Нажатая — заливка цветом.
            GUI.color = z.Active
                ? new Color(tint.r, tint.g, tint.b, 0.34f)
                : new Color(1f, 1f, 1f, 0.075f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);

            // Рамка в один пиксель опорного кадра: даёт кнопке край, не занимая площадь.
            GUI.color = new Color(tint.r, tint.g, tint.b, z.Active ? 0.85f : 0.34f);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            _zone.normal.textColor = new Color(tint.r, tint.g, tint.b, z.Active ? 1f : 0.72f);
            GUI.Label(r, z.Label, _zone);
        }

        private void EnsureStyles()
        {
            if (_hud != null) return;
            _hud = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperLeft };
            _hud.normal.textColor = new Color(0.86f, 0.93f, 0.98f, 0.92f);
            _big = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            _big.normal.textColor = new Color(1f, 0.44f, 0.38f, 0.95f);
            _zone = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            _zone.fontStyle = FontStyle.Bold;
            _warn = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
            };
        }
    }
}
