using System;
using UnityEngine;

namespace ChartRunner.Tuning
{
    /// <summary>
    /// Единый профиль настройки байка. Порт данных из четырёх разных блоков исходника
    /// (`RB`, `TUNE`, `PHX_DEF`, `DRV_TUNE`) — см. docs/GAMEPLAY_SYSTEM_MAP.md §3.
    ///
    /// ГЛАВНОЕ ПРЕДУПРЕЖДЕНИЕ. Часть чисел исходника НЕ ЯВЛЯЕТСЯ физическими величинами и
    /// переносу не подлежит (docs/BIKE_PHYSICS_SPEC.md §1.4, §6): `mu = 2.6` — не коэффициент
    /// трения резины, `engine = 1.15` — не сила в ньютонах, `springK/springC` — жёсткость
    /// penalty-контакта, а не подвески. Эти поля здесь заполнены ПЛЕЙСХОЛДЕРАМИ и перечислены
    /// в <see cref="pendingCalibration"/>. Пока имя поля стоит в этом списке, его значение
    /// нельзя предъявлять как перенесённое из исходника.
    /// </summary>
    [CreateAssetMenu(menuName = "ChartRunner/Bike Tuning Profile", fileName = "BikeTuningProfile")]
    public class BikeTuningProfile : ScriptableObject
    {
        [Header("ЧТО ЕЩЁ НЕ ОТКАЛИБРОВАНО")]
        [Tooltip("Имена полей, значения которых — плейсхолдеры, а не перенос из исходника. " +
                 "Список обязан быть непустым до пункта 5; ProjectBootstrap это проверяет.")]
        public string[] pendingCalibration = Array.Empty<string>();

        // ================= геометрия и массы (пересчёт из RB, §1.4 спеки) =================

        [Header("Геометрия — пересчитано из RB, менять только вместе со спекой")]
        [Tooltip("RB.wb = 26 px → 0.735 м. Колёса на ±это от центра.")]
        public float halfWheelbaseM = UnitsContract.HalfWheelbaseM;

        [Tooltip("RB.wr = 12 px → 0.339 м. В ФИЗИКЕ 12, а не 13 — см. docs/TECH_DEBT.md §5.")]
        public float wheelRadiusM = UnitsContract.WheelRadiusM;

        [Tooltip("RB.oy = 15.5 px → 0.438 м, ЦТ выше оси колёс.")]
        public float cgAboveAxleM = UnitsContract.CgAboveAxleM;

        [Header("Массы")]
        [Tooltip("Принято: YZ250F ~106 кг + райдер ~75 кг. В исходнике масса была 1.0 безразмерная.")]
        public float chassisMassKg = UnitsContract.ChassisMassKg;

        [Tooltip("НОВАЯ величина: в исходнике колесо не было телом. Реальное колесо MX в сборе ~11-13 кг.")]
        public float wheelMassKg = 12f;

        [Tooltip("RB.inertia = 230 → 33.27 кг·м². Реальный ~54, то есть тангаж ЛЕГЧЕ реального — это живость вилли.")]
        public float chassisInertiaKgM2 = UnitsContract.ChassisInertiaKgM2;

        // ================= тяга =================

        [Header("Тяга — ЯДРО ИГРЫ, docs/BIKE_PHYSICS_SPEC.md §3")]
        [Tooltip("ПЛЕЙСХОЛДЕР. Выводится из требования «заезжает на 45° со стоячего старта» §7.4, " +
                 "не из RB.engine. Определяет РАЗГОН и способность лезть, но не верхнюю скорость.")]
        public float engineForceN = 4000f;   // ОТКАЛИБРОВАНО на полигоне: минимум, при котором байк заезжает на 45°

        [Tooltip("ВЕРХНЯЯ СКОРОСТЬ, м/с. Задаёт целевые обороты мотора, то есть крейсер на ровном. " +
                 "ИЗМЕРЕНО на исходнике для КРОСС 250: 3.6971 px/кадр = 6.271 м/с = 22.6 км/ч, " +
                 "установившийся, 108 сэмплов, разброс 0.003. Первая редакция кода держала здесь " +
                 "захардкоженные 30 м/с «с запасом», из-за чего крейсер был случайной величиной, " +
                 "а не эталоном.")]
        public float topSpeedMPerS = 6.271f;

