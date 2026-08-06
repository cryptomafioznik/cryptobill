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
    /// Диагностика провала теста H (окно восстановления 0.53 с против критерия 1.5 с).
    ///
    /// Вопрос, на который отвечает этот тест: провал — это НАСТРОЙКА или СТРУКТУРА?
    /// Если хотя бы один помощник, поднятый до стокового значения, возвращает окно выше 1.5 с,
    /// значит момент от тяги в принципе перекрываем и вопрос в калибровке. Если ни один не
    /// возвращает — значит эмерджентный момент от тяги на плече высоты ЦТ сильнее всего, чем
    /// его пытались держать, и это структурная проблема переноса.
    ///
    /// Утверждение здесь адресное и способно провалиться в обе стороны, поэтому тест имеет
    /// смысл держать в наборе, а не выкидывать после разового прогона.
    /// </summary>
    public class RecoveryWindowDiagnosis
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

        /// <summary>Прогон с зажатым газом по дизайн-трассе; возвращает окно и обстоятельства.</summary>
        private IEnumerator Measure(LevelPhysicsOverride level, System.Action<float, float, float,
            BikeFailure> report)
        {
            var profile = ScriptableObject.CreateInstance<BikeTuningProfile>();
            var track = VerticalSliceTrack.CreateProfile();
            var sampler = new TerrainSampler(track);
            _spawned.Add(TrackBuilder.Build(track, profile.tyreFriction));
            var axle = BikeFactory.RestingRearAxle(sampler, profile, 60f * UnitsContract.PxToM);
            var ctrl = BikeFactory.Spawn(profile, level, sampler,
                ScriptedBikeInput.HoldThrottle(), axle, false);
            _spawned.Add(ctrl.gameObject);

            var onset = -1f;
            var t = 0f;
            var maxRel = 0f;

            // Кап 2700 кадров = 45 с. Опрокид, если он есть, случается на ~40.5 с;
            // прежние 4200 (70 с) тратили по полминуты на каждый вариант без опрокида,
            // и прогон семи вариантов не укладывался в лимит инструмента.
            for (var i = 0; i < 2700; i++)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                var s = ctrl.State;
                maxRel = Mathf.Max(maxRel, s.PitchRelRad);

                if (s.PitchRelRad > 0.35f) { if (onset < 0f) onset = t; }
                else if (s.IsGrounded) onset = -1f;

                if (s.Failure != BikeFailure.None)
                {
                    report(onset < 0f ? -1f : t - onset, s.PositionXM, maxRel, s.Failure);
                    TearDown();
                    yield break;
                }
                if (s.PositionXM >= VerticalSliceTrack.EndPx * UnitsContract.PxToM) break;
            }

            // Отказа не случилось — окно не ограничено.
            report(float.PositiveInfinity, ctrl.State.PositionXM, maxRel, BikeFailure.None);
            TearDown();
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Diagnose_WhichAssistOwnsTheRecoveryWindow()
        {
            var results = new List<(string name, float window, float x, float maxRelDeg, BikeFailure f)>();

            // Варианты: профиль уровня как есть, затем по одному помощнику до стока (1.0),
            // затем полный сток. Меняется РОВНО ОДНА ручка за прогон — иначе нельзя сказать,
            // какая из них отвечает за результат.
            var variants = new List<(string name, System.Func<LevelPhysicsOverride> make)>
            {
                ("профиль уровня как есть", () => ScriptableObject.CreateInstance<LevelPhysicsOverride>()),
                ("groundAlign 0.30 → 1.0", () =>
                {
                    var l = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
                    l.groundAlign = 1f; return l;
                }),
                ("wheelieGuardScale 0 → 1.0", () =>
                {
                    var l = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
                    l.wheelieGuardScale = 1f; return l;
                }),
                ("edgeGuard 0.25 → 1.0", () =>
                {
                    var l = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
                    l.edgeGuard = 1f; return l;
                }),
                ("leanTorque 1.5 → 1.0", () =>
                {
                    var l = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
                    l.leanTorque = 1f; return l;
                }),
                ("failHold 0.25 → 0.60 с", () =>
                {
                    var l = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
                    l.failHoldSeconds = 0.60f; return l;
                }),
                ("полный сток (все ручки 1.0)", LevelPhysicsOverride.CreateStock)
            };

            foreach (var v in variants)
            {
                var w = 0f; var x = 0f; var mr = 0f; var f = BikeFailure.None;
                yield return Measure(v.make(), (window, posX, maxRel, fail) =>
                {
                    w = window; x = posX; mr = maxRel; f = fail;
                });
                results.Add((v.name, w, x, mr * Mathf.Rad2Deg, f));
            }

            var report = "RECOVERY WINDOW DIAGNOSIS (дизайн-трасса, газ зажат)\n";
            report += "  критерий остановки аудита §14.1: окно ≥ 1.5 с\n";
            foreach (var r in results)
            {
                var w = float.IsPositiveInfinity(r.window) ? "опрокида не было"
                    : r.window < 0f ? "нос-вверх не пройден"
                    : F(r.window) + " с";
                report += "  " + r.name.PadRight(28) + " окно " + w.PadRight(20)
                          + " до " + F(r.x, 1) + " м, макс тангаж " + F(r.maxRelDeg, 1) + "°, "
                          + r.f + "\n";
            }
            Debug.Log(report);

            // Ответ на поставленный вопрос: нашёлся ли хоть один вариант с окном ≥ 1.5 с.
            var rescued = results.FindAll(r =>
                float.IsPositiveInfinity(r.window) || r.window >= 1.5f);

            Assert.IsNotEmpty(rescued,
                "НИ ОДИН помощник, поднятый до стока, не возвращает окно ≥ 1.5 с — значит провал "
                + "теста H структурный, а не настроечный: эмерджентный момент от тяги на плече "
                + "высоты ЦТ сильнее всего, чем его держат. Это меняет план: надо пересматривать "
                + "либо высоту ЦТ, либо способ приложения тяги, а не крутить ручки.");
        }
    }
}
