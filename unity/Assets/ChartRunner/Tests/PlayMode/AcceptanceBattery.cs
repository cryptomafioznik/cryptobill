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
    /// Пункт 8 плана: батарея приёмочных тестов A-F из хендоффа плюс G и H, добавленные
    /// в `docs/TECH_DEBT.md` §4, с отчётом в `docs/unity-acceptance-report.md`.
    ///
    /// ПОЧЕМУ G И H ДОБАВЛЕНЫ. Батарея A-F целиком проверяет, что физика НЕ СЛОМАНА, и ни один
    /// её тест не проверяет, что она ИНТЕРЕСНА. Это та же асимметрия, из-за которой предыдущая
    /// версия проекта получила «12/12 гейтов» и непроходимую игру. G мерит окно навыка (влияет
    /// ли перенос веса на результат), H — окно восстановления (единственный из шести критериев
    /// остановки аудита §14, проверяемый headless).
    ///
    /// ОТЧЁТ пишется из `[OneTimeTearDown]` и содержит ИЗМЕРЕННЫЕ числа, а не «PASS/FAIL».
    /// Число без величины ничего не говорит утреннему читателю.
    /// </summary>
    public class AcceptanceBattery
    {
        private static readonly List<string> Report = new List<string>();
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private static string F(float v, int d = 2) =>
            v.ToString("F" + d, CultureInfo.InvariantCulture);

        private static void Row(string test, string what, string measured, string gate, bool pass)
        {
            RowStatus(test, what, measured, gate, pass ? "прошёл" : "**НЕ ПРОШЁЛ**");
        }

        /// <summary>
        /// Третий статус нужен для теста G. Написать «прошёл» рядом с «выигрыш 0.0 %» значило бы
        /// подать отсутствие результата как успех: тест действительно не упал, но измеренное
        /// число говорит, что окно навыка пока НЕ продемонстрировано.
        /// </summary>
        private static void RowStatus(string test, string what, string measured, string gate, string status)
        {
            Report.Add("| " + test + " | " + what + " | " + measured + " | " + gate + " | "
                       + status + " |");
        }

        private static void Note(string s) => Report.Add(s);

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [OneTimeTearDown]
        public void WriteReport()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                "docs", "unity-acceptance-report.md"));

            var head = new List<string>
            {
                "# ОТЧЁТ ПРИЁМОЧНОЙ БАТАРЕИ (Unity)",
                "",
                "Сгенерирован автоматически из `AcceptanceBattery` при прогоне",
                "`-runTests -testPlatform PlayMode`. Править руками бессмысленно — перезапишется.",
                "",
                "Unity " + Application.unityVersion
                + " · fixedDeltaTime " + F(1f / 60f, 5)
                + " · gravity " + F(Physics2D.gravity.y, 2) + " м/с²",
                "",
                "Тесты A-F — из хендоффа `docs/UNITY-MIGRATION-START-HERE.md` §3 пункт 8.",
                "Тесты G и H добавлены в `docs/TECH_DEBT.md` §4: батарея A-F проверяет только,",
                "что физика не сломана, и ни один её тест не проверяет, что она интересна.",
                "",
                "| Тест | Что мерится | Измерено | Гейт | Итог |",
                "|---|---|---|---|---|"
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, head.Concat(Report));
            Debug.Log("ACCEPTANCE REPORT записан: " + path + "\n"
                      + string.Join("\n", head.Concat(Report)));
        }

        // ================= оснастка =================

        private BikeController Spawn(LabTerrains.Pad pad, IBikeInputSource input,
            out BikeTuningProfile profile, out Vector2 axle, out TerrainSampler sampler)
        {
            return SpawnOn(LabTerrains.CreateProfile(pad),
                LabTerrains.StartXPx(pad), input, out profile, out axle, out sampler);
        }

        /// <summary>
        /// Спавн на ЛЮБОМ профиле трассы, включая настоящую дизайн-трассу. Нужен тесту G:
        /// окно навыка невозможно измерить на площадке, которую чистый газ проходит целиком.
        /// </summary>
        private BikeController SpawnOn(TrackProfile track, float startXPx, IBikeInputSource input,
            out BikeTuningProfile profile, out Vector2 axle, out TerrainSampler sampler,
            LevelPhysicsOverride level = null)
        {
            profile = ScriptableObject.CreateInstance<BikeTuningProfile>();
            sampler = new TerrainSampler(track);
            _spawned.Add(TrackBuilder.Build(track, profile.tyreFriction));
            var startXM = startXPx * UnitsContract.PxToM;
            axle = BikeFactory.RestingRearAxle(sampler, profile, startXM);
            var ctrl = BikeFactory.Spawn(profile, level ?? LevelPhysicsOverride.CreateStock(), sampler,
                input, axle, false);
            _spawned.Add(ctrl.gameObject);
            return ctrl;
        }

        private static IEnumerator Steps(int n)
        {
            for (var i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        // ================= A: разгон на ровном =================

        [UnityTest, Timeout(180000)]
        public IEnumerator A_AccelerationOnFlat()
        {
            var ctrl = Spawn(LabTerrains.Pad.Flat, ScriptedBikeInput.HoldThrottle(),
                out var profile, out _, out _);

            var speeds = new List<float>();
            for (var i = 0; i < 600; i++)
            {
                yield return new WaitForFixedUpdate();
                speeds.Add(ctrl.State.SpeedMPerS);
            }

            var cruise = speeds.Skip(400).Average();
            var target = 0.9f * cruise;
            var frame90 = speeds.FindIndex(v => v >= target);
            var t90 = frame90 < 0 ? -1f : frame90 / 60f;

            var pass = Mathf.Abs(cruise - profile.topSpeedMPerS) < profile.topSpeedMPerS * 0.1f
                       && t90 > 0f && t90 < 3f;
            Row("A", "крейсер на ровном, полный газ",
                F(cruise, 3) + " м/с (" + F(cruise * 3.6f, 1) + " км/ч), 90 % за " + F(t90) + " с",
                "эталон исходника 6.271 м/с ±10 %", pass);

            Assert.That(cruise, Is.EqualTo(profile.topSpeedMPerS).Within(profile.topSpeedMPerS * 0.1f),
                "крейсер " + F(cruise, 3) + " м/с против эталона " + F(profile.topSpeedMPerS, 3));
            Assert.Greater(t90, 0f, "90 % крейсера обязаны быть достигнуты");
            Assert.Less(t90, 3f, "и не позже 3 с: получено " + F(t90) + " с");
        }

        // ================= B: старт на подъёме =================

        [UnityTest, Timeout(180000)]
        public IEnumerator B_StandingStartOnClimb()
        {
            var ctrl = Spawn(LabTerrains.Pad.SteepClimb, ScriptedBikeInput.HoldThrottle(),
                out _, out _, out _);

            var topXM = 2250f * UnitsContract.PxToM;
            var climbStartXM = 1500f * UnitsContract.PxToM;
            var reached = false;
            var minSpeed = 999f;

            for (var i = 0; i < 900; i++)
            {
                yield return new WaitForFixedUpdate();
                var s = ctrl.State;
                if (s.PositionXM > climbStartXM && s.IsGrounded)
                    minSpeed = Mathf.Min(minSpeed, s.SpeedMPerS);
                if (s.PositionXM >= topXM) { reached = true; break; }
            }

            var pass = reached && minSpeed > 0f;
            Row("B", "заезд на 45° со стоячего старта",
                (reached ? "доехал" : "НЕ доехал") + ", мин. скорость на подъёме " + F(minSpeed) + " м/с",
                "доехать и ни разу не съехать назад", pass);

            Assert.IsTrue(reached, "байк обязан заехать на 45°");
            Assert.Greater(minSpeed, 0f, "и ни разу не съехать назад");
        }

        // ================= C: прыжок и приземление =================

        [UnityTest, Timeout(180000)]
        public IEnumerator C_JumpAndLanding()
        {
            var ctrl = Spawn(LabTerrains.Pad.JumpRamp, ScriptedBikeInput.HoldThrottle(),
                out _, out _, out _);

            var maxAir = 0f;
            var wasAirborne = false;
            var landed = false;
            var maxCompression = 0f;

            for (var i = 0; i < 900; i++)
            {
                yield return new WaitForFixedUpdate();
                var s = ctrl.State;
                maxAir = Mathf.Max(maxAir, s.AirTimeSeconds);
                maxCompression = Mathf.Max(maxCompression, s.RearCompressionM);
                if (s.AirTimeSeconds > 0.2f) wasAirborne = true;
                if (wasAirborne && s.IsGrounded && s.AirTimeSeconds < 0.02f) landed = true;
                if (s.Failure != BikeFailure.None) break;
                if (s.PositionXM > 2400f * UnitsContract.PxToM) break;
            }

            var pass = wasAirborne && landed && ctrl.State.Failure == BikeFailure.None;
            Row("C", "отрыв с вогнутого липа и посадка",
                "воздух " + F(maxAir, 3) + " с, сел " + (landed ? "да" : "нет")
                + ", макс сжатие подвески " + F(maxCompression, 3) + " м, отказ " + ctrl.State.Failure,
                "воздух ≥ 0.2 с, посадка без краша", pass);

            Assert.IsTrue(wasAirborne, "обязан оторваться: воздух " + F(maxAir, 3) + " с");
            Assert.IsTrue(landed, "и снова встать на колёса");
            Assert.AreEqual(BikeFailure.None, ctrl.State.Failure, "посадка без краша");
        }

        // ================= D: мелкие неровности =================

        [UnityTest, Timeout(180000)]
        public IEnumerator D_SmallBumps()
        {
            var ctrl = Spawn(LabTerrains.Pad.SmallBumps, ScriptedBikeInput.HoldThrottle(),
                out _, out _, out _);

            var endXM = 3400f * UnitsContract.PxToM;
            var airFrames = 0;
            var frames = 0;
            var maxPitch = 0f;
            var passed = false;

            for (var i = 0; i < 900; i++)
            {
                yield return new WaitForFixedUpdate();
                var s = ctrl.State;
                frames++;
                if (!s.IsGrounded) airFrames++;
                maxPitch = Mathf.Max(maxPitch, Mathf.Abs(s.PitchRelRad * Mathf.Rad2Deg));
                if (s.Failure != BikeFailure.None) break;
                if (s.PositionXM >= endXM) { passed = true; break; }
            }

            var airFraction = frames > 0 ? (float)airFrames / frames : 0f;
            var pass = passed && ctrl.State.Failure == BikeFailure.None;
            Row("D", "серия вупсов до −35.6° без застревания и краша",
                (passed ? "прошёл секцию" : "НЕ прошёл") + ", доля воздуха " + F(airFraction, 3)
                + ", макс тангаж к склону " + F(maxPitch, 1) + "°, отказ " + ctrl.State.Failure,
                "пройти секцию без отказа", pass);

            Assert.IsTrue(passed, "байк обязан пройти вупсы");
            Assert.AreEqual(BikeFailure.None, ctrl.State.Failure, "и без отказа");
        }

        // ================= E: краш и рестарт =================

        [UnityTest, Timeout(180000)]
        public IEnumerator E_CrashAndRestart()
        {
            var ctrl = Spawn(LabTerrains.Pad.Flat, ScriptedBikeInput.HoldThrottle(),
                out _, out var axle, out _);
            var rig = ctrl.GetComponent<BikeRig>();

            yield return Steps(120);

            for (var i = 0; i < 120; i++)
            {
                rig.Chassis.rotation = -150f;
                rig.Chassis.angularVelocity = -60f;
                yield return new WaitForFixedUpdate();
                if (ctrl.State.Failure != BikeFailure.None) break;
            }

            var failed = ctrl.State.Failure != BikeFailure.None;
            var halted = ctrl.Halted;

            ctrl.ResetTo(axle);
            yield return Steps(180);
            var s = ctrl.State;

            var pass = failed && halted && s.Failure == BikeFailure.None
                       && s.SpeedMPerS > 1f && s.IsGrounded;
            Row("E", "отказ, остановка, сброс и повторный ход",
                "отказ " + (failed ? "получен" : "НЕ получен") + ", остановка "
                + (halted ? "да" : "нет") + "; после сброса скорость " + F(s.SpeedMPerS)
                + " м/с, контактов " + s.GroundedWheelCount,
                "довести до отказа, сбросить, снова поехать", pass);

            Assert.IsTrue(failed, "сценарий обязан суметь довести до отказа");
            Assert.IsTrue(halted, "после отказа управление обязано остановиться");
            Assert.AreEqual(BikeFailure.None, s.Failure, "после сброса байк живой");
            Assert.Greater(s.SpeedMPerS, 1f, "и снова разгоняется");
        }

        // ================= F: независимость от частоты =================

        [UnityTest, Timeout(300000)]
        public IEnumerator F_FrameRateIndependence()
        {
            // Сравниваются ТРАЕКТОРИИ по времени, а не «работает ли». Ловушка исходника
            // была именно в этом: физика шла по кадрам, и на 45 FPS игра становилась другой.
            var saved = Time.fixedDeltaTime;
            var samples = new Dictionary<float, List<Vector2>>();
            var sampleTimes = new[] { 0.5f, 1f, 1.5f, 2f, 2.5f, 3f };

            foreach (var step in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
            {
                Time.fixedDeltaTime = step;
                var ctrl = Spawn(LabTerrains.Pad.SmallBumps, ScriptedBikeInput.HoldThrottle(),
                    out _, out _, out _);

                var got = new List<Vector2>();
                var t = 0f;
                var next = 0;
                var guard = 0;
                while (next < sampleTimes.Length && guard++ < 20000)
                {
                    yield return new WaitForFixedUpdate();
                    t += step;
                    if (t >= sampleTimes[next])
                    {
                        got.Add(new Vector2(ctrl.State.PositionXM, ctrl.State.PositionYM));
                        next++;
                    }
                }
                samples[step] = got;
                TearDown();
            }

            Time.fixedDeltaTime = saved;

            var baseline = samples[1f / 60f];
            var worst = 0f;
            var detail = new List<string>();
            foreach (var step in new[] { 1f / 30f, 1f / 120f })
            {
                var other = samples[step];
                var d = 0f;
                for (var i = 0; i < Mathf.Min(baseline.Count, other.Count); i++)
                    d = Mathf.Max(d, Vector2.Distance(baseline[i], other[i]));
                worst = Mathf.Max(worst, d);
                detail.Add(Mathf.RoundToInt(1f / step) + " Гц: " + F(d, 3) + " м");
            }

            // Гейт назначен так, чтобы ловить возврат к покадровой физике, а не шум решателя.
            // Box2D не детерминирован между разными шагами по построению, поэтому нулевого
            // расхождения требовать нельзя — цена принята в хендоффе §1.
            const float gate = 2.5f;
            var pass = worst < gate;
            Row("F", "траектория при 30 / 60 / 120 Гц (расхождение с 60 Гц)",
                string.Join(", ", detail) + " — макс " + F(worst, 3) + " м на 3 с прогона",
                "< " + F(gate, 1) + " м (ловит возврат к покадровой физике)", pass);

            Assert.Less(worst, gate,
                "траектория обязана не зависеть от частоты: макс расхождение " + F(worst, 3) + " м");
        }

        // ================= G: окно навыка =================

        /// <summary>
        /// Политика WEIGHT теста G: газ всегда, вес РЕАКТИВНО и ТОЛЬКО вес.
        /// Газ и тормоз не модулируются намеренно — иначе замер смешивает два навыка,
        /// и именно этим негоден шипованный `technical` пилот исходника.
        /// </summary>
        private static IBikeInputSource WeightPolicy(System.Func<BikeState> read)
        {
            return new ScriptedBikeInput(() =>
            {
                var s = read();
                var rel = s.PitchRelRad;
                var lean = 0f;
                if (rel > 0.18f) lean = 1f;        // нос задрало → вес ВПЕРЁД
                else if (rel < -0.18f) lean = -1f; // клюнул → вес НАЗАД
                return new BikeInputState { Throttle = 1f, Lean = lean };
            });
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator G_SkillWindow()
        {
            // МЕРИТСЯ НА НАСТОЯЩЕЙ ДИЗАЙН-ТРАССЕ, а не на площадке полигона. Первая редакция
            // этого теста гоняла площадку SteepClimb и получила РОВНО одинаковые 63.7039452 м
            // у обеих политик: площадку чистый газ проходит целиком, поэтому переносу веса там
            // нечего добавить и окно навыка не проявляется в принципе. В исходнике этот замер
            // тоже делался на 13-секционной трассе, где `gas` умирает на 22 %.
            var distances = new float[2];
            var failures = new BikeFailure[2];
            var seconds = new float[2];

            for (var mode = 0; mode < 2; mode++)
            {
                BikeController ctrl = null;
                var input = mode == 0
                    ? (IBikeInputSource)ScriptedBikeInput.HoldThrottle()
                    : WeightPolicy(() => ctrl.State);

                // ПРОФИЛЬ УРОВНЯ, а не сток. Со стоковыми помощниками (все ручки 1.0) тангаж
                // к склону не выходит за 5.9° — при пороге реакции 10.3° политика веса не
                // активируется НИ РАЗУ, и обе политики дают побитово одинаковый результат.
                // Весь смысл VS.physics в том, что помощники ослаблены (groundAlign 0.30,
                // wheelieGuard 0, edgeGuard 0.25): тогда газ реально задирает нос и появляется
                // что исправлять. Без этого окно навыка измерять не на чем.
                ctrl = SpawnOn(VerticalSliceTrack.CreateProfile(), 60f, input,
                    out _, out _, out _, ScriptableObject.CreateInstance<LevelPhysicsOverride>());

                var endXM = VerticalSliceTrack.EndPx * UnitsContract.PxToM;
                var maxX = 0f;
                var frames = 0;
                for (var i = 0; i < 4200; i++)
                {
                    yield return new WaitForFixedUpdate();
                    frames++;
                    var s = ctrl.State;
                    maxX = Mathf.Max(maxX, s.PositionXM);
                    if (s.Failure != BikeFailure.None) break;
                    if (s.PositionXM >= endXM) break;
                }
                distances[mode] = maxX;
                failures[mode] = ctrl.State.Failure;
                seconds[mode] = frames / 60f;
                TearDown();
            }

            var gain = (distances[1] - distances[0]) / Mathf.Max(0.01f, distances[0]) * 100f;
            var endM = VerticalSliceTrack.EndPx * UnitsContract.PxToM;

            Note("");
            Note("**G — измерение, а не гейт.** Порог здесь НЕ назначен, и это осознанно.");
            Note("Прежнее «+46.2 %» из хендоффа §5 воспроизвести нельзя: «нейтраль» — это политика");
            Note("`gas`, и она воспроизводится точно (2468 / 2433 px на КРОСС 250), а «реактивная»");
            Note("политика, давшая 3053 / 3558, в поставленном коде отсутствует и её определение");
            Note("не записано нигде. Назначать порог по числу без метода — тот самоподтверждающийся");
            Note("критерий, который проекту уже дорого обошёлся. Здесь зафиксировано ПЕРВОЕ");
            Note("измерение своей политики WEIGHT на настоящей трассе; порог назначается после");
            Note("разговора с пользователем, от этого числа с запасом.");
            Note("");
            Note("| политика | дистанция | % трассы | время | отказ |");
            Note("|---|---|---|---|---|");
            Note("| NEUTRAL (газ, вес ≡ 0) | " + F(distances[0], 1) + " м | "
                 + F(100f * distances[0] / endM, 1) + " % | " + F(seconds[0]) + " с | " + failures[0] + " |");
            Note("| WEIGHT (газ, вес реактивно) | " + F(distances[1], 1) + " м | "
                 + F(100f * distances[1] / endM, 1) + " % | " + F(seconds[1]) + " с | " + failures[1] + " |");
            Note("| **выигрыш веса** | **" + F(gain, 1) + " %** | | | |");
            Note("");

            RowStatus("G", "окно навыка на дизайн-трассе: WEIGHT против NEUTRAL",
                "NEUTRAL " + F(100f * distances[0] / endM, 1) + " % трассы, WEIGHT "
                + F(100f * distances[1] / endM, 1) + " %, выигрыш " + F(gain, 1) + " %",
                "порог НЕ назначен — см. примечание",
                Mathf.Abs(gain) < 5f
                    ? "**измерено, окно НЕ продемонстрировано**"
                    : "измерено (гейта нет)");

            Assert.Greater(distances[0], 1f, "прогон NEUTRAL обязан быть содержательным");
            Assert.Greater(distances[1], 1f, "прогон WEIGHT обязан быть содержательным");
            Assert.AreNotEqual(distances[0], distances[1],
                "две разные политики обязаны давать разный результат, иначе замер ничего не мерит");
        }

        // ================= H: окно восстановления =================

        [UnityTest, Timeout(600000)]
        public IEnumerator H_RecoveryWindow()
        {
            // Единственный из шести критериев остановки аудита §14, проверяемый headless.
            //
            // Мерится НА ДИЗАЙН-ТРАССЕ с профилем уровня, а не на площадке полигона. Первая
            // редакция гоняла площадку SteepClimb и честно отчиталась «опрокида не произошло,
            // макс тангаж 5.9°» — то есть НЕ ИЗМЕРИЛА ничего: площадку байк проезжает, лупов
            // на ней не бывает. Окно восстановления можно мерить только там, где опрокид есть.
            //
            // На HTML-сборке этот замер выполнить не удалось: игра не отдаёт уклон наружу
            // (спека §9), поэтому хендоффные «2.22 с» не воспроизведены, и порог 1.5 с взят
            // из критерия остановки аудита §14.1, а не из них.
            BikeController ctrl = null;
            ctrl = SpawnOn(VerticalSliceTrack.CreateProfile(), 60f,
                ScriptedBikeInput.HoldThrottle(), out _, out _, out _,
                ScriptableObject.CreateInstance<LevelPhysicsOverride>());

            var onsetTime = -1f;
            var failTime = -1f;
            var t = 0f;
            var maxRel = 0f;
            var failX = 0f;

            for (var i = 0; i < 4200; i++)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                var s = ctrl.State;
                maxRel = Mathf.Max(maxRel, s.PitchRelRad);

                // Отсчёт начинается, когда нос ушёл за 0.35 рад (20°) ОТНОСИТЕЛЬНО поверхности
                // и больше не возвращался. Пересбрасываем, пока байк выправляется сам.
                if (s.PitchRelRad > 0.35f) { if (onsetTime < 0f) onsetTime = t; }
                else if (s.IsGrounded) onsetTime = -1f;

                if (s.Failure != BikeFailure.None) { failTime = t; failX = s.PositionXM; break; }
                if (s.PositionXM >= VerticalSliceTrack.EndPx * UnitsContract.PxToM) break;
            }

            if (failTime < 0f)
            {
                RowStatus("H", "окно от нос-вверх до невозврата, газ зажат",
                    "опрокида не произошло за прогон; макс тангаж к склону "
                    + F(maxRel * Mathf.Rad2Deg, 1) + "°",
                    "≥ 1.5 с (аудит §14.1)", "**не измерено: опрокида не случилось**");
                Assert.Pass("опрокида не произошло — окно не ограничено, но и не измерено");
            }
            else
            {
                var window = onsetTime < 0f ? -1f : failTime - onsetTime;
                if (window < 0f)
                {
                    RowStatus("H", "окно от нос-вверх до невозврата, газ зажат",
                        "отказ " + ctrl.State.Failure + " на " + F(failX, 1)
                        + " м в " + F(failTime) + " с, но порог nose-up 20° не был пройден",
                        "≥ 1.5 с (аудит §14.1)", "**не измерено: причина не в тангаже**");
                    Assert.Pass("отказ произошёл не через задирание носа — окно неприменимо");
                }
                else
                {
                    var pass = window >= 1.5f;
                    Row("H", "окно от нос-вверх (20° к склону) до невозврата, газ зажат",
                        F(window, 2) + " с (нос-вверх " + F(onsetTime) + " с, отказ "
                        + ctrl.State.Failure + " на " + F(failX, 1) + " м в " + F(failTime) + " с)",
                        "≥ 1.5 с (аудит §14.1)", pass);
                    Assert.GreaterOrEqual(window, 1.5f,
                        "окно восстановления " + F(window, 2) + " с должно быть ≥ 1.5 с");
                }
            }
        }
    }
}
