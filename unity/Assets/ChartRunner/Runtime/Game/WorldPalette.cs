using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Палитра мира — ПЕРЕНОС, а не сочинение. Все значения сняты с toys/chartrider.html:
    /// восемь рыночных эпох (BIOMES, стр. 1023) с приведением к сине-чёрной базе
    /// (SKY_UNIFY, стр. 1035) — та самая картинка, которую пользователь принимал годами
    /// и назвал лучше перенесённой. Урок этой сессии: направление визуала не выдумывается
    /// заново, когда валидированное уже существует.
    /// </summary>
    public static class WorldPalette
    {
        public struct Era
        {
            public string Name;
            /// <summary>Четыре остановки неба, сверху вниз (стопы 0 / 0.45 / 0.72 / 1.0).</summary>
            public Color Sky0, Sky1, Sky2, Sky3;
            /// <summary>Цвет света горизонта/лучей (BLINE).</summary>
            public Color Line;
            /// <summary>Цвет дальних масс (BRIDGE).</summary>
            public Color Bridge;
        }

        // ---- константы идентичности (не зависят от эпохи) ----

        /// <summary>Сталь структуры трассы (TSTEEL): своя во всех эпохах, цвет — у данных.</summary>
        public static readonly Color Steel = C(208, 224, 248);

        /// <summary>Данные: подъём/спуск. Те же 46,230,166 / 255,59,92, что в исходнике.</summary>
        public static readonly Color DataUp = C(46, 230, 166);
        public static readonly Color DataDown = C(255, 59, 92);

        /// <summary>Тело деки: графит сверху → темнее к низу (_trkBodyG).</summary>
        public static readonly Color DeckTop = C(17, 21, 40);
        public static readonly Color DeckBottom = C(9, 11, 24);
        public static readonly Color DeckShadow = C(6, 4, 16);

        /// <summary>Неон крыш и окон города (cityPass glow): бирюза/розовый.</summary>
        public static readonly Color RoofTeal = C(64, 225, 190);
        public static readonly Color RoofPink = C(255, 96, 150);
        public static readonly Color WindowWarm = C(255, 206, 140);

        /// <summary>Тональная лепка зданий ближнего плана (b222): свет → тень.</summary>
        public static readonly Color CityLit = C(104, 66, 70);
        public static readonly Color CityMid = C(30, 21, 40);
        public static readonly Color CityShade = C(7, 6, 17);
        /// <summary>Дальний план приглушён (атмосферная перспектива).</summary>
        public static readonly Color CityFarLit = new Color(96 / 255f, 86 / 255f, 110 / 255f, 0.62f);
        public static readonly Color CityFarMid = new Color(60 / 255f, 54 / 255f, 78 / 255f, 0.60f);
        public static readonly Color CityFarShade = new Color(40 / 255f, 36 / 255f, 58 / 255f, 0.58f);

        /// <summary>Тёплая кромка здания со стороны солнца (b221).</summary>
        public static readonly Color SunEdge = C(255, 176, 116);

        /// <summary>Силуэт пальм и тёмных масс (как #0e0a20 исходника).</summary>
        public static readonly Color Silhouette = C(14, 10, 32);

        /// <summary>Вуаль-дымка над всем задним планом (b209): 148,138,198 × 0.12.</summary>
        public static readonly Color Veil = new Color(148 / 255f, 138 / 255f, 198 / 255f, 0.12f);

        // ---- эпохи ----

        // ВАЖНО: Unify/Base объявлены ДО Eras — статические инициализаторы выполняются
        // в порядке объявления, и Eras зависит от них. Обратный порядок = NRE в конструкторе.
        /// <summary>SKY_UNIFY исходника: сила приведения каждой остановки к базе.</summary>
        private static readonly float[] Unify = { 0.62f, 0.45f, 0.30f, 0.26f };
        private static readonly Vector3[] Base =
        {
            new Vector3(3, 6, 14), new Vector3(8, 13, 26), new Vector3(16, 24, 44), new Vector3(26, 34, 56)
        };

        /// <summary>
        /// Эпохи из BIOMES с уже применённым SKY_UNIFY (пересчитано один раз здесь,
        /// чтобы в рантайме не таскать two-step формулу исходника).
        /// </summary>
        public static readonly Era[] Eras =
        {
            Era8("НАКОПЛЕНИЕ", 20, 16, 48, 46, 30, 78, 150, 80, 110, 230, 150, 110, 228, 250, 255, 60, 40, 90),
            Era8("БЫЧИЙ РЫНОК", 16, 32, 52, 26, 66, 86, 120, 195, 150, 235, 205, 120, 180, 255, 210, 40, 80, 78),
            Era8("ЭЙФОРИЯ", 30, 12, 54, 74, 26, 104, 185, 72, 165, 255, 150, 120, 255, 205, 245, 80, 30, 100),
            Era8("ВОЛАТИЛЬНОСТЬ", 16, 20, 34, 30, 42, 62, 78, 98, 128, 150, 160, 172, 205, 228, 245, 40, 52, 74),
            Era8("МЕДВЕЖИЙ РЫНОК", 24, 8, 20, 52, 14, 30, 112, 30, 42, 58, 16, 22, 255, 180, 180, 70, 22, 30),
            Era8("РАЛЛИ", 30, 18, 34, 82, 44, 28, 220, 138, 58, 255, 200, 92, 255, 226, 160, 92, 56, 30),
            Era8("КАПИТУЛЯЦИЯ", 10, 10, 16, 24, 22, 34, 46, 42, 60, 78, 72, 96, 186, 186, 214, 34, 32, 48),
            Era8("ОТСКОК", 12, 28, 40, 20, 70, 90, 64, 172, 168, 150, 232, 200, 192, 255, 236, 30, 76, 74),
        };

        private static Era Era8(string name,
            float a0, float a1, float a2, float b0, float b1, float b2,
            float c0, float c1, float c2, float d0, float d1, float d2,
            float l0, float l1, float l2, float r0, float r1, float r2)
        {
            return new Era
            {
                Name = name,
                Sky0 = UnifyStop(new Vector3(a0, a1, a2), 0),
                Sky1 = UnifyStop(new Vector3(b0, b1, b2), 1),
                Sky2 = UnifyStop(new Vector3(c0, c1, c2), 2),
                Sky3 = UnifyStop(new Vector3(d0, d1, d2), 3),
                Line = C(l0, l1, l2),
                Bridge = C(r0, r1, r2)
            };
        }

        private static Color UnifyStop(Vector3 c, int i)
        {
            var u = Vector3.Lerp(c, Base[i], Unify[i]);
            return C(u.x, u.y, u.z);
        }

        private static Color C(float r, float g, float b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
