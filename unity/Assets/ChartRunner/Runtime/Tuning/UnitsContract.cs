namespace ChartRunner.Tuning
{
    /// <summary>
    /// Контракт единиц переноса Canvas → Unity. Обоснование и вывод каждого числа —
    /// docs/BIKE_PHYSICS_SPEC.md §1. Менять только вместе с тем документом.
    ///
    /// Исходная симуляция БЕЗРАЗМЕРНА: интегратор использовал sdt = 1/subSteps, поэтому
    /// скорости жили в px/кадр, ускорения в px/кадр². Гравитация ИЗМЕРЕНА запуском как
    /// ровно 0.26 px/кадр² (единственное различное значение на 286 падающих кадрах).
    ///
    /// Геометрический масштаб (база 52 px ≡ 1470 мм у YZ250F) и динамический (тот, при
    /// котором 0.26 px/кадр² равнялось бы 9.81 м/с²) расходятся в 2.70 раза. Принят
    /// ВАРИАНТ A: держим геометрию и соглашаемся, что мир идёт при 2.70 g. Это сохраняет
    /// фил без перекалибровки и отменяется одним числом (см. Gravity ниже).
    /// </summary>
    public static class UnitsContract
    {
        /// <summary>Колёсная база в пикселях исходной симуляции: 2 × RB.wb. НЕ 54 — см. TECH_DEBT §5.</summary>
        public const float SimWheelbasePx = 52f;

        /// <summary>Реальная колёсная база Yamaha YZ250F, метры. Опорная точка масштаба.</summary>
        public const float RealWheelbaseM = 1.470f;

        /// <summary>1 пиксель исходной симуляции в метрах = 28.269 мм.</summary>
        public const float PxToM = RealWheelbaseM / SimWheelbasePx;

        /// <summary>Один кадр исходной симуляции = один фикс-шаг.</summary>
        public const float SimFrameSeconds = 1f / 60f;

        /// <summary>1 px/кадр в м/с = 1.696.</summary>
        public const float PxPerFrameToMPerS = PxToM / SimFrameSeconds;

        /// <summary>1 px/кадр² в м/с² = 101.769.</summary>
        public const float PxPerFrame2ToMPerS2 = PxPerFrameToMPerS / SimFrameSeconds;

        /// <summary>Измеренная гравитация исходной симуляции, px/кадр².</summary>
        public const float SimGravityPxPerFrame2 = 0.26f;

        /// <summary>
        /// Гравитация проекта, м/с² = −26.46 (2.70 g). ЭТО И ЕСТЬ переключатель варианта A → B:
        /// поставить −9.81 и перекалибровать всё остальное по docs/BIKE_PHYSICS_SPEC.md §7.
        /// </summary>
        public const float GravityMPerS2 = -(SimGravityPxPerFrame2 * PxPerFrame2ToMPerS2);

        // ---- геометрия, пересчитанная из констант RB (docs/BIKE_PHYSICS_SPEC.md §1.4) ----

        /// <summary>Полубаза RB.wb = 26 px → 0.735 м.</summary>
        public const float HalfWheelbaseM = 26f * PxToM;

        /// <summary>Радиус колеса RB.wr = 12 px → 0.339 м. В физике 12, НЕ 13 — см. TECH_DEBT §5.</summary>
        public const float WheelRadiusM = 12f * PxToM;

        /// <summary>ЦТ над осью колёс RB.oy = 15.5 px → 0.438 м.</summary>
        public const float CgAboveAxleM = 15.5f * PxToM;

        /// <summary>ЦТ над линией земли = 27.5 px → 0.777 м. Главный рычаг опрокидывающего момента.</summary>
        public const float CgAboveGroundM = 27.5f * PxToM;

        /// <summary>Шаг узла трассы STEP = 26 px → 0.735 м.</summary>
        public const float TrackNodeStepM = 26f * PxToM;

        /// <summary>Масса шасси с райдером, кг. Принято: YZ250F ~106 + райдер ~75.</summary>
        public const float ChassisMassKg = 181f;

        /// <summary>
        /// Момент инерции по тангажу, кг·м² = RB.inertia(230) × масса × PxToM².
        /// Реальный ~54, то есть байк крутится по тангажу ЛЕГЧЕ реального — это «живость» вилли.
        /// </summary>
        public const float ChassisInertiaKgM2 = 230f * ChassisMassKg * PxToM * PxToM;

        /// <summary>Перевод силы симуляции (масса·px/кадр²) в ньютоны = 18 420.</summary>
        public const float SimForceToNewton = ChassisMassKg * PxPerFrame2ToMPerS2;
    }
}
