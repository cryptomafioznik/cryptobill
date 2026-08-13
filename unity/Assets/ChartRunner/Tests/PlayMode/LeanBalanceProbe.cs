using System.Collections;
using System.Globalization;
using System.Text;
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
    /// ЗАМЕР СИММЕТРИИ УПРАВЛЕНИЯ НОСОМ. Живой вердикт: «когда подымаешь нос вперёд-назад,
    /// будто бы несбалансировано это сделано». Проверяю это числом, а не на глаз.
    ///
    /// Метод: с одинакового установившегося хода по ровному даю ЧИСТЫЙ наклон назад
    /// (нос вверх) и чистый наклон вперёд (нос вниз), одинаковой длительности и величины,
    /// и меряю набранный угол тангажа. Если модуль отклика по двум сторонам различается
    /// заметно — управление и вправду несимметрично, и видно, НАСКОЛЬКО.
    ///
    /// Тест диагностический: он печатает числа и не имеет права провалиться на пороге,
    /// который никто не назначал. Порог назначит пользователь, увидев числа.
    /// </summary>
    public class LeanBalanceProbe
    {
        private const float Dt = 1f / 60f;

        [UnityTest]
        public IEnumerator LeanSymmetry()
        {
            var report = new StringBuilder();
            report.AppendLine("=== СИММЕТРИЯ НАКЛОНА: нос вверх против носа вниз ===");
            report.AppendLine("Метод: разгон 2.0 с на ровном, затем 0.8 с чистого наклона.");
            report.AppendLine();
            report.AppendLine("| фил | нос ВВЕРХ, ° | нос ВНИЗ, ° | перекос |");
            report.AppendLine("|---|---|---|---|");

            foreach (Game.Feel feel in System.Enum.GetValues(typeof(Game.Feel)))
            {
                var up = 0f;
                var down = 0f;
                yield return Run(feel, -1f, r => up = r);
                yield return Run(feel, +1f, r => down = r);

                var a = Mathf.Abs(up);
                var b = Mathf.Abs(down);
                var ratio = b > 0.01f ? a / b : 0f;
                report.AppendLine("| " + Game.FeelPreset.Name(feel)
                                       + " | " + up.ToString("0.0", CultureInfo.InvariantCulture)
                                       + " | " + down.ToString("0.0", CultureInfo.InvariantCulture)
                                       + " | ×" + ratio.ToString("0.00", CultureInfo.InvariantCulture)
                                       + " |");
            }

            report.AppendLine();
            report.AppendLine("Перекос = |вверх| / |вниз|. 1.00 — симметрично.");
            Debug.Log(report.ToString());
        }

        private IEnumerator Run(Game.Feel feel, float lean, System.Action<float> result)
        {
            var profile = ScriptableObject.CreateInstance<BikeTuningProfile>();
            var level = LevelPhysicsOverride.CreateStock();
            Game.FeelPreset.Apply(level, feel);

            var track = LabTerrains.CreateProfile(LabTerrains.Pad.Flat);
            var sampler = new TerrainSampler(track);
            Physics2D.gravity = new Vector2(0f, UnitsContract.GravityMPerS2);

            var ground = TrackBuilder.Build(track, profile.tyreFriction);
            var cmd = new BikeInputState();
            var input = new ScriptedBikeInput(() => cmd);
            var startXM = LabTerrains.StartXPx(LabTerrains.Pad.Flat) * UnitsContract.PxToM;
            var rear = BikeFactory.RestingRearAxle(sampler, profile, startXM);
            var bike = BikeFactory.Spawn(profile, level, sampler, input, rear, false);

            // Разгон: только газ, вес ровно ноль.
            cmd = new BikeInputState { Throttle = 1f };
            for (var i = 0; i < Mathf.RoundToInt(2.0f / Dt); i++) yield return new WaitForFixedUpdate();

            var basePitch = bike.State.PitchRelRad * Mathf.Rad2Deg;

            // Чистый наклон при том же газе — сравниваем прирост угла.
            cmd = new BikeInputState { Throttle = 1f, Lean = lean };
            var peak = 0f;
            for (var i = 0; i < Mathf.RoundToInt(0.8f / Dt); i++)
            {
                yield return new WaitForFixedUpdate();
                var d = bike.State.PitchRelRad * Mathf.Rad2Deg - basePitch;
                if (Mathf.Abs(d) > Mathf.Abs(peak)) peak = d;
            }

            result(peak);
            Object.Destroy(bike.gameObject);
            Object.Destroy(ground);
            yield return null;
        }
    }
}