        [Tooltip("ПЛЕЙСХОЛДЕР. Выводится из «заезжает на 45° и не буксует» §7.4, не из RB.mu = 2.6.")]
        public float tyreFriction = 1.2f;    // ОТКАЛИБРОВАНО: µ обязано превышать tan(45°) = 1.0, при µ=1.0 не заезжает ни при какой тяге

        [Tooltip("RB.thrRamp = 0.11 за кадр → время выхода газа на полную, с.")]
        public float throttleRampSeconds = 0.15f;

        [Tooltip("DRV_TUNE.fnSmooth = 0.22 за под-шаг при 6 под-шагах ≈ 0.075 с. Задаётся временем, не коэффициентом.")]
        public float normalLoadSmoothSeconds = 0.075f;

        [Tooltip("DRV_TUNE.floorFrac = 0.10. Остаточный пол прижима, доля веса — иначе байк глохнет на ровном.")]
        public float driveFloorFraction = 0.10f;

        [Tooltip("RB.climbGrip = 1.3. Эндуро-бонус сцепления на крутом подъёме: крутое берётся тягой.")]
        public float climbGrip = 1.3f;

        [Tooltip("RB.climbFrom = 0.28 рад ≈ 16°. С какого уклона включается бонус.")]
        public float climbFromRad = 0.28f;

        [Tooltip("RB.climbSpan = 0.30 рад ≈ +17°. За сколько бонус выходит на полную.")]
        public float climbSpanRad = 0.30f;

        [Tooltip("TUNE.driveTrade = 0.20. Вес назад грузит ведущее (+тяга), вперёд разгружает (−тяга). " +
                 "ГИПОТЕЗА: именно это ломает старого скриптового пилота, см. спеку §8.")]
        public float driveTrade = 0.20f;

        // ================= тормоз =================

        [Header("Тормоз")]
        [Tooltip("ПЛЕЙСХОЛДЕР. Выводится из пути торможения §7.7, не из RB.brakeF = 0.5.")]
        public float brakeForceN = 3000f;    // ОТКАЛИБРОВАНО: при этом значении предел ставит сцепление, дальнейший рост путь не сокращает

        [Tooltip("RB.reverse = 5.5. Сила заднего хода когда тормоз нажат на почти стоящем байке.")]
        public float reverseForceScale = 5.5f;

        [Tooltip("RB.reverseV = 1.6 px/кадр → м/с. Ниже этой скорости тормоз = реверс.")]
        public float reverseSpeedThresholdMPerS = 1.6f * UnitsContract.PxPerFrameToMPerS;

        [Tooltip("RB.wheelieBrk = 0.075. Задний тормоз опускает нос на вилли. НЕ гаснет ни в одном режиме — " +
                 "это главный инструмент спасения игрока.")]
        public float wheelieBrakeAuthority = 0.075f;

        // ================= подвеска и качение =================

        [Header("Подвеска — ПЛЕЙСХОЛДЕРЫ: penalty-контакт исходника не переводится в ход подвески")]
        public float suspensionFrequency = 8f;   // ИЗМЕРЕНО: даёт статическую просадку 0.0996 м = треть хода
        public float suspensionDamping = 0.7f;
        public float suspensionTravelM = 0.30f;

        [Header("Качение")]
        [Tooltip("RB.rollResist = 0.05 — ГЛАВНЫЙ лимитер крейсера на ровном, а не аэродинамика.")]
        public float rollResistance = 0.05f;

        [Tooltip("RB.mudRoll = 0.20 — сопротивление в грязи.")]
        public float mudRollResistance = 0.20f;

        // ================= угловой контроль =================

        [Header("Перенос веса — переносится числами, docs/BIKE_PHYSICS_SPEC.md §4.3")]
        [Tooltip("RB.leanGround = 0.016. Момент от ПОЛОЖЕНИЯ веса на земле.")]
        public float leanGround = 0.016f;

        [Tooltip("RB.leanAir = 0.050. Авторитет наклона в воздухе.")]
        public float leanAir = 0.050f;

        [Tooltip("RB.leanYank = 0.55. Момент от РЫВКА веса на земле. Не занижать: вилли инициируется рывком.")]
        public float leanYank = 0.55f;

