using UnityEngine;

namespace ChartRunner.Input
{
    /// <summary>
    /// Команды игрока за один физ-шаг. Всё аналоговое: жанр про дозировку, и дискретный
    /// наклон ±1 из дефолтной схемы исходника (`btn4`) был отдельным дефектом (аудит §6.9).
    /// </summary>
    public struct BikeInputState
    {
        /// <summary>0..1, сырое нажатие газа.</summary>
        public float Throttle;

        /// <summary>0..1, тормоз.</summary>
        public float Brake;

        /// <summary>−1..+1. Положительное = вес ВПЕРЁД (нос вниз), отрицательное = НАЗАД (вилли).</summary>
        public float Lean;

        /// <summary>Запрос прыжка. Работает только если профиль его разрешает.</summary>
        public bool JumpPressed;

        public static BikeInputState Neutral => new BikeInputState();
    }

    /// <summary>Источник ввода. Позволяет подменять клавиатуру/тач скриптовым пилотом в тестах.</summary>
    public interface IBikeInputSource
    {
        BikeInputState Read();
    }

    /// <summary>
    /// Клавиатура — для разработки и headless-прогонов. Намеренно НЕ через Input System actions:
    /// в batchmode их граф не поднимается, а тестам нужен предсказуемый путь.
    /// Продуктовая схема ввода на устройстве — <see cref="TouchBikeInput"/>.
    /// </summary>
    public class KeyboardBikeInput : IBikeInputSource
    {
        public BikeInputState Read()
        {
            var s = new BikeInputState();
#if ENABLE_LEGACY_INPUT_MANAGER
            s.Throttle = UnityEngine.Input.GetKey(KeyCode.UpArrow) || UnityEngine.Input.GetKey(KeyCode.W) ? 1f : 0f;
            s.Brake = UnityEngine.Input.GetKey(KeyCode.DownArrow) || UnityEngine.Input.GetKey(KeyCode.S) ? 1f : 0f;
            var lean = 0f;
            if (UnityEngine.Input.GetKey(KeyCode.RightArrow) || UnityEngine.Input.GetKey(KeyCode.D)) lean += 1f;
            if (UnityEngine.Input.GetKey(KeyCode.LeftArrow) || UnityEngine.Input.GetKey(KeyCode.A)) lean -= 1f;
            s.Lean = lean;
            s.JumpPressed = UnityEngine.Input.GetKeyDown(KeyCode.Space);
#endif
            return s;
        }
    }

    /// <summary>
    /// Тач: левая половина экрана — тормоз, правая — газ, вертикальный сдвиг пальца — наклон.
    ///
    /// Два дефекта исходника, которые здесь не воспроизводятся:
    /// 1. Наклон был дискретным ±1 (аудит §6.9) — здесь аналоговый по смещению пальца.
    /// 2. `touchmove` не переоценивал попадание в кнопку: палец, соскользнувший с газа,
    ///    продолжал держать газ, и перевести палец с газа на тормоз драгом было нельзя.
    ///    Здесь зона определяется положением пальца КАЖДЫЙ кадр, поэтому соскальзывание
    ///    честно меняет команду.
    ///
    /// Раскладка предварительная: композиция под portrait — это P1, и трогать её до
    /// подтверждения размера героя на устройстве запрещено правилом №15.
    /// </summary>
    public class TouchBikeInput : IBikeInputSource
    {
        private readonly float _leanTravelPx;

        public TouchBikeInput(float leanTravelPx = 90f)
        {
            // LEANRANGE = 90 px в исходнике: столько драга = полный наклон.
            _leanTravelPx = leanTravelPx;
        }

        public BikeInputState Read()
        {
            var s = new BikeInputState();
#if ENABLE_LEGACY_INPUT_MANAGER
            var half = Screen.width * 0.5f;
            for (var i = 0; i < UnityEngine.Input.touchCount; i++)
            {
                var t = UnityEngine.Input.GetTouch(i);
                // Зона пересчитывается по ТЕКУЩЕЙ позиции, а не по позиции нажатия.
                if (t.position.x >= half) s.Throttle = 1f;
                else s.Brake = 1f;

                var dy = t.position.y - (t.position.y - t.deltaPosition.y);
                s.Lean += Mathf.Clamp(dy / _leanTravelPx, -1f, 1f);
            }
            s.Lean = Mathf.Clamp(s.Lean, -1f, 1f);
#endif
            return s;
        }
    }

    /// <summary>
    /// Скриптовый пилот. Нужен для батареи тестов и для теста G (окно навыка).
    ///
    /// ВАЖНОЕ ОГРАНИЧЕНИЕ, которое надо помнить каждый раз при чтении его результатов:
    /// скриптовый пилот НЕ доказывает играбельность (правило №13 в памяти проекта).
    /// Измерено на исходнике: пилот «только газ» прошёл 41.6 % трассы, живой человек — 22 %.
    /// Пилот годится только для регрессии и для сравнения политик между собой.
    /// </summary>
    public class ScriptedBikeInput : IBikeInputSource
    {
        private readonly System.Func<BikeInputState> _policy;

        public ScriptedBikeInput(System.Func<BikeInputState> policy)
        {
            _policy = policy;
        }

        public BikeInputState Read() => _policy();

        /// <summary>Политика NEUTRAL теста G: газ всегда, вес ровно ноль.</summary>
        public static ScriptedBikeInput HoldThrottle()
        {
            return new ScriptedBikeInput(() => new BikeInputState { Throttle = 1f });
        }
    }
}
