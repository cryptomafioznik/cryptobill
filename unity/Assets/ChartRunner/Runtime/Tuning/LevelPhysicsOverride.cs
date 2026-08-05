using UnityEngine;

namespace ChartRunner.Tuning
{
    /// <summary>
    /// Профиль помощников уровня — порт `PHX_DEF` + `VS.physics` из исходника
    /// (стр. 549-563 и 3598-3602).
    ///
    /// Смысл конструкции, который надо сохранить: 1.0 = в точности сток, 0 = помощник выключен,
    /// и профиль выбирается ОДИН РАЗ при старте уровня, поэтому в самом решателе нет ни одного
    /// `if (level == ...)`. Это была хорошая инженерия исходника (аудит §4.4) и она переносится.
    ///
    /// Отдельно переносится то, что в исходнике было зашито в две строки решателя, а не в конфиг:
    /// сложность управления зависела от ИСТОЧНИКА трассы (`trackSource !== 'ticker'`), то есть
    /// на реальном крипто-графике байк прощал больше, чем на авторской трассе. Здесь это
    /// явное поле <see cref="applyDesignHard"/> — иначе связь потерялась бы молча.
    /// </summary>
    [CreateAssetMenu(menuName = "ChartRunner/Level Physics Override", fileName = "LevelPhysicsOverride")]
    public class LevelPhysicsOverride : ScriptableObject
    {
        [Header("Стабилизация и выравнивание")]
        [Tooltip("PD-стабилизатор переда к углу поверхности. Сток 1, у vertical-slice 0.30: " +
                 "тяга реально задирает нос на крутом.")]
        public float groundAlign = 0.30f;

        [Tooltip("Авто-выравнивание в полёте. Сток 1, у vertical-slice 0: pitch держит игрок.")]
        public float airAutoLevel;

        [Tooltip("Гашение angV в полёте когда игрок НЕ наклоняет. Сток 1, у slice 0.45.")]
        public float airSpinDamp = 0.45f;

        [Tooltip("Гашение angV в полёте ПРИ УДЕРЖАНИИ наклона. Сток 0 (как было), у slice 0.45: " +
                 "без него вращение уходило на кап — замерено 450°/500 мс.")]
        public float airSpinDampLean = 0.45f;

        [Header("Авторитет игрока")]
        public float leanTorque = 1.5f;
        public float leanTorqueAir = 0.35f;
        public float throttleTorque = 1f;

        [Tooltip("Эндуро-сцепление на подъёме. Сток 1, у slice 0.35: крутое берётся импульсом, не одной тягой.")]
        public float climbTraction = 0.35f;

        [Header("Прощение у грани")]
        public float angDampAssist = 1f;
        public float edgeGuard = 0.25f;
        public float wheelieGuardScale;

        [Header("Пороги отказа")]
        [Tooltip("Масштаб порога опрокида назад. У slice 0.62.")]
        public float loopAngle = 0.62f;

        [Tooltip("Масштаб порога клевка вперёд. У slice 0.72.")]
        public float endoAngle = 0.72f;

        [Tooltip("Сколько секунд держать опасный угол ДО wipeout. Сток 0, у slice 0.25. " +
                 "Именно это даёт игроку окно на коррекцию: смерть не от угла, а от угла, " +
                 "удержанного при вращении в сторону опрокида.")]
        public float failHoldSeconds = 0.25f;

        [Header("Связь со источником трассы")]
        [Tooltip("Снимать ли прощение управления на крутом подъёме (designHard). В исходнике: " +
                 "true для авторских трасс, false для реального крипто-графика. Было зашито в " +
                 "решатель, здесь вынесено явно.")]
        public bool applyDesignHard = true;

        /// <summary>Значения «в точности сток» — все ручки 1, выдержки нет.</summary>
        public static LevelPhysicsOverride CreateStock()
        {
            var o = CreateInstance<LevelPhysicsOverride>();
            o.groundAlign = 1f;
            o.airAutoLevel = 1f;
            o.airSpinDamp = 1f;
            o.airSpinDampLean = 0f;
            o.leanTorque = 1f;
            o.leanTorqueAir = 1f;
            o.throttleTorque = 1f;
            o.climbTraction = 1f;
            o.angDampAssist = 1f;
            o.edgeGuard = 1f;
            o.wheelieGuardScale = 1f;
            o.loopAngle = 1f;
            o.endoAngle = 1f;
            o.failHoldSeconds = 0f;
            o.applyDesignHard = true;
            return o;
        }
    }
}