        [Tooltip("RB.leanYankAir = 0.34. Импульс вращения за переброс веса в воздухе.")]
        public float leanYankAir = 0.34f;

        [Tooltip("RB.airHold = 0. ДЕРЖАТЬ 0: статичный вес в полёте не крутит — нет опоры. " +
                 "Любое ненулевое значение × длинный полёт = случайный оборот.")]
        public float airHold;

        [Header("Помощники")]
        public float frontLevel = 0.18f;
        public float frontLevelDamp = 0.35f;
        public float wheelieGuard = 0.10f;
        public float wheelieZone = 0.60f;
        public float wheelieEdge = 0.78f;
        public float antiLoop = 0.07f;
        public float loopLimitRad = 0.95f;
        public float uprightAir = 0.028f;
        public float uprightDamp = 0.34f;

        [Tooltip("RB.airAuthMin = 0.25 и airAuthT = 14 кадров → 0.233 с. Авторитет вращения ∝ времени полёта: " +
                 "в мелком подскоке байк не бросить телом, случайных переворотов с кочек нет.")]
        public float airAuthorityMin = 0.25f;

        public float airAuthorityRampSeconds = 14f / 60f;

        [Header("Демпферы и капы")]
        [Tooltip("RB.angDamp = 0.992 за кадр.")]
        public float angularDampingPerFrame = 0.992f;

        [Tooltip("RB.avMax = 0.42 рад/кадр → рад/с.")]
        public float maxAngularSpeedRadPerS = 0.42f * 60f;

        [Tooltip("RB.wheelieAvMax = 0.065 рад/кадр → рад/с. Кап темпа тангажа при активном вилли-помощнике.")]
        public float maxWheelieAngularSpeedRadPerS = 0.065f * 60f;

        // ================= отказы =================

        [Header("Краш и опрокид — переносится дословно, docs/BIKE_PHYSICS_SPEC.md §5")]
        [Tooltip("RB.crashAngle = 1.2 рад ≈ 69°. Отклонение от колёс-вниз при посадке.")]
        public float crashAngleRad = 1.2f;

        [Tooltip("RB.spinCrash = 0.34 рад/кадр. Остаточное вращение при посадке.")]
        public float spinCrashRadPerFrame = 0.34f;

        [Tooltip("RB.landCone = 0.8 рад ≈ 46°. Спин-краш ТОЛЬКО вне этого конуса — чинит ложные краши.")]
        public float landConeRad = 0.8f;

        [Tooltip("RB.spinHard = 0.9. Дикий перекрут крашит всегда.")]
        public float spinHardRadPerFrame = 0.9f;

        [Tooltip("Минимум кадров в воздухе, после которых проверяется посадка (14 в исходнике).")]
        public int landingCheckMinAirFrames = 14;

        [Tooltip("RB.loopCommit = 1.5 рад ≈ 86°. Точка невозврата по тангажу относительно поверхности.")]
        public float loopCommitRad = 1.5f;

        // ================= сложность дизайн-трассы =================

        [Header("designHard — снятие прощения на крутом подъёме")]
        [Tooltip("RB.designHard = 0.6. 0 = всё прощает, 1 = злее. Действует только на дизайн-трассах, " +
                 "не на реальном графике — в исходнике это было зашито в две строки решателя.")]
        public float designHard = 0.6f;

        public float designHardLevelAssist = 1f;
        public float designHardGuardAssist = 1f;

        [Tooltip("RB.climbCalm = 0.45. Гашение закрутки на крутом подъёме.")]
        public float climbCalm = 0.45f;

        // ================= прыжок =================

        [Header("Кнопка прыжка — НЕ ПЕРЕНОСИТСЯ по умолчанию")]
        [Tooltip("RB.jump = 9.0 px/кадр = 15.27 м/с, подъём 4.41 м. Нефизично при любом масштабе: " +
                 "аркадный хоп, в Gravity Defied такой кнопки нет. Решение пользователя (аудит §15.3). " +
                 "Пока выключено.")]
        public bool jumpButtonEnabled;

        public float jumpImpulseMPerS = 9f * UnitsContract.PxPerFrameToMPerS;
    }
}
