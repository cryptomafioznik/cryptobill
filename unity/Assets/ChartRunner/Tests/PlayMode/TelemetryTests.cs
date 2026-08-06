using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChartRunner.Bike;
using ChartRunner.Input;
using ChartRunner.Telemetry;
using ChartRunner.Track;
using ChartRunner.Tuning;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChartRunner.Tests
{
    /// <summary>
    /// Пункт 7: телеметрия и оверлей.
    ///
    /// Главный тест здесь — <see cref="Telemetry_DoesNotPerturbPhysics"/>. Инструмент, который
    /// меняет измеряемое, хуже отсутствия инструмента, а поймать это глазами невозможно.
    /// </summary>
    public class TelemetryTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static string F(float v, int d = 3) =>
            v.ToString("F" + d, CultureInfo.InvariantCulture);

        private BikeController Spawn(LabTerrains.Pad pad, IBikeInputSource input,
            bool withTelemetry, out BikeTuningProfile profile, out Vector2 axle)
        {
            profile = ScriptableObject.CreateInstance<BikeTuningProfile>();
            var track = LabTerrains.CreateProfile(pad);
            var sampler = new TerrainSampler(track);
            _spawned.Add(TrackBuilder.Build(track, profile.tyreFriction));
            var startXM = LabTerrains.StartXPx(pad) * UnitsContract.PxToM;
            axle = BikeFactory.RestingRearAxle(sampler, profile, startXM);
            var ctrl = BikeFactory.Spawn(profile, LevelPhysicsOverride.CreateStock(), sampler,
                input, axle, withTelemetry);
            _spawned.Add(ctrl.gameObject);
            return ctrl;
        }

        private static IEnumerator Steps(int n)
        {
            for (var i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        // ============================================================================

        [UnityTest, Timeout(180000)]
        public IEnumerator Telemetry_DoesNotPerturbPhysics()
        {
            // ГЛАВНЫЙ тест пункта 7. Один и тот же прогон с телеметрией и без обязан дать
            // одинаковую траекторию. Если сбор данных что-то трогает — все последующие замеры
            // мерят инструмент вместе с объектом, и отличить это по числам уже нельзя.
            var traces = new List<List<Vector3>>();

            foreach (var withTelemetry in new[] { false, true })
            {
                var ctrl = Spawn(LabTerrains.Pad.JumpRamp, ScriptedBikeInput.HoldThrottle(),
                    withTelemetry, out _, out _);

                var trace = new List<Vector3>();
                for (var i = 0; i < 480; i++)
                {
                    yield return new WaitForFixedUpdate();
                    var s = ctrl.State;
                    trace.Add(new Vector3(s.PositionXM, s.PositionYM, s.PitchRad));
                }
                traces.Add(trace);

                if (withTelemetry)
                {
                    var t = ctrl.GetComponent<BikeTelemetry>();
                    Assert.IsNotNull(t, "во втором прогоне телеметрия должна быть навешена");
                    Assert.Greater(t.TotalSamples, 400, "и должна была собрать снимки");
                }

                TearDown();
            }

            var worst = 0f;
            var worstAt = -1;
            for (var i = 0; i < traces[0].Count; i++)
            {
                var d = Vector3.Distance(traces[0][i], traces[1][i]);
                if (d > worst) { worst = d; worstAt = i; }
            }

            Debug.Log("TELEMETRY PERTURBATION\n  макс расхождение траекторий = " + F(worst, 6)
                      + " на кадре " + worstAt + " из " + traces[0].Count
                      + "\n  (площадка с прыжком выбрана намеренно: воздух и посадка усиливают "
                      + "любое расхождение)");

            Assert.That(worst, Is.LessThan(1e-4f),
                "траектории с телеметрией и без обязаны совпасть: расхождение " + F(worst, 6)
                + " м на кадре " + worstAt);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Telemetry_ReadsControllerStateNotItsOwnCopy()
        {
            // Телеметрия обязана ЧИТАТЬ состояние продакшена, а не считать своё. Иначе она
            // начнёт расходиться с физикой незаметно (ловушка №5 из памяти проекта).
            var ctrl = Spawn(LabTerrains.Pad.SteepClimb, ScriptedBikeInput.HoldThrottle(),
                true, out _, out _);
            var t = ctrl.GetComponent<BikeTelemetry>();

            yield return Steps(120);

            var s = ctrl.State;
            var l = t.Latest;
            Assert.AreEqual(s.PositionXM, l.PositionXM, 1e-6f, "позиция обязана совпадать");
            Assert.AreEqual(s.SpeedMPerS, l.SpeedMPerS, 1e-6f, "скорость обязана совпадать");
            Assert.AreEqual(s.PitchRelRad, l.PitchRelRad, 1e-6f, "тангаж обязан совпадать");
            Assert.AreEqual(s.State, l.State, "ярлык состояния обязан совпадать");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Telemetry_SuspensionCompressionIsFilledAndMatchesStaticSag()
        {
            // Поля сжатия подвески существовали с пункта 5, но НЕ ЗАПОЛНЯЛИСЬ — оверлей показывал
            // бы нули и выглядел работающим. Проверяем, что теперь там реальная величина и что
            // она равна измеренной статической просадке.
            var ctrl = Spawn(LabTerrains.Pad.Flat,
                new ScriptedBikeInput(() => BikeInputState.Neutral), true, out var profile, out _);

            yield return Steps(300);

            var s = ctrl.State;
            Debug.Log("TELEMETRY SUSPENSION\n  сжатие: зад " + F(s.RearCompressionM)
                      + " м, перед " + F(s.FrontCompressionM) + " м"
                      + "\n  эталон статической просадки при 8 Гц = 0.0996 м");

            Assert.That(s.RearCompressionM, Is.EqualTo(0.0996f).Within(0.02f),
                "сжатие зада в покое = статическая просадка: " + F(s.RearCompressionM) + " м");
            Assert.That(s.FrontCompressionM, Is.EqualTo(0.0996f).Within(0.02f),
                "сжатие переда в покое = статическая просадка: " + F(s.FrontCompressionM) + " м");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Telemetry_StateClassifierDistinguishesSituations()
        {
            // Ярлык состояния бесполезен, если он не меняется. Проверяем, что за один прогон
            // по трамплину встречаются РАЗНЫЕ состояния, включая воздух и приземление.
            var ctrl = Spawn(LabTerrains.Pad.JumpRamp, ScriptedBikeInput.HoldThrottle(),
                true, out _, out _);
            var t = ctrl.GetComponent<BikeTelemetry>();
            t.Capacity = 2000;

            for (var i = 0; i < 600; i++)
            {
                yield return new WaitForFixedUpdate();
                if (ctrl.State.PositionXM > 2400f * UnitsContract.PxToM) break;
            }

            var seen = t.Buffer.Select(b => b.State).Distinct().OrderBy(x => x.ToString()).ToList();
            Debug.Log("TELEMETRY STATES\n  встречены: " + string.Join(", ", seen));

            Assert.Contains(RidingState.Airborne, seen, "прогон по трамплину обязан включать воздух");
            Assert.Greater(seen.Count, 2,
                "классификатор обязан различать ситуации, а встречено состояний: " + seen.Count);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Overlay_ShowsEveryFieldRequiredByTheHandoff()
        {
            // Хендофф §3 пункт 7 перечисляет ровно то, что оверлей обязан показывать:
            // speed, angular velocity, grounded wheel count, throttle, brake, lean,
            // suspension compression, current state. Этот тест — гейт против того, чтобы
            // какое-то из полей молча выпало при рефакторинге.
            var ctrl = Spawn(LabTerrains.Pad.Flat, ScriptedBikeInput.HoldThrottle(),
                true, out _, out _);
            var t = ctrl.GetComponent<BikeTelemetry>();
            var overlay = ctrl.GetComponent<TelemetryOverlay>();

            yield return Steps(120);

            var text = string.Join("\n", t.OverlayLines());
            Debug.Log("TELEMETRY OVERLAY\n" + text);

            foreach (var required in new[]
                     {
                         "состояние", "скорость", "угл.скор.", "колёс на з.",
                         "газ", "тормоз", "вес", "подвеска", "прижим"
                     })
            {
                Assert.IsTrue(text.Contains(required),
                    "оверлей обязан показывать «" + required + "», а его нет в выводе");
            }

            // Переключатель обязан работать без ввода: тесты идут headless.
            Assert.IsTrue(overlay.Visible, "по умолчанию оверлей видим");
            overlay.Toggle();
            Assert.IsFalse(overlay.Visible, "переключатель обязан выключать");
            overlay.Toggle();
            Assert.IsTrue(overlay.Visible, "и включать обратно");

            // Сбор можно выключить отдельно от рендера.
            t.Enabled = false;
            var before = t.TotalSamples;
            yield return Steps(60);
            Assert.AreEqual(before, t.TotalSamples,
                "при Enabled = false сбор обязан прекратиться, а снимков стало " + t.TotalSamples);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Telemetry_SummaryMatchesBufferContents()
        {
            // Сводка — то, что попадёт в отчёты пункта 8. Замкнутая сверка: сводка обязана
            // согласовываться с буфером, из которого она посчитана.
            var ctrl = Spawn(LabTerrains.Pad.JumpRamp, ScriptedBikeInput.HoldThrottle(),
                true, out _, out _);
            var t = ctrl.GetComponent<BikeTelemetry>();
            t.Capacity = 2000;

            yield return Steps(420);

            var sum = t.Summarise();
            var buf = t.Buffer;

            Assert.AreEqual(buf.Count, sum.Samples, "число снимков обязано совпадать");
            Assert.AreEqual(buf.Max(b => b.SpeedMPerS), sum.MaxSpeedMPerS, 1e-4f,
                "максимум скорости обязан совпадать с буфером");
            Assert.AreEqual(buf.Count(b => !b.IsGrounded) / (float)buf.Count, sum.AirFraction, 1e-4f,
                "доля воздуха обязана совпадать");

            // И проверка, что величины не вырождены: иначе сверка прошла бы на нулях.
            Assert.Greater(sum.MaxSpeedMPerS, 1f, "прогон обязан быть содержательным");
            Assert.Greater(sum.DistanceM, 5f, "и байк обязан проехать заметное расстояние");

            Debug.Log("TELEMETRY SUMMARY\n  снимков " + sum.Samples
                      + "  длительность " + F(sum.DurationSeconds, 2) + " с"
                      + "  дистанция " + F(sum.DistanceM, 1) + " м"
                      + "\n  скорость макс " + F(sum.MaxSpeedMPerS, 2)
                      + "  средняя " + F(sum.MeanSpeedMPerS, 2) + " м/с"
                      + "\n  доля воздуха " + F(sum.AirFraction, 3)
                      + "  макс тангаж к склону " + F(sum.MaxAbsPitchRelDeg, 1) + "°"
                      + "\n  макс сжатие зад " + F(sum.MaxRearCompressionM)
                      + " перед " + F(sum.MaxFrontCompressionM) + " м"
                      + "\n  макс прижим зада " + F(sum.MaxRearLoadN, 0) + " Н"
                      + "  макс букс " + F(sum.MaxSlipRear, 2));
        }
    }
}
