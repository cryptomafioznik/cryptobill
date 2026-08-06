using System.Collections.Generic;
using ChartRunner.Bike;
using UnityEngine;

namespace ChartRunner.Telemetry
{
    /// <summary>
    /// Пункт 7 плана: сбор телеметрии. Пишет снимки <see cref="BikeState"/> в кольцевой буфер
    /// и считает по ним сводку.
    ///
    /// ДВА ПРАВИЛА, ИЗ КОТОРЫХ СЛЕДУЕТ ВСЯ КОНСТРУКЦИЯ.
    ///
    /// 1. Телеметрия ЧИТАЕТ, но не считает. Все величины берутся из `BikeController.State` —
    ///    того же объекта, который использует физика. Пересчитывать здесь скорость или уклон
    ///    заново значило бы мерить свою копию логики, а не продукт. Это ловушка №5 из
    ///    memory/measurement-instrument-traps: тест, который зовёт уровень ниже продакшена,
    ///    «нашёл» несуществующий баг.
    ///
    /// 2. Инструмент не имеет права влиять на измеряемое. Ни один метод здесь не трогает
    ///    Rigidbody2D, ввод и профиль. На это есть тест: траектории с включённой и выключенной
    ///    телеметрией обязаны совпасть побитово.
    /// </summary>
    public class BikeTelemetry : MonoBehaviour
    {
        [Tooltip("Сбор можно выключить целиком: на устройстве оверлей и буфер не нужны в релизе.")]
        public bool Enabled = true;

        [Tooltip("Сколько физ-шагов хранить. 600 при 60 Гц = 10 секунд.")]
        public int Capacity = 600;

        private BikeController _controller;
        private readonly List<BikeState> _buffer = new List<BikeState>();
        private int _samples;

        /// <summary>Последний снимок. Если сбор выключен — состояние по умолчанию.</summary>
        public BikeState Latest { get; private set; }

        /// <summary>Сколько снимков собрано за всё время (не размер буфера).</summary>
        public int TotalSamples => _samples;

        public IReadOnlyList<BikeState> Buffer => _buffer;

        public void Bind(BikeController controller)
        {
            _controller = controller;
        }

        private void FixedUpdate()
        {
            if (!Enabled || _controller == null) return;

            var s = _controller.State;
            Latest = s;
            _samples++;

            _buffer.Add(s);
            if (_buffer.Count > Mathf.Max(1, Capacity)) _buffer.RemoveAt(0);
        }

        public void Clear()
        {
            _buffer.Clear();
            _samples = 0;
            Latest = default;
        }

        // ================= сводка =================

        /// <summary>Сводка по буферу. Нужна отчётам пункта 8 и утреннему разговору.</summary>
        public struct Summary
        {
            public int Samples;
            public float DurationSeconds;
            public float DistanceM;
            public float MaxSpeedMPerS;
            public float MeanSpeedMPerS;
            public float MaxAbsPitchRelDeg;
            public float MaxAbsAngularSpeedDegPerS;
            public float AirFraction;
            public float MaxRearCompressionM;
            public float MaxFrontCompressionM;
            public float MaxRearLoadN;
            public float MaxSlipRear;
            public BikeFailure Failure;
        }

        public Summary Summarise()
        {
            var r = new Summary { Samples = _buffer.Count };
            if (_buffer.Count == 0) return r;

            var speedSum = 0f;
            var airFrames = 0;
            var minX = float.MaxValue;
            var maxX = float.MinValue;

            foreach (var s in _buffer)
            {
                speedSum += s.SpeedMPerS;
                if (!s.IsGrounded) airFrames++;
                minX = Mathf.Min(minX, s.PositionXM);
                maxX = Mathf.Max(maxX, s.PositionXM);
                r.MaxSpeedMPerS = Mathf.Max(r.MaxSpeedMPerS, s.SpeedMPerS);
                r.MaxAbsPitchRelDeg = Mathf.Max(r.MaxAbsPitchRelDeg,
                    Mathf.Abs(s.PitchRelRad * Mathf.Rad2Deg));
                r.MaxAbsAngularSpeedDegPerS = Mathf.Max(r.MaxAbsAngularSpeedDegPerS,
                    Mathf.Abs(s.AngularVelocityRadPerS * Mathf.Rad2Deg));
                r.MaxRearCompressionM = Mathf.Max(r.MaxRearCompressionM, s.RearCompressionM);
                r.MaxFrontCompressionM = Mathf.Max(r.MaxFrontCompressionM, s.FrontCompressionM);
                r.MaxRearLoadN = Mathf.Max(r.MaxRearLoadN, s.RearNormalLoadN);
                r.MaxSlipRear = Mathf.Max(r.MaxSlipRear, s.RearSlip);
                if (s.Failure != BikeFailure.None) r.Failure = s.Failure;
            }

            r.DurationSeconds = _buffer.Count * Time.fixedDeltaTime;
            r.DistanceM = maxX - minX;
            r.MeanSpeedMPerS = speedSum / _buffer.Count;
            r.AirFraction = (float)airFrames / _buffer.Count;
            return r;
        }

        /// <summary>Строки для оверлея. Отдельно от рендера, чтобы это можно было тестировать.</summary>
        public string[] OverlayLines()
        {
            var s = Latest;
            return new[]
            {
                "состояние   " + s.State,
                "скорость    " + s.SpeedMPerS.ToString("F2") + " м/с  ("
                    + (s.SpeedMPerS * 3.6f).ToString("F0") + " км/ч)",
                "тангаж      " + (s.PitchRad * Mathf.Rad2Deg).ToString("F1") + "°  к склону "
                    + (s.PitchRelRad * Mathf.Rad2Deg).ToString("F1") + "°",
                "угл.скор.   " + (s.AngularVelocityRadPerS * Mathf.Rad2Deg).ToString("F0") + "°/с",
                "уклон       " + (s.GroundSlopeRad * Mathf.Rad2Deg).ToString("F1") + "°",
                "колёс на з. " + s.GroundedWheelCount + "   воздух "
                    + s.AirTimeSeconds.ToString("F2") + " с",
                "газ " + s.ThrottleApplied.ToString("F2")
                    + "   тормоз " + s.BrakeApplied.ToString("F2")
                    + "   вес " + s.WeightShift.ToString("+0.00;-0.00; 0.00"),
                "подвеска    зад " + s.RearCompressionM.ToString("F3")
                    + " м   перед " + s.FrontCompressionM.ToString("F3") + " м",
                "прижим      зад " + s.RearNormalLoadN.ToString("F0")
                    + " Н   перед " + s.FrontNormalLoadN.ToString("F0") + " Н",
                "букс зада   " + s.RearSlip.ToString("F2")
                    + "   выдержка отказа " + s.FailHoldSeconds.ToString("F2") + " с",
                "дистанция   " + s.DistanceM.ToString("F1") + " м"
                    + (s.Failure != BikeFailure.None ? "   ОТКАЗ: " + s.Failure : "")
            };
        }
    }
}
