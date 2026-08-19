using System.Collections;
using System.Globalization;
using ChartRunner.Bike;
using ChartRunner.Game;
using ChartRunner.Input;
using ChartRunner.Track;
using ChartRunner.Tuning;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChartRunner.Tests
{
    /// <summary>
    /// ГЕЙТ ВОЛНЫ ЛИКВИДАЦИИ. Волна — это ставка заезда, и у неё есть ровно два способа
    /// быть сломанной, оба тихих:
    ///   1. Слишком медленная — стоящего игрока не наказывает, ставки нет.
    ///   2. Слишком быстрая — едущего на полном газу догоняет, и игра нечестна.
    /// Оба проверяются здесь, и оба утверждения способны провалиться.
    ///
    /// Третий тест — негативный контроль: если обнулить скорость волны, первый тест
    /// обязан перестать проходить. Без него «волна догоняет» может оказаться правдой
    /// по любой другой причине (например, байк сам упал).
    /// </summary>
    public class WaveGate
    {
        private const float Dt = 1f / 60f;

        private static Camera MakeCamera()
        {
            var go = new GameObject("TestCam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            // Тот же кадр, что на устройстве: стартовый отрыв волны считается от ширины кадра.
            cam.orthographicSize = 7.4f;
            cam.aspect = 430f / 932f;
            return cam;
        }

        private struct Rig
        {
            public BikeController Bike;
            public LiquidationWave Wave;
            public GameObject Ground;
            public Camera Cam;
        }

        private static Rig Spawn(BikeInputState cmd)
        {
            var profile = ScriptableObject.CreateInstance<BikeTuningProfile>();
            var level = LevelPhysicsOverride.CreateStock();
            var track = LabTerrains.CreateProfile(LabTerrains.Pad.Flat);
            var sampler = new TerrainSampler(track);
            Physics2D.gravity = new Vector2(0f, UnitsContract.GravityMPerS2);

            var ground = TrackBuilder.Build(track, profile.tyreFriction);
            var input = new ScriptedBikeInput(() => cmd);
            var startXM = LabTerrains.StartXPx(LabTerrains.Pad.Flat) * UnitsContract.PxToM;
            var axle = BikeFactory.RestingRearAxle(sampler, profile, startXM);
            var bike = BikeFactory.Spawn(profile, level, sampler, input, axle, false);

            var cam = MakeCamera();
            var wave = LiquidationWave.Attach(bike, cam, null);
            return new Rig { Bike = bike, Wave = wave, Ground = ground, Cam = cam };
        }

        private static void Cleanup(Rig r)
        {
            Object.Destroy(r.Bike.gameObject);
            Object.Destroy(r.Wave.gameObject);
            Object.Destroy(r.Ground);
            Object.Destroy(r.Cam.gameObject);
        }

        // ---- 1. Стоящего волна догоняет ----

        [UnityTest, Timeout(120000)]
        public IEnumerator StandingRiderIsLiquidated()
        {
            var rig = Spawn(BikeInputState.Neutral);
            var caught = -1f;

            for (var i = 0; i < Mathf.RoundToInt(25f / Dt); i++)
            {
                yield return new WaitForFixedUpdate();
                if (rig.Bike.State.Failure == BikeFailure.Liquidated)
                {
                    caught = i * Dt;
                    break;
                }
            }

            Debug.Log("ВОЛНА: стоящего догнала за "
                      + caught.ToString("0.0", CultureInfo.InvariantCulture) + " с");
            Cleanup(rig);

            Assert.Greater(caught, 0f, "волна не догнала стоящего игрока за 25 с — ставки нет");
            Assert.Less(caught, 20f, "волна догоняет слишком долго: давление не читается");
        }

        // ---- 2. Едущего на полном газу волна НЕ догоняет ----

        [UnityTest, Timeout(180000)]
        public IEnumerator FullThrottleRiderSurvivesOpening()
        {
            var rig = Spawn(new BikeInputState { Throttle = 1f });
            var minLead = float.MaxValue;
            var liquidated = false;

            for (var i = 0; i < Mathf.RoundToInt(60f / Dt); i++)
            {
                yield return new WaitForFixedUpdate();
                if (rig.Bike.Halted)
                {
                    liquidated = rig.Bike.State.Failure == BikeFailure.Liquidated;
                    break;
                }
                minLead = Mathf.Min(minLead, rig.Wave.LeadM);
            }

            Debug.Log("ВОЛНА: на полном газу минимальный отрыв "
                      + minLead.ToString("0.0", CultureInfo.InvariantCulture) + " м");
            Cleanup(rig);

            Assert.IsFalse(liquidated,
                "волна догнала игрока, который ехал на полном газу по ровному — нечестно");
            Assert.Greater(minLead, 0.5f, "отрыв схлопнулся до нуля: волна слишком быстрая");
        }

        // ---- 3. Негативный контроль ----

        [UnityTest, Timeout(120000)]
        public IEnumerator NegativeControl_FrozenWaveDoesNotKill()
        {
            var rig = Spawn(BikeInputState.Neutral);
            // Замораживаем волну: единственное отличие от теста 1.
            rig.Wave.enabled = false;

            var liquidated = false;
            for (var i = 0; i < Mathf.RoundToInt(25f / Dt); i++)
            {
                yield return new WaitForFixedUpdate();
                if (rig.Bike.State.Failure == BikeFailure.Liquidated) { liquidated = true; break; }
            }
            Cleanup(rig);

            Assert.IsFalse(liquidated,
                "замороженная волна всё равно убила — значит тест 1 проходил не из-за волны");
        }
    }
}
