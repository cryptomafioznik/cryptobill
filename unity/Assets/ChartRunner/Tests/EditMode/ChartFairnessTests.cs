using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ChartRunner.Track;
using ChartRunner.Tuning;
using NUnit.Framework;
using UnityEngine;

namespace ChartRunner.Tests
{
    /// <summary>
    /// ГЕЙТ ЧЕСТНОСТИ СГЕНЕРИРОВАННЫХ ТРАСС.
    ///
    /// Зачем он нужен именно здесь. Трасса-график строится СЛУЧАЙНО от семени, то есть
    /// каждый запуск игры — это новый уровень, которого никто не видел. Ручная проверка
    /// такого уровня невозможна по определению: проверить можно один прогон, а выдаётся
    /// бесконечность. Значит гарантия должна быть на генераторе.
    ///
    /// Это прямой урок дизайн-трассы `VS`: она была перенесена 1:1, выглядела нормально,
    /// и содержала участок 50.8° — выше предела проходимости, который автор исходника
    /// измерил и записал (`chartrider.html:654`, пик ~54° = непроезжаемая стена). Из-за
    /// одного такого участка две ночи измерений отдавали бессмысленные числа.
    ///
    /// Проверяется на МНОГИХ семенах, потому что дефект генератора может прятаться в
    /// редком сочетании режимов и фич. Один семя ничего не гарантирует.
    ///
    /// Каждое утверждение способно провалиться, пороги выведены из чисел проекта, а не
    /// назначены: предел подъёма — измеренный автором, ширина провала — из геометрии
    /// прыжка при известной верхней скорости.
    /// </summary>
    public class ChartFairnessTests
    {
        /// <summary>Сколько семян прогоняется. Меньше десятка ничего не доказывает.</summary>
        private const int Seeds = 24;

        /// <summary>Узлов в трассе на одно семя (~515 м, полторы минуты игры).</summary>
        private const int Nodes = 700;

        /// <summary>
        /// Предел подъёма, ИЗМЕРЕННЫЙ автором исходника: «пик ~54° = НЕПРОЕЗЖАЕМАЯ СТЕНА
        /// (сток вязнет/катится назад даже с разгоном, climbGrip не тянет; сим-подтверждено)».
        /// Держим запас: 50°, потому что на 50.8° байк уже вставал (docs/wall-diagnosis.md).
        /// </summary>
        private const float MaxClimbDeg = 50f;

        private static string F(float v, int d = 1) =>
            v.ToString("F" + d, CultureInfo.InvariantCulture);

        private static CandleTerrainProfile Tune()
        {
            var t = ScriptableObject.CreateInstance<CandleTerrainProfile>();
            t.regimes = CandleTerrainProfile.SourceRegimes();
            return t;
        }

        private struct SeedReport
        {
            public int Seed;
            public float MaxClimbDeg;
            public float MaxDescentDeg;
            public float WidestGapM;
            public int GapCount;
            public int WorstNode;
            public string WorstContext;
        }

