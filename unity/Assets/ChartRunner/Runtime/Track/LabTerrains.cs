using UnityEngine;

namespace ChartRunner.Track
{
    /// <summary>
    /// Шесть площадок полигона `BikePhysicsLab` (пункт 6 плана): ровно, пологий склон,
    /// резкий подъём, спуск, трамплин, мелкие неровности.
    ///
    /// Заданы как ДАННЫЕ в авторских пикселях, тем же форматом, что и дизайн-трасса, и
    /// прогоняются через ту же <see cref="MonotoneCubic"/>. Причина: если полигон будет
    /// использовать свою интерполяцию, он начнёт мерить не ту геометрию, по которой едет игра.
    ///
    /// Уклоны выбраны по эталонам docs/BIKE_PHYSICS_SPEC.md §7: 45° повторяет секцию 3
    /// дизайн-трассы, −44° — секцию 5, 35° — вупсы.
    /// </summary>
    public static class LabTerrains
    {
        public enum Pad
        {
            Flat,
            GentleSlope,
            SteepClimb,
            SteepDescent,
            JumpRamp,
            SmallBumps
        }

        /// <summary>Профиль площадки: узлы в авторских px, высота вверх = +.</summary>
        public static Vector2[] Nodes(Pad pad)
        {
            switch (pad)
            {
                case Pad.Flat:
                    // Длинная ровная полоса: разгон до крейсера и торможение.
                    return new[] { new Vector2(0, 0), new Vector2(6000, 0) };

                // ВАЖНО ПРО ВЫСОТЫ НИЖЕ. Высоты подобраны НЕ по углу хорды, а решением обратной
                // задачи, потому что монотонная кубика между двумя ПЛОСКИМИ участками гасит
                // касательные на обоих концах в ноль, и середина сегмента выходит ровно в
                // 1.5 раза круче хорды: tan(уклон сетки) = 1.5 · Δy/h.
                // Измерено на первой редакции этих данных: хорда 45° давала 56.3°, хорда 15° —
                // 21.9°, хорда −44° — −55.4°, коэффициент 1.500 во всех трёх случаях.
                // Поэтому Δy = tan(цель)/1.5 · h.

                case Pad.GentleSlope:
                    // 15° по СЕТКЕ: ниже climbFrom (16°), эндуро-бонус ещё не включается.
                    // Δy = tan(15°)/1.5 · 2000 = 357 px.
                    return new[]
                    {
                        new Vector2(0, 0), new Vector2(1000, 0),
                        new Vector2(3000, 357), new Vector2(4000, 357)
                    };

                case Pad.SteepClimb:
                    // 45° по СЕТКЕ: та же крутизна И ТА ЖЕ ДЛИНА, что секция 3 дизайн-трассы
                    // (3400→4150 px = 750 px подъёма). Первая редакция брала 1500 px, то есть
                    // вдвое длиннее реальной секции: при верхней скорости 6.27 м/с такой подъём
                    // физически не пройти за разумное время, и тест мерил длину, а не тягу.
                    // Δy = tan(45°)/1.5 · 750 = 500 px.
                    return new[]
                    {
                        new Vector2(0, 0), new Vector2(1500, 0),
                        new Vector2(2250, 500), new Vector2(3250, 500)
                    };

                case Pad.SteepDescent:
                    // −44° по СЕТКЕ: та же крутизна, что секция 5 (эталон §7.9).
                    // Δy = tan(44°)/1.5 · 1450 = 933 px.
                    return new[]
                    {
                        new Vector2(0, 933), new Vector2(1200, 933),
                        new Vector2(2650, 0), new Vector2(4500, 0)
                    };

                case Pad.JumpRamp:
                    // Анатомия из исходника: разгон-спуск → ВОГНУТЫЙ лип → провал → встречный
                    // довнслоп посадки. Вылет эмерджентный от схода липа, а не скрипт-пуск.
                    //
                    // РАЗГОН-СПУСК УГЛУБЛЁН против первой редакции. Замерено: с прежним спуском
                    // (140 px за 600 px ≈ −13°) байк подходил к липу на верхней скорости
                    // 6.27 м/с и НЕ отрывался — максимум 0.017 с, то есть один кадр. Причина
                    // в контракте единиц: при 2.70 g и крейсере 6.27 м/с воздуха мало по
                    // существу, а мотор выше крейсера не разгоняет. Скорость на лип должна
                    // приносить ГРАВИТАЦИЯ, а не мотор — как и в анатомии исходника.
                    return new[]
                    {
                        new Vector2(0, 500), new Vector2(700, 500),
                        new Vector2(1400, 60),                       // разгон-спуск ~−44°
                        new Vector2(2000, 60),
                        new Vector2(2200, 30), new Vector2(2400, 130),// вогнутый лип
                        new Vector2(2500, 210),
                        new Vector2(3100, 120), new Vector2(3600, 0), // встречный довнслоп
                        new Vector2(5000, 0)
                    };

                case Pad.SmallBumps:
                    // Вупсы растущей амплитуды, как секция 2 дизайн-трассы.
                    return new[]
                    {
                        new Vector2(0, 0), new Vector2(900, 0),
                        new Vector2(1150, 54), new Vector2(1400, 0),
                        new Vector2(1650, 74), new Vector2(1900, 0),
                        new Vector2(2150, 96), new Vector2(2400, 0),
                        new Vector2(2650, 120), new Vector2(2900, 0),
                        new Vector2(3600, 0)
                    };

                default:
                    return new[] { new Vector2(0, 0), new Vector2(1000, 0) };
            }
        }

        /// <summary>Где ставить байк на этой площадке, авторские px.</summary>
        public static float StartXPx(Pad pad)
        {
            switch (pad)
            {
                case Pad.Flat: return 200f;
                case Pad.GentleSlope: return 300f;
                case Pad.SteepClimb: return 300f;
                case Pad.SteepDescent: return 300f;
                case Pad.JumpRamp: return 200f;
                case Pad.SmallBumps: return 200f;
                default: return 200f;
            }
        }

        /// <summary>Готовый TrackProfile для площадки — тот же тип, что у дизайн-трассы.</summary>
        public static TrackProfile CreateProfile(Pad pad)
        {
            var t = ScriptableObject.CreateInstance<TrackProfile>();
            t.name = "Lab_" + pad;
            t.nodesPx = Nodes(pad);
            t.nodeStepPx = 26f;
            t.endPx = t.nodesPx[t.nodesPx.Length - 1].x;
            t.flowBoostEnabled = false;
            return t;
        }

        /// <summary>Смещение площадок друг от друга по x, чтобы они не пересекались, метры.</summary>
        public const float PadSpacingM = 250f;
    }
}
