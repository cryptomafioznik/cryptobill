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
        Void,

        /// <summary>Догнала волна ликвидации. Единственный отказ, приходящий ИЗВНЕ физики.</summary>
        Liquidated
    }

    /// <summary>
    /// Текущее состояние езды. Порядок приоритета повторяет исходник (стр. 3132):
    /// воздух → приземление → подъём → вилли → нейтраль.
    ///
    /// Зачем вообще нужен ярлык состояния: без него в оверлее видны только числа, и человек
    /// не может сказать, ПОЧЕМУ байк ведёт себя так. В исходнике эта машина состояний уже
    /// была (`_pcState` / `_ph.state`) и использовалась анимацией; здесь она нужна для
    /// диагностики и для того, чтобы причина падения читалась из того же события, что его
    /// вызвало (критерий остановки аудита §14.5).
    /// </summary>
    public enum RidingState
    {
        Neutral,
        Airborne,
        Landing,
        Climbing,
        Descending,
        Wheelie,
        Stoppie,
        Braking,
        Reversing,
        Failed
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

        /// <summary>
        /// Сжатие подвески, метры: зад и перед. Читается из `WheelJoint2D.jointTranslation`,
        /// то есть это ФАКТИЧЕСКОЕ смещение по оси подвески, а не пересчёт из проникновения.
        /// В покое равно статической просадке (измерено 0.0996 м при 8 Гц).
        /// </summary>
        public float RearCompressionM;
        public float FrontCompressionM;

        /// <summary>Текущее состояние езды — ярлык для оверлея и для причины падения.</summary>
        public RidingState State;

        /// <summary>Сколько секунд байк в воздухе.</summary>
        public float AirTimeSeconds;

        /// <summary>Сколько секунд удерживается опасный угол (аналог `_phxFail`).</summary>
        public float FailHoldSeconds;

        public BikeFailure Failure;

        /// <summary>Пройдено по трассе, метры.</summary>
        public float DistanceM;
    }
}
