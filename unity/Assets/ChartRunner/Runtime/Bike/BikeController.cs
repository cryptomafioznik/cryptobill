using ChartRunner.Input;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Bike
{
    /// <summary>
    /// Физика управления байком. Порт docs/BIKE_PHYSICS_SPEC.md §3-§5.
    ///
    /// ЧТО ЗДЕСЬ ЕСТЬ И ЧЕГО ЗДЕСЬ НЕТ — это главное решение переноса.
    /// На настоящем решателе часть членов исходника воспроизводится сама, и добавлять их
    /// явно было бы двойным учётом (подробно в <see cref="BikeRig"/>). Поэтому здесь НЕТ:
    /// ограничения тяги по µ·Fn (его делает трение Box2D), driveTrade (его делает сдвиг ЦТ),
    /// момента от газа (его делает тяга в точке контакта на плече высоты ЦТ),
    /// импульса по склону (его делает гравитация).
    ///
    /// Здесь ЕСТЬ ровно то, что в исходнике было сознательным ПОМОЩНИКОМ, а не физикой:
    /// стабилизатор переда, подушка у грани вилли, anti-loop, задний тормоз на вилли,
    /// авто-выравнивание в полёте, авторитет вращения ∝ времени полёта, рывок телом.
    ///
    /// ЕДИНИЦЫ. Коэффициенты исходника заданы в рад/кадр² при 60 кадрах в секунду и умножались
    /// на момент инерции. Здесь они переводятся явно: жёсткостные члены ×3600, демпферные ×60
    /// (см. <see cref="PerFrame2ToPerS2"/>). Пропустить этот перевод — значит получить помощники
    /// в 3600 раз слабее, чем задумано, и не заметить.
    ///
    /// ЗНАКИ. В исходнике ось y смотрела вниз и «нос вверх» был ОТРИЦАТЕЛЬНЫМ rel. Здесь ось y
    /// вверх, поэтому положительный <see cref="BikeState.PitchRelRad"/> = нос вверх. Все пороги
    /// перевёрнуты соответственно; перепутать знак = получить помощники наоборот.
    /// </summary>
    [RequireComponent(typeof(BikeRig))]
    public class BikeController : MonoBehaviour
    {
        private const float PerFrame2ToPerS2 = 3600f; // (1/60 с)⁻²
        private const float PerFrameToPerS = 60f;

        public BikeTuningProfile Profile;
        public LevelPhysicsOverride Level;

        private BikeRig _rig;
        private TerrainSampler _terrain;
        private IBikeInputSource _input;

        private float _throttle;
        private float _weightShift;
        private float _prevWeightShift;
        private float _airTime;
        private float _failHold;
        private float _rearLoad;
        private float _frontLoad;
        private float _startX;
        private BikeFailure _failure;
        private bool _wasGrounded;
        private int _rearContacts;
        private int _frontContacts;
        private float _rearNormalRaw;
        private float _frontNormalRaw;

        public BikeState State { get; private set; }

        /// <summary>Физика остановлена (после отказа), пока не вызван Reset.</summary>
        public bool Halted { get; private set; }

        public void Initialise(BikeTuningProfile profile, LevelPhysicsOverride level,
            TerrainSampler terrain, IBikeInputSource input)
        {
            Profile = profile;
            Level = level;
            _terrain = terrain;
            _input = input;
            _rig = GetComponent<BikeRig>();
            _startX = _rig.Chassis.position.x;
            ResetTransients();
        }

        public void SetInput(IBikeInputSource input) => _input = input;

        private void ResetTransients()
        {
            _throttle = 0f;
            _weightShift = 0f;
            _prevWeightShift = 0f;
            _airTime = 0f;
            _failHold = 0f;
            _rearLoad = 0f;
            _frontLoad = 0f;
            _failure = BikeFailure.None;
            _wasGrounded = true;
            _rearContacts = 0;
            _frontContacts = 0;
            _rearNormalRaw = 0f;
            _frontNormalRaw = 0f;
            Halted = false;
        }

        /// <summary>
        /// Полный сброс. Список обнуляемого — ЯВНЫЙ, а не «что вспомнил».
        ///
        /// Это прямой урок из исходника: там `reset()` не обнулял `_wFnS` — аккумулятор
        /// сглаженного прижима, — и последовательные прогоны загрязняли друг друга. Замерено:
        /// та же политика на той же трассе давала 17.1 % вместо 87.8 %. Поэтому здесь сброс
        /// трогает ВСЁ переносимое между попытками, включая скорости колёс и мотор.
        /// </summary>
        public void ResetTo(Vector2 rearAxleWorldPos)
        {
            var halfWb = Profile.halfWheelbaseM;
            var center = rearAxleWorldPos + new Vector2(halfWb, 0f);

            _rig.Chassis.position = center;
            _rig.Chassis.rotation = 0f;
            _rig.Chassis.linearVelocity = Vector2.zero;
            _rig.Chassis.angularVelocity = 0f;
            _rig.Chassis.centerOfMass = _rig.BaseCenterOfMass;

            _rig.RearWheel.position = center + new Vector2(-halfWb, 0f);
            _rig.RearWheel.rotation = 0f;
            _rig.RearWheel.linearVelocity = Vector2.zero;
            _rig.RearWheel.angularVelocity = 0f;

            _rig.FrontWheel.position = center + new Vector2(halfWb, 0f);
            _rig.FrontWheel.rotation = 0f;
            _rig.FrontWheel.linearVelocity = Vector2.zero;
            _rig.FrontWheel.angularVelocity = 0f;

            var motor = _rig.RearJoint.motor;
            motor.motorSpeed = 0f;
            motor.maxMotorTorque = 0f;
            _rig.RearJoint.motor = motor;

            _startX = center.x;
            ResetTransients();
            State = default;
        }

        private void FixedUpdate()
        {
            if (_rig == null || _terrain == null) return;

            var dt = Time.fixedDeltaTime;
            var cmd = Halted ? BikeInputState.Neutral : (_input?.Read() ?? BikeInputState.Neutral);

            SampleContacts();
            var grounded = _rearContacts + _frontContacts > 0;

            // --- аналоговый газ: рампа по ВРЕМЕНИ, а не за кадр ---
            var rampRate = Profile.throttleRampSeconds > 1e-4f ? dt / Profile.throttleRampSeconds : 1f;
            _throttle = Mathf.MoveTowards(_throttle, Mathf.Clamp01(cmd.Throttle), rampRate);

            // --- перенос веса: сдвиг ЦТ + рывок ---
            ApplyWeightShift(cmd.Lean, dt);

            // --- тяга и тормоз ---
            var forwardSpeed = ForwardSpeed();
            ApplyDrive(cmd, forwardSpeed);

            // --- геометрия для помощников ---
            var slope = _terrain.SlopeAt(_rig.Chassis.position.x);
            var pitch = _rig.Chassis.rotation * Mathf.Deg2Rad;
            var rel = Mathf.DeltaAngle(slope * Mathf.Rad2Deg, pitch * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            var angV = _rig.Chassis.angularVelocity * Mathf.Deg2Rad;

            if (grounded)
            {
                _airTime = 0f;
                ApplyGroundAssists(rel, angV, slope, cmd);
            }
            else
            {
                _airTime += dt;
                ApplyAirAssists(cmd, angV);
            }

            ClampAngularSpeed();
            EvaluateFailure(grounded, rel, angV, slope, dt);

            PublishState(grounded, slope, pitch, rel, angV, forwardSpeed);
            _wasGrounded = grounded;
            _rearContacts = 0;
            _frontContacts = 0;
            _rearNormalRaw = 0f;
            _frontNormalRaw = 0f;
        }

        // ================= тяга =================

        private float ForwardSpeed()
        {
            // Скорость вдоль оси байка, а не по мировому x: на крутом склоне это разные вещи.
            var fwd = (Vector2)(_rig.Chassis.transform.right);
            return Vector2.Dot(_rig.Chassis.linearVelocity, fwd);
        }

        private void ApplyDrive(BikeInputState cmd, float forwardSpeed)
        {
            var motor = _rig.RearJoint.motor;

            var braking = cmd.Brake > 0.01f;
            var reversing = braking && cmd.Throttle < 0.01f
                            && forwardSpeed < Profile.reverseSpeedThresholdMPerS;

            if (reversing)
            {
                // Задний ход: тормоз на почти стоящем байке = сдать назад. Гейт «газ приоритетнее»
                // и порог скорости — как в исходнике (RB.reverse / RB.reverseV).
                motor.motorSpeed = TargetMotorSpeed(+1f);
                motor.maxMotorTorque = Profile.reverseForceScale * Profile.wheelRadiusM
                                       * Profile.chassisMassKg * 0.02f;
            }
            else if (braking)
            {
                motor.motorSpeed = 0f;
                motor.maxMotorTorque = Profile.brakeForceN * Profile.wheelRadiusM * cmd.Brake;
            }
            else if (_throttle > 0.01f)
            {
                // Момент мотора. ТЯГУ ОГРАНИЧИВАЕТ НЕ ЭТО ЧИСЛО, а трение Box2D в точке
                // контакта, которое само пропорционально нормальной реакции. Именно поэтому
                // ручной cap по µ·Fn из исходника здесь не нужен: свойство «газ на разгруженном
                // колесе не даёт тяги» получается из физики, а не из формулы.
                var target = TargetMotorSpeed(-1f);

                // МОТОР НЕ ИМЕЕТ ПРАВА ТОРМОЗИТЬ. Joint motor держит ЦЕЛЕВУЮ скорость, поэтому
                // при вращении быстрее цели он тянет назад — то есть удержанный газ гасил бы
                // всё, что набрала гравитация на спуске. Замерено на площадке JumpRamp: байк
                // разгонялся до 15.30 м/с на разгонном спуске и приходил к липу снова на 6.2 м/с
                // (ровно верхняя скорость), из-за чего не отрывался вообще — 0.017 с воздуха.
                // Настоящий двигатель так не работает: закрытый газ даёт выбег, а открытый
                // тем более не замедляет. Поэтому выше цели момент снимается.
                var wheelSpeed = _rig.RearWheel.angularVelocity; // град/с, отрицательная = вперёд
                var alreadyFaster = wheelSpeed <= target;        // «быстрее цели» в сторону движения
                motor.motorSpeed = target;
                motor.maxMotorTorque = alreadyFaster
                    ? 0f
                    : Profile.engineForceN * Profile.wheelRadiusM * _throttle * ClimbGripBoost();
            }
            else
            {
                motor.motorSpeed = 0f;
                motor.maxMotorTorque = 0f;
            }

            _rig.RearJoint.motor = motor;

            // Сопротивление качению — ГЛАВНЫЙ лимитер крейсера на ровном (в исходнике
            // rollResist, а не аэродинамика). Прикладываем к колёсам, а не к шасси.
            ApplyRollResistance(_rig.RearWheel);
            ApplyRollResistance(_rig.FrontWheel);
        }

        /// <summary>
        /// Эндуро-сцепление на подъёме. Порт строки 1562 исходника:
        /// <c>FnDrive *= 1 + min(1,(up−climbFrom)/climbSpan) × climbGrip × climbTraction</c>.
        ///
        /// ПОЧЕМУ ЭТО ПОЯВИЛОСЬ ЗДЕСЬ ПОЗЖЕ ОСТАЛЬНОГО. При переносе `climbGrip` попала в
        /// `notImplementedYet` — честно, но следствие не отследили, и оно оказалось тяжёлым:
        /// без буста дизайн-трасса становится физически непроходимой. Измерено трассировкой
        /// (docs/wall-diagnosis.md): байк встаёт на 271.0 м, уклон 50.5°, скорость падает до
        /// 2.42 м/с и дальше он не едет НИКОГДА и ни при какой политике пилота.
        ///
        /// Арифметика совпадает с замером: удержаться на 50.5° требует
        /// m·g·sin50.5° = 205 × 26.46 × 0.772 = 4187 Н, а мотор даёт 4000 Н при пределе
        /// сцепления µ·Fn = 1.2 × 3436 = 4123 Н. Не хватает 64 Н — отсюда «ползёт и встаёт».
        ///
        /// Это ассист, а не физика, и в исходнике он ассистом и задуман: комментарий там
        /// прямо говорит «заезжаемость = тяга, вызов = баланс» и требует climbGrip НЕ трогать
        /// при настройке сложности. То есть крутое берётся тягой ПО ЗАМЫСЛУ, а испытанием
        /// должен быть баланс, а не проходимость.
        ///
        /// Предел сцепления поднят соответственно в <see cref="BikeRig"/> — иначе поднятый
        /// момент упёрся бы в старый потолок трения и буст не дошёл бы до земли.
        /// </summary>
        private float ClimbGripBoost()
        {
            var up = _terrain.SlopeAt(_rig.Chassis.position.x);
            if (up <= Profile.climbFromRad) return 1f;
            var k = Mathf.Clamp01((up - Profile.climbFromRad) / Profile.climbSpanRad);

            // ГЕЙТ ПО СКОРОСТИ — не украшение, а исправление собственной регрессии.
            //
            // Исходник поднимал `FnDrive`, то есть ПРЕДЕЛ сцепления. Поднятый предел ничего
            // не меняет, пока в него не упираются: на разгоне по прямой тяга ограничена не
            // сцеплением, и буст там не действовал. Здесь же буст стоит на моменте мотора,
            // потому что предел трения Box2D нельзя менять на катящемся колесе, — и без
            // гейта он добавлял МОЩНОСТИ там, где исходник только снимал ограничение.
            //
            // Что это сломало, измерено батареей: тест C (отрыв с вогнутого липа) проходил
            // с воздухом 0.250 с, а с безусловным бустом дал 0.117 с и клевок на посадке.
            // Лип — это подъём, гейт по уклону на нём срабатывал, и лишний момент на полной
            // скорости портил вылет.
            //
            // Гейт восстанавливает смысл исходника: преимущество зубастой резины и первой
            // передачи существует, когда байк ГРЕБЁТ, и не существует, когда он летит на
            // верхней скорости. Порог 0.75 от верхней — там, где мотор ещё далёк от предела
            // оборотов и тяга действительно упирается в сцепление.
            var speed = Mathf.Abs(ForwardSpeed());
            var struggling = 1f - Mathf.Clamp01(speed / (0.75f * Profile.topSpeedMPerS));
            if (struggling <= 0f) return 1f;

            return 1f + k * struggling * Profile.climbGrip * Level.climbTraction;
        }

        private float TargetMotorSpeed(float sign)
        {
            // Целевая угловая скорость колеса, град/с, из ВЕРХНЕЙ СКОРОСТИ профиля.
            // Знак: движение в +x = вращение по часовой = отрицательная угловая скорость.
            //
            // Именно это число задаёт крейсер на ровном, а не баланс сил: мотор перестаёт
            // разгонять, дойдя до целевых оборотов. Физически это рев-лимит с передачей.
            // В первой редакции здесь стояли захардкоженные 30 м/с «с запасом» — из-за чего
            // крейсер был случайной величиной, не сверяемой ни с каким эталоном.
            var radPerS = Profile.topSpeedMPerS / Mathf.Max(0.01f, Profile.wheelRadiusM);
            return sign * radPerS * Mathf.Rad2Deg;
        }

        private void ApplyRollResistance(Rigidbody2D wheel)
        {
            // Момент сопротивления ∝ угловой скорости колеса.
            var w = wheel.angularVelocity * Mathf.Deg2Rad;
            var inertia = wheel.inertia > 1e-6f ? wheel.inertia : Profile.wheelMassKg * 0.05f;
            wheel.AddTorque(-w * Profile.rollResistance * inertia * PerFrameToPerS, ForceMode2D.Force);
        }

        // ================= перенос веса =================

        private void ApplyWeightShift(float lean, float dt)
        {
            _prevWeightShift = _weightShift;

            // Рампа переноса веса. Исходник: leanRampGnd 0.08 за кадр ≈ 0.55 с до полного.
            // Вынесена в профиль уровня, потому что это ГЛАВНАЯ ручка отзывчивости: чем
            // короче рампа, тем сильнее ощущается короткое нажатие, то есть тем ближе
            // управление к «любое микродвижение чувствуется».
            var rate = dt / Mathf.Max(0.05f, Level.leanRampSeconds);
            _weightShift = Mathf.MoveTowards(_weightShift, Mathf.Clamp(lean, -1f, 1f), rate);

            // СДВИГ ЦЕНТРА МАСС. Это и есть перенос веса райдера: реально меняет нагрузку
            // на колёса, поэтому driveTrade (вес назад = больше тяги) получается сам.
            //
            // Решение моделировать райдера сдвигом ЦТ, а не отдельным телом на слайдере:
            // отдельное тело даёт то же эмерджентно, но добавляет дребезг соединения и
            // ещё один источник настройки. Если фил окажется неверным — это первое, что
            // надо пересмотреть; отмечено в docs/IMPLEMENTATION_LOG.md.
            var reach = Profile.halfWheelbaseM * 0.55f;
            _rig.Chassis.centerOfMass = _rig.BaseCenterOfMass + new Vector2(_weightShift * reach, 0f);

            // РЫВОК ТЕЛОМ. Момент ∝ СКОРОСТИ переноса веса, а не его положению.
            // Причинно: вилли инициируется броском массы, а не тем, что масса стоит сзади —
            // на устоявшемся круизе моменты колёс взаимно гасятся и свободного подъёма нет.
            var dShift = (_weightShift - _prevWeightShift) / Mathf.Max(1e-6f, dt);
            var yank = _rig.Chassis.IsSleeping() ? 0f : dShift;
            var authority = _rearContacts + _frontContacts > 0 ? Profile.leanYank : Profile.leanYankAir;
            if (!(_rearContacts + _frontContacts > 0))
                authority *= AirAuthority();
            // Вес ВПЕРЁД (положительный) должен опускать нос → отрицательный момент.
            _rig.Chassis.AddTorque(-yank * authority * _rig.Chassis.inertia * Level.leanTorque,
                ForceMode2D.Force);

            // Момент от ПОЛОЖЕНИЯ веса. На земле частично гасится на крутом подъёме
            // (climbCalm), но на дизайн-трассе НЕ гасится: это единственный контр-инструмент
            // игрока против power-loop, и он нужен полным.
            if (_rearContacts + _frontContacts > 0)
            {
                var k = LeanScaleOnClimb();
                _rig.Chassis.AddTorque(
                    -_weightShift * Profile.leanGround * PerFrame2ToPerS2 * _rig.Chassis.inertia
                    * k * Level.leanTorque, ForceMode2D.Force);
            }
        }

        private float LeanScaleOnClimb()
        {
            var slope = _terrain.SlopeAt(_rig.Chassis.position.x);
            if (slope <= Profile.climbFromRad) return 1f;
            var ck = Mathf.Clamp01((slope - Profile.climbFromRad) / Profile.climbSpanRad);
            var dh = Level.applyDesignHard ? Profile.designHard * ck : 0f;
            return (1f - Profile.climbCalm * ck) * (1f - dh) + dh;
        }

        // ================= помощники =================

        private float DesignHardFactor()
        {
            if (!Level.applyDesignHard) return 0f;
            var slope = _terrain.SlopeAt(_rig.Chassis.position.x);
            if (slope <= Profile.climbFromRad) return 0f;
            var ck = Mathf.Clamp01((slope - Profile.climbFromRad) / Profile.climbSpanRad);
            return Profile.designHard * ck;
        }

        private void ApplyGroundAssists(float rel, float angV, float slope, BikeInputState cmd)
        {
            var I = _rig.Chassis.inertia;
            var dh = DesignHardFactor();

            // 1. PD-стабилизатор переда к углу склона.
            //
            // РАНЬШЕ ЗДЕСЬ БЫЛ ПОРОГ `|вес| < 0.2` — то есть выключатель. Любое нажатие
            // кнопки наклона гасило стабилизатор ЦЕЛИКОМ, а отпускание возвращало его разом
            // на полную. Для игрока это читалось так: «нажал — держать перестало, улетел;
            // отпустил — швырнуло носом вниз». Обрыв, а не кривая, и никакого баланса на
            // заднем колесе на таком не построить.
            //
            // Теперь авторитет стабилизатора УГАСАЕТ ПЛАВНО с ростом переноса веса: при
            // малых наклонах демпфирование сохраняется (там и живёт тонкий баланс), при
            // полном наклоне игрок получает всю власть, как и раньше.
            var leanAuthority = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((Mathf.Abs(_weightShift) - Level.leanFadeFrom)
                              / Mathf.Max(0.01f, Level.leanFadeSpan)));
            if (leanAuthority > 0.001f)
            {
                var acc = -rel * Profile.frontLevel * PerFrame2ToPerS2
                          - angV * Profile.frontLevelDamp * PerFrameToPerS;
                _rig.Chassis.AddTorque(acc * I * (1f - dh * Profile.designHardLevelAssist)
                                       * Level.groundAlign * leanAuthority, ForceMode2D.Force);
            }

            // 2. Подушка у грани вилли: мягко возвращает нос, начиная с wheelieZone и
            //    полностью к wheelieEdge. Гаснет на дизайн-крутом → грань пробивается.
            if (rel > Profile.wheelieZone)
            {
                var ed = Mathf.Clamp01((rel - Profile.wheelieZone)
                                       / Mathf.Max(1e-4f, Profile.wheelieEdge - Profile.wheelieZone));
                var acc = -ed * ed * Profile.wheelieGuard * PerFrame2ToPerS2
                          - angV * 0.5f * PerFrameToPerS;
                _rig.Chassis.AddTorque(acc * I * (1f - dh * Profile.designHardGuardAssist)
                                       * Level.wheelieGuardScale, ForceMode2D.Force);
            }
            // 2б. ЗЕРКАЛЬНАЯ ПОДУШКА У ГРАНИ КЛЕВКА.
            //
            // Измерено (LeanBalanceProbe): без неё нос ВНИЗ отзывался ВДВОЕ сильнее носа
            // вверх (перекос ×0.51 на филе БАЛАНС) — ровно то, что живой тест назвал
            // «несбалансировано». Причина структурная: у носа вверх подушка была, у носа
            // вниз — ничего до самого anti-loop, то есть одна сторона демпфирована,
            // другая падает свободно.
            //
            // Зона клевка своя, а не отражение вилли: клевок опаснее по последствиям
            // (через руль), поэтому подушка включается РАНЬШЕ по модулю угла. Множитель
            // общий (wheelieGuardScale): один пресет фила правит обе стороны, иначе они
            // снова разъедутся.
            else if (rel < -Profile.endoZone)
            {
                var ed = Mathf.Clamp01((-rel - Profile.endoZone)
                                       / Mathf.Max(1e-4f, Profile.endoEdge - Profile.endoZone));
                var acc = ed * ed * Profile.endoGuard * PerFrame2ToPerS2
                          - angV * 0.5f * PerFrameToPerS;
                _rig.Chassis.AddTorque(acc * I * Level.wheelieGuardScale, ForceMode2D.Force);
            }

            // 3. Задний тормоз опускает нос на вилли — НЕ гаснет никогда.
            //    Это главный инструмент спасения игрока, и он физически честный.
            if (cmd.Brake > 0.01f && rel > 0.15f)
            {
                var acc = -Mathf.Clamp01(rel) * Profile.wheelieBrakeAuthority * PerFrame2ToPerS2;
                _rig.Chassis.AddTorque(acc * I, ForceMode2D.Force);
            }

            // 4. anti-loop у предела. Возврат НОСА ВВЕРХ гаснет на дизайн-крутом,
            //    возврат носа вниз (endo) прощается всегда.
            if (rel > Profile.loopLimitRad)
            {
                var acc = -(rel - Profile.loopLimitRad) * Profile.antiLoop * PerFrame2ToPerS2;
                _rig.Chassis.AddTorque(acc * I * (1f - dh) * Level.edgeGuard, ForceMode2D.Force);
            }
            else if (rel < -Profile.loopLimitRad)
            {
                var acc = (-Profile.loopLimitRad - rel) * -Profile.antiLoop * PerFrame2ToPerS2;
                _rig.Chassis.AddTorque(acc * I * Level.edgeGuard, ForceMode2D.Force);
            }
        }

        private float AirAuthority()
        {
            // Авторитет вращения ∝ времени полёта: в мелком подскоке байк не «бросить» телом,
            // поэтому случайных переворотов с кочек нет; на большом полёте контроль полный.
            var t = Mathf.Clamp01(_airTime / Mathf.Max(1e-4f, Profile.airAuthorityRampSeconds));
            return Mathf.Lerp(Profile.airAuthorityMin, 1f, t);
        }

        private void ApplyAirAssists(BikeInputState cmd, float angV)
        {
            var I = _rig.Chassis.inertia;

            if (Mathf.Abs(_weightShift) < 0.15f)
            {
                // Авто-выравнивание к углу БУДУЩЕЙ поверхности + гашение спина.
                // Смотрим вперёд по ходу — как в исходнике (terrainAt(x + max(20, vx*8))).
                var lookAhead = Mathf.Max(0.55f, State.SpeedMPerS * 0.13f);
                var futureSlope = _terrain.SlopeAt(_rig.Chassis.position.x + lookAhead);
                var pitch = _rig.Chassis.rotation * Mathf.Deg2Rad;
                var d = Mathf.DeltaAngle(pitch * Mathf.Rad2Deg, futureSlope * Mathf.Rad2Deg)
                        * Mathf.Deg2Rad;

                var acc = d * Profile.uprightAir * PerFrame2ToPerS2 * Level.airAutoLevel
                          - angV * Profile.uprightDamp * PerFrameToPerS * Level.airSpinDamp;
                _rig.Chassis.AddTorque(acc * I, ForceMode2D.Force);
            }
            else if (Level.airSpinDampLean > 0f)
            {
                // Демпфер ПРИ УДЕРЖАНИИ наклона. В стоке он 0 (как было), в профиле уровня >0:
                // без него вращение уходило на кап — в исходнике замерено 450°/500 мс.
                var acc = -angV * Profile.uprightDamp * PerFrameToPerS * Level.airSpinDampLean;
                _rig.Chassis.AddTorque(acc * I, ForceMode2D.Force);
            }

            // Прыжок — только если профиль разрешает. По умолчанию выключен.
            if (cmd.JumpPressed && Profile.jumpButtonEnabled && _airTime < 0.05f)
            {
                _rig.Chassis.linearVelocity += new Vector2(0f, Profile.jumpImpulseMPerS);
            }
        }

        private void ClampAngularSpeed()
        {
            var maxDeg = Profile.maxAngularSpeedRadPerS * Mathf.Rad2Deg;

            // КАП ТЕМПА ТАНГАЖА НА ВИЛЛИ. В исходнике (RB.wheelieAvMax) он существовал потому,
            // что без него «angV −0.1 на −0.8 рад отрывал заднее колесо → воздух → флип → краш»:
            // перед поднимался рывком и перелетал цель. Кап делает подъём переда медленным.
            // Действует только когда игрок ДЕРЖИТ вес назад и байк на земле — обычная езда
            // и воздух не тронуты.
            if (_weightShift < -0.05f && _rearContacts + _frontContacts > 0)
                maxDeg = Mathf.Min(maxDeg, Profile.maxWheelieAngularSpeedRadPerS * Mathf.Rad2Deg);

            var w = _rig.Chassis.angularVelocity;

            // Демпфер: 0.992 за кадр → эквивалент за dt.
            var damp = Mathf.Pow(Profile.angularDampingPerFrame, Time.fixedDeltaTime * 60f);
            w *= Mathf.Lerp(1f, damp, Level.angDampAssist);

            _rig.Chassis.angularVelocity = Mathf.Clamp(w, -maxDeg, maxDeg);
        }

        // ================= контакты и отказы =================

        private void SampleContacts()
        {
            // Контакты считаются из коллизий колёс; нормальная реакция берётся из импульса,
            // а не из скорости после решателя. Это прямое требование спеки §5.1: velocity в
            // момент коллизии уже пост-импульсная.
            var dt = Time.fixedDeltaTime;
            _rearLoad = Mathf.Lerp(_rearLoad, _rearNormalRaw / Mathf.Max(1e-6f, dt),
                SmoothAlpha(dt, Profile.normalLoadSmoothSeconds));
            _frontLoad = Mathf.Lerp(_frontLoad, _frontNormalRaw / Mathf.Max(1e-6f, dt),
                SmoothAlpha(dt, Profile.normalLoadSmoothSeconds));
        }

        private static float SmoothAlpha(float dt, float tau)
        {
            if (tau <= 1e-5f) return 1f;
            return 1f - Mathf.Exp(-dt / tau);
        }

        /// <summary>Вызывается из <see cref="BikeWheelContactRelay"/>.</summary>
        internal void ReportWheelContact(bool rear, float normalImpulse)
        {
            if (rear)
            {
                _rearContacts++;
                _rearNormalRaw += normalImpulse;
            }
            else
            {
                _frontContacts++;
                _frontNormalRaw += normalImpulse;
            }
        }

        private void EvaluateFailure(bool grounded, float rel, float angV, float slope, float dt)
        {
            if (_failure != BikeFailure.None) return;

            // Падение в пропасть: провалился существенно ниже уровня трассы.
            var groundY = _terrain.HeightAt(_rig.Chassis.position.x);
            if (_rig.Chassis.position.y < groundY - 6f)
            {
                Fail(BikeFailure.Void);
                return;
            }

            // КРАШ ПРИ ПОСАДКЕ. Проверяется на кадре касания после достаточного полёта.
            var minAir = Profile.landingCheckMinAirFrames * (1f / 60f);
            if (grounded && !_wasGrounded && _airTime > minAir)
            {
                var offCone = Mathf.Abs(rel);
                var spin = Mathf.Abs(angV) / PerFrameToPerS; // рад/кадр, как в исходнике
                var hard = offCone > Profile.crashAngleRad
                           || (spin > Profile.spinCrashRadPerFrame && offCone > Profile.landConeRad)
                           || spin > Profile.spinHardRadPerFrame;
                if (hard)
                {
                    Fail(BikeFailure.Crash);
                    return;
                }
            }

            // ОПРОКИД С ВЫДЕРЖКОЙ. Смерть наступает не от угла, а от угла, УДЕРЖАННОГО
            // failHold секунд при вращении в сторону опрокида. Именно это даёт игроку окно.
            var loopThreshold = Profile.loopCommitRad * Level.loopAngle;
            var endoThreshold = Profile.loopCommitRad * Level.endoAngle;
            var onClimb = slope > Profile.climbFromRad;

            var loopRisk = onClimb && rel > loopThreshold && angV > 0.9f * Mathf.Deg2Rad;
            var endoRisk = rel < -endoThreshold && angV < -0.9f * Mathf.Deg2Rad;

            if (loopRisk || endoRisk)
            {
                _failHold += dt;
                if (_failHold >= Level.failHoldSeconds)
                    Fail(loopRisk ? BikeFailure.Loop : BikeFailure.Endo);
            }
            else if (grounded)
            {
                _failHold = 0f;
            }
        }

        /// <summary>
        /// Отказ, назначенный ИЗВНЕ: волна ликвидации догнала игрока. Физика тут ни при
        /// чём — это правило заезда, поэтому и вход отдельный, а не подмена внутреннего
        /// состояния. Мотор глушится тем же путём, что при физическом отказе.
        /// </summary>
        public void Liquidate()
        {
            if (Halted) return;
            Fail(BikeFailure.Liquidated);
        }

        /// <summary>
        /// PUMP (b1602): мягкий постоянный толчок вперёд во время рывка — исходник делал
        /// `bike.vx += pumpForce` каждый кадр. Здесь — прибавка скорости шасси вдоль его оси.
        /// </summary>
        public void PushForward(float deltaMPerS)
        {
            if (Halted || _rig == null) return;
            var fwd = (Vector2)_rig.Chassis.transform.right;
            _rig.Chassis.linearVelocity += fwd * deltaMPerS;
        }

        private void Fail(BikeFailure reason)
        {
            _failure = reason;
            Halted = true;
            var motor = _rig.RearJoint.motor;
            motor.maxMotorTorque = 0f;
            _rig.RearJoint.motor = motor;
        }

        private void PublishState(bool grounded, float slope, float pitch, float rel, float angV,
            float forwardSpeed)
        {
            var pos = _rig.Chassis.position;
            State = new BikeState
            {
                PositionXM = pos.x,
                PositionYM = pos.y,
                SpeedMPerS = forwardSpeed,
                VerticalSpeedMPerS = _rig.Chassis.linearVelocity.y,
                PitchRad = pitch,
                PitchRelRad = rel,
                AngularVelocityRadPerS = angV,
                GroundSlopeRad = slope,
                GroundedWheelCount = (_rearContacts > 0 ? 1 : 0) + (_frontContacts > 0 ? 1 : 0),
                ThrottleApplied = _throttle,
                BrakeApplied = _input == null ? 0f : Mathf.Clamp01(_input.Read().Brake),
                WeightShift = _weightShift,
                RearNormalLoadN = _rearLoad,
                FrontNormalLoadN = _frontLoad,
                RearSlip = ComputeRearSlip(forwardSpeed),
                RearCompressionM = _rig.RearJoint.jointTranslation,
                FrontCompressionM = _rig.FrontJoint.jointTranslation,
                AirTimeSeconds = _airTime,
                FailHoldSeconds = _failHold,
                Failure = _failure,
                State = ClassifyState(grounded, rel, slope, forwardSpeed),
                DistanceM = pos.x - _startX
            };
        }

        /// <summary>
        /// Ярлык текущего состояния. Приоритет повторяет исходник (стр. 3132):
        /// воздух → приземление → подъём → вилли → нейтраль. Это диагностика, а не физика:
        /// ни одно решение решателя от ярлыка не зависит.
        /// </summary>
        private RidingState ClassifyState(bool grounded, float rel, float slope, float forwardSpeed)
        {
            if (_failure != BikeFailure.None) return RidingState.Failed;
            if (!grounded) return RidingState.Airborne;

            // Приземление: только что коснулись после существенного полёта.
            if (!_wasGrounded && _airTime > Profile.landingCheckMinAirFrames * (1f / 60f))
                return RidingState.Landing;

            if (rel > 0.25f) return RidingState.Wheelie;
            if (rel < -0.25f) return RidingState.Stoppie;

            if (forwardSpeed < -0.05f) return RidingState.Reversing;

            var braking = _input != null && _input.Read().Brake > 0.01f;
            if (braking && forwardSpeed > 0.2f) return RidingState.Braking;

            if (slope > Profile.climbFromRad) return RidingState.Climbing;
            if (slope < -Profile.climbFromRad) return RidingState.Descending;

            return RidingState.Neutral;
        }

        private float ComputeRearSlip(float forwardSpeed)
        {
            // Пробуксовка: окружная скорость колеса против продольной скорости байка.
            var surface = -_rig.RearWheel.angularVelocity * Mathf.Deg2Rad * Profile.wheelRadiusM;
            var diff = surface - forwardSpeed;
            return Mathf.Clamp01(Mathf.Abs(diff) / Mathf.Max(1f, Mathf.Abs(surface)));
        }
    }
}
