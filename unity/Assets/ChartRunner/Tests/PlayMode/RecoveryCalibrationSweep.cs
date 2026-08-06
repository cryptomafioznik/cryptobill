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
    /// Калибровка помощников под ВАРИАНТ A: тяга остаётся в пятне контакта (физически верно),
    /// помощники подбираются под получившееся плечо.
    ///
    /// ЧТО ЗДЕСЬ ГЛАВНОЕ — критерий, а не перебор. Окно восстановления НЕЛЬЗЯ оптимизировать
    /// в одиночку: любое окно удлиняется до бесконечности, если сделать байк неопрокидываемым,
    /// и тогда «газ зажат» проходит трассу целиком, а игры нет. Поэтому каждая настройка
    /// оценивается ТРЕМЯ условиями сразу:
    ///
    ///   1. ОКНО ≥ 1.0 с — у игрока физически есть время на коррекцию. Порог выведен, а не
    ///      назначен: рампа переноса веса 0.55 с до полного плюс ~0.25 с на восприятие =
    ///      0.80 с; ниже этого приложить вес просто не успеваешь, 1.0 с — минимальный запас.
    ///   2. NEUTRAL (газ зажат, вес ≡ 0) ОБЯЗАН провалиться до финиша. Если проходит —
    ///      настройка убила игру, сколько бы ни было окно.
    ///   3. ВЫИГРЫШ ВЕСА ≥ 15 %. Окно, которым нельзя воспользоваться, — не окно.
    ///
    /// Условие 2 — это и есть защита от лёгкого пути. Прошлой ночью провалились ДВА теста
    /// (H: окно 0.52 с, G: выигрыш 0 %), и гипотеза была, что это один провал: если окна нет,
    /// навыку негде проявиться. Здесь оба меряются на одной настройке, поэтому гипотеза
    /// проверяется, а не предполагается.
    /// </summary>
    public class RecoveryCalibrationSweep
    {
        private const float WindowTargetSeconds = 1.0f;
        private const float GainTargetPercent = 15f;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private float _timeScaleBackup;
        private float _maxDeltaBackup;

        private static string F(float v, int d = 2) =>
            v.ToString("F" + d, CultureInfo.InvariantCulture);

        [SetUp]
        public void SetUp()
        {
            // Разгон прогона. fixedDeltaTime НЕ трогаем — шаг физики остаётся 1/60, поэтому
            // результат побитово тот же, просто шагов на кадр помещается больше. Менять
            // fixedDeltaTime было бы подменой измеряемого объекта.
            _timeScaleBackup = Time.timeScale;
            _maxDeltaBackup = Time.maximumDeltaTime;
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
            Time.timeScale = _timeScaleBackup <= 0f ? 1f : _timeScaleBackup;
            Time.maximumDeltaTime = _maxDeltaBackup <= 0f ? 0.3333333f : _maxDeltaBackup;
        }

        // ================= настройка-кандидат =================

        private struct Candidate
        {
            public string Name;
            public float GroundAlign;
            public float WheelieGuard;
            public float EdgeGuard;
            public float FailHold;
            public float DesignHard;

            /// <summary>
            /// Момент инерции шасси, кг·м². Это НЕ помощник, а физика, и он здесь потому,
            /// что он — единственная величина, действующая ровно на задир носа от газа.
            ///
            /// Вывод из кода, а не из общих соображений: все помощники и весь авторитет игрока
            /// прикладываются как `acc * I` (BikeController.ApplyGroundAssists, ApplyWeightShift),
            /// то есть их УГЛОВОЕ УСКОРЕНИЕ от инерции не зависит вовсе. А момент от тяги —
            /// это сила в пятне контакта на плече высоты ЦТ, и его угловое ускорение обратно
            /// пропорционально I. Поэтому изменение I двигает ровно одно: как быстро газ
            /// задирает нос. Ни прощение у грани, ни сила игрока не меняются.
            ///
            /// База 33.27 = RB.inertia(230) исходника. Комментарий UnitsContract §ChassisInertia
            /// сам отмечает, что реальный момент ~54, то есть байк крутится по тангажу в 1.62
            /// раза легче настоящего — это была намеренная «живость» вилли ПРИ ПЛЕЧЕ 0.438 м.
            /// При плече 0.777 м та же живость складывается с моментом в 1.77 раза большим.
            /// 59.0 = 33.27 × 1.774 — значение, при котором угловое ускорение от тяги в точности
            /// совпадает с исходником, то есть честный вариант A без единой правки помощников.
            /// </summary>
            public float InertiaKgM2;

            public LevelPhysicsOverride Level()
            {
                var l = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
                l.groundAlign = GroundAlign;
                l.wheelieGuardScale = WheelieGuard;
                l.edgeGuard = EdgeGuard;
                l.failHoldSeconds = FailHold;
                return l;
            }

            public BikeTuningProfile Profile()
            {
                var p = ScriptableObject.CreateInstance<BikeTuningProfile>();
                p.designHard = DesignHard;
                p.chassisInertiaKgM2 = InertiaKgM2;
                return p;
            }
        }

        /// <summary>Настройка «как сейчас» — базовая точка отсчёта, из ассета уровня.</summary>
        private static Candidate Baseline(string name = "база (как сейчас)")
        {
            return new Candidate
            {
                Name = name,
                GroundAlign = 0.30f,
                WheelieGuard = 0f,
                EdgeGuard = 0.25f,
                FailHold = 0.25f,
                DesignHard = 0.6f,
                InertiaKgM2 = UnitsContract.ChassisInertiaKgM2
            };
        }

        // ================= измерение =================

        private struct Run
        {
            public float DistanceM;
            public float WindowSeconds;
            public BikeFailure Failure;
            public float MaxRelDeg;
            public float Seconds;
        }

        /// <summary>
        /// Политика WEIGHT — та же, что в тесте G, скопирована сознательно и без изменений:
        /// если менять политику вместе с калибровкой, нельзя будет сказать, что именно
        /// подействовало.
        /// </summary>
        private static IBikeInputSource WeightPolicy(System.Func<BikeState> read)
        {
            return new ScriptedBikeInput(() =>
            {
                var s = read();
                var rel = s.PitchRelRad;
                var lean = 0f;
                if (rel > 0.18f) lean = 1f;
                else if (rel < -0.18f) lean = -1f;
                return new BikeInputState { Throttle = 1f, Lean = lean };
            });
        }

        private IEnumerator Measure(Candidate c, bool weightPolicy, System.Action<Run> report)
        {
            var profile = c.Profile();
            var level = c.Level();
            var track = VerticalSliceTrack.CreateProfile();
            var sampler = new TerrainSampler(track);
            _spawned.Add(TrackBuilder.Build(track, profile.tyreFriction));

            BikeController ctrl = null;
            var input = weightPolicy
                ? WeightPolicy(() => ctrl.State)
                : (IBikeInputSource)ScriptedBikeInput.HoldThrottle();

            var axle = BikeFactory.RestingRearAxle(sampler, profile, 60f * UnitsContract.PxToM);
            ctrl = BikeFactory.Spawn(profile, level, sampler, input, axle, false);
            _spawned.Add(ctrl.gameObject);

            var endXM = VerticalSliceTrack.EndPx * UnitsContract.PxToM;
            var onset = -1f;
            var t = 0f;
            var maxX = 0f;
            var maxRel = 0f;
            var window = float.PositiveInfinity;
            var failure = BikeFailure.None;

            for (var i = 0; i < 4200; i++)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                var s = ctrl.State;
                maxX = Mathf.Max(maxX, s.PositionXM);
                maxRel = Mathf.Max(maxRel, s.PitchRelRad);

                // Отсчёт окна — ровно как в тесте H: от 0.35 рад (20°) к поверхности,
                // со сбросом, пока байк выправляется сам.
                if (s.PitchRelRad > 0.35f) { if (onset < 0f) onset = t; }
                else if (s.IsGrounded) onset = -1f;

                if (s.Failure != BikeFailure.None)
                {
                    failure = s.Failure;
                    window = onset < 0f ? -1f : t - onset;
                    break;
                }
                if (s.PositionXM >= endXM) break;
            }

            report(new Run
            {
                DistanceM = maxX,
                WindowSeconds = window,
                Failure = failure,
                MaxRelDeg = maxRel * Mathf.Rad2Deg,
                Seconds = t
            });
            TearDown();
        }

        private struct Verdict
        {
            public Candidate C;
            public Run Neutral;
            public Run Weight;
            public float GainPercent;
            public bool WindowOk;
            public bool NeutralStillFails;
            public bool SkillPays;
            public bool AllThree => WindowOk && NeutralStillFails && SkillPays;
        }

        private IEnumerator Evaluate(Candidate c, System.Action<Verdict> report)
        {
            Run n = default, w = default;
            yield return Measure(c, false, r => n = r);
            yield return Measure(c, true, r => w = r);

            var endM = VerticalSliceTrack.EndPx * UnitsContract.PxToM;
            var gain = (w.DistanceM - n.DistanceM) / Mathf.Max(0.01f, n.DistanceM) * 100f;

            report(new Verdict
            {
                C = c,
                Neutral = n,
                Weight = w,
                GainPercent = gain,
                // Окно берём с прогона NEUTRAL: именно он повторяет условия теста H.
                WindowOk = float.IsPositiveInfinity(n.WindowSeconds) || n.WindowSeconds >= WindowTargetSeconds,
                NeutralStillFails = n.Failure != BikeFailure.None && n.DistanceM < endM * 0.95f,
                SkillPays = gain >= GainTargetPercent
            });
        }

        // ================= развёртка =================

        [UnityTest, Timeout(3000000)]
        public IEnumerator Sweep_FindCalibrationThatGivesWindowAndKeepsTheGame()
        {
            var log = new List<string>();
            var all = new List<Verdict>();

            void Record(Verdict v)
            {
                all.Add(v);
                var endM = VerticalSliceTrack.EndPx * UnitsContract.PxToM;
                log.Add("| " + v.C.Name.PadRight(30)
                        + " | " + (float.IsPositiveInfinity(v.Neutral.WindowSeconds) ? "нет опрокида"
                            : v.Neutral.WindowSeconds < 0f ? "нос-вверх не пройден"
                            : F(v.Neutral.WindowSeconds) + " с")
                        + " | " + F(100f * v.Neutral.DistanceM / endM, 1) + " % / " + v.Neutral.Failure
                        + " | " + F(100f * v.Weight.DistanceM / endM, 1) + " % / " + v.Weight.Failure
                        + " | " + F(v.GainPercent, 1) + " %"
                        + " | " + (v.WindowOk ? "О" : "-") + (v.NeutralStillFails ? "N" : "-")
                        + (v.SkillPays ? "S" : "-") + " |");
            }

            // --- ЭТАП 0: база. Без неё сравнивать не с чем. ---
            yield return Evaluate(Baseline(), Record);

            // --- ЭТАП 1: по одной ручке за раз, от базы. Что именно двигает окно? ---
            var stage1 = new List<Candidate>();
            foreach (var g in new[] { 0.45f, 0.60f, 0.80f })
            {
                var c = Baseline("groundAlign " + F(g, 2)); c.GroundAlign = g; stage1.Add(c);
            }
            foreach (var g in new[] { 0.25f, 0.50f })
            {
                var c = Baseline("wheelieGuard " + F(g, 2)); c.WheelieGuard = g; stage1.Add(c);
            }
            foreach (var g in new[] { 0.50f, 1.00f })
            {
                var c = Baseline("edgeGuard " + F(g, 2)); c.EdgeGuard = g; stage1.Add(c);
            }
            foreach (var g in new[] { 0.40f, 0.60f })
            {
                var c = Baseline("failHold " + F(g, 2) + " с"); c.FailHold = g; stage1.Add(c);
            }
            foreach (var g in new[] { 0.45f, 0.30f })
            {
                var c = Baseline("designHard " + F(g, 2)); c.DesignHard = g; stage1.Add(c);
            }

            // Инерция — не помощник, а физика. Стоит отдельно и последней в этапе, чтобы её
            // результат читался на фоне всех ручек: если она одна делает то, чего не делают
            // они все вместе, это и есть ответ на вопрос «настройка или структура».
            var baseI = UnitsContract.ChassisInertiaKgM2;
            foreach (var k in new[] { 1.30f, 1.62f, 1.774f })
            {
                var c = Baseline("инерция ×" + F(k, 2) + " = " + F(baseI * k, 1));
                c.InertiaKgM2 = baseI * k;
                stage1.Add(c);
            }

            foreach (var c in stage1) yield return Evaluate(c, Record);

            // --- ЭТАП 2: комбинация двух лучших одиночных ручек по ВЫИГРЫШУ, а не по окну.
            // Выбор по окну привёл бы прямиком к «сделать неопрокидываемым».
            var byGain = all.Skip(1).Where(v => v.NeutralStillFails)
                .OrderByDescending(v => v.GainPercent).Take(2).ToList();
            if (byGain.Count == 2)
            {
                var a = byGain[0].C;
                var b = byGain[1].C;
                var combo = Baseline(Short(a) + " + " + Short(b));
                combo.GroundAlign = Pick(a.GroundAlign, b.GroundAlign, Baseline().GroundAlign);
                combo.WheelieGuard = Pick(a.WheelieGuard, b.WheelieGuard, Baseline().WheelieGuard);
                combo.EdgeGuard = Pick(a.EdgeGuard, b.EdgeGuard, Baseline().EdgeGuard);
                combo.FailHold = Pick(a.FailHold, b.FailHold, Baseline().FailHold);
                combo.DesignHard = Pick(a.DesignHard, b.DesignHard, Baseline().DesignHard);
                combo.InertiaKgM2 = Pick(a.InertiaKgM2, b.InertiaKgM2, Baseline().InertiaKgM2);
                yield return Evaluate(combo, Record);
            }

            Write(log, all);

            var winners = all.Where(v => v.AllThree).ToList();
            Assert.IsNotEmpty(winners,
                "НИ ОДНА настройка не даёт всех трёх условий сразу (окно ≥ " + WindowTargetSeconds
                + " с, NEUTRAL всё ещё проваливается, выигрыш веса ≥ " + GainTargetPercent + " %). "
                + "Это значит, что вопрос не в калибровке помощников: подробности в таблице отчёта.");
        }

        private static string Short(Candidate c) => c.Name.Split(' ')[0] + " " + c.Name.Split(' ')[1];

        /// <summary>Из двух кандидатов берём то значение, которое отличается от базы.</summary>
        private static float Pick(float a, float b, float baseline)
        {
            if (!Mathf.Approximately(a, baseline)) return a;
            if (!Mathf.Approximately(b, baseline)) return b;
            return baseline;
        }

        private void Write(List<string> rows, List<Verdict> all)
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                "docs", "recovery-calibration-sweep.md"));

            var head = new List<string>
            {
                "# КАЛИБРОВКА ОКНА ВОССТАНОВЛЕНИЯ (вариант A)",
                "",
                "Сгенерирован автоматически классом `RecoveryCalibrationSweep`. Править руками",
                "бессмысленно — перезапишется следующим прогоном.",
                "",
                "Вариант A принят пользователем: тяга остаётся в ПЯТНЕ КОНТАКТА (плечо 0.777 м —",
                "физически верно), помощники калибруются под это плечо. Момент от тяги при этом",
                "в 1.77 раза больше, чем прикладывал исходник (плечо ступицы 0.438 м).",
                "",
                "Оцениваются ТРИ условия сразу, потому что окно в одиночку оптимизируется",
                "тривиально и неверно — достаточно сделать байк неопрокидываемым:",
                "",
                "- **О** — окно ≥ " + F(WindowTargetSeconds, 1) + " с (рампа веса 0.55 с + восприятие 0.25 с = 0.80 с, плюс запас)",
                "- **N** — NEUTRAL (газ зажат) всё ещё проваливается до финиша, то есть игра осталась",
                "- **S** — выигрыш веса ≥ " + F(GainTargetPercent, 0) + " %, то есть окном можно воспользоваться",
                "",
                "| настройка | окно (NEUTRAL) | NEUTRAL | WEIGHT | выигрыш | ОNS |",
                "|---|---|---|---|---|---|"
            };

            var tail = new List<string> { "" };
            var winners = all.Where(v => v.AllThree).ToList();
            if (winners.Count > 0)
            {
                var best = winners.OrderByDescending(v => v.GainPercent).First();
                tail.Add("## Итог: настройка найдена");
                tail.Add("");
                tail.Add("Лучшая по выигрышу веса: **" + best.C.Name + "** — окно "
                         + F(best.Neutral.WindowSeconds) + " с, выигрыш " + F(best.GainPercent, 1) + " %.");
            }
            else
            {
                tail.Add("## Итог: настройки, дающей все три условия, НЕ НАЙДЕНО");
                tail.Add("");
                tail.Add("Гипотеза «два провала — один провал» этой развёрткой НЕ подтверждена:");
                tail.Add("окно можно удлинить, но выигрыш веса за ним не приходит. Значит вопрос");
                tail.Add("не в калибровке помощников, а в том, есть ли на трассе места, где вес");
                tail.Add("вообще что-то решает — то есть в дизайне уровня.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, head.Concat(rows).Concat(tail));
            Debug.Log("SWEEP REPORT записан: " + path + "\n"
                      + string.Join("\n", head.Concat(rows).Concat(tail)));
        }
    }
}
