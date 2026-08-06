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
    /// РАСКЛАДКА — схема `btn4` исходника: четыре кнопки, ГАЗ и ТОРМОЗ справа, НОС↑ и
    /// НОС↓ парой слева. Подробности и причина выбора — в <see cref="TouchButtons"/>.
    ///
    /// Первая редакция делила экран пополам и брала наклон драгом пальца. Это была моя
    /// выдумка при том, что схема уже была выбрана пользователем и записана в исходнике
    /// как дефолт. Вердикт живого теста: «управление очень неудобное, старое намного
    /// удобнее». Схему нельзя изобретать заново, если она уже выбрана.
    /// </summary>
    public class PlayInput : IBikeInputSource
    {
        /// <summary>Кнопки схемы btn4. Общие с HUD: он рисует ровно то, что опрашивается.</summary>
        public readonly TouchButtons Buttons = new TouchButtons();

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

            // ---- тач (телефон): схема btn4 из исходника ----
            //
            // Раскладка и зоны — в TouchButtons. Здесь только опрос: каждый палец
            // проверяется против КАЖДОЙ кнопки, поэтому нажатия складываются (газ + наклон
            // одновременно) и палец, соскользнувший с кнопки, честно её отпускает.
            var touches = UnityEngine.Input.touchCount;
            if (touches > 0)
            {
                var gas = false; var brake = false; var up = false; var down = false;
                for (var i = 0; i < touches; i++)
                {
                    var t = UnityEngine.Input.GetTouch(i);
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                    var pos = t.position;
                    if (TouchButtons.Hit(Buttons.Gas.R, pos)) gas = true;
                    if (TouchButtons.Hit(Buttons.Brake.R, pos)) brake = true;
                    if (TouchButtons.Hit(Buttons.NoseUp.R, pos)) up = true;
                    if (TouchButtons.Hit(Buttons.NoseDown.R, pos)) down = true;
                }

                if (gas) s.Throttle = 1f;
                if (brake) s.Brake = 1f;
                // НОС↑ = вес НАЗАД = отрицательный Lean; НОС↓ = вес ВПЕРЁД.
                // Удержание даёт плавный набор веса: рампа 0.55 с живёт в физике.
                var lean = (down ? 1f : 0f) - (up ? 1f : 0f);
                if (Mathf.Abs(lean) > 0f) s.Lean = lean;

                Buttons.Gas.Active = gas;
                Buttons.Brake.Active = brake;
                Buttons.NoseUp.Active = up;
                Buttons.NoseDown.Active = down;
            }
            else
            {
                Buttons.Gas.Active = false; Buttons.Brake.Active = false;
                Buttons.NoseUp.Active = false; Buttons.NoseDown.Active = false;
            }
#endif
            return s;
        }
    }
}
