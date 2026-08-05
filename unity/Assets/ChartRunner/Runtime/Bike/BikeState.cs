namespace ChartRunner.Bike
{
    /// <summary>Чем закончилась попытка. Порт причин `wipeout` из исходника.</summary>
    public enum BikeFailure
    {
        None,

        /// <summary>Опрокид назад через заднее колесо.</summary>
        Loop,

        /// <summary>Клевок вперёд через руль.</summary>
        Endo,

        /// <summary>Посадка вне конуса или с остаточным вращением.</summary>
        Crash,

        /// <summary>Упал в пропасть.</summary>
        Void
    }

    /// <summary>
    /// Снимок состояния байка за физ-шаг. Это же — источник для оверлея телеметрии (пункт 7)
    /// и для тестов (пункт 8).
    ///
    /// Правило, вынесенное из ловушек исходника: тесты обязаны читать ЭТУ структуру, а не
    /// пересчитывать величины сами. Иначе тест меряет свою копию логики, а не продукт
    /// (ловушка №5 в memory/measurement-instrument-traps).
    /// </summary>
    public struct BikeState
    {
        public float PositionXM;
        public float PositionYM;

        /// <summary>Продольная скорость, м/с.</summary>
        public float SpeedMPerS;

        public float VerticalSpeedMPerS;

        /// <summary>Тангаж шасси в мире, радианы. Положительный = нос вверх.</summary>
        public float PitchRad;

        /// <summary>Тангаж ОТНОСИТЕЛЬНО поверхности, радианы. Положительный = нос вверх.</summary>
        public float PitchRelRad;

        /// <summary>Угловая скорость, рад/с. Положительная = нос идёт вверх.</summary>
        public float AngularVelocityRadPerS;

        /// <summary>Уклон поверхности под байком, радианы. Положительный = вверх по ходу.</summary>
        public float GroundSlopeRad;

        public int GroundedWheelCount;
        public bool IsGrounded => GroundedWheelCount > 0;

        /// <summary>Газ после рампы, 0..1.</summary>
        public float ThrottleApplied;

        public float BrakeApplied;

        /// <summary>Перенос веса, −1..+1. Положительное = вперёд.</summary>
        public float WeightShift;

        /// <summary>Сглаженная нормальная реакция на заднем колесе, Н.</summary>
        public float RearNormalLoadN;

        /// <summary>Сглаженная нормальная реакция на переднем колесе, Н.</summary>
        public float FrontNormalLoadN;

        /// <summary>Пробуксовка ведущего колеса: 0 = держит, 1 = сорвано.</summary>
        public float RearSlip;

        /// <summary>Сжатие подвески, метры: зад и перед.</summary>
        public float RearCompressionM;
        public float FrontCompressionM;

        /// <summary>Сколько секунд байк в воздухе.</summary>
        public float AirTimeSeconds;

        /// <summary>Сколько секунд удерживается опасный угол (аналог `_phxFail`).</summary>
        public float FailHoldSeconds;

        public BikeFailure Failure;

        /// <summary>Пройдено по трассе, метры.</summary>
        public float DistanceM;
    }
}
