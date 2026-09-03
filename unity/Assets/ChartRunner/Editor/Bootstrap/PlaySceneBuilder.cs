using System.Collections.Generic;
using System.IO;
using ChartRunner.Game;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChartRunner.EditorTools
{
    /// <summary>
    /// Собирает сцену `Play` — ту, в которую играет ЖИВОЙ ЧЕЛОВЕК, и ставит её первой
    /// в списке сцен билда.
    ///
    /// Почему сцена состоит из одного объекта: всё остальное строится кодом в
    /// <see cref="PlaySession"/>. Ссылки на ассеты профилей проставляются здесь, потому что
    /// это единственный способ затащить ScriptableObject в билд, не заводя папку Resources —
    /// а Resources тянет в сборку всё подряд и потом молча расходится с тем, что грузят тесты.
    /// </summary>
    public static class PlaySceneBuilder
    {
        private const string SceneDir = "Assets/ChartRunner/Scenes";
        private const string ScenePath = SceneDir + "/Play.unity";
        private const string BikeProfilePath = "Assets/ChartRunner/Profiles/Default.BikeTuningProfile.asset";
        // СПЕЦИФИКАЦИЯ = браузерная игра (решение пользователя 2026-09-02): уровень Stock.
        // VerticalSlice — профиль «челленджа» C1 (без авто-выравнивания в воздухе, спин гасится
        // при удержании, воздушный момент 0.35, сцепление на подъёме 0.35): с ним сальто
        // невозможны (замер -trace -hop: 9° за 20 кадров против 360° спецификации).
        private const string LevelProfilePath = "Assets/ChartRunner/Profiles/Stock.LevelPhysicsOverride.asset";
        private const string TrackProfilePath = "Assets/ChartRunner/Profiles/VerticalSlice.TrackProfile.asset";
        private const string CandleProfilePath = "Assets/ChartRunner/Profiles/Default.CandleTerrainProfile.asset";

        private static readonly List<string> Lines = new List<string>();
        private static readonly List<string> Failures = new List<string>();
        private static int CheckCount;

        /// <summary>-executeMethod ChartRunner.EditorTools.PlaySceneBuilder.Build</summary>
        public static void Build()
        {
            Lines.Add("=== СБОРКА ИГРАБЕЛЬНОЙ СЦЕНЫ Play ===");
            Lines.Add("Unity " + Application.unityVersion);

            Directory.CreateDirectory(SceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bike = AssetDatabase.LoadAssetAtPath<BikeTuningProfile>(BikeProfilePath);
            var level = AssetDatabase.LoadAssetAtPath<LevelPhysicsOverride>(LevelProfilePath);
            var track = AssetDatabase.LoadAssetAtPath<TrackProfile>(TrackProfilePath);

            Check("bike_profile_found", bike != null, BikeProfilePath);
            Check("level_profile_found", level != null, LevelProfilePath);
            Check("track_profile_found", track != null, TrackProfilePath);

            var go = new GameObject("PlaySession");
            var session = go.AddComponent<PlaySession>();
            session.BikeProfile = bike;
            session.LevelProfile = level;
            session.Track = track;
            var candle = AssetDatabase.LoadAssetAtPath<CandleTerrainProfile>(CandleProfilePath);
            Check("candle_profile_found", candle != null, CandleProfilePath);
            session.CandleProfile = candle;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            // Play первой в билде: именно она должна открываться на телефоне.
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            EditorBuildSettings.scenes = scenes.ToArray();

            Verify(track);

            Lines.Add("");
            if (Failures.Count == 0)
            {
                Lines.Add("PLAY SCENE RESULT: PASS (" + CheckCount + " проверок), сцена " + ScenePath);
                Flush();
                EditorApplication.Exit(0);
            }
            else
            {
                Lines.Add("PLAY SCENE RESULT: FAIL — провалено " + Failures.Count + " из " + CheckCount);
                foreach (var f in Failures) Lines.Add("  FAIL: " + f);
                Flush();
                EditorApplication.Exit(1);
            }
        }

        private static void Verify(TrackProfile track)
        {
            Lines.Add("");
            Lines.Add("-- проверки --");

            Check("scene_asset_exists", File.Exists(ScenePath), ScenePath);
            Check("scene_first_in_build", EditorBuildSettings.scenes.Length > 0
                                          && EditorBuildSettings.scenes[0].path == ScenePath,
                EditorBuildSettings.scenes.Length + " сцен в билде");

            var reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var session = Object.FindFirstObjectByType<PlaySession>();
            Check("session_survives_save", session != null, reopened.name);
            if (session != null)
            {
                // Ссылки обязаны пережить сохранение — иначе в билде игра стартует пустой,
                // и это выяснится только на телефоне.
                Check("bike_ref_serialised", session.BikeProfile != null, "BikeProfile");
                Check("level_ref_serialised", session.LevelProfile != null, "LevelProfile");
                Check("track_ref_serialised", session.Track != null, "Track");
                // Без этой ссылки трасса-график молча выродится в VS, и «вернули график»
                // окажется неправдой при зелёной сборке.
                Check("candle_ref_serialised", session.CandleProfile != null, "CandleProfile");
            }

            if (track != null)
            {
                // Композиция считается ИЗ КОНСТАНТ ChaseCamera, а не из копии чисел здесь:
                // копия разъезжается с камерой молча, и гейт начинает проверять фантом.
                var fraction = ChaseCamera.DefaultHeroFraction;
                var halfH = ChaseCamera.DefaultHeroHeightM / (2f * fraction);
                var aspect = 430f / 932f;
                var halfW = halfH * aspect;
                var top = BikeTopSpeed();
                var aheadStatic = halfW * 2f * (1f - ChaseCamera.DefaultBikeScreenXRest);
                var aheadMoving = halfW * 2f * (1f - ChaseCamera.DefaultBikeScreenXFast);

                Lines.Add("  композиция 430×932: герой " + (fraction * 100f).ToString("0.0")
                          + " % высоты, кадр " + (halfH * 2f).ToString("0.0") + " × "
                          + (halfW * 2f).ToString("0.0") + " м");
                Lines.Add("  обзор впереди: " + aheadStatic.ToString("0.0") + " м стоя, "
                          + aheadMoving.ToString("0.0") + " м на верхней скорости "
                          + top.ToString("0.00") + " м/с = "
                          + (aheadMoving / top).ToString("0.00") + " с");
                Lines.Add("  бюджет реакции: рампа веса " + ChaseCamera.WeightShiftRampSeconds
                          + " с + восприятие " + ChaseCamera.PerceptionSeconds + " с = "
                          + ChaseCamera.MinReactionSeconds + " с");

                Check("hero_fraction_meets_gate", fraction >= 0.12f,
                    (fraction * 100f).ToString("0.0") + " % против гейта 12 %");
                Check("lookahead_covers_reaction_budget",
                    aheadMoving / top >= ChaseCamera.MinReactionSeconds,
                    (aheadMoving / top).ToString("0.00") + " с против бюджета "
                    + ChaseCamera.MinReactionSeconds.ToString("0.00") + " с");
                Check("track_has_checkpoints", track.checkpoints.Length > 0,
                    track.checkpoints.Length + " чекпоинтов");
            }
        }

        /// <summary>
        /// Негативный контроль: гейт композиции обязан уметь провалиться.
        /// -executeMethod ChartRunner.EditorTools.PlaySceneBuilder.NegativeControl
        /// </summary>
        public static void NegativeControl()
        {
            Lines.Add("=== NEGATIVE CONTROL композиции: ожидается FAIL ===");
            // 4.7 % — измеренная доля героя в старой Canvas-версии, из-за которой месяцами
            // доводили движение амплитудой 1-3 px. Гейт обязан её отклонить.
            const float fraction = 0.047f;
            Check("hero_fraction_meets_gate", fraction >= 0.12f,
                (fraction * 100f).ToString("0.0") + " % против гейта 12 %");
            var caught = Failures.Count > 0;
            Lines.Add("проверка композиции: " + (caught ? "ПОЙМАЛА (контроль пройден)" : "НЕ ПОЙМАЛА (тест сломан)"));
            Flush();
            EditorApplication.Exit(caught ? 0 : 1);
        }

        /// <summary>
        /// Верхняя скорость берётся ИЗ ПРОФИЛЯ, а не вписывается числом: гейт обзора обязан
        /// пересчитаться сам, если байк станет быстрее. Прошлой ночью так нашлись четыре
        /// числа в документах, разошедшихся с кодом.
        /// </summary>
        private static float BikeTopSpeed()
        {
            var p = AssetDatabase.LoadAssetAtPath<BikeTuningProfile>(BikeProfilePath);
            return p != null ? p.topSpeedMPerS : 6.271f;
        }

        private static void Check(string label, bool ok, string detail)
        {
            CheckCount++;
            Lines.Add((ok ? "  ok   " : "  FAIL ") + label + " — " + detail);
            if (!ok) Failures.Add(label + " — " + detail);
        }

        private static void Flush()
        {
            foreach (var l in Lines) Debug.Log(l);
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "play-scene-build.txt"), Lines);
        }
    }
}
