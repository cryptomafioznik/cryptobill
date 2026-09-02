using System.Collections;
using System.Globalization;
using System.IO;
using ChartRunner.Bike;
using ChartRunner.Input;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Снимает кадры ИЗ ИГРЫ и меряет композицию по фактическому кадру, а не по расчёту.
    ///
    /// Зачем отдельный класс, если размер камеры считается формулой: формула говорит, каким
    /// кадр ДОЛЖЕН быть, а этот класс — каким он ВЫШЕЛ. Между ними помещаются aspect
    /// реального окна, safe area, положение байка на склоне и всё, что формула не знает.
    /// Правило проекта: визуальная приёмка только в конечном размере устройства.
    ///
    /// Включается аргументом командной строки `-shots`. В обычном запуске не существует.
    /// </summary>
    public class ScreenshotProbe : MonoBehaviour
    {
        public static bool Requested
        {
            get
            {
                var args = System.Environment.GetCommandLineArgs();
                for (var i = 0; i < args.Length; i++)
                    if (args[i] == "-shots" || args[i] == "-shotsUi") return true;
                return false;
            }
        }

        private PlaySession _session;
        private BikeController _controller;
        private ChaseCamera _chase;
        private string _dir;

        public static ScreenshotProbe Attach(PlaySession session, BikeController controller,
            ChaseCamera chase)
        {
            var probe = session.gameObject.AddComponent<ScreenshotProbe>();
            probe._session = session;
            probe._controller = controller;
            probe._chase = chase;
            return probe;
        }

        private void Start()
        {
            _dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "shots");
            Directory.CreateDirectory(_dir);
            StartCoroutine(Run());
        }

        /// <summary>-shotsUi: снять экраны потока (howto/title/setup/garage/dead), а не заезд.</summary>
        public static bool UiRequested
        {
            get
            {
                foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-shotsUi") return true;
                return false;
            }
        }

        private IEnumerator Run()
        {
            if (UiRequested) { yield return RunUi(); yield break; }
            // Скриптовый пилот: он НЕ доказывает играбельность и здесь не для этого —
            // ему нужно только провезти байк по трассе, чтобы кадры были не с места старта.
            _controller.SetInput(ScriptedBikeInput.HoldThrottle());

            Debug.Log("SHOTS: экран " + Screen.width + "×" + Screen.height
                      + ", aspect " + (Screen.width / (float)Screen.height).ToString("0.000"));

            yield return Frame("00-start", 0.6f);
            yield return Frame("01-roll", 2.5f);
            yield return Frame("02-woops", 5.0f);
            yield return Frame("03-climb", 9.0f);
            yield return Frame("04-late", 14.0f);

            Debug.Log("SHOTS: готово, кадры в " + _dir);
            Application.Quit(0);
        }

        private IEnumerator RunUi()
        {
            _controller.SetInput(ScriptedBikeInput.HoldThrottle());
            var screens = new[]
            {
                PlaySession.Screen.Howto, PlaySession.Screen.Title, PlaySession.Screen.Setup,
                PlaySession.Screen.Garage, PlaySession.Screen.Settings
            };
            var i = 0;
            foreach (var sc in screens)
            {
                PlaySession.Flow = sc;
                // Терминалу нужны превью с Binance — ждём сеть, иначе кадр покажет «загрузка…».
                yield return new WaitForSeconds(sc == PlaySession.Screen.Setup ? 6f : 0.6f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui-" + (i++) + "-" + sc.ToString().ToLower() + ".png"));
                yield return new WaitForEndOfFrame(); yield return null; yield return null;
            }
            // Экран итога: даём проехать, потом ликвидируем.
            PlaySession.Flow = PlaySession.Screen.Play;
            _session.SendMessage("Unfreeze");
            yield return new WaitForSeconds(4f);
            _controller.Liquidate();
            yield return new WaitForSeconds(1.2f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui-9-dead.png"));
            yield return new WaitForEndOfFrame(); yield return null; yield return null;
            Debug.Log("SHOTS: UI готово, кадры в " + _dir);
            Application.Quit(0);
        }

        private IEnumerator Frame(string label, float atSeconds)
        {
            while (Time.timeSinceLevelLoad < atSeconds) yield return null;

            Measure(label);
            var path = Path.Combine(_dir, label + ".png");
            ScreenCapture.CaptureScreenshot(path);
            // Съёмка происходит в конце кадра, файл появляется на следующем — ждём его,
            // иначе Quit обгонит запись и половина кадров не доедет.
            yield return new WaitForEndOfFrame();
            yield return null;
            yield return null;
        }

        /// <summary>
        /// Доля высоты экрана под героя — измеренная ПРОЕКЦИЕЙ, а не формулой камеры.
        /// Берём габарит по всем рендерам байка: это и есть то, что видит глаз.
        /// </summary>
        private void Measure(string label)
        {
            var cam = _chase.Cam;
            var renderers = _controller.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0 || cam == null) return;

            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            var lo = cam.WorldToScreenPoint(new Vector3(b.min.x, b.min.y, 0f));
            var hi = cam.WorldToScreenPoint(new Vector3(b.max.x, b.max.y, 0f));
            var hPx = Mathf.Abs(hi.y - lo.y);
            var wPx = Mathf.Abs(hi.x - lo.x);

            var st = _controller.State;
            Debug.Log("SHOT " + label
                      + " | герой " + (hPx / Screen.height * 100f).ToString("0.0", CultureInfo.InvariantCulture)
                      + " % высоты (" + hPx.ToString("0") + " px из " + Screen.height + ")"
                      + ", ширина " + (wPx / Screen.width * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " %"
                      + " | габарит " + b.size.y.ToString("0.00", CultureInfo.InvariantCulture) + " м"
                      + " | x=" + st.PositionXM.ToString("0.0", CultureInfo.InvariantCulture) + " м"
                      + ", v=" + st.SpeedMPerS.ToString("0.0", CultureInfo.InvariantCulture) + " м/с"
                      + ", тангаж " + (st.PitchRelRad * Mathf.Rad2Deg).ToString("0.0", CultureInfo.InvariantCulture) + "°"
                      + ", " + st.State);
        }
    }
}
