using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ChartRunner.Bike;
using ChartRunner.Input;
using ChartRunner.Track;
using ChartRunner.Tuning;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChartRunner.Tests
{
    /// <summary>
    /// Приёмка рукотворного отрезка. Единственный критерий, и он falsifiable в обе стороны:
    ///
    ///   «ТОЛЬКО ГАЗ» ОБЯЗАН ПРОВАЛИТЬСЯ, ДОЗИРОВКА ОБЯЗАНА ПРОЙТИ.
    ///
    /// Это и есть то, что должно стать правдой для игрока, переведённое в измеримое. Обе
    /// половины нужны: если проходит только газ — отрезок не наказывает, играть не во что;
    /// если не проходит никто — отрезок стена, и это ровно тот дефект, который найден у `VS`.
    ///
    /// Оговорка, которую надо держать в голове при чтении результата: скриптовый пилот НЕ
    /// доказывает играбельность (измерено на исходнике: бот «только газ» 41.6 % трассы,
    /// живой человек 22 %). Он доказывает ровно одно — что окно для навыка в физике этого
    /// отрезка ЕСТЬ. Интересно ли им пользоваться, решает человек со сборкой в руках.
    /// </summary>
    public class CruxSliceAcceptance
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private static string F(float v, int d = 2) =>
            v.ToString("F" + d, CultureInfo.InvariantCulture);

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 24f;
            Time.maximumDeltaTime = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [OneTimeTearDown]
        public void Restore()
        {
            Time.timeScale = 1f;
            Time.maximumDeltaTime = 0.3333333f;
        }

        private static IBikeInputSource Dosed(System.Func<BikeState> read, BikeTuningProfile p,
            float cutRel, float easedGas)
        {
            return new ScriptedBikeInput(() =>
            {
                var s = read();
                var rel = s.PitchRelRad;

                // В ВОЗДУХЕ ВЕС НЕ ПОДАЁТСЯ ВПЕРЁД. Это исправление дефекта самого пилота,
                // а не подгонка: без этой ветки политика после кикера держала вес вперёд,
                // приземлялась носом вниз и гибла клевком РОВНО в одном месте — 62.1 % у
                // всех шести вариантов. Одинаковый результат у всего перебора уже встречался
                // этой ночью и означал одно и то же: меряется не то, что варьируется.
                //
                // Живой райдер в полёте выравнивает байк по будущей поверхности, а не
                // клюёт вперёд. Газ в воздухе тоже не нужен: тяги нет, а момент от колеса есть.
                if (!s.IsGrounded)
                    return new BikeInputState { Throttle = 0f, Lean = rel > 0.25f ? 0.35f : 0f };

                if (rel > p.wheelieZone)
                    return new BikeInputState { Throttle = 0f, Brake = 0.55f, Lean = 1f };
                if (rel > cutRel)
                    return new BikeInputState { Throttle = easedGas, Lean = 0.8f };
                if (rel < -0.30f)
                    return new BikeInputState { Throttle = 1f, Lean = -1f };
                return new BikeInputState { Throttle = 1f, Lean = 0f };
            });
        }

        private struct Run
        {
            public string Name;
            public float DistanceM;
            public float Seconds;
            public BikeFailure Failure;
            public bool Finished;
        }

        private IEnumerator Measure(string name,
            System.Func<System.Func<BikeState>, BikeTuningProfile, IBikeInputSource> make,
            System.Action<Run> report)
        {
            var profile = ScriptableObject.CreateInstance<BikeTuningProfile>();
            var level = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
            var track = CruxSliceTrack.CreateProfile();
            var sampler = new TerrainSampler(track);
            _spawned.Add(TrackBuilder.Build(track, profile.tyreFriction));

            BikeController ctrl = null;
            var input = make(() => ctrl.State, profile);
            var axle = BikeFactory.RestingRearAxle(sampler, profile, 60f * UnitsContract.PxToM);
            ctrl = BikeFactory.Spawn(profile, level, sampler, input, axle, false);
            _spawned.Add(ctrl.gameObject);

            var endXM = CruxSliceTrack.EndPx * UnitsContract.PxToM;
            var maxX = 0f;
            var t = 0f;
            var finished = false;

            for (var i = 0; i < 3600; i++)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                var s = ctrl.State;
                maxX = Mathf.Max(maxX, s.PositionXM);
                if (s.Failure != BikeFailure.None) break;
                if (s.PositionXM >= endXM) { finished = true; break; }
            }

            report(new Run
            {
                Name = name, DistanceM = maxX, Seconds = t,
                Failure = ctrl.State.Failure, Finished = finished
            });
            TearDown();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator ThrottleOnly_Fails_And_Dosing_Passes()
        {
            var track = CruxSliceTrack.CreateProfile();
            var endXM = CruxSliceTrack.EndPx * UnitsContract.PxToM;
            // Раздельно подъём и спуск: MaxGridSlopeDeg берёт максимум ПО МОДУЛЮ, и в первой
            // редакции он показал −52.1° (спуск), из-за чего отчёт говорил про крутизну не того
            // места. Для проходимости важен подъём, для приземления — спуск.
            var maxUp = 0f;
            var maxDown = 0f;
            for (var x = 0f; x <= CruxSliceTrack.EndPx; x += CruxSliceTrack.NodeStepPx)
            {
                var d = track.GridSlopeRadAt(x) * Mathf.Rad2Deg;
                if (d > maxUp) maxUp = d;
                if (d < maxDown) maxDown = d;
            }

            var runs = new List<Run>();
            yield return Measure("NEUTRAL (только газ)", (r, p) => Neutral(), x => runs.Add(x));
            foreach (var cut in new[] { 0.25f, 0.40f, 0.55f })
            foreach (var gas in new[] { 0.00f, 0.35f })
            {
                var c = cut; var g = gas;
                yield return Measure("DOSED срез " + F(c, 2) + " газ " + F(g, 2),
                    (r, p) => Dosed(r, p, c, g), x => runs.Add(x));
            }

            var neutral = runs[0];
            var best = runs.Skip(1).OrderByDescending(r => r.DistanceM).First();

            var lines = new List<string>
            {
                "# ПРИЁМКА РУКОТВОРНОГО ОТРЕЗКА (крукс-30)",
                "",
                "Сгенерирован `CruxSliceAcceptance`. Перезапишется следующим прогоном.",
                "",
                "Отрезок построен по калибровке автора исходника (chartrider.html:654):",
                "пик ~41°, средний подъём ~30° — коридор, в котором «сток ЕДВА вылезает».",
                "Перенесённая `VS` из этого коридора вышла: узлы 45.0° и 47.7°, по интерполяции",
                "50.8°, и туда упираются все политики пилота без исключения.",
                "",
                "Длина отрезка: " + F(endXM, 1) + " м. Максимальный ПОДЪЁМ по сетке: **"
                    + F(maxUp, 1) + "°** (предел автора 54°, цель ~41°), максимальный спуск "
                    + F(maxDown, 1) + "°.",
                "",
                "| политика | дистанция | % | время | итог |",
                "|---|---|---|---|---|"
            };
            foreach (var r in runs)
            {
                lines.Add("| " + r.Name + " | " + F(r.DistanceM, 1) + " м | "
                          + F(100f * r.DistanceM / endXM, 1) + " % | " + F(r.Seconds, 1) + " с | "
                          + (r.Finished ? "**ФИНИШ**" : r.Failure.ToString()) + " |");
            }

            lines.Add("");
            lines.Add("## Вывод");
            lines.Add("");
            var pass = !neutral.Finished && best.Finished;
            if (pass)
            {
                lines.Add("**Критерий выполнен.** «Только газ» проваливается на "
                          + F(100f * neutral.DistanceM / endXM, 1) + " % ("
                          + neutral.Failure + "), дозировка проходит целиком.");
                lines.Add("Окно для навыка в физике этого отрезка ЕСТЬ.");
                lines.Add("");
                lines.Add("Чего это не доказывает: что играть интересно. Это решает человек");
                lines.Add("со сборкой в руках — скриптовый пилот на такой вопрос не отвечает.");
            }
            else if (neutral.Finished)
            {
                lines.Add("**Критерий НЕ выполнен: «только газ» проходит отрезок целиком.**");
                lines.Add("Отрезок не наказывает за отсутствие дозировки, значит навыку в нём");
                lines.Add("негде проявиться. Нужно поднимать крутизну крукса ближе к 41°.");
            }
            else
            {
                lines.Add("**Критерий НЕ выполнен: не проходит НИКТО** (лучший результат "
                          + F(100f * best.DistanceM / endXM, 1) + " %).");
                lines.Add("Это тот же дефект, что у `VS`: отрезок стал стеной. Крукс надо");
                lines.Add("положе, либо давать больше разгона перед ним.");
            }

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                "docs", "crux-slice-acceptance.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
            Debug.Log("CRUX ACCEPTANCE записан: " + path + "\n" + string.Join("\n", lines));

            Assert.IsFalse(neutral.Finished,
                "«только газ» прошёл отрезок целиком — отрезок не наказывает, играть не во что");
            Assert.IsTrue(best.Finished,
                "ни одна дозировка не прошла отрезок — он стал стеной, как и VS");
        }

        private static IBikeInputSource Neutral() => ScriptedBikeInput.HoldThrottle();
    }
}
