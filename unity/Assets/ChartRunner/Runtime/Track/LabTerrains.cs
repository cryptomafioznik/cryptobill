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
                    // АНАТОМИЯ МЕГА-РАМПЫ ИСХОДНИКА, перенесённая по числам, а не придуманная:
                    //   megaRunupN 7 × megaRunupH 20 = 182 px разгон-спуска на −140 px
                    //   megaRampN 7 узлов ВОГНУТОГО липа, megaTotalH 104 px, megaCurve 2.6
                    //   megaGapDrop 44 px — чистый провал ниже липа
                    //   megaLandSlope 12 px/узел × megaLandN 16 = встречный довнслоп −192 px
                    //
                    // Первая редакция была ПРИДУМАНА, а не портирована: лип 180 px против 62 px
                    // у кикера и 104 px у мега-рампы исходника. Замерено: байк приходил к
                    // 19.4 м/с на подходе и терял всё на подъёме липа, доезжая до кромки на
                    // 5.8 м/с — то есть лип съедал скорость вместо того, чтобы её конвертировать.
                    //
                    // Кромка (индекс 10) помечена ОСТРОЙ: кубика Фрича–Карлсона гасит касательные
                    // в нуль на локальном максимуме, и без пометки кромка вырождается в гладкий
                    // купол, с которого колесо-круг не слетает. Исходник делал то же — не
                    // сглаживал узлы фич (стр. 248-249).
                    return new[]
                    {
                        new Vector2(0, 400), new Vector2(700, 400),        // ровный подход
                        new Vector2(882, 260),                            // разгон-спуск −140
                        new Vector2(1100, 260),                           // короткая полка
                        new Vector2(1126, 260.7f), new Vector2(1152, 264f),   // вогнутый лип
                        new Vector2(1178, 271.5f), new Vector2(1204, 284.3f),
                        new Vector2(1230, 303.4f), new Vector2(1256, 329.7f),
                        new Vector2(1282, 364f),                          // КРОМКА (индекс 10)
                        new Vector2(1308, 320f),                          // провал 44 px ниже липа
                        new Vector2(1724, 128f),                          // встречный довнслоп
                        new Vector2(2800, 128f)                           // выкат
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
            t.sharpNodeIndices = SharpNodes(pad);
            return t;
        }

        /// <summary>
        /// Острые узлы площадки. Кромка липа трамплина обязана быть острой: иначе кубика
        /// скругляет её в купол, и колесо-круг с него не слетает (замерено: 0.167 с воздуха).
        /// </summary>
        public static int[] SharpNodes(Pad pad)
        {
            // Индекс 10 в JumpRamp — узел (1282, 364), кромка липа.
            if (pad == Pad.JumpRamp) return new[] { 10 };
            return System.Array.Empty<int>();
        }

        /// <summary>Смещение площадок друг от друга по x, чтобы они не пересекались, метры.</summary>
        public const float PadSpacingM = 250f;
    }
}