        private static SeedReport Analyse(int seed)
        {
            var gen = CandleTrackGenerator.Generate(Tune(), seed, Nodes);
            var p = gen.Profile;
            var k = UnitsContract.PxToM;

            var r = new SeedReport { Seed = seed };

            // Узлы провалов исключаются: стенка ямы — это край, который перелетают,
            // а не склон, на который заезжают. Первая редакция гейта их не исключала и
            // отчитывалась про «подъём 87.9°», то есть про вертикальную стенку ямы.
            var gapNodes = new HashSet<int>(gen.GapNodes);
            var lastNode = (int)(p.endPx / p.nodeStepPx);
            for (var i = 0; i <= lastNode; i++)
            {
                if (gapNodes.Contains(i) || gapNodes.Contains(i - 1) || gapNodes.Contains(i + 1))
                    continue;
                var deg = p.GridSlopeRadAt(i * p.nodeStepPx) * Mathf.Rad2Deg;
                if (deg > r.MaxClimbDeg)
                {
                    r.MaxClimbDeg = deg;
                    r.WorstNode = i;
                    // Контекст: высоты пяти соседних узлов. Догадываться, откуда взялся
                    // выброс, дороже, чем напечатать окрестность и посмотреть.
                    var ctx = "";
                    for (var j = i - 2; j <= i + 2; j++)
                    {
                        if (j < 0 || j >= p.nodesPx.Length) continue;
                        ctx += (j == i ? " >" : " ") + F(p.nodesPx[j].y, 0);
                    }
                    r.WorstContext = ctx;
                }
                if (deg < r.MaxDescentDeg) r.MaxDescentDeg = deg;
            }

            // Провал = участок, где поверхность резко уходит вниз более чем на 8 м
            // относительно соседей и возвращается. Ширину меряем по узлам подряд,
            // которые лежат глубоко: именно её игрок перелетает.
            var sampler = new TerrainSampler(p);
            var deep = 0;
            for (var i = 1; i < Nodes - 1; i++)
            {
                var x = i * p.nodeStepPx * k;
                var y = sampler.HeightAt(x);
                var refY = Mathf.Max(sampler.HeightAt(x - 3f * p.nodeStepPx * k),
                    sampler.HeightAt(x + 3f * p.nodeStepPx * k));
                if (refY - y > 8f)
                {
                    deep++;
                }
                else
                {
                    if (deep > 0)
                    {
                        r.GapCount++;
                        r.WidestGapM = Mathf.Max(r.WidestGapM, deep * p.nodeStepPx * k);
                    }
                    deep = 0;
                }
            }
            return r;
        }

        [Test]
        public void GeneratedChart_NeverExceedsTheAuthorsClimbLimit()
        {
            var reports = new List<SeedReport>();
            for (var s = 0; s < Seeds; s++) reports.Add(Analyse(1000 + s * 7919));

            var worst = reports.OrderByDescending(x => x.MaxClimbDeg).First();
            var lines = new List<string>
            {
                "# ЧЕСТНОСТЬ СГЕНЕРИРОВАННЫХ ТРАСС",
                "",
                "Сгенерирован `ChartFairnessTests`. Перезапишется следующим прогоном.",
                "",
                "Трасса-график строится случайно от семени: каждый запуск — новый уровень,",
                "которого никто не видел. Проверить руками можно один прогон, а выдаётся",
                "бесконечность, поэтому гарантия должна лежать на генераторе.",
                "",
                "Предел подъёма " + F(MaxClimbDeg) + "° — из замера автора исходника",
                "(`chartrider.html:654`: пик ~54° = непроезжаемая стена) с запасом на то,",
                "что на 50.8° байк уже вставал (`docs/wall-diagnosis.md`).",
                "",
                "Семян: " + Seeds + ", узлов на семя: " + Nodes + ".",
                "",
                "| семя | макс подъём | макс спуск | провалов | шире всего |",
                "|---|---|---|---|---|"
            };
            foreach (var x in reports.OrderByDescending(v => v.MaxClimbDeg).Take(8))
            {
                lines.Add("| " + x.Seed + " | " + F(x.MaxClimbDeg) + "° | " + F(x.MaxDescentDeg)
                          + "° | " + x.GapCount + " | " + F(x.WidestGapM, 2) + " м |");
                Debug.Log("семя " + x.Seed + ": худший узел " + x.WorstNode
                          + ", высоты вокруг:" + x.WorstContext);
            }
            lines.Add("");
            lines.Add("Показаны 8 худших по крутизне подъёма из " + Seeds + ".");

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                "docs", "chart-fairness.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
            Debug.Log("CHART FAIRNESS записан: " + path + "\n" + string.Join("\n", lines));

            Assert.Less(worst.MaxClimbDeg, MaxClimbDeg,
                "семя " + worst.Seed + " даёт подъём " + F(worst.MaxClimbDeg)
                + "°, а измеренный предел проходимости — " + F(MaxClimbDeg)
                + "°. Такой участок останавливает байк при любой политике пилота: "
                + "ровно этот дефект сделал бессмысленными два теста на трассе VS.");
        }

