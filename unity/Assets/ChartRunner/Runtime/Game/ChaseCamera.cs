using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Камера портретной композиции. Задаётся не «красиво», а ДВУМЯ ЧИСЛАМИ, которые можно
    /// проверить скриншотом (критерии остановки из аудита):
    ///
    ///   1. герой занимает ≥ 12 % высоты экрана;
    ///   2. игровая полоса (герой + дорога + видимый рельеф впереди) ≥ 35 % высоты экрана.
    ///
    /// Оплачено месяцами: биомеханику с амплитудой 1-3 px доводили, когда герой занимал
    /// 4.7 % высоты. Поэтому размер камеры выводится ИЗ высоты героя, а не подбирается.
    ///
    /// НАЙДЕННЫЙ КОНФЛИКТ — измерен, а не предположен. В портрете 430×932 ширина кадра =
    /// 0.923 × высоты, поэтому обзор вперёд и размер героя связаны жёстко и обратно:
    ///
    ///   герой 13.0 % → кадр 15.0 × 13.8 м → впереди 4.8 м = 0.73 с на 6.27 м/с
    ///   герой 12.5 % → кадр 15.6 × 14.4 м → впереди 5.5 м = 0.87 с
    ///   герой  7.5 % → кадр 26.1 × 24.1 м → впереди 9.4 м = 1.50 с   ← ниже гейта 12 %
    ///
    /// То есть «герой ≥ 12 %» и «обзор 1.5 с» ОДНОВРЕМЕННО в портрете недостижимы. Это
    /// свойство формата, а не настройки. 1.5 с при этом было числом «на глаз»; настоящий
    /// бюджет выводится из кода: рампа переноса веса 0.55 с до полного плюс ~0.25 с на
    /// восприятие и решение = 0.80 с. Именно на нём стоит гейт <see cref="MinReactionSeconds"/>.
    ///
    /// Второе слагаемое обзора — упреждение по скорости: камера уезжает вперёд на
    /// v × <see cref="LookAheadSeconds"/>, то есть обзор РАСТЁТ ровно тогда, когда нужен.
    /// </summary>
    public class ChaseCamera : MonoBehaviour
    {
        /// <summary>Рампа переноса веса до полного, секунды (BikeController.ApplyWeightShift).</summary>
        public const float WeightShiftRampSeconds = 0.55f;

        /// <summary>Восприятие + решение, секунды. Консервативная оценка.</summary>
        public const float PerceptionSeconds = 0.25f;

        /// <summary>Минимальный обзор впереди в секундах: успеть увидеть И доложить вес.</summary>
        public const float MinReactionSeconds = WeightShiftRampSeconds + PerceptionSeconds;

        // Значения композиции — здесь, потому что по ним же считает гейт в PlaySceneBuilder.

        /// <summary>
        /// Высота силуэта героя, метры. ИЗМЕРЕНА проекцией габарита рендеров в снятом кадре
        /// (ScreenshotProbe), а не взята из пропорций: предположение было 1.95 м, факт 2.13 м.
        /// </summary>
        public const float DefaultHeroHeightM = 2.13f;

        public const float DefaultHeroFraction = 0.125f;

        /// <summary>Положение байка по ширине кадра на стоянке.</summary>
        public const float DefaultBikeScreenXRest = 0.40f;

        /// <summary>Положение байка по ширине кадра на верхней скорости.</summary>
        public const float DefaultBikeScreenXFast = 0.30f;

        [Tooltip("Высота силуэта героя (байк с райдером в стойке), метры. Измерена в кадре.")]
        public float HeroHeightM = DefaultHeroHeightM;

        [Tooltip("Целевая доля высоты экрана под героя. Гейт: ≥ 0.12.")]
        public float HeroScreenFraction = DefaultHeroFraction;

        [Tooltip("Положение байка по ширине кадра на стоянке: 0 = слева, 1 = справа.")]
        public float BikeScreenXRest = DefaultBikeScreenXRest;

        [Tooltip("Положение байка по ширине кадра на верхней скорости. Байк едет к левому " +
                 "краю по мере разгона — обзор вперёд растёт ровно тогда, когда нужен.")]
        public float BikeScreenXFast = DefaultBikeScreenXFast;

        [Tooltip("Где байк стоит по высоте кадра.")]
        public float BikeScreenY = 0.42f;

        [Tooltip("Верхняя скорость байка, м/с. Задаёт, при какой скорости достигается XFast.")]
        public float TopSpeedMPerS = 6.271f;

        public float FollowLerpX = 12f;
        public float FollowLerpY = 6f;

        private Transform _target;
        private Camera _cam;
        private float _y;
        private bool _snapped;

        public Camera Cam => _cam;

        public void Bind(Camera cam, Transform target)
        {
            _cam = cam;
            _target = target;
            _cam.orthographic = true;
            ApplySize();
            _snapped = false;
        }

        private void ApplySize()
        {
            // orthographicSize — ПОЛОВИНА высоты кадра в метрах.
            _cam.orthographicSize = HeroHeightM / (2f * Mathf.Max(0.01f, HeroScreenFraction));
        }

        /// <summary>Доля высоты экрана, которую занимает герой при текущей настройке.</summary>
        public float MeasuredHeroFraction => _cam == null
            ? 0f
            : HeroHeightM / (2f * _cam.orthographicSize);

        /// <summary>Сколько метров трассы видно ВПЕРЕДИ байка на стоящем байке.</summary>
        public float LookAheadMetres => AheadAtSpeed(0f);

        /// <summary>
        /// Положение байка по ширине кадра при данной скорости.
        ///
        /// ПОЧЕМУ ЭТО ИНТЕРПОЛЯЦИЯ, А НЕ СЛАГАЕМОЕ. В первой редакции упреждение было
        /// аддитивным: центр камеры уезжал на v × 0.32 с. Снятый кадр показал, чем это
        /// кончается — на 6.2 м/с сдвиг составил 2.0 м при полуширине кадра 3.5 м, то есть
        /// 29 % ширины, и байк уехал за левый край экрана. Формула не имела верхней границы,
        /// а портрет узкий. Интерполяция ограничена по построению.
        /// </summary>
        public float BikeScreenXAt(float speedMPerS)
        {
            var k = Mathf.Clamp01(Mathf.Abs(speedMPerS) / Mathf.Max(0.1f, TopSpeedMPerS));
            return Mathf.Lerp(BikeScreenXRest, BikeScreenXFast, k);
        }

        /// <summary>Обзор впереди на скорости, метры.</summary>
        public float AheadAtSpeed(float speedMPerS)
        {
            if (_cam == null) return 0f;
            var halfW = _cam.orthographicSize * _cam.aspect;
            return halfW * 2f * (1f - BikeScreenXAt(speedMPerS));
        }

        /// <summary>Обзор впереди в СЕКУНДАХ хода на данной скорости — величина для гейта.</summary>
        public float AheadSecondsAt(float speedMPerS)
        {
            var s = Mathf.Max(0.1f, speedMPerS);
            return AheadAtSpeed(s) / s;
        }

        private void LateUpdate()
        {
            if (_cam == null || _target == null) return;
            ApplySize();

            var halfH = _cam.orthographicSize;
            var halfW = halfH * _cam.aspect;

            var vel = 0f;
            var rb = _target.GetComponent<Rigidbody2D>();
            if (rb != null) vel = rb.linearVelocity.x;

            // Смещение центра кадра так, чтобы байк оказался в заданной точке экрана.
            var offsetX = (0.5f - BikeScreenXAt(vel)) * halfW * 2f;
            var offsetY = (0.5f - BikeScreenY) * halfH * 2f;

            var wantX = _target.position.x + offsetX;
            var wantY = _target.position.y + offsetY;

            if (!_snapped)
            {
                transform.position = new Vector3(wantX, wantY, -20f);
                _y = wantY;
                _snapped = true;
                return;
            }

            // По вертикали медленнее, чем по горизонтали: рельеф качает байк каждый метр,
            // и камера, повторяющая это один в один, вызывает тошноту и прячет собственный
            // тангаж байка — а тангаж здесь главный носитель информации.
            _y = Mathf.Lerp(_y, wantY, 1f - Mathf.Exp(-FollowLerpY * Time.deltaTime));
            var x = Mathf.Lerp(transform.position.x, wantX, 1f - Mathf.Exp(-FollowLerpX * Time.deltaTime));
            transform.position = new Vector3(x, _y, -20f);
        }

        public void Snap()
        {
            _snapped = false;
        }
    }
}
