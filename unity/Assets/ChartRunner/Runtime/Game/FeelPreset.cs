using ChartRunner.Meta;
using ChartRunner.Tuning;

namespace ChartRunner.Game
{
    /// <summary>
    /// Три варианта ФИЛА УПРАВЛЕНИЯ, переключаемых в игре.
    ///
    /// Почему вариантами, а не одной «правильной» настройкой: это вкусовой вопрос, а вкус
    /// решает человек. Правило из памяти проекта: для вкусового выбора три грубых варианта
    /// за час дороже одного полированного за ночь. Скриптовый пилот тут не судья — он уже
    /// дважды за эту сессию «доказывал» вещи, которые оказывались его собственными дефектами.
    ///
    /// ЧТО ИМЕННО РАЗЛИЧАЕТСЯ И ПОЧЕМУ ЭТИ РУЧКИ. Живой тест дал точную жалобу: «нужно
    /// сильно нажать, чтобы поднять, и тогда переворачивается; и сильно отпускает — очень
    /// сильный дисбаланс; должно быть как в Gravity Defied, любое микродвижение должно
    /// ощущаться, и на заднем колесе можно ехать балансируя». Разбор показал две структурные
    /// причины, и обе сняты ручками ниже:
    ///
    ///   1. `wheelieGuardScale = 0` — подушка у грани вилли ВЫКЛЮЧЕНА. Между wheelieZone
    ///      (34°) и anti-loop (54°) нет ни одной возвращающей силы, то есть нет точки, вокруг
    ///      которой можно балансировать. Есть только обрыв в опрокид.
    ///   2. Стабилизатор переда гасился ПОРОГОМ `|вес| < 0.2` — выключателем. Нажал кнопку
    ///      наклона — опора исчезла целиком; отпустил — вернулась рывком на полную.
    ///      Теперь это плавное угасание (leanFadeFrom / leanFadeSpan).
    ///
    /// Третья ручка — `leanRampSeconds`: за сколько удержание кнопки даёт полный перенос.
    /// Именно она отвечает за «микродвижение ощущается»: короткая рампа делает короткий тап
    /// заметным, длинная требует «сильно нажать».
    /// </summary>
    public enum Feel
    {
        /// <summary>Баланс: есть точка равновесия на заднем колесе, отклик быстрый.</summary>
        Balance,

        /// <summary>Среднее между балансом и стоком.</summary>
        Middle,

        /// <summary>Как было до правки: подушка выключена, стабилизатор с порогом.</summary>
        Raw
    }

    public static class FeelPreset
    {
        public static string Name(Feel f)
        {
            switch (f)
            {
                case Feel.Balance: return Loc.T("БАЛАНС");
                case Feel.Middle: return Loc.T("СРЕДНЕ");
                default: return Loc.T("ОСТРО");
            }
        }

        public static Feel Next(Feel f)
        {
            switch (f)
            {
                case Feel.Balance: return Feel.Middle;
                case Feel.Middle: return Feel.Raw;
                default: return Feel.Balance;
            }
        }

        /// <summary>
        /// Накладывает вариант на профиль уровня. Меняются ТОЛЬКО ручки фила наклона —
        /// всё остальное (пороги отказа, прощение у грани, тяга) остаётся общим, иначе
        /// сравнение вариантов перестало бы быть сравнением одной вещи.
        /// </summary>
        public static void Apply(LevelPhysicsOverride level, Feel f)
        {
            switch (f)
            {
                case Feel.Balance:
                    // Подушка включена заметно: она и создаёт точку равновесия, вокруг
                    // которой можно держать вилли. Рампа короткая — тап чувствуется.
                    level.wheelieGuardScale = 0.45f;
                    level.leanRampSeconds = 0.28f;
                    level.leanTorque = 1.15f;
                    level.leanFadeFrom = 0.20f;
                    level.leanFadeSpan = 0.65f;
                    break;

                case Feel.Middle:
                    level.wheelieGuardScale = 0.22f;
                    level.leanRampSeconds = 0.40f;
                    level.leanTorque = 1.30f;
                    level.leanFadeFrom = 0.18f;
                    level.leanFadeSpan = 0.45f;
                    break;

                default:
                    // Ровно прежнее поведение: подушки нет, стабилизатор с жёстким порогом.
                    // Нужен как точка отсчёта — без него «стало лучше» нечем проверить.
                    level.wheelieGuardScale = 0f;
                    level.leanRampSeconds = 0.55f;
                    level.leanTorque = 1.5f;
                    level.leanFadeFrom = 0.20f;
                    level.leanFadeSpan = 0.001f;
                    break;
            }
        }
    }
}
