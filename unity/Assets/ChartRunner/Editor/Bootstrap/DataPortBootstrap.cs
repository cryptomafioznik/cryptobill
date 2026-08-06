using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEditor;
using UnityEngine;

namespace ChartRunner.EditorTools
{
    /// <summary>
    /// Пункт 4 плана: создаёт ассеты профилей ИЗ КОДА и проверяет перенос данных.
    ///
    /// Ассеты создаются скриптом, а не руками, по той же причине, что и сцены (хендофф §4):
    /// YAML вручную писать нельзя — ошибки не поймать. Здесь единственный источник истины
    /// для данных трассы, и он читаемый.
    ///
    /// Ожидаемые значения проверок получены НЕЗАВИСИМОЙ реализацией той же формулы на Python
    /// (см. отчёт итерации 4 в docs/IMPLEMENTATION_LOG.md), а три из них дополнительно
    /// совпадают с числами, задокументированными в docs/full-project-audit-2026-07-25.md §6.4
    /// и §8 задолго до этой работы. То есть проверка сверяет C# не сам с собой.
    /// </summary>
    public static class DataPortBootstrap
    {
        private const string ProfilesDir = "Assets/ChartRunner/Profiles";

        private static readonly List<string> Failures = new List<string>();
        private static readonly List<string> Lines = new List<string>();
        private static int CheckCount;

        // ---- Данные VS из toys/chartrider.html стр. 3572-3592, перенесены 1:1 ----
        private static readonly Vector2[] VsNodes =
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

