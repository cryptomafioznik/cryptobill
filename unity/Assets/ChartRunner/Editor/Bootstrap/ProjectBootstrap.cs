using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ChartRunner.Tuning;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChartRunner.EditorTools
{
    /// <summary>
    /// Точка входа batchmode для проверки состояния проекта.
    ///
    /// Правило, из-за которого этот класс выглядит именно так (docs/TECH_DEBT.md §3.1):
    /// "-executeMethod завершается кодом 0 при пустом теле метода", поэтому КОМПИЛЯЦИЯ НЕ ЕСТЬ
    /// ПРОВЕРКА. Каждая проверка ниже печатает проверяемое утверждение и способна провалиться;
    /// при любом провале процесс выходит с кодом 1.
    ///
    /// Отдельно важно: настройки (гравитация, фикс-шаг) выставляются НЕ этим скриптом, а прямой
    /// правкой ProjectSettings/*.asset до запуска Unity. Проверка читает их через рантайм-API.
    /// Механизм установки и механизм проверки РАЗНЫЕ — иначе тест подтверждал бы сам себя.
    /// </summary>
    public static class ProjectBootstrap
    {
        private const string Assets = "Assets/ChartRunner";

        private static readonly List<string> Failures = new List<string>();
        private static readonly List<string> Lines = new List<string>();

        /// <summary>Вызывается как: -executeMethod ChartRunner.EditorTools.ProjectBootstrap.Verify</summary>
        public static void Verify()
        {
            Lines.Add("=== CHARTRUNNER BOOTSTRAP VERIFY ===");
            Lines.Add("Unity " + Application.unityVersion);

            VerifyUnitsContract();
            VerifyPhysics();
            VerifyRenderPipeline();
            VerifyFolders();
            VerifyAssemblies();

            Lines.Add("");
            if (Failures.Count == 0)
            {
                Lines.Add("BOOTSTRAP RESULT: PASS (" + CheckCount + " проверок)");
                Flush();
                EditorApplication.Exit(0);
            }
            else
            {
                Lines.Add("BOOTSTRAP RESULT: FAIL — провалено " + Failures.Count + " из " + CheckCount);
                foreach (var f in Failures) Lines.Add("  FAIL: " + f);
                Flush();
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Негативный контроль: намеренно ломает одну проверку, чтобы доказать, что Verify()
        /// вообще способен провалиться. Если этот вызов даёт exit 0 — сломан сам тест.
        /// Вызывается как: -executeMethod ChartRunner.EditorTools.ProjectBootstrap.NegativeControl
        /// </summary>
        public static void NegativeControl()
        {
            Lines.Add("=== NEGATIVE CONTROL: ожидается FAIL ===");
            var saved = Physics2D.gravity;

            Physics2D.gravity = new Vector2(0f, -9.81f); // заведомо неверно для варианта A
            VerifyPhysics();
            var caughtIt = Failures.Count > 0;

            // ВОССТАНАВЛИВАЕМ ДО Exit, а не в finally: EditorApplication.Exit убивает процесс,
            // finally не выполнится, и подменённая гравитация осталась бы записанной в
            // ProjectSettings/Physics2DSettings.asset — то есть контроль испортил бы проект.
            Physics2D.gravity = saved;
            AssetDatabase.SaveAssets();
            Lines.Add("гравитация восстановлена: " + Physics2D.gravity.ToString("F4"));

            Lines.Add("проверка гравитации при подменённом значении: "
                      + (caughtIt ? "ПОЙМАЛА (контроль пройден)" : "НЕ ПОЙМАЛА (тест сломан)"));
            Flush();
            EditorApplication.Exit(caughtIt ? 0 : 1);
        }

        private static int CheckCount;

        private static void Check(string label, bool ok, string detail)
        {
            CheckCount++;
            Lines.Add((ok ? "  ok   " : "  FAIL ") + label + " — " + detail);
            if (!ok) Failures.Add(label + " — " + detail);
        }

        private static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        private static void VerifyUnitsContract()
        {
            Lines.Add("");
            Lines.Add("-- контракт единиц (docs/BIKE_PHYSICS_SPEC.md §1) --");
            Lines.Add("  1 px = " + F(UnitsContract.PxToM * 1000f) + " мм");
            Lines.Add("  1 px/кадр = " + F(UnitsContract.PxPerFrameToMPerS) + " м/с");
            Lines.Add("  база = " + F(UnitsContract.SimWheelbasePx * UnitsContract.PxToM) + " м");
            Lines.Add("  радиус колеса = " + F(UnitsContract.WheelRadiusM) + " м");
            Lines.Add("  ЦТ над землёй = " + F(UnitsContract.CgAboveGroundM) + " м");
            Lines.Add("  момент инерции = " + F(UnitsContract.ChassisInertiaKgM2) + " кг·м²");
            Lines.Add("  1 ед. силы симуляции = " + F(UnitsContract.SimForceToNewton) + " Н");

            // Замкнутая сверка: база в пикселях, переведённая в метры, обязана дать реальную базу.
            Check("units_roundtrip_wheelbase",
                Mathf.Abs(UnitsContract.SimWheelbasePx * UnitsContract.PxToM - UnitsContract.RealWheelbaseM) < 1e-4f,
                F(UnitsContract.SimWheelbasePx * UnitsContract.PxToM) + " м против ожидаемых "
                + F(UnitsContract.RealWheelbaseM));

            // Сила тяжести, посчитанная из массы и гравитации, обязана совпасть с переводом
            // единичной силы симуляции × измеренная гравитация. Две разные дороги к одному числу.
            var weightDirect = UnitsContract.ChassisMassKg * Mathf.Abs(UnitsContract.GravityMPerS2);
            var weightViaForce = UnitsContract.SimGravityPxPerFrame2 * UnitsContract.SimForceToNewton;
            Check("units_roundtrip_weight",
                Mathf.Abs(weightDirect - weightViaForce) < 1f,
                F(weightDirect) + " Н против " + F(weightViaForce) + " Н");
        }

        private static void VerifyPhysics()
        {
            Lines.Add("");
            Lines.Add("-- физика 2D --");

            var g = Physics2D.gravity;
            Check("gravity_matches_units_contract",
                Mathf.Abs(g.y - UnitsContract.GravityMPerS2) < 0.01f && Mathf.Abs(g.x) < 1e-6f,
                "Physics2D.gravity = " + g.ToString("F4") + ", ожидалось y = " + F(UnitsContract.GravityMPerS2));

            Check("fixed_timestep_is_1_60",
                Mathf.Abs(Time.fixedDeltaTime - 1f / 60f) < 1e-5f,
                "Time.fixedDeltaTime = " + F(Time.fixedDeltaTime) + ", ожидалось " + F(1f / 60f));

            Check("simulation_mode_is_fixed_update",
                Physics2D.simulationMode == SimulationMode2D.FixedUpdate,
                "Physics2D.simulationMode = " + Physics2D.simulationMode);

            Check("velocity_iterations_at_least_8",
                Physics2D.velocityIterations >= 8,
                "velocityIterations = " + Physics2D.velocityIterations);
        }

        private static void VerifyRenderPipeline()
        {
            Lines.Add("");
            Lines.Add("-- render pipeline --");

            var rp = GraphicsSettings.currentRenderPipeline;
            var rpName = rp == null ? "<null: встроенный пайплайн>" : rp.GetType().Name;
            Check("urp_is_active_pipeline",
                rp != null && rp.GetType().Name.Contains("Universal"),
                "GraphicsSettings.currentRenderPipeline = " + rpName);

            // 2D-рендерер проверяем через сериализованное поле, без internal API.
            var has2D = false;
            var rendererNames = "нет данных";
            if (rp != null)
            {
                var so = new SerializedObject(rp);
                var list = so.FindProperty("m_RendererDataList");
                if (list != null && list.isArray)
                {
                    var names = new List<string>();
                    for (var i = 0; i < list.arraySize; i++)
                    {
                        var o = list.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (o == null) continue;
                        names.Add(o.GetType().Name);
                        if (o.GetType().Name.Contains("Renderer2D")) has2D = true;
                    }
                    rendererNames = names.Count > 0 ? string.Join(", ", names) : "список пуст";
                }
            }
            Check("urp_uses_2d_renderer", has2D, "рендереры: " + rendererNames);
        }

        private static void VerifyFolders()
        {
            Lines.Add("");
            Lines.Add("-- структура папок --");
            var expected = new[]
            {
                Assets + "/Runtime/Bike",
                Assets + "/Runtime/Track",
                Assets + "/Runtime/Input",
                Assets + "/Runtime/Telemetry",
                Assets + "/Runtime/Tuning",
                Assets + "/Editor/Bootstrap",
                Assets + "/Tests/PlayMode",
                Assets + "/Tests/EditMode",
                Assets + "/Profiles",
                Assets + "/Scenes"
            };
            var missing = expected.Where(p => !Directory.Exists(p)).ToArray();
            Check("folder_structure_complete", missing.Length == 0,
                missing.Length == 0
                    ? expected.Length + "/" + expected.Length + " папок на месте"
                    : "отсутствуют: " + string.Join(", ", missing));
        }

        private static void VerifyAssemblies()
        {
            Lines.Add("");
            Lines.Add("-- сборки --");
            var asms = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToArray();
            foreach (var want in new[] { "ChartRunner.Runtime", "ChartRunner.Editor" })
            {
                Check("assembly_loaded_" + want, asms.Contains(want),
                    asms.Contains(want) ? "загружена" : "НЕ загружена (asmdef или ошибка компиляции)");
            }

            // Тестовые сборки под UNITY_INCLUDE_TESTS: в обычном запуске их может не быть,
            // поэтому это НЕ проверка, а печать факта — иначе она провалилась бы штатно.
            foreach (var t in new[] { "ChartRunner.Tests.PlayMode", "ChartRunner.Tests.EditMode" })
                Lines.Add("  info " + t + " — " + (asms.Contains(t) ? "загружена" : "не загружена (ожидаемо вне -runTests)"));
        }

        private static void Flush()
        {
            Debug.Log(string.Join("\n", Lines));
            Console.Out.Write(string.Join("\n", Lines) + "\n");
            Console.Out.Flush();
        }
    }
}
