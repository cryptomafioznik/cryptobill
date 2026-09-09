using System.Collections;
using System.Globalization;
using System.IO;
using ChartRunner.Bike;
using ChartRunner.Input;
using ChartRunner.Tuning;
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
                    if (args[i] == "-shots" || args[i] == "-shotsUi" || args[i] == "-trace") return true;
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
#if UNITY_IOS
            // На устройстве текущая папка — «/», писать туда нельзя. Контейнер приложения
            // читается снаружи: devicectl device copy from --domain-type appDataContainer.
            _dir = Path.Combine(Application.persistentDataPath, "shots");
#else
            _dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "shots");
#endif
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

        /// <summary>-superSize N: рендер кадра в N× разрешения (для скриншотов стора точного размера).</summary>
        private static int SuperSize
        {
            get
            {
                var a = System.Environment.GetCommandLineArgs();
                for (var i = 0; i < a.Length - 1; i++) if (a[i] == "-superSize" && int.TryParse(a[i + 1], out var n)) return Mathf.Clamp(n, 1, 4);
                return 1;
            }
        }

        /// <summary>-trace: трасса фолбэка BTC, газ в пол, CSV состояния каждый физ-шаг.
        /// Доп. аргументы: -hop T (секунда хопа), -hold N (кадров удержания наклона после хопа),
        /// -lean ±1 (−1 = нос вверх), -dur S (длительность). Тот же протокол, что __mrun в браузере.</summary>
        public static bool TraceRequested
        {
            get { foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-trace") return true; return false; }
        }

        private static float ArgF(string key, float def)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < a.Length - 1; i++)
                if (a[i] == key && float.TryParse(a[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
            return def;
        }

        private IEnumerator RunTrace()
        {
            var hopAt = ArgF("-hop", -1f); var hold = ArgF("-hold", 0f); var lean = ArgF("-lean", -1f); var dur = ArgF("-dur", 25f);
            // -leanAt T: удержание наклона N=-hold кадров с секунды T БЕЗ хопа (вилли/стоппи на земле);
            // -brakeAt T: тормоз (газ отпущен) N=-hold кадров с секунды T.
            var leanAt = ArgF("-leanAt", -1f); var brakeAt = ArgF("-brakeAt", -1f);
            // -pilot N: политика веса на КРУТОМ подъёме (slope > climbFrom), контр-приём исходника.
            //   0 = не наклонять (газ-и-держи), +1 = вес ВПЕРЁД, −1 = вес НАЗАД.
            // Меряет окно навыка: меняет ли ввод игрока исход на одной и той же трассе.
            var pilot = ArgF("-pilot", 0f);
            if (hopAt >= 0f) _controller.Profile.jumpButtonEnabled = true;
            // Экспериментальные ручки для поиска потерь энергии вращения (не игровые).
            var rig = _controller.GetComponent<BikeRig>();
            var wheelMass = ArgF("-wheelMass", -1f);
            if (wheelMass > 0f && rig != null) { rig.RearWheel.mass = wheelMass; rig.FrontWheel.mass = wheelMass; }
            var chassisDamp = ArgF("-chassisAngDamp", -1f);
            if (chassisDamp >= 0f && rig != null) rig.Chassis.angularDamping = chassisDamp;
            Debug.Log("TRACE: wheelMass=" + (rig != null ? rig.RearWheel.mass.ToString("0.0", CultureInfo.InvariantCulture) : "?")
                      + " chassisAngDamp=" + (rig != null ? rig.Chassis.angularDamping.ToString("0.000", CultureInfo.InvariantCulture) : "?")
                      + " I=" + (rig != null ? rig.Chassis.inertia.ToString("0.0", CultureInfo.InvariantCulture) : "?"));
            var t0 = Time.fixedTime; var jumped = false;
            _controller.SetInput(new ScriptedBikeInput(() =>
            {
                var t = Time.fixedTime - t0;
                var s = new BikeInputState { Throttle = 1f };
                if (hopAt >= 0f && !jumped && t >= hopAt) { s.JumpPressed = true; jumped = true; }
                if (hopAt >= 0f && t >= hopAt + 2f / 60f && t < hopAt + (2f + hold) / 60f) s.Lean = lean;
                if (leanAt >= 0f && t >= leanAt && t < leanAt + hold / 60f) s.Lean = lean;
                if (brakeAt >= 0f && t >= brakeAt && t < brakeAt + hold / 60f) { s.Brake = 1f; s.Throttle = 0f; }
                if (Mathf.Abs(pilot) > 0.01f)
                {
                    var sl = _controller.Terrain.SlopeAt(_controller.transform.position.x);
                    if (sl > _controller.Profile.climbFromRad) s.Lean = pilot;
                }
                return s;
            }));
            var K = UnitsContract.PxToM; var F = UnitsContract.SimFrameSeconds; var ci = CultureInfo.InvariantCulture;
            var nodes = _session.TrackNodesPx;
            if (nodes != null)
            {
                var nb = new System.Text.StringBuilder("i,x_px,y_px\n");
                for (var i = 0; i < nodes.Length; i++) nb.Append(i).Append(',').Append(nodes[i].x.ToString("0.0", ci)).Append(',').Append(nodes[i].y.ToString("0.0", ci)).Append('\n');
                File.WriteAllText(Path.Combine(_dir, "nodes.csv"), nb.ToString());
            }
            var sb = new System.Text.StringBuilder("t,x_px,y_px,vx_pxf,vy_pxf,grounded,pitch_deg,pitchRel_deg,rearN,frontN,throttle,state,fail,air_s,compR_px,compF_px,angV_dps\n");
            while (Time.fixedTime - t0 < dur)
            {
                yield return new WaitForFixedUpdate();
                var st = _controller.State;
                sb.Append((Time.fixedTime - t0).ToString("0.0000", ci)).Append(',')
                  .Append((st.PositionXM / K).ToString("0.00", ci)).Append(',')
                  .Append((st.PositionYM / K).ToString("0.00", ci)).Append(',')
                  .Append((st.SpeedMPerS * F / K).ToString("0.000", ci)).Append(',')
                  .Append((st.VerticalSpeedMPerS * F / K).ToString("0.000", ci)).Append(',')
                  .Append(st.GroundedWheelCount).Append(',')
                  .Append((st.PitchRad * Mathf.Rad2Deg).ToString("0.0", ci)).Append(',')
                  .Append((st.PitchRelRad * Mathf.Rad2Deg).ToString("0.0", ci)).Append(',')
                  .Append(st.RearNormalLoadN.ToString("0", ci)).Append(',')
                  .Append(st.FrontNormalLoadN.ToString("0", ci)).Append(',')
                  .Append(st.ThrottleApplied.ToString("0.00", ci)).Append(',')
                  .Append(st.State).Append(',').Append(st.Failure).Append(',')
                  .Append(st.AirTimeSeconds.ToString("0.000", ci)).Append(',')
                  .Append((st.RearCompressionM / K).ToString("0.00", ci)).Append(',')
                  .Append((st.FrontCompressionM / K).ToString("0.00", ci)).Append(',')
                  .Append((st.AngularVelocityRadPerS * Mathf.Rad2Deg).ToString("0.0", ci)).Append('\n');
            }
            File.WriteAllText(Path.Combine(_dir, "trace.csv"), sb.ToString());
            Debug.Log("TRACE: готово, " + Path.Combine(_dir, "trace.csv"));
            Application.Quit(0);
        }

        private IEnumerator Run()
        {
            if (TraceRequested) { yield return RunTrace(); yield break; }
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
                PlaySession.Screen.Garage, PlaySession.Screen.Bikes, PlaySession.Screen.Path, PlaySession.Screen.Settings
            };
            var i = 0;
            foreach (var sc in screens)
            {
                PlaySession.Flow = sc;
                // Терминалу нужны превью с Binance — ждём сеть, иначе кадр покажет «загрузка…».
                yield return new WaitForSeconds(sc == PlaySession.Screen.Setup ? 6f : 0.6f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui-" + (i++) + "-" + sc.ToString().ToLower() + ".png"), SuperSize);
                yield return new WaitForEndOfFrame(); yield return null; yield return null;
            }
            // Экран итога: даём проехать, потом ликвидируем.
            PlaySession.Flow = PlaySession.Screen.Play;
            _session.SendMessage("Unfreeze");
            yield return new WaitForSeconds(4f);
            _controller.Liquidate();
            yield return new WaitForSeconds(1.2f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "ui-9-dead.png"), SuperSize);
            yield return new WaitForEndOfFrame(); yield return null; yield return null;
            Debug.Log("SHOTS: UI готово, кадры в " + _dir);
            Application.Quit(0);
        }

        private IEnumerator Frame(string label, float atSeconds)
        {
            while (Time.timeSinceLevelLoad < atSeconds) yield return null;

            Measure(label);
            var path = Path.Combine(_dir, label + ".png");
            ScreenCapture.CaptureScreenshot(path, SuperSize);
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
