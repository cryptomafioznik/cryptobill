using UnityEngine;

namespace ChartRunner.Telemetry
{
    /// <summary>
    /// Рисует телеметрию на экране. Включается и выключается — на устройстве в релизе не нужна.
    ///
    /// Намеренно на IMGUI (`OnGUI`), а не на Canvas/UI Toolkit: оверлею не нужны префабы,
    /// шрифты и раскладка, а любой из них — это ассеты и композиция, то есть работа, которая
    /// ночью запрещена. IMGUI даёт диагностику нулём ассетов.
    ///
    /// Переключатели: клавиша F1 на маке и тап ТРЕМЯ пальцами на устройстве. Три пальца, потому
    /// что одним и двумя игрок управляет байком, и жест не должен конфликтовать с газом.
    ///
    /// Рендер сюда попадает только чтением: класс не трогает ни физику, ни ввод. На это есть
    /// тест — траектории с включённой и выключенной телеметрией обязаны совпасть.
    /// </summary>
    [RequireComponent(typeof(BikeTelemetry))]
    public class TelemetryOverlay : MonoBehaviour
    {
        [Tooltip("Видим ли оверлей. Сбор телеметрии управляется отдельно, в BikeTelemetry.")]
        public bool Visible = true;

        public KeyCode ToggleKey = KeyCode.F1;

        [Tooltip("Сколько пальцев на экране переключают оверлей. Один и два заняты управлением.")]
        public int ToggleTouchCount = 3;

        private BikeTelemetry _telemetry;
        private GUIStyle _style;
        private bool _touchLatch;

        private void Awake()
        {
            _telemetry = GetComponent<BikeTelemetry>();
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (UnityEngine.Input.GetKeyDown(ToggleKey)) Toggle();

            var touches = UnityEngine.Input.touchCount;
            if (touches >= ToggleTouchCount && !_touchLatch)
            {
                _touchLatch = true;
                Toggle();
            }
            else if (touches < ToggleTouchCount)
            {
                _touchLatch = false;
            }
#endif
        }

        /// <summary>Переключает видимость. Вынесено отдельно, чтобы это можно было позвать из теста.</summary>
        public void Toggle() => Visible = !Visible;

        private void OnGUI()
        {
            if (!Visible || _telemetry == null) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    richText = false,
                    alignment = TextAnchor.UpperLeft
                };
                _style.normal.textColor = new Color(0.62f, 1f, 0.90f);
            }

            var lines = _telemetry.OverlayLines();
            var lineHeight = _style.fontSize + 4;
            var height = lines.Length * lineHeight + 14;
            const float width = 330f;

            GUI.color = new Color(0f, 0f, 0f, 0.66f);
            GUI.DrawTexture(new Rect(6f, 6f, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            for (var i = 0; i < lines.Length; i++)
                GUI.Label(new Rect(14f, 10f + i * lineHeight, width - 16f, lineHeight),
                    lines[i], _style);
        }
    }
}
