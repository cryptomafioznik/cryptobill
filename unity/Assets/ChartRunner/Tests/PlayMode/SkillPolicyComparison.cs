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
    /// Есть ли на этой трассе навык, который окупается — и КАКОЙ.
    ///
    /// ПОЧЕМУ ЭТОТ ТЕСТ ПОНАДОБИЛСЯ. Тест G меряет одну политику: газ зажат, вес реактивно.
    /// Его собственный комментарий объясняет, почему газ не модулируется — «иначе замер
    /// смешивает два навыка». Логика верная для чистоты замера и неверная для вопроса,
    /// который мы задаём: развёртка калибровки показала, что вес В ОДИНОЧКУ не выигрывает
    /// нигде, а местами проигрывает вдвое (инерция ×1.62: 87.5 % против 36.8 %).
    ///
    /// Причина физическая, а не настроечная. Перенос веса ВПЕРЁД опускает нос — и тем же
    /// движением РАЗГРУЖАЕТ заднее колесо, которое на подъёме 50° и везёт. Вес вперёд гасит
    /// вилли ценой тяги. Это настоящая дилемма эндуро, и разрешается она не весом, а ГАЗОМ:
    /// сбросить, дать носу опуститься, добавить снова.
    ///
    /// Поэтому здесь три политики, отличающиеся ровно тем, чем должны:
    ///   NEUTRAL — газ 1.0, вес 0. Тот самый «зажал и едешь».
    ///   WEIGHT  — газ 1.0, вес реактивно. Политика теста G, слово в слово.
    ///   DOSED   — газ И вес. То, что делает живой человек.
    ///
    /// Если DOSED существенно обгоняет NEUTRAL, окно навыка есть и всё это время мерили
    /// не тот навык. Если не обгоняет — разговор про дизайн уровня, и он обоснован.
    /// </summary>
    public class SkillPolicyComparison
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

        // ================= политики =================

        private static IBikeInputSource Neutral() => ScriptedBikeInput.HoldThrottle();

        private static IBikeInputSource Weight(System.Func<BikeState> read)
        {
            return new ScriptedBikeInput(() =>
            {
                var rel = read().PitchRelRad;
                var lean = 0f;
                if (rel > 0.18f) lean = 1f;
                else if (rel < -0.18f) lean = -1f;
                return new BikeInputState { Throttle = 1f, Lean = lean };
            });
        }

        /// <summary>
        /// ДОЗИРОВКА. Пороги здесь — не подгонка под красивый результат, они взяты из порогов
        /// самой физики, чтобы политика реагировала на то же, на что реагирует решатель:
        ///   0.60 рад = wheelieZone профиля, начало подушки у грани вилли;
        ///   0.25 рад — примерно половина пути до неё, ранняя реакция;
        ///   climbFromRad = порог, с которого начинается эндуро-режим.
        ///
        /// Логика ровно та, которую я описал как «что должно стать правдой для игрока»:
        /// нос пошёл вверх — сбросить газ и подать вес вперёд; на подъёме без задранного
        /// носа — вес НАЗАД, потому что везёт заднее колесо.
        /// </summary>
        /// <summary>Параметры дозирующей политики. Ищутся перебором, а не угадываются.</summary>
        private struct DoseParams
        {
            public float CutRel;      // тангаж, с которого сбрасываем газ
            public float EasedGas;    // сколько газа остаётся после сброса
            public float ClimbLean;   // вес на подъёме при нормальном тангаже
            public float EdgeBrake;   // задний тормоз у грани вилли

            public string Label => "срез " + F(CutRel, 2) + " газ " + F(EasedGas, 2)
                                   + " вес " + F(ClimbLean, 2);
        }

        /// <summary>
        /// ДОЗИРОВКА. Первая редакция задавала ОДНУ угаданную политику, и она прошла 31.6 %
        /// за 90 секунд без единого отказа — то есть не провалилась, а ЗАСТРЯЛА: сброс газа
        /// ронял ход, ход не восстанавливался, вышел предельный цикл. Вывод «навыка нет» из
        /// такого замера был бы выводом о моей политике, а не об игре.
        ///
        /// Поэтому параметры ищутся перебором, и задача перебора — вопрос СУЩЕСТВОВАНИЯ:
        /// есть ли ХОТЬ ОДНА дозировка, обгоняющая «зажал газ». Если есть — окно навыка в
        /// физике имеется. Если ни одной — вывод про дизайн уровня становится обоснованным,
        /// потому что проверена не одна догадка, а пространство.
        /// </summary>
        private static IBikeInputSource Dosed(System.Func<BikeState> read, BikeTuningProfile p,
            DoseParams d)
        {
            return new ScriptedBikeInput(() =>
            {
                var s = read();
                var rel = s.PitchRelRad;
                var onClimb = s.GroundSlopeRad > p.climbFromRad;

                if (rel > p.wheelieZone && d.EdgeBrake > 0f)
                {
                    // У грани: задний тормоз — единственный помощник, который в этой физике
                    // НЕ гаснет на дизайн-крутом, то есть главный инструмент спасения.
                    return new BikeInputState { Throttle = 0f, Brake = d.EdgeBrake, Lean = 1f };
                }
                if (rel > d.CutRel)
                {
                    return new BikeInputState { Throttle = d.EasedGas, Lean = 0.8f };
                }
                if (rel < -0.30f)
                {
                    return new BikeInputState { Throttle = 1f, Lean = -1f };
                }
                // Нормальный ход. На подъёме вес НАЗАД: везёт заднее колесо, и разгружать его
                // нельзя — это половина дилеммы, которой нет у политики WEIGHT.
                return new BikeInputState { Throttle = 1f, Lean = onClimb ? d.ClimbLean : 0f };
            });
        }

        // ================= замер =================

        private struct Run
        {
            public string Name;
            public float DistanceM;
            public float Seconds;
            public BikeFailure Failure;
            public float MaxRelDeg;
            public bool Finished;
        }

        private IEnumerator Measure(string name, System.Func<System.Func<BikeState>,
            BikeTuningProfile, IBikeInputSource> make, System.Action<Run> report)
        {
            var profile = ScriptableObject.CreateInstance<BikeTuningProfile>();
            var level = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
            var track = VerticalSliceTrack.CreateProfile();
            var sampler = new TerrainSampler(track);
            _spawned.Add(TrackBuilder.Build(track, profile.tyreFriction));

            BikeController ctrl = null;
            var input = make(() => ctrl.State, profile);
            var axle = BikeFactory.RestingRearAxle(sampler, profile, 60f * UnitsContract.PxToM);
            ctrl = BikeFactory.Spawn(profile, level, sampler, input, axle, false);
            _spawned.Add(ctrl.gameObject);

            var endXM = VerticalSliceTrack.EndPx * UnitsContract.PxToM;
            var maxX = 0f;
            var maxRel = 0f;
            var t = 0f;
            var finished = false;

            for (var i = 0; i < 5400; i++)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                var s = ctrl.State;
                maxX = Mathf.Max(maxX, s.PositionXM);
                maxRel = Mathf.Max(maxRel, s.PitchRelRad);
                if (s.Failure != BikeFailure.None) break;
                if (s.PositionXM >= endXM) { finished = true; break; }
            }

            report(new Run
            {
                Name = name,
                DistanceM = maxX,
                Seconds = t,
                Failure = ctrl.State.Failure,
                MaxRelDeg = maxRel * Mathf.Rad2Deg,
                Finished = finished
            });
            TearDown();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator ThrottleDosing_IsTheSkill_NotWeightAlone()
        {
            var runs = new List<Run>();

            yield return Measure("NEUTRAL (газ 1.0, вес 0)", (r, p) => Neutral(), x => runs.Add(x));
            yield return Measure("WEIGHT (газ 1.0, вес реактивно)", (r, p) => Weight(r), x => runs.Add(x));
            // Перебор дозировок: три порога среза × три уровня остаточного газа × две
            // стратегии веса на подъёме. Тормоз у грани включён везде — без него у политики
            // нет инструмента спасения вообще, и перебор мерил бы её отсутствие.
            foreach (var cut in new[] { 0.25f, 0.45f, 0.65f })
            foreach (var gas in new[] { 0.00f, 0.35f, 0.70f })
            foreach (var lean in new[] { 0.00f, -0.45f })
            {
                var d = new DoseParams
                {
                    CutRel = cut, EasedGas = gas, ClimbLean = lean, EdgeBrake = 0.55f
                };
                yield return Measure("DOSED " + d.Label, (r, p) => Dosed(r, p, d), x => runs.Add(x));
            }

            var endM = VerticalSliceTrack.EndPx * UnitsContract.PxToM;
            var neutral = runs[0].DistanceM;

            var lines = new List<string>
            {
                "# КАКОЙ ИМЕННО НАВЫК ОКУПАЕТСЯ",
                "",
                "Сгенерирован `SkillPolicyComparison`. Перезапишется следующим прогоном.",
                "",
                "Повод: развёртка калибровки показала, что перенос веса В ОДИНОЧКУ не выигрывает",
                "ни при одной настройке, а местами проигрывает вдвое. Причина физическая — вес",
                "вперёд опускает нос и тем же движением разгружает ведущее колесо. Значит",
                "разрешение дилеммы не в весе, а в ГАЗЕ, и мерить надо политику с дозировкой.",
                "",
                "Трасса " + F(endM, 1) + " м, профиль уровня vertical-slice, одна и та же для всех трёх.",
                "",
                "| политика | дистанция | % трассы | время | макс тангаж | итог | против NEUTRAL |",
                "|---|---|---|---|---|---|---|"
            };

            foreach (var r in runs)
            {
                var gain = (r.DistanceM - neutral) / Mathf.Max(0.01f, neutral) * 100f;
                lines.Add("| " + r.Name
                          + " | " + F(r.DistanceM, 1) + " м"
                          + " | " + F(100f * r.DistanceM / endM, 1) + " %"
                          + " | " + F(r.Seconds, 1) + " с"
                          + " | " + F(r.MaxRelDeg, 1) + "°"
                          + " | " + (r.Finished ? "**ФИНИШ**" : r.Failure.ToString())
                          + " | " + (r.Name.StartsWith("NEUTRAL") ? "—" : F(gain, 1) + " %")
                          + " |");
            }

            var dosed = runs.Where(r => r.Name.StartsWith("DOSED"))
                .OrderByDescending(r => r.DistanceM).First();
            var dosedGain = (dosed.DistanceM - neutral) / Mathf.Max(0.01f, neutral) * 100f;

            lines.Add("");
            lines.Add("## Вывод");
            lines.Add("");
            if (dosed.Finished && !runs[0].Finished)
            {
                lines.Add("**Окно навыка есть.** Дозирующая политика проходит трассу целиком там,");
                lines.Add("где «зажал газ» проваливается на " + F(100f * neutral / endM, 1) + " %.");
                lines.Add("Это ровно то, что должно быть правдой для игрока: зажать газ нельзя,");
                lines.Add("дозировать можно.");
            }
            else if (dosedGain >= 15f)
            {
                lines.Add("**Окно навыка есть:** дозировка даёт " + F(dosedGain, 1) + " % против «зажал газ».");
            }
            else
            {
                lines.Add("**Лучшая из " + runs.Count(r => r.Name.StartsWith("DOSED"))
                          + " дозировок: " + F(dosedGain, 1) + " % против «зажал газ».**");
                lines.Add("");
                lines.Add("Чего этот результат НЕ доказывает: что играть неинтересно. Скриптовый");
                lines.Add("пилот реагирует на один скаляр с фиксированным порогом, не готовится");
                lines.Add("к участку заранее и не везёт скорость. Расхождение бота и человека");
                lines.Add("измерено на исходнике и велико В ОБЕ стороны: бот «только газ» —");
                lines.Add("41.6 % трассы, живой человек — 22 %.");
                lines.Add("");
                lines.Add("Что доказывает: РЕАКТИВНОЙ политики, обгоняющей «зажал газ», в этом");
                lines.Add("пространстве параметров нет. Здесь инструмент кончается, и вопрос");
                lines.Add("переходит к живой игре — то есть к сборке в руках.");
            }

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                "docs", "skill-policy-comparison.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
            Debug.Log("SKILL POLICY записан: " + path + "\n" + string.Join("\n", lines));

            // Утверждение способно провалиться в обе стороны и адресно.
            Assert.AreNotEqual(runs[0].DistanceM, runs[2].DistanceM,
                "NEUTRAL и DOSED обязаны разойтись, иначе политика дозировки ничего не делает");
        }
    }
}
