using System.Collections;
using System.Collections.Generic;
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
    /// Замер зависимости статической просадки подвески от частоты — чтобы выбрать
    /// suspensionFrequency ИЗМЕРЕНИЕМ, а не подбором наугад.
    ///
    /// Зачем отдельно: docs/BIKE_PHYSICS_SPEC.md §4.2 фиксирует, что springK/springC исходника
    /// не переводятся в ход подвески (там был penalty-контакт без хода), поэтому частота —
    /// параметр, который обязан выводиться из требования, а не переноситься. Требование:
    /// статическая просадка ≈ треть хода, как у реального кросса (ход 0.30 м → просадка ~0.10 м).
    /// </summary>
    public class SuspensionCalibrationProbe
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [UnityTest]
        public IEnumerator Probe_SagVersusFrequency()
        {
            var report = "PROBE SAG vs FREQUENCY (ход 0.30 м, цель просадки ≈0.10 м)\n";
            var pairs = new List<Vector2>();

            foreach (var freq in new[] { 3f, 5f, 8f, 12f, 16f, 22f })
            {
                var p = ScriptableObject.CreateInstance<BikeTuningProfile>();
                p.engineForceN = 4000f;
                p.tyreFriction = 1.2f;
                p.suspensionFrequency = freq;
                p.suspensionDamping = 0.8f;

                var track = ScriptableObject.CreateInstance<TrackProfile>();
                track.nodesPx = new[] { new Vector2(0, 0), new Vector2(20000, 0) };
                track.endPx = 20000f;
                track.nodeStepPx = 26f;

                var sampler = new TerrainSampler(track);
                _spawned.Add(TrackBuilder.Build(track, p.tyreFriction));

                var axle = BikeFactory.RestingRearAxle(sampler, p, 10f);
                var ctrl = BikeFactory.Spawn(p, ScriptableObject.CreateInstance<LevelPhysicsOverride>(),
                    sampler, new ScriptedBikeInput(() => BikeInputState.Neutral), axle);
                _spawned.Add(ctrl.gameObject);
                var rig = ctrl.GetComponent<BikeRig>();

                for (var i = 0; i < 300; i++) yield return new WaitForFixedUpdate();

                var sag = rig.RearWheel.position.y - rig.Chassis.position.y;
                pairs.Add(new Vector2(freq, sag));
                report += "  f = " + freq.ToString("F0").PadLeft(2) + " Гц  →  просадка "
                          + sag.ToString("F4") + " м  (jointTranslation "
                          + rig.RearJoint.jointTranslation.ToString("F4") + ")\n";

                TearDown();
            }

            Debug.Log(report);

            // Проверка, которая ЛОВИТ поломку модели: просадка обязана МОНОТОННО падать
            // с ростом частоты. Если это не так, значит частота вообще не управляет пружиной,
            // и калибровать по ней нельзя.
            for (var i = 1; i < pairs.Count; i++)
            {
                Assert.Less(pairs[i].y, pairs[i - 1].y + 1e-4f,
                    "просадка обязана падать с ростом частоты: при f=" + pairs[i - 1].x
                    + " просадка " + pairs[i - 1].y.ToString("F4")
                    + ", при f=" + pairs[i].x + " просадка " + pairs[i].y.ToString("F4"));
            }
        }
    }
}