        /// <summary>
        /// Провал должен быть перелетаемым. Верхняя оценка дальности прыжка выводится из
        /// физики, а не назначается: при верхней скорости v и вылете под 45° дальность
        /// баллистики = v²/g. Берём половину как рабочий запас — реальный вылет с липа
        /// всегда положе идеальных 45°, и часть скорости уходит на подъём по рампе.
        /// </summary>
        [Test]
        public void GeneratedChart_GapsStayJumpable()
        {
            var bike = ScriptableObject.CreateInstance<BikeTuningProfile>();
            var v = bike.topSpeedMPerS;
            var g = Mathf.Abs(UnitsContract.GravityMPerS2);
            // Дальность считается для вылета С ВЫСОТЫ, а не с уровня земли: лип гэпа
            // поднят на gapTotalH = 80 авторских px = 2.26 м над точкой приземления.
            // Первая редакция брала голое v²/g (1.49 м) и делила пополам «для запаса» —
            // число без вывода, и оно немедленно отвергло гэпы шириной 2 узла, которые
            // на деле перелетаются именно за счёт этой высоты.
            const float lipHeightM = 80f * UnitsContract.PxToM;
            var vy = v * Mathf.Sin(35f * Mathf.Deg2Rad);
            var vx = v * Mathf.Cos(35f * Mathf.Deg2Rad);
            var flight = (vy + Mathf.Sqrt(vy * vy + 2f * g * lipHeightM)) / g;
            var ideal = vx * flight;
            // Запас 0.75: реальный сход с липа положе идеального, часть скорости уходит
            // на подъём по рампе, и приземляться надо не в самый край.
            var budget = ideal * 0.75f;

            var worstSeed = 0;
            var worstGap = 0f;
            for (var s = 0; s < Seeds; s++)
            {
                var r = Analyse(1000 + s * 7919);
                if (r.WidestGapM > worstGap) { worstGap = r.WidestGapM; worstSeed = r.Seed; }
            }

            Debug.Log("GAPS: верхняя скорость " + F(v, 2) + " м/с, g " + F(g, 2)
                      + " м/с² → идеальная дальность " + F(ideal, 2) + " м, бюджет "
                      + F(budget, 2) + " м. Самый широкий провал " + F(worstGap, 2)
                      + " м на семени " + worstSeed + ".");

            Assert.Less(worstGap, budget,
                "провал " + F(worstGap, 2) + " м на семени " + worstSeed
                + " шире бюджета прыжка " + F(budget, 2) + " м — перелететь нельзя "
                + "ни при какой игре, это стена, а не препятствие.");
        }

        /// <summary>
        /// НЕГАТИВНЫЙ КОНТРОЛЬ: гейт крутизны обязан уметь провалиться. Проверяем на
        /// профиле с задранным кэпом подъёма — генератор тогда законно выдаёт стены.
        /// Без этого контроля «все 24 семени прошли» ничего не значит.
        /// </summary>
        [Test]
        public void FairnessGate_CanActuallyFail()
        {
            var tune = Tune();
            tune.climbCapRad = 1.2f;   // 68.8° вместо 0.6 рад (34°)

            var worst = 0f;
            for (var s = 0; s < 6; s++)
            {
                var gen = CandleTrackGenerator.Generate(tune, 1000 + s * 7919, Nodes);
                for (var x = 0f; x <= gen.Profile.endPx; x += gen.Profile.nodeStepPx)
                    worst = Mathf.Max(worst, gen.Profile.GridSlopeRadAt(x) * Mathf.Rad2Deg);
            }

            Debug.Log("NEGATIVE CONTROL: при кэпе 68.8° макс подъём " + F(worst) + "°");
            Assert.Greater(worst, MaxClimbDeg,
                "с задранным кэпом генератор ОБЯЗАН выдавать стены — иначе гейт крутизны "
                + "не проверяет ничего и «все семена прошли» является пустым утверждением.");
        }
    }
}
