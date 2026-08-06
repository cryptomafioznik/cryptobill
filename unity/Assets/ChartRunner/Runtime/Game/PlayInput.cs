using ChartRunner.Input;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Ввод для ЖИВОГО игрока: клавиатура на маке и тач на телефоне из одного класса.
    ///
    /// ПОЧЕМУ НЕ <see cref="TouchBikeInput"/>. Там наклон считался из
    /// <c>t.deltaPosition.y</c> — то есть из смещения пальца ЗА КАДР. Такой наклон живёт
    /// только пока палец движется: остановил палец — вес мгновенно вернулся в ноль.
    /// А механика этой игры — «держать вес сзади на подъёме», то есть УДЕРЖАНИЕ. С дельтой
    /// удержать вес физически нельзя, надо бесконечно возить пальцем.
    /// Здесь наклон = положение пальца относительно точки касания, и он ДЕРЖИТСЯ.
    ///
    /// Раскладка (портрет):
    ///   правая половина — газ, левая — тормоз, зона определяется каждый кадр по текущей
    ///   позиции пальца (соскользнул с газа на тормоз — команда честно поменялась);
    ///   вертикальный увод пальца от точки касания — перенос веса, ВВЕРХ = вес НАЗАД.
    ///
    /// Вверх = назад, потому что это жест «потянуть на себя»: подъём носа на руле.
    /// Если фил окажется обратным, менять здесь одну строку <see cref="LeanSign"/>.
    /// </summary>
    public class PlayInput : IBikeInputSource
    {
        /// <summary>Сколько пикселей увода пальца = полный перенос веса.</summary>
        public float LeanTravelPx = 110f;

        /// <summary>+1: палец вверх = вес назад (нос вверх). −1 — обратная схема.</summary>
        public float LeanSign = 1f;

        /// <summary>Мёртвая зона у точки касания, px: чтобы газ не давал случайного наклона.</summary>
        public float LeanDeadZonePx = 14f;

        private readonly float[] _startY = new float[8];
        private readonly bool[] _active = new bool[8];

        public BikeInputState Read()
        {
            var s = new BikeInputState();

#if ENABLE_LEGACY_INPUT_MANAGER
            // ---- клавиатура (мак, редактор) ----
            var kThrottle = UnityEngine.Input.GetKey(KeyCode.UpArrow) || UnityEngine.Input.GetKey(KeyCode.W);
            var kBrake = UnityEngine.Input.GetKey(KeyCode.DownArrow) || UnityEngine.Input.GetKey(KeyCode.S);
            if (kThrottle) s.Throttle = 1f;
            if (kBrake) s.Brake = 1f;

            var kLean = 0f;
            if (UnityEngine.Input.GetKey(KeyCode.RightArrow) || UnityEngine.Input.GetKey(KeyCode.D)) kLean += 1f;
            if (UnityEngine.Input.GetKey(KeyCode.LeftArrow) || UnityEngine.Input.GetKey(KeyCode.A)) kLean -= 1f;
            s.Lean = kLean;
            s.JumpPressed = UnityEngine.Input.GetKeyDown(KeyCode.Space);

            // ---- тач (телефон) ----
            var touches = UnityEngine.Input.touchCount;
            // Три пальца зарезервированы под переключение оверлея телеметрии — управление игнорируем,
            // иначе жест диагностики попутно давал бы газ.
            if (touches > 0 && touches < 3)
            {
                var half = Screen.width * 0.5f;
                var lean = 0f;
                var leanSources = 0;

                for (var i = 0; i < touches && i < _startY.Length; i++)
                {
                    var t = UnityEngine.Input.GetTouch(i);
                    var slot = Mathf.Clamp(t.fingerId, 0, _startY.Length - 1);

                    if (t.phase == TouchPhase.Began || !_active[slot])
                    {
                        _startY[slot] = t.position.y;
                        _active[slot] = true;
                    }

                    if (t.position.x >= half) s.Throttle = 1f;
                    else s.Brake = 1f;

                    var dy = t.position.y - _startY[slot];
                    var mag = Mathf.Abs(dy);
                    if (mag > LeanDeadZonePx)
                    {
                        var eff = (mag - LeanDeadZonePx) * Mathf.Sign(dy);
                        lean += Mathf.Clamp(eff / LeanTravelPx, -1f, 1f);
                        leanSources++;
                    }

                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                        _active[slot] = false;
                }

                if (leanSources > 0)
                {
                    // Вес НАЗАД = отрицательный Lean в контракте BikeInputState.
                    s.Lean = Mathf.Clamp(-LeanSign * lean / leanSources, -1f, 1f);
                }
            }
            else if (touches == 0)
            {
                for (var i = 0; i < _active.Length; i++) _active[i] = false;
            }
#endif
            return s;
        }
    }
}
