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
    /// Почему ВСЕ шестнадцать настроек развёртки дали одну и ту же дистанцию 86.2-86.4 %.
    ///
    /// Такое совпадение невозможно, если дистанцию определяет то, что варьировалось.
    /// Значит дистанцию определяет что-то другое — и пока это «что-то» не названо, любое
    /// измерение навыка на этой трассе меряет положение препятствия, а не навык. Ровно этой
    /// ошибкой уже был испорчен первый вариант теста G (мерил положение разрыва) — см.
    /// комментарий в TrackProfile.gapsCutCollision.
    ///
    /// Тест ведёт трассировку прогона и печатает профиль трассы вместе с тем, что на нём
    /// происходило: где скорость упала, какой был уклон, буксовало ли колесо. Утверждение
    /// в конце адресное и способно провалиться в обе стороны.
    /// </summary>
    public class WallDiagnosis
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

        private struct Sample
        {
            public float T;
            public float XM;
            public float SpeedMPerS;
            public float SlopeDeg;
            public float RelDeg;
            public float Slip;
            public float RearLoadN;
            public RidingState State;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Diagnose_WhereAndWhyTheRunEnds()
        {
            var profile = ScriptableObject.CreateInstance<BikeTuningProfile>();
            var level = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
            var track = VerticalSliceTrack.CreateProfile();
            var sampler = new TerrainSampler(track);
            _spawned.Add(TrackBuilder.Build(track, profile.tyreFriction));

            var axle = BikeFactory.RestingRearAxle(sampler, profile, 60f * UnitsContract.PxToM);
            var ctrl = BikeFactory.Spawn(profile, level, sampler,
                ScriptedBikeInput.HoldThrottle(), axle, false);
            _spawned.Add(ctrl.gameObject);

            var trace = new List<Sample>();
            var t = 0f;
            var endXM = VerticalSliceTrack.EndPx * UnitsContract.PxToM;

            for (var i = 0; i < 4200; i++)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                var s = ctrl.State;
                if (i % 6 == 0)
                {
                    trace.Add(new Sample
                    {
                        T = t,
                        XM = s.PositionXM,
                        SpeedMPerS = s.SpeedMPerS,
                        SlopeDeg = s.GroundSlopeRad * Mathf.Rad2Deg,
                        RelDeg = s.PitchRelRad * Mathf.Rad2Deg,
                        Slip = s.RearSlip,
                        RearLoadN = s.RearNormalLoadN,
                        State = s.State
                    });
                }
                if (s.Failure != BikeFailure.None) break;
                if (s.PositionXM >= endXM) break;
            }

            Assert.IsNotEmpty(trace, "трассировка пуста — прогон не состоялся");

            var maxX = trace.Max(s => s.XM);
            var stallFrom = -1f;
            // «Стена» — первая точка, после которой байк за 3 секунды не продвинулся дальше
            // чем на 1 метр. Порог по ВРЕМЕНИ, а не по скорости: мгновенная скорость на
            // буксующем колесе скачет, и по ней стена не ловится.
            for (var i = 0; i < trace.Count; i++)
            {
                var later = trace.Where(s => s.T >= trace[i].T && s.T <= trace[i].T + 3f).ToList();
                if (later.Count < 5) break;
                if (later.Max(s => s.XM) - trace[i].XM < 1f) { stallFrom = trace[i].T; break; }
            }

            var lines = new List<string>
            {
                "# ГДЕ ЗАКАНЧИВАЕТСЯ ПРОГОН И ПОЧЕМУ",
                "",
                "Сгенерирован `WallDiagnosis`. Повод: в развёртке калибровки ВСЕ 16 настроек дали",
                "дистанцию 86.2-86.4 % — значит её определяет не то, что варьировалось.",
                "",
                "Байк: газ зажат, профиль уровня vertical-slice.",
                "Трасса: " + F(endXM, 1) + " м (" + VerticalSliceTrack.EndPx + " авторских px).",
                "",
                "## Профиль трассы по узлам",
                "",
                "| от px | до px | длина м | перепад м | уклон ° |",
                "|---|---|---|---|---|"
            };

            var nodes = VerticalSliceTrack.Nodes;
            for (var i = 0; i < nodes.Length - 1; i++)
            {
                var dxPx = nodes[i + 1].x - nodes[i].x;
                var dyPx = nodes[i + 1].y - nodes[i].y;
                if (dxPx <= 0f) continue;
                var deg = Mathf.Atan2(dyPx, dxPx) * Mathf.Rad2Deg;
                lines.Add("| " + nodes[i].x + " | " + nodes[i + 1].x
                          + " | " + F(dxPx * UnitsContract.PxToM, 1)
                          + " | " + F(dyPx * UnitsContract.PxToM, 1)
                          + " | " + F(deg, 1) + (Mathf.Abs(deg) >= 40f ? " **" : "") + " |");
            }

            lines.Add("");
            lines.Add("## Трассировка (каждая 10-я выборка)");
            lines.Add("");
            lines.Add("| т, с | x, м | % | v, м/с | уклон ° | тангаж ° | букс | Fn зад, Н | состояние |");
            lines.Add("|---|---|---|---|---|---|---|---|---|");
            for (var i = 0; i < trace.Count; i += 10)
            {
                var s = trace[i];
                lines.Add("| " + F(s.T, 1) + " | " + F(s.XM, 1)
                          + " | " + F(100f * s.XM / endXM, 1)
                          + " | " + F(s.SpeedMPerS, 2)
                          + " | " + F(s.SlopeDeg, 1)
                          + " | " + F(s.RelDeg, 1)
                          + " | " + F(s.Slip, 2)
                          + " | " + F(s.RearLoadN, 0)
                          + " | " + s.State + " |");
            }

            lines.Add("");
            lines.Add("## Вывод");
            lines.Add("");
            if (stallFrom >= 0f)
            {
                var at = trace.First(s => s.T >= stallFrom);
                var window = trace.Where(s => s.T >= stallFrom && s.T <= stallFrom + 6f).ToList();
                lines.Add("Байк ВСТАЁТ на " + F(at.XM, 1) + " м (" + F(100f * at.XM / endXM, 1)
                          + " % трассы, " + F(at.XM / UnitsContract.PxToM, 0) + " авторских px)"
                          + " на " + F(stallFrom, 1) + " секунде.");
                lines.Add("");
                lines.Add("- уклон в точке остановки: **" + F(at.SlopeDeg, 1) + "°**");
                lines.Add("- скорость: " + F(at.SpeedMPerS, 2) + " м/с при верхней "
                          + F(profile.topSpeedMPerS, 2) + " м/с");
                lines.Add("- пробуксовка заднего колеса: средняя за 6 с после остановки **"
                          + F(window.Average(s => s.Slip), 2) + "**");
                lines.Add("- нормальная реакция на заднем: средняя " + F(window.Average(s => s.RearLoadN), 0) + " Н");
                lines.Add("- тангаж к склону: средний " + F(window.Average(s => s.RelDeg), 1) + "°");
                lines.Add("");
                lines.Add("Это значит, что дистанция всех прогонов ограничена ЭТИМ местом, а не");
                lines.Add("настройкой помощников и не политикой пилота. Любое измерение «выигрыша");
                lines.Add("веса» на такой трассе меряет положение стены.");
            }
            else
            {
                lines.Add("Точки, где байк стоит дольше 3 секунд, не нашлось: прогон закончился");
                lines.Add("иначе (отказ или финиш) на " + F(maxX, 1) + " м.");
            }

            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..",
                "docs", "wall-diagnosis.md"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
            Debug.Log("WALL DIAGNOSIS записан: " + path + "\n" + string.Join("\n", lines.Take(40)));

            Assert.Greater(stallFrom, -0.5f,
                "Ожидалась точка остановки: развёртка показала одинаковую дистанцию на всех "
                + "настройках, что объяснимо только препятствием. Если её нет — объяснение другое.");
        }
    }
}
