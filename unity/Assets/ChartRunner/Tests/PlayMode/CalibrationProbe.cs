using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    /// Калибровка трёх плейсхолдеров профиля ИЗМЕРЕНИЕМ на полигоне, а не подбором на глаз.
    ///
    /// Каждый параметр выводится из СФОРМУЛИРОВАННОГО требования, как предписывает
    /// docs/BIKE_PHYSICS_SPEC.md §6:
    ///   topSpeedMPerS  ← эталон исходника: крейсер КРОСС 250 = 6.271 м/с (уже задан)
    ///   engineForceN   ← «заезжает на 45° со стоячего старта» (§7.4)
    ///   tyreFriction   ← то же требование, но со стороны сцепления: не буксует
    ///   brakeForceN    ← «тормоз способен дойти до предела сцепления», то есть предел ставит
    ///                     резина, а не сила тормоза. Тогда путь торможения — СЛЕДСТВИЕ, а не
    ///                     подогнанное число.
    /// </summary>
    public class CalibrationProbe
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static string F(float v, int d = 2) =>
            v.ToString("F" + d, CultureInfo.InvariantCulture);

        private BikeTuningProfile Profile(float engine, float friction, float brake)
        {
            var p = ScriptableObject.CreateInstance<BikeTuningProfile>();
            p.engineForceN = engine;
            p.tyreFriction = friction;
            p.brakeForceN = brake;
            return p;
        }

        private BikeController Spawn(LabTerrains.Pad pad, BikeTuningProfile p,
            IBikeInputSource input, out TerrainSampler sampler, out float startXM)
        {
            var track = LabTerrains.CreateProfile(pad);
            sampler = new TerrainSampler(track);
            _spawned.Add(TrackBuilder.Build(track, p.tyreFriction));
            startXM = LabTerrains.StartXPx(pad) * UnitsContract.PxToM;
            var axle = BikeFactory.RestingRearAxle(sampler, p, startXM);
            var ctrl = BikeFactory.Spawn(p, LevelPhysicsOverride.CreateStock(), sampler, input, axle);
            _spawned.Add(ctrl.gameObject);
            return ctrl;
        }

        // ============================================================================

        [UnityTest]
        public IEnumerator Probe_CruiseMatchesSourceReference()
        {
            // Эталон §7.2/§7.9: КРОСС 250 на ровном под полным газом идёт 6.271 м/с.
            var p = Profile(4000f, 1.2f, 3000f);
            var ctrl = Spawn(LabTerrains.Pad.Flat, p, ScriptedBikeInput.HoldThrottle(),
                out _, out _);

            var samples = new List<float>();
            for (var i = 0; i < 600; i++)
            {
                yield return new WaitForFixedUpdate();
                if (i > 300) samples.Add(ctrl.State.SpeedMPerS);
            }

            var mean = 0f;
            foreach (var s in samples) mean += s;
            mean /= samples.Count;

            Debug.Log("PROBE CRUISE\n  установившийся крейсер = " + F(mean, 3) + " м/с ("
                      + F(mean * 3.6f, 1) + " км/ч)\n  эталон исходника (КРОСС 250) = 6.271 м/с (22.6 км/ч)\n"
                      + "  отклонение = " + F((mean - 6.271f) / 6.271f * 100f, 1) + " %");

            Assert.That(mean, Is.EqualTo(6.271f).Within(6.271f * 0.10f),
                "крейсер " + F(mean, 3) + " м/с против эталона 6.271 м/с");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Probe_EngineAndFrictionGridOn45DegreeClimb()
        {
            // Требование §7.4: байк заезжает на 45° со стоячего старта, удерживая газ.
            // Сетка по двум параметрам сразу, потому что они взаимозависимы: момента может
            // хватать, а сцепления нет, и наоборот.
            var report = "PROBE 45° CLIMB GRID (площадка SteepClimb, уклон 45.0° по сетке)\n";
            report += "  engineN × friction → доехал до вершины / макс продвижение / мин скорость\n";

            foreach (var engine in new[] { 3000f, 4000f, 6000f })
            {
                foreach (var friction in new[] { 1.0f, 1.2f })
                {
                    var p = Profile(engine, friction, 3000f);
                    var ctrl = Spawn(LabTerrains.Pad.SteepClimb, p, ScriptedBikeInput.HoldThrottle(),
                        out var sampler, out var startXM);

                    var topXM = 2250f * UnitsContract.PxToM; // конец подъёма
                    var maxX = startXM;
                    var minSpeedOnClimb = 999f;
                    var reached = false;

                    for (var i = 0; i < 900; i++) // 15 с
                    {
                        yield return new WaitForFixedUpdate();
                        var s = ctrl.State;
                        if (s.PositionXM > maxX) maxX = s.PositionXM;
                        if (s.PositionXM > 1500f * UnitsContract.PxToM && s.IsGrounded)
                            minSpeedOnClimb = Mathf.Min(minSpeedOnClimb, s.SpeedMPerS);
                        if (s.PositionXM >= topXM) { reached = true; break; }
                        if (s.Failure != BikeFailure.None) break;
                    }

                    report += "  " + F(engine, 0).PadLeft(5) + " Н × " + F(friction, 1)
                              + "  →  " + (reached ? "ДА " : "нет")
                              + "  x=" + F(maxX, 1).PadLeft(6) + " м из " + F(topXM, 1) + " м"
                              + "  vmin=" + (minSpeedOnClimb > 900f ? "—" : F(minSpeedOnClimb, 2))
                              + "  отказ=" + ctrl.State.Failure + "\n";

                    TearDown();
                }
            }

            Debug.Log(report);

            // Отчёт — это отчёт, но тест обязан что-то УТВЕРЖДАТЬ, иначе он не может упасть.
            // Утверждаем вывод калибровки: выбранная пара (4000 Н, µ 1.2) заезжает на 45°
            // и НИ РАЗУ не съезжает назад. Если физика изменится так, что этого больше не
            // происходит, тест упадёт.
            {
                var p = Profile(4000f, 1.2f, 3000f);
                var ctrl = Spawn(LabTerrains.Pad.SteepClimb, p, ScriptedBikeInput.HoldThrottle(),
                    out _, out var startXM);
                var topXM = 2250f * UnitsContract.PxToM;
                var reached = false;
                var minSpeed = 999f;
                for (var i = 0; i < 900; i++)
                {
                    yield return new WaitForFixedUpdate();
                    var s = ctrl.State;
                    if (s.PositionXM > 1500f * UnitsContract.PxToM && s.IsGrounded)
                        minSpeed = Mathf.Min(minSpeed, s.SpeedMPerS);
                    if (s.PositionXM >= topXM) { reached = true; break; }
                }
                Assert.IsTrue(reached,
                    "калиброванная пара (4000 Н, µ 1.2) обязана заехать на 45° до x=" + F(topXM, 1) + " м");
                Assert.Greater(minSpeed, 0f,
                    "и ни разу не съехать назад: минимальная скорость на подъёме " + F(minSpeed, 2) + " м/с");
            }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Probe_BrakeReachesFrictionLimit()
        {
            // Требование: тормоз обязан дойти до предела сцепления, тогда путь торможения —
            // следствие резины, а не силы тормоза. Признак достижения предела: дальнейший
            // рост brakeForceN уже НЕ сокращает путь.
            var report = "PROBE BRAKE (разгон на ровном до крейсера, затем полный тормоз)\n";
            var trace = "    трасса торможения при 6000 Н:\n";
            var distances = new List<Vector2>();

            foreach (var brake in new[] { 500f, 1500f, 3000f, 6000f, 12000f })
            {
                var p = Profile(4000f, 1.2f, brake);
                var braking = false;
                var ctrl = Spawn(LabTerrains.Pad.Flat, p,
                    new ScriptedBikeInput(() => braking
                        ? new BikeInputState { Brake = 1f }
                        : new BikeInputState { Throttle = 1f }),
                    out _, out _);

                for (var i = 0; i < 300; i++) yield return new WaitForFixedUpdate();
                var v0 = ctrl.State.SpeedMPerS;
                var x0 = ctrl.State.PositionXM;

                braking = true;
                var stopped = false;
                var rig = ctrl.GetComponent<BikeRig>();
                var minWheelSpin = 9999f;   // блокируется ли колесо вообще
                var maxSlip = 0f;
                for (var i = 0; i < 600; i++)
                {
                    yield return new WaitForFixedUpdate();
                    minWheelSpin = Mathf.Min(minWheelSpin, Mathf.Abs(rig.RearWheel.angularVelocity));
                    maxSlip = Mathf.Max(maxSlip, ctrl.State.RearSlip);
                    if (Mathf.Approximately(brake, 6000f) && i % 20 == 0)
                        trace += "      t=" + F(i / 60f, 2) + "  v=" + F(ctrl.State.SpeedMPerS, 2)
                               + "  контактов=" + ctrl.State.GroundedWheelCount
                               + "  зад=" + F(ctrl.State.RearNormalLoadN, 0)
                               + "  перед=" + F(ctrl.State.FrontNormalLoadN, 0)
                               + "  тангаж=" + F(ctrl.State.PitchRelRad * Mathf.Rad2Deg, 1) + "°\n";
                    if (Mathf.Abs(ctrl.State.SpeedMPerS) < 0.1f) { stopped = true; break; }
                }

                var dist = ctrl.State.PositionXM - x0;
                distances.Add(new Vector2(brake, dist));
                // Диагностика: если колесо НЕ блокируется, значит момент тормоза не доходит,
                // и «предел сцепления» на самом деле не достигнут, что бы ни говорил путь.
                report += "  brake = " + F(brake, 0).PadLeft(6) + " Н  →  путь "
                          + F(dist, 3) + " м с " + F(v0, 2) + " м/с"
                          + (stopped ? "" : "  (НЕ ОСТАНОВИЛСЯ)")
                          + "  замедление " + F(v0 * v0 / (2f * Mathf.Max(0.001f, dist)), 1) + " м/с²"
                          + "  мин|ω| колеса " + F(minWheelSpin, 1) + "°/с"
                          + "  макс.букс " + F(maxSlip, 2) + "\n";

                TearDown();
            }

            report += trace;
            report += "  предел сцепления по теории: µ·g = 1.2 × 26.46 = "
                      + F(1.2f * 26.46f, 1) + " м/с²\n";
            Debug.Log(report);

            // Проверка, способная упасть: путь обязан ПЕРЕСТАТЬ сокращаться при росте силы
            // тормоза. Если он сокращается и на 12000 Н, значит предел ставит тормоз, а не
            // резина, и требование не выполнено.
            var last = distances[distances.Count - 1].y;
            var prev = distances[distances.Count - 2].y;
            Assert.That(last, Is.EqualTo(prev).Within(Mathf.Max(0.05f, prev * 0.15f)),
                "при 6000 Н путь " + F(prev, 3) + " м, при 12000 Н " + F(last, 3)
                + " м — предел должен ставить сцепление, а не тормоз");
        }
    }
}
