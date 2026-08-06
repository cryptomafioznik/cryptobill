using UnityEngine;

namespace ChartRunner.Track
{
    /// <summary>
    /// Данные дизайн-трассы `VS` из toys/chartrider.html (стр. 3572-3603), перенесённые 1:1.
    ///
    /// Живут в РАНТАЙМЕ, а не в Editor-бутстрапе, потому что источник истины должен быть один:
    /// и генератор ассетов, и тесты обязаны брать одни и те же узлы. Пока данные лежали в
    /// Editor-сборке, приёмочный тест окна навыка не мог до них дотянуться и мерил площадку
    /// полигона — а её чистый газ проходит целиком, поэтому окно навыка на ней не проявляется
    /// в принципе.
    /// </summary>
    public static class VerticalSliceTrack
    {
        /// <summary>35 узлов профиля в авторских px, высота вверх = +.</summary>
        public static Vector2[] Nodes =>
            new[]
            {
                new Vector2(0, 0), new Vector2(950, 0),                                  // 1. старт и разгон
                new Vector2(1200, 54), new Vector2(1450, 0),                             // 2. вупсы ×4, растущие
                new Vector2(1700, 74), new Vector2(1950, 0),
                new Vector2(2200, 96), new Vector2(2450, 0),
                new Vector2(2700, 120), new Vector2(2950, 0),
                new Vector2(3150, 0),                                                    // короткий выход
                new Vector2(3400, 150), new Vector2(4150, 900),                          // 3. технический подъём
                new Vector2(4320, 1010), new Vector2(4460, 1030),                        // 4. острый перелом вершины
                new Vector2(4700, 880), new Vector2(5150, 470),                          // 5. спуск
                new Vector2(5400, 300), new Vector2(5650, 340),                          // ...и яма (компрессия)
                new Vector2(5850, 360), new Vector2(6450, 360),                          // 6a. разгонная полоса
                new Vector2(6750, 470),                                                  // 6b. лип
                new Vector2(7090, 470), new Vector2(7650, 150),                          // 6c. посадка под уклон
                new Vector2(7900, 110), new Vector2(8150, 190), new Vector2(8350, 110),  // 7. техническая зона
                new Vector2(8600, 150), new Vector2(8850, 150),                          // короткий разгон
                new Vector2(9100, 240), new Vector2(9420, 560), new Vector2(10120, 1330),// 8. финальный подъём
                new Vector2(10380, 1400), new Vector2(10900, 1400), new Vector2(11400, 1400) // 9. финишная площадка
            };

        public const float EndPx = 11150f;
        public const float NodeStepPx = 26f;
        public const float GapFromPx = 6750f;
        public const float GapToPx = 7090f;

        /// <summary>Готовый профиль трассы. Один и тот же объект данных для ассета и для тестов.</summary>
        public static TrackProfile CreateProfile()
        {
            var t = ScriptableObject.CreateInstance<TrackProfile>();
            t.name = "VerticalSlice";
            t.nodesPx = Nodes;
            t.gapsPx = new[] { new TrackProfile.Gap { fromPx = GapFromPx, toPx = GapToPx } };
            t.endPx = EndPx;
            t.nodeStepPx = NodeStepPx;
            t.checkpoints = new[]
            {
                new TrackProfile.Checkpoint { xPx = 6100f, name = "перед прыжком" },
                new TrackProfile.Checkpoint { xPx = 8800f, name = "перед финальным подъёмом" }
            };
            t.flowBoostEnabled = false;
            return t;
        }
    }
}