        /// <summary>-executeMethod ChartRunner.EditorTools.DataPortBootstrap.Run</summary>
        public static void Run()
        {
            Lines.Add("=== ПУНКТ 4: ПОРТ ДАННЫХ ===");
            Lines.Add("Unity " + Application.unityVersion);

            Directory.CreateDirectory(ProfilesDir);

            var track = BuildTrackProfile();
            var bike = BuildBikeProfile();
            var level = BuildLevelOverride();
            var candles = BuildCandleProfile();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            VerifyTrackData(track);
            VerifyInterpolation(track);
            VerifySlopes(track);
            VerifyPolyline(track);
            VerifyBikeProfile(bike);
            VerifyLevelOverride(level);
            VerifyCandleProfile(candles);

            Lines.Add("");
            if (Failures.Count == 0)
            {
                Lines.Add("DATA PORT RESULT: PASS (" + CheckCount + " проверок)");
                Flush();
                EditorApplication.Exit(0);
            }
            else
            {
                Lines.Add("DATA PORT RESULT: FAIL — провалено " + Failures.Count + " из " + CheckCount);
                foreach (var f in Failures) Lines.Add("  FAIL: " + f);
                Flush();
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Негативный контроль: портит один узел трассы и проверяет, что проверки уклонов это
        /// ловят. Ассеты не сохраняются. -executeMethod ...DataPortBootstrap.NegativeControl
        /// </summary>
        public static void NegativeControl()
        {
            Lines.Add("=== NEGATIVE CONTROL порта данных: ожидается FAIL ===");
            var t = ScriptableObject.CreateInstance<TrackProfile>();
            t.nodesPx = (Vector2[])VsNodes.Clone();
            t.nodeStepPx = 26f;
            t.endPx = 11150f;
            // Смещаем вершину технического подъёма: уклон секции 3 обязан измениться.
            t.nodesPx[12] = new Vector2(4150, 700);
            Lines.Add("узел [4150,900] подменён на [4150,700]");

            VerifySlopes(t);
            var caught = Failures.Count > 0;
            Lines.Add("проверки уклонов: " + (caught ? "ПОЙМАЛИ (контроль пройден)" : "НЕ ПОЙМАЛИ (тест сломан)"));
            Flush();
            EditorApplication.Exit(caught ? 0 : 1);
        }

        // ================= построение =================

        private static TrackProfile BuildTrackProfile()
        {
            var t = ScriptableObject.CreateInstance<TrackProfile>();
            t.nodesPx = (Vector2[])VsNodes.Clone();
            t.gapsPx = new[] { new TrackProfile.Gap { fromPx = 6750f, toPx = 7090f } };
            t.endPx = 11150f;
            t.nodeStepPx = 26f;
            t.checkpoints = new[]
            {
                new TrackProfile.Checkpoint { xPx = 6100f, name = "перед прыжком" },
                new TrackProfile.Checkpoint { xPx = 8800f, name = "перед финальным подъёмом" }
            };
            t.flowBoostEnabled = false;
            t.sections = new[]
            {
                new TrackProfile.Section { name = "1 старт и разгон", fromPx = 0, toPx = 950 },
                new TrackProfile.Section
                {
                    name = "2 вупсы ×4", fromPx = 950, toPx = 3150,
                    knownDefect = "ИНВЕРТИРОВАННАЯ КРИВАЯ: обучающая секция насыщает жёсткий режим " +
                                  "помощников при 33.2°, а имеет 35.6°. Первая же нетривиальная секция " +
                                  "самая наказывающая. Порядок секций — решение пользователя (аудит §6.4, §8)."
                },
                new TrackProfile.Section { name = "3 технический подъём", fromPx = 3150, toPx = 4320 },
                new TrackProfile.Section { name = "4 острый перелом вершины", fromPx = 4320, toPx = 4460 },
                new TrackProfile.Section { name = "5 спуск", fromPx = 4460, toPx = 5400 },
                new TrackProfile.Section { name = "6 яма (компрессия)", fromPx = 5400, toPx = 5850 },
                new TrackProfile.Section { name = "7 разгонная полоса", fromPx = 5850, toPx = 6450 },
                new TrackProfile.Section
                {
                    name = "8 лип + гэп", fromPx = 6450, toPx = 7090,
                    knownDefect = "Ошибка необратима (аудит §8): восстановления нет."
                },
                new TrackProfile.Section { name = "9 посадка под уклон", fromPx = 7090, toPx = 7900 },
                new TrackProfile.Section { name = "10 техническая зона", fromPx = 7900, toPx = 8600 },
                new TrackProfile.Section { name = "11 короткий разгон", fromPx = 8600, toPx = 9100 },
                new TrackProfile.Section
                {
                    name = "12 финальный подъём", fromPx = 8850, toPx = 10380,
                    knownDefect = "Ошибка необратима (аудит §8)."
                },
                new TrackProfile.Section { name = "13 финишная площадка", fromPx = 10380, toPx = 11400 }
            };
            AssetDatabase.CreateAsset(t, ProfilesDir + "/VerticalSlice.TrackProfile.asset");
            return t;
        }

        private static BikeTuningProfile BuildBikeProfile()
        {
            var b = ScriptableObject.CreateInstance<BikeTuningProfile>();
            // Поля-плейсхолдеры перечислены явно: пока имя здесь, значение нельзя
            // предъявлять как перенесённое из исходника (docs/BIKE_PHYSICS_SPEC.md §6).
            // Обновлено на пункте 6: engineForceN, tyreFriction, brakeForceN и
            // suspensionFrequency ОТКАЛИБРОВАНЫ измерением на полигоне против сформулированных
            // требований (см. docs/IMPLEMENTATION_LOG.md, итерация 6) и из списка убраны.
            // Остаются те, для которых требование ещё не сформулировано или не измерено.
            b.pendingCalibration = new[]
            {
                nameof(BikeTuningProfile.suspensionDamping),
                nameof(BikeTuningProfile.suspensionTravelM),
                nameof(BikeTuningProfile.wheelMassKg)
            };
            AssetDatabase.CreateAsset(b, ProfilesDir + "/Default.BikeTuningProfile.asset");
            return b;
        }

        private static LevelPhysicsOverride BuildLevelOverride()
        {
            var o = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
            AssetDatabase.CreateAsset(o, ProfilesDir + "/VerticalSlice.LevelPhysicsOverride.asset");
            var stock = LevelPhysicsOverride.CreateStock();
            AssetDatabase.CreateAsset(stock, ProfilesDir + "/Stock.LevelPhysicsOverride.asset");
            return o;
        }

        private static CandleTerrainProfile BuildCandleProfile()
        {
            var c = ScriptableObject.CreateInstance<CandleTerrainProfile>();
            c.regimes = CandleTerrainProfile.SourceRegimes();
            AssetDatabase.CreateAsset(c, ProfilesDir + "/Default.CandleTerrainProfile.asset");
            return c;
        }

        // ================= проверки =================

        private static void Check(string label, bool ok, string detail)
        {
            CheckCount++;
            Lines.Add((ok ? "  ok   " : "  FAIL ") + label + " — " + detail);
            if (!ok) Failures.Add(label + " — " + detail);
        }

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        private static void VerifyTrackData(TrackProfile t)
        {
            Lines.Add("");
            Lines.Add("-- данные трассы --");
            Check("node_count_is_35", t.nodesPx.Length == 35, "узлов " + t.nodesPx.Length);
            Check("first_node_is_origin", t.nodesPx[0] == Vector2.zero, t.nodesPx[0].ToString());
            Check("last_node_is_11400_1400", t.nodesPx[t.nodesPx.Length - 1] == new Vector2(11400, 1400),
                t.nodesPx[t.nodesPx.Length - 1].ToString());
            Check("end_is_11150", Mathf.Approximately(t.endPx, 11150f), F(t.endPx) + " px = " + F(t.EndM) + " м");
            Check("one_gap_6750_7090", t.gapsPx.Length == 1
                                      && Mathf.Approximately(t.gapsPx[0].fromPx, 6750f)
                                      && Mathf.Approximately(t.gapsPx[0].toPx, 7090f),
                t.gapsPx.Length + " разрыв(ов)");
            Check("gap_predicate_matches_source", !t.IsGap(6750f) && t.IsGap(6800f) && !t.IsGap(7090f),
                "границы исключены, середина включена — как в vsIsGap");
            Check("two_checkpoints_6100_8800", t.checkpoints.Length == 2
                                               && Mathf.Approximately(t.checkpoints[0].xPx, 6100f)
                                               && Mathf.Approximately(t.checkpoints[1].xPx, 8800f),
                string.Join(", ", t.checkpoints.Select(c => F(c.xPx))));
            Check("flow_boost_disabled_by_level_config", !t.flowBoostEnabled, "flowBoostEnabled = false");

            var defects = t.sections.Count(s => !string.IsNullOrEmpty(s.knownDefect));
            Check("known_defects_carried_with_data", defects >= 3,
                defects + " секций помечены дефектом (инвертированная кривая + необратимые ошибки)");
        }

        private static void VerifyInterpolation(TrackProfile t)
        {
            Lines.Add("");
            Lines.Add("-- интерполяция --");

            var nodeErr = t.nodesPx.Max(n => Mathf.Abs(t.HeightPx(n.x) - n.y));
            Check("curve_passes_through_all_nodes", nodeErr < 1e-3f, "макс ошибка " + nodeErr.ToString("E3") + " px");

            // Смысл выбора Фрича–Карлсона вместо Catmull-Rom: отсутствие перелётов.
            // Если кто-то заменит схему на обычный сплайн, эта проверка упадёт.
            var worst = 0f;
            for (var i = 0; i < t.nodesPx.Length - 1; i++)
            {
                float x0 = t.nodesPx[i].x, x1 = t.nodesPx[i + 1].x;
                float lo = Mathf.Min(t.nodesPx[i].y, t.nodesPx[i + 1].y);
                float hi = Mathf.Max(t.nodesPx[i].y, t.nodesPx[i + 1].y);
                for (var k = 1; k < 40; k++)
                {
                    var y = t.HeightPx(Mathf.Lerp(x0, x1, k / 40f));
                    worst = Mathf.Max(worst, Mathf.Max(0f, Mathf.Max(y - hi, lo - y)));
                }
            }
            Check("no_overshoot_between_nodes", worst < 1e-2f, "макс перелёт " + F(worst) + " px");

            Check("flat_outside_range", Mathf.Approximately(t.HeightPx(-500f), 0f)
                                        && Mathf.Approximately(t.HeightPx(20000f), 1400f),
                "вне узлов — константа крайнего узла, как в исходнике");
        }

        private static void VerifySlopes(TrackProfile t)
        {
            Lines.Add("");
            Lines.Add("-- уклоны --");
            Lines.Add("  ВАЖНО: физика видит уклон СЕТКИ 26 px (terrainAt), а аудит публиковал");
            Lines.Add("  производную КРИВОЙ. Это разные числа, проверяются оба.");

            // Уклоны сетки — то, что читает физика. Эталон: независимая реализация на Python.
            foreach (var c in new[]
            {
                new { name = "вупсы", from = 950f, to = 3150f, want = -35.6f },
                new { name = "секция 3", from = 3150f, to = 4320f, want = 47.6f },
                new { name = "секция 5", from = 4460f, to = 5400f, want = -44.3f },
                new { name = "секция 12", from = 8850f, to = 10380f, want = 51.1f }
            })
            {
                var got = t.MaxGridSlopeDeg(c.from, c.to);
                Check("grid_slope_" + c.name.Replace(' ', '_'), Mathf.Abs(got - c.want) <= 0.15f,
                    "сетка " + got.ToString("0.0") + "° против эталона " + c.want.ToString("0.0") + "°");
            }

            // Производная кривой — сверка с числами аудита, опубликованными до этой работы.
            foreach (var c in new[]
            {
                new { name = "вупсы", from = 950f, to = 3150f, audit = 35.7f },
                new { name = "секция 3", from = 3150f, to = 4320f, audit = 47.6f },
                new { name = "секция 12", from = 8850f, to = 10380f, audit = 51.1f }
            })
            {
                var best = 0f;
                for (var x = c.from; x <= c.to; x += 1f)
                {
                    var d = MonotoneCubic.SlopeRadAt(x, t.nodesPx) * Mathf.Rad2Deg;
                    if (Mathf.Abs(d) > Mathf.Abs(best)) best = d;
                }
                Check("curve_slope_matches_audit_" + c.name.Replace(' ', '_'),
                    Mathf.Abs(Mathf.Abs(best) - c.audit) <= 0.15f,
                    "кривая " + best.ToString("0.00") + "° против аудита " + c.audit.ToString("0.0") + "°");
            }
        }

        private static void VerifyPolyline(TrackProfile t)
        {
            Lines.Add("");
            Lines.Add("-- полилиния для EdgeCollider2D --");
            var pts = t.SamplePolylineM();
            var expected = Mathf.FloorToInt((t.endPx + 400f) / t.nodeStepPx) + 1;
            Check("polyline_point_count", pts.Count == expected,
                pts.Count + " точек, ожидалось " + expected);
            Check("polyline_step_is_node_step",
                pts.Count > 1 && Mathf.Abs((pts[1].x - pts[0].x) - t.NodeStepM) < 1e-5f,
                "шаг " + F(pts[1].x - pts[0].x) + " м = " + F(t.NodeStepM) + " м");
            Check("polyline_length_in_metres",
                Mathf.Abs(pts[pts.Count - 1].x - (t.endPx + 400f) * UnitsContract.PxToM) < 1f,
                "трасса до финиша " + F(t.EndM) + " м, полилиния до " + F(pts[pts.Count - 1].x) + " м");
            Check("max_height_in_metres",
                Mathf.Abs(pts.Max(p => p.y) - 1400f * UnitsContract.PxToM) < 0.05f,
                "перепад высот " + F(pts.Max(p => p.y)) + " м");
        }

        private static void VerifyBikeProfile(BikeTuningProfile b)
        {
            Lines.Add("");
            Lines.Add("-- профиль байка --");

            Check("pending_calibration_not_empty", b.pendingCalibration.Length > 0,
                b.pendingCalibration.Length + " полей помечены как неоткалиброванные");

            var fields = typeof(BikeTuningProfile).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(f => f.Name).ToHashSet();
            var bogus = b.pendingCalibration.Where(n => !fields.Contains(n)).ToArray();
            Check("pending_calibration_names_are_real_fields", bogus.Length == 0,
                bogus.Length == 0 ? "все имена существуют" : "нет таких полей: " + string.Join(", ", bogus));

            Check("geometry_matches_units_contract",
                Mathf.Abs(b.halfWheelbaseM - UnitsContract.HalfWheelbaseM) < 1e-6f
                && Mathf.Abs(b.wheelRadiusM - UnitsContract.WheelRadiusM) < 1e-6f
                && Mathf.Abs(b.cgAboveAxleM - UnitsContract.CgAboveAxleM) < 1e-6f,
                "полубаза " + F(b.halfWheelbaseM) + " м, колесо " + F(b.wheelRadiusM)
                + " м, ЦТ над осью " + F(b.cgAboveAxleM) + " м");

            Check("air_hold_is_zero", Mathf.Approximately(b.airHold, 0f),
                "airHold = " + F(b.airHold) + " (статичный вес в полёте не крутит — нет опоры)");

            Check("jump_button_disabled_by_default", !b.jumpButtonEnabled,
                "аркадный хоп 4.41 м не переносится без решения пользователя");

            Check("wheelie_brake_authority_nonzero", b.wheelieBrakeAuthority > 0f,
                "wheelieBrakeAuthority = " + F(b.wheelieBrakeAuthority) + " — главный инструмент спасения игрока");
        }

        private static void VerifyLevelOverride(LevelPhysicsOverride o)
        {
            Lines.Add("");
            Lines.Add("-- профиль помощников уровня --");
            Check("slice_fail_hold_is_0_25", Mathf.Abs(o.failHoldSeconds - 0.25f) < 1e-5f,
                "failHoldSeconds = " + F(o.failHoldSeconds) + " с — это и есть окно на коррекцию");
            Check("slice_air_auto_level_off", Mathf.Approximately(o.airAutoLevel, 0f),
                "airAutoLevel = " + F(o.airAutoLevel) + " — pitch держит игрок");
            Check("design_hard_flag_is_explicit", o.applyDesignHard,
                "applyDesignHard = true; в исходнике это было зашито в две строки решателя");

            var stock = LevelPhysicsOverride.CreateStock();
            Check("stock_profile_is_all_ones",
                Mathf.Approximately(stock.groundAlign, 1f) && Mathf.Approximately(stock.leanTorque, 1f)
                && Mathf.Approximately(stock.failHoldSeconds, 0f),
                "сток = все ручки 1.0, выдержки нет");
        }

        private static void VerifyCandleProfile(CandleTerrainProfile c)
        {
            Lines.Add("");
            Lines.Add("-- свеча → рельеф --");
            Check("six_regimes", c.regimes.Length == 6,
                "режимов " + c.regimes.Length + ": " + string.Join(", ", c.regimes.Select(r => r.regime.ToString())));
            Check("bull_drifts_up_bear_drifts_down",
                c.regimes.First(r => r.regime == CandleTerrainProfile.Regime.Bull).driftMax < 0f
                && c.regimes.First(r => r.regime == CandleTerrainProfile.Regime.Bear).driftMin > 0f,
                "зелёная свеча = подъём (дрейф < 0), красная = спуск (дрейф > 0)");
            Check("crash_is_most_volatile",
                c.regimes.First(r => r.regime == CandleTerrainProfile.Regime.Crash).volatility
                >= c.regimes.Max(r => r.volatility),
                "у crash пила " + F(c.regimes.First(r => r.regime == CandleTerrainProfile.Regime.Crash).volatility));
        }

        private static void Flush()
        {
            Debug.Log(string.Join("\n", Lines));
            Console.Out.Write(string.Join("\n", Lines) + "\n");
            Console.Out.Flush();
        }
    }
}
