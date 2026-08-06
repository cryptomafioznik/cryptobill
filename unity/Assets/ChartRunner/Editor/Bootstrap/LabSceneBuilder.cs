using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChartRunner.EditorTools
{
    /// <summary>
    /// Собирает сцену полигона `BikePhysicsLab` СКРИПТОМ и сохраняет её как ассет.
    ///
    /// Почему скриптом, а не руками (хендофф §4): сцены и префабы нельзя собрать руками в
    /// batchmode, а писать YAML вручную нельзя — ошибки не поймать. Пересборка сцены при
    /// изменении данных площадок становится одной командой, а не ручной операцией.
    ///
    /// Сцена нужна человеку — чтобы утром открыть и посмотреть. Автоматические проверки
    /// сцену не используют: тесты собирают свои площадки в памяти, потому что тест обязан
    /// быть независим от того, сохранил ли кто-то сцену.
    /// </summary>
    public static class LabSceneBuilder
    {
        private const string SceneDir = "Assets/ChartRunner/Scenes";
        private const string ScenePath = SceneDir + "/BikePhysicsLab.unity";

        private static readonly List<string> Lines = new List<string>();
        private static readonly List<string> Failures = new List<string>();
        private static int CheckCount;

        /// <summary>-executeMethod ChartRunner.EditorTools.LabSceneBuilder.Build</summary>
        public static void Build()
        {
            Lines.Add("=== ПУНКТ 6: СБОРКА ПОЛИГОНА BikePhysicsLab ===");
            Lines.Add("Unity " + Application.unityVersion);

            Directory.CreateDirectory(SceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var profile = LoadOrCreateBikeProfile();
            var pads = (LabTerrains.Pad[])Enum.GetValues(typeof(LabTerrains.Pad));

            var root = new GameObject("BikePhysicsLab");
            var built = new List<string>();

            for (var i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                var padProfile = LabTerrains.CreateProfile(pad);
                var offsetX = i * LabTerrains.PadSpacingM;

                var padRoot = new GameObject(i.ToString("00") + "_" + pad);
                padRoot.transform.SetParent(root.transform, false);
                padRoot.transform.position = new Vector3(offsetX, 0f, 0f);

                var trackGo = TrackBuilder.Build(padProfile, profile.tyreFriction, "Surface");
                trackGo.transform.SetParent(padRoot.transform, false);

                // Маркер точки старта: человек видит, откуда поедет байк.
                var marker = new GameObject("StartMarker");
                marker.transform.SetParent(padRoot.transform, false);
                var sampler = new TerrainSampler(padProfile);
                var sx = LabTerrains.StartXPx(pad) * UnitsContract.PxToM;
                marker.transform.localPosition = new Vector3(sx,
                    sampler.HeightAt(sx) + profile.wheelRadiusM, 0f);

                var runs = TrackBuilder.SplitIntoRuns(padProfile);
                var maxSlope = padProfile.MaxGridSlopeDeg(0f, padProfile.endPx);
                built.Add(pad + ": участков " + runs.Count
                          + ", точек " + runs.Sum(r => r.Count)
                          + ", макс уклон " + maxSlope.ToString("0.0", CultureInfo.InvariantCulture) + "°"
                          + ", старт x=" + sx.ToString("0.00", CultureInfo.InvariantCulture) + " м");
            }

            // Камера, чтобы сцена открывалась во что-то осмысленное.
            var cam = new GameObject("LabCamera");
            cam.transform.SetParent(root.transform, false);
            var c = cam.AddComponent<Camera>();
            c.orthographic = true;
            c.orthographicSize = 20f;
            c.transform.position = new Vector3(20f, 12f, -10f);
            c.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            c.clearFlags = CameraClearFlags.SolidColor;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Lines.Add("");
            Lines.Add("-- площадки --");
            foreach (var b in built) Lines.Add("  " + b);

            Verify(pads);

            Lines.Add("");
            if (Failures.Count == 0)
            {
                Lines.Add("LAB BUILD RESULT: PASS (" + CheckCount + " проверок), сцена " + ScenePath);
                Flush();
                EditorApplication.Exit(0);
            }
            else
            {
                Lines.Add("LAB BUILD RESULT: FAIL — провалено " + Failures.Count + " из " + CheckCount);
                foreach (var f in Failures) Lines.Add("  FAIL: " + f);
                Flush();
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Негативный контроль: проверяет, что верификация уклонов площадок способна упасть.
        /// -executeMethod ChartRunner.EditorTools.LabSceneBuilder.NegativeControl
        /// </summary>
        public static void NegativeControl()
        {
            Lines.Add("=== NEGATIVE CONTROL полигона: ожидается FAIL ===");
            var p = LabTerrains.CreateProfile(LabTerrains.Pad.SteepClimb);
            Lines.Add("площадка SteepClimb: подъём 1500 px за 1500 px заменён на 300 px");
            p.nodesPx[2] = new Vector2(3000, 300);

            var got = p.MaxGridSlopeDeg(0f, p.endPx);
            CheckSlope("steep_climb_is_45deg", got, 45f, 1.5f);

            var caught = Failures.Count > 0;
            Lines.Add("проверка уклона: " + (caught ? "ПОЙМАЛА (контроль пройден)" : "НЕ ПОЙМАЛА (тест сломан)"));
            Flush();
            EditorApplication.Exit(caught ? 0 : 1);
        }

        private static BikeTuningProfile LoadOrCreateBikeProfile()
        {
            var path = "Assets/ChartRunner/Profiles/Default.BikeTuningProfile.asset";
            var p = AssetDatabase.LoadAssetAtPath<BikeTuningProfile>(path);
            if (p == null)
            {
                Lines.Add("  ВНИМАНИЕ: профиль байка не найден по " + path + ", создан временный");
                p = ScriptableObject.CreateInstance<BikeTuningProfile>();
            }
            return p;
        }

        private static void Verify(LabTerrains.Pad[] pads)
        {
            Lines.Add("");
            Lines.Add("-- проверки --");

            Check("scene_asset_exists", File.Exists(ScenePath), ScenePath);
            Check("all_six_pads_present", pads.Length == 6, "площадок " + pads.Length);

            // Уклоны площадок обязаны совпадать с тем, что они называют. Эти числа взяты
            // из эталонов спеки §7, а не назначены здесь: 45° = секция 3, −44° = секция 5.
            CheckSlope("flat_is_flat", Slope(LabTerrains.Pad.Flat), 0f, 0.1f);
            CheckSlope("gentle_below_climbFrom", Slope(LabTerrains.Pad.GentleSlope), 15f, 1.5f);
            CheckSlope("steep_climb_is_45deg", Slope(LabTerrains.Pad.SteepClimb), 45f, 1.5f);
            CheckSlope("steep_descent_is_minus44deg", Slope(LabTerrains.Pad.SteepDescent), -44f, 1.5f);
            CheckSlope("bumps_match_section2", Slope(LabTerrains.Pad.SmallBumps), -35.6f, 1.5f);

            // Пологий склон обязан быть НИЖЕ порога эндуро-бонуса, иначе он проверяет не то.
            var gentle = Mathf.Abs(Slope(LabTerrains.Pad.GentleSlope));
            var climbFromDeg = LoadOrCreateBikeProfile().climbFromRad * Mathf.Rad2Deg;
            Check("gentle_slope_does_not_trigger_climb_bonus", gentle < climbFromDeg,
                gentle.ToString("0.0") + "° против порога climbFrom "
                + climbFromDeg.ToString("0.0") + "°");

            // Трамплин обязан иметь ВОГНУТЫЙ лип: уклон растёт к кромке. Если он выпуклый,
            // вылет станет скрипт-подобным, а не эмерджентным — это прямо тот дефект,
            // который в исходнике чинили анатомией рампы.
            var jump = LabTerrains.CreateProfile(LabTerrains.Pad.JumpRamp);
            var s1 = jump.GridSlopeRadAt(1140f) * Mathf.Rad2Deg;
            var s2 = jump.GridSlopeRadAt(1270f) * Mathf.Rad2Deg;
            Check("jump_lip_is_concave", s2 > s1,
                "уклон у основания липа " + s1.ToString("0.0") + "°, у кромки "
                + s2.ToString("0.0") + "° — обязан РАСТИ");

            // Каждая площадка должна быть непрерывной: разрывов в полигоне нет по замыслу,
            // иначе байк провалится там, где мы этого не задумывали.
            foreach (var pad in pads)
            {
                var prof = LabTerrains.CreateProfile(pad);
                var runs = TrackBuilder.SplitIntoRuns(prof);
                Check("pad_is_continuous_" + pad, runs.Count == 1,
                    "участков " + runs.Count + " (ожидался 1)");
            }
        }

        private static float Slope(LabTerrains.Pad pad)
        {
            var p = LabTerrains.CreateProfile(pad);
            return p.MaxGridSlopeDeg(0f, p.endPx);
        }

        private static void CheckSlope(string label, float got, float want, float tol)
        {
            Check(label, Mathf.Abs(got - want) <= tol,
                got.ToString("0.0", CultureInfo.InvariantCulture) + "° против ожидаемых "
                + want.ToString("0.0", CultureInfo.InvariantCulture) + "° ±" + tol);
        }

        private static void Check(string label, bool ok, string detail)
        {
            CheckCount++;
            Lines.Add((ok ? "  ok   " : "  FAIL ") + label + " — " + detail);
            if (!ok) Failures.Add(label + " — " + detail);
        }

        private static void Flush()
        {
            Debug.Log(string.Join("\n", Lines));
            Console.Out.Write(string.Join("\n", Lines) + "\n");
            Console.Out.Flush();
        }
    }
}
