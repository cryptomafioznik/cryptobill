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
    /// Пункт 5: проверки СВОЙСТВ физики, а не абсолютных чисел.
    ///
    /// Почему свойств. Три ключевых параметра профиля (engineForceN, tyreFriction, brakeForceN)
    /// пока плейсхолдеры и перечислены в BikeTuningProfile.pendingCalibration — предъявлять
    /// абсолютные значения разгона или пути торможения было бы предъявлением плейсхолдера как
    /// результата. Калибровка по эталонам docs/BIKE_PHYSICS_SPEC.md §7 — это пункты 6 и 8.
    ///
    /// Свойства при этом падать умеют, и каждое проверяет утверждение, из-за которого игра
    /// либо работает, либо нет.
    /// </summary>
    public class BikePhysicsTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private BikeTuningProfile MakeProfile()
        {
            var p = ScriptableObject.CreateInstance<BikeTuningProfile>();
            // Переопределяем ТОЛЬКО те плейсхолдеры, без которых байк не поедет.
            // Подвеску НЕ трогаем: её частота откалибрована измерением (8 Гц → просадка
            // 0.0996 м), и если тест перезапишет её своим числом, он будет проверять
            // не тот профиль, что поедет в игре. Ровно на этом первая редакция теста
            // и упала: хелпер ставил 5 Гц, просадка выходила 0.255 м, а гейт ждал 0.10 м.
            p.engineForceN = 4000f;
            p.tyreFriction = 1.2f;
            p.brakeForceN = 2500f;
            return p;
        }

        private TrackProfile MakeFlatTrack()
        {
            var t = ScriptableObject.CreateInstance<TrackProfile>();
            t.nodesPx = new[] { new Vector2(0, 0), new Vector2(20000, 0) };
            t.endPx = 20000f;
            t.nodeStepPx = 26f;
            return t;
        }

        private TrackProfile MakeRampTrack(float risePx, float runPx)
        {
            var t = ScriptableObject.CreateInstance<TrackProfile>();
            t.nodesPx = new[]
            {
                new Vector2(0, 0), new Vector2(2000, 0),
                new Vector2(2000 + runPx, risePx),
                new Vector2(2000 + runPx + 2000, risePx)
            };
            t.endPx = 2000 + runPx + 2000;
            t.nodeStepPx = 26f;
            return t;
        }

        private BikeController Spawn(TrackProfile track, BikeTuningProfile profile,
            IBikeInputSource input, float startXM, out TerrainSampler sampler)
        {
            sampler = new TerrainSampler(track);
            var trackGo = TrackBuilder.Build(track, profile.tyreFriction);
            _spawned.Add(trackGo);

            var axle = BikeFactory.RestingRearAxle(sampler, profile, startXM);
            var ctrl = BikeFactory.Spawn(profile, ScriptableObject.CreateInstance<LevelPhysicsOverride>(),
                sampler, input, axle);
            _spawned.Add(ctrl.gameObject);
            return ctrl;
        }

        private static IEnumerator Steps(int n)
        {
            for (var i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        // ============================================================================

        [UnityTest]
        public IEnumerator Gravity_MatchesUnitsContract()
        {
            // Эталон §7.1: 26.46 м/с². Тест ловит подмену гравитации на дефолтные 9.81.
            var go = new GameObject("Probe");
            _spawned.Add(go);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 1f;
            go.transform.position = new Vector2(0f, 100f);

            yield return new WaitForFixedUpdate();
            var v0 = rb.linearVelocity.y;
            yield return Steps(30);
            var v1 = rb.linearVelocity.y;

            var measured = (v0 - v1) / (30f * Time.fixedDeltaTime);
            Assert.That(measured, Is.EqualTo(26.46f).Within(0.3f),
                "измерено " + measured + " м/с², ожидалось 26.46 (контракт единиц, вариант A)");
        }

        [UnityTest]
        public IEnumerator Bike_RestsOnFlatGround_WithoutSinkingOrDrifting()
        {
            var p = MakeProfile();
            var ctrl = Spawn(MakeFlatTrack(), p, new ScriptedBikeInput(() => BikeInputState.Neutral),
                10f, out var sampler);

            yield return Steps(240); // 4 секунды на успокоение

            var s = ctrl.State;
            var rig = ctrl.GetComponent<BikeRig>();
            Assert.IsTrue(s.IsGrounded, "байк должен стоять на земле, контактов " + s.GroundedWheelCount);
            Assert.That(Mathf.Abs(s.SpeedMPerS), Is.LessThan(0.15f),
                "стоящий байк не должен ползти, скорость " + s.SpeedMPerS);

            // Колесо стоит на поверхности: центр на высоте рельефа плюс радиус.
            var groundY = sampler.HeightAt(rig.RearWheel.position.x);
            Assert.That(rig.RearWheel.position.y, Is.EqualTo(groundY + p.wheelRadiusM).Within(0.03f),
                "колесо должно лежать на поверхности: " + rig.RearWheel.position.y.ToString("F4")
                + " против " + (groundY + p.wheelRadiusM).ToString("F4"));

            // Шасси висит НИЖЕ линии осей на статическую просадку подвески. Первая редакция
            // этого теста ошибочно ждала «ось + ЦТ»: transform шасси И ЕСТЬ линия осей,
            // а centerOfMass — внутреннее смещение, в position оно не входит.
            var sag = rig.RearWheel.position.y - rig.Chassis.position.y;
            Assert.That(sag, Is.InRange(0.05f, 0.15f),
                "статическая просадка " + sag.ToString("F4") + " м должна быть около трети хода ("
                + p.suspensionTravelM.ToString("F2") + " м). Измерено при 8 Гц: 0.0996 м");
        }

        [UnityTest]
        public IEnumerator RestingBike_CarriesFullWeightOnWheels()
        {
            // Замкнутая сверка: сумма нормальных реакций обязана равняться весу.
            // Если реакция читается неверно (например из скорости после решателя вместо
            // импульса), эта проверка провалится.
            var p = MakeProfile();
            var ctrl = Spawn(MakeFlatTrack(), p, new ScriptedBikeInput(() => BikeInputState.Neutral),
                10f, out _);

            yield return Steps(240);

            var s = ctrl.State;
            var total = s.RearNormalLoadN + s.FrontNormalLoadN;
            var weight = (p.chassisMassKg + 2f * p.wheelMassKg) * Mathf.Abs(Physics2D.gravity.y);
            Assert.That(total, Is.EqualTo(weight).Within(weight * 0.25f),
                "сумма реакций " + total.ToString("F0") + " Н против веса " + weight.ToString("F0") + " Н");
        }

        [UnityTest]
        public IEnumerator WeightShiftBack_IncreasesRearLoad_EmergentDriveTrade()
        {
            // ГЛАВНОЕ свойство переноса: driveTrade не переносится формулой, он должен
            // получаться из сдвига центра масс. Если это не так, окна навыка не будет.
            //
            // Условия замера выведены измерением, а не выбраны наугад:
            //  · вращение НЕ фиксируем. Первая редакция фиксировала (`freezeRotation`), чтобы
            //    «изолировать» сдвиг ЦТ, и получила «эффекта нет»: у тела с бесконечной инерцией
            //    вращения решателю не нужно балансировать момент, поэтому фиксация убивает сам
            //    механизм перераспределения. Это была ошибка инструмента, не физики;
            //  · помощники СТОКОВЫЕ (groundAlign = 1) — держат положение, пока идёт замер;
            //  · окно КОРОТКОЕ (0.75 с) — успевает установиться нагрузка, но не успевает
            //    развиться опрокидывание.
            var p = MakeProfile();
            var lean = 0f;
            var ctrl = Spawn(MakeFlatTrack(), p, new ScriptedBikeInput(() => new BikeInputState { Lean = lean }),
                10f, out _);
            ctrl.Level = LevelPhysicsOverride.CreateStock();

            yield return Steps(180);
            var neutralRear = ctrl.State.RearNormalLoadN;
            var neutralFront = ctrl.State.FrontNormalLoadN;
            Assert.That(neutralRear, Is.EqualTo(neutralFront).Within(neutralRear * 0.1f),
                "на нейтральном весе байк симметричен: зад " + neutralRear.ToString("F0")
                + " против переда " + neutralFront.ToString("F0") + " Н");

            lean = -1f; // вес НАЗАД
            yield return Steps(45);
            var backRear = ctrl.State.RearNormalLoadN;
            var backFront = ctrl.State.FrontNormalLoadN;

            lean = 0f;
            yield return Steps(45);

            lean = 1f; // вес ВПЕРЁД
            yield return Steps(45);
            var fwdRear = ctrl.State.RearNormalLoadN;
            var fwdFront = ctrl.State.FrontNormalLoadN;

            Assert.Greater(backRear, neutralRear * 1.2f,
                "вес назад обязан ГРУЗИТЬ заднее колесо: " + backRear.ToString("F0")
                + " против нейтрального " + neutralRear.ToString("F0") + " Н");
            Assert.Less(backFront, neutralFront * 0.8f,
                "и одновременно РАЗГРУЖАТЬ переднее: " + backFront.ToString("F0") + " Н");

            Assert.Less(fwdRear, neutralRear * 0.8f,
                "вес вперёд обязан РАЗГРУЖАТЬ заднее: " + fwdRear.ToString("F0")
                + " против нейтрального " + neutralRear.ToString("F0") + " Н");
            Assert.Greater(fwdFront, neutralFront * 1.2f,
                "и одновременно ГРУЗИТЬ переднее: " + fwdFront.ToString("F0") + " Н");

            // Замкнутая сверка: сумма нагрузок сохраняется при переносе веса — вес никуда
            // не исчезает, он ПЕРЕРАСПРЕДЕЛЯЕТСЯ. Если бы сдвиг ЦТ просто масштабировал
            // одну из реакций, эта проверка провалилась бы.
            var sumNeutral = neutralRear + neutralFront;
            var sumBack = backRear + backFront;
            var sumFwd = fwdRear + fwdFront;
            Assert.That(sumBack, Is.EqualTo(sumNeutral).Within(sumNeutral * 0.15f),
                "сумма реакций при весе назад " + sumBack.ToString("F0")
                + " против нейтральной " + sumNeutral.ToString("F0") + " Н");
            Assert.That(sumFwd, Is.EqualTo(sumNeutral).Within(sumNeutral * 0.15f),
                "сумма реакций при весе вперёд " + sumFwd.ToString("F0")
                + " против нейтральной " + sumNeutral.ToString("F0") + " Н");
        }

        [UnityTest]
        public IEnumerator ThrottleInAir_ProducesNoForwardAcceleration()
        {
            // Свойство «тяга опирается на реальный прижим». В воздухе прижима нет, значит
            // газ не должен разгонять. В legacy-модели исходника был ПОЛ нагрузки, и это
            // свойство нарушалось — из-за него вилли на газу был арифметически неизбежен.
            var p = MakeProfile();
            var ctrl = Spawn(MakeFlatTrack(), p, ScriptedBikeInput.HoldThrottle(), 10f, out _);

            yield return Steps(60);

            // Поднимаем байк в воздух и ждём, пока контакты пропадут.
            var rig = ctrl.GetComponent<BikeRig>();
            rig.Chassis.position += new Vector2(0f, 8f);
            rig.RearWheel.position += new Vector2(0f, 8f);
            rig.FrontWheel.position += new Vector2(0f, 8f);
            rig.Chassis.linearVelocity = Vector2.zero;
            rig.RearWheel.linearVelocity = Vector2.zero;
            rig.FrontWheel.linearVelocity = Vector2.zero;

            yield return Steps(5);
            Assert.IsFalse(ctrl.State.IsGrounded, "байк должен быть в воздухе");

            var vx0 = rig.Chassis.linearVelocity.x;
            yield return Steps(20);
            var vx1 = rig.Chassis.linearVelocity.x;

            Assert.That(Mathf.Abs(vx1 - vx0), Is.LessThan(0.35f),
                "в воздухе газ не должен разгонять: Δvx = " + (vx1 - vx0).ToString("F3") + " м/с");
        }

        [UnityTest]
        public IEnumerator ThrottleRamp_IsTimeBased_NotPerFrame()
        {
            // Ловушка исходника: физика шла по кадрам, поэтому на 45 FPS игра была другой.
            // Рампа газа обязана зависеть от ВРЕМЕНИ. Прогоняем при двух разных шагах.
            var results = new List<float>();
            foreach (var step in new[] { 1f / 60f, 1f / 120f })
            {
                var saved = Time.fixedDeltaTime;
                Time.fixedDeltaTime = step;

                var p = MakeProfile();
                var ctrl = Spawn(MakeFlatTrack(), p, ScriptedBikeInput.HoldThrottle(), 10f, out _);

                var targetSeconds = 0.3f;
                var steps = Mathf.RoundToInt(targetSeconds / step);
                yield return Steps(steps);
                results.Add(ctrl.State.ThrottleApplied);

                Time.fixedDeltaTime = saved;
                TearDown();
            }

            Assert.That(results[0], Is.EqualTo(results[1]).Within(0.05f),
                "газ через 0.3 с: при 60 Гц " + results[0].ToString("F3")
                + ", при 120 Гц " + results[1].ToString("F3") + " — обязаны совпадать");
        }

        [UnityTest]
        public IEnumerator Reset_ClearsHiddenState_TrajectoriesMatch()
        {
            // ЭТОТ ТЕСТ КОДИРУЕТ КОНКРЕТНУЮ ОШИБКУ ИСХОДНИКА. Там reset() не обнулял _wFnS,
            // и второй прогон в той же загрузке шёл по другой траектории: замерено 17.1 %
            // вместо 87.8 % на одной политике и одной трассе. Здесь два прогона после сброса
            // обязаны совпасть.
            var p = MakeProfile();
            var ctrl = Spawn(MakeRampTrack(600f, 1200f), p, ScriptedBikeInput.HoldThrottle(),
                10f, out var sampler);
            var startAxle = BikeFactory.RestingRearAxle(sampler, p, 10f);

            var runA = new List<Vector3>();
            for (var i = 0; i < 240; i++)
            {
                yield return new WaitForFixedUpdate();
                var s = ctrl.State;
                runA.Add(new Vector3(s.PositionXM, s.PositionYM, s.PitchRad));
            }

            ctrl.ResetTo(startAxle);

            var runB = new List<Vector3>();
            for (var i = 0; i < 240; i++)
            {
                yield return new WaitForFixedUpdate();
                var s = ctrl.State;
                runB.Add(new Vector3(s.PositionXM, s.PositionYM, s.PitchRad));
            }

            var worst = 0f;
            var worstAt = -1;
            for (var i = 0; i < runA.Count; i++)
            {
                var d = Vector3.Distance(runA[i], runB[i]);
                if (d > worst) { worst = d; worstAt = i; }
            }

            Assert.That(worst, Is.LessThan(0.05f),
                "траектории после сброса разошлись на " + worst.ToString("F4")
                + " на кадре " + worstAt + " — значит сброс что-то не обнуляет");
        }

        [UnityTest]
        public IEnumerator FailHold_GivesRecoveryWindow_NotInstantDeath()
        {
            // Смерть обязана наступать не от угла, а от угла, УДЕРЖАННОГО failHold секунд.
            // Проверяем механизм: при достижении опасного угла отказ не наступает в тот же кадр.
            var p = MakeProfile();
            var level = ScriptableObject.CreateInstance<LevelPhysicsOverride>();
            Assert.Greater(level.failHoldSeconds, 0f, "профиль уровня обязан задавать выдержку");

            var ctrl = Spawn(MakeFlatTrack(), p, new ScriptedBikeInput(() => BikeInputState.Neutral),
                10f, out _);
            ctrl.Level = level;

            yield return Steps(60);

            // Насильно задираем нос за порог и держим одно мгновение.
            var rig = ctrl.GetComponent<BikeRig>();
            rig.Chassis.rotation = 120f;
            rig.Chassis.angularVelocity = 30f;

            yield return new WaitForFixedUpdate();
            Assert.AreEqual(BikeFailure.None, ctrl.State.Failure,
                "отказ не должен наступать в тот же кадр, когда угол пересёк порог");
        }
    }
}
