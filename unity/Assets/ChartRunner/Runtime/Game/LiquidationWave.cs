using System.Collections.Generic;
using ChartRunner.Bike;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// ВОЛНА ЛИКВИДАЦИИ — догоняющий дамп. Порт из toys/chartrider.html (стр. 1772-1774).
    ///
    /// Зачем она в списке первой. Разбор топовых игр жанра даёт один общий знаменатель:
    /// у заезда должна быть СТАВКА, иначе это симулятор, а не игра. В Hill Climb Racing
    /// ставку держит топливо, в Alto's — погоня и провалы, в Trials — таймер и медали.
    /// У нас ставка уже была придумана и годами обкатана в браузерной версии: сзади
    /// наступает ликвидация, и она ускоряется с дистанцией. Ехать медленно = проиграть.
    ///
    /// Именно её отсутствие делало Unity-сборку хуже браузерной при лучшей физике:
    /// ехать было НЕ ЗА ЧЕМ. Скорость ничего не решала, потому что никто не догонял.
    ///
    /// ФОРМУЛА — исходника, без сочинительства (px/кадр при 60 fps → м/с через контракт):
    ///     v = (waveSpeed + (min(d/150, 4.8) + max(0, (d-700)/620)) * waveAccel)
    ///         * eraWave * lerp(0.6, 1, clamp(d / easeInDist))
    /// где d — пройденная дистанция в метрах. Последний множитель — «мягкий въезд»:
    /// первые 350 м волна идёт на 60 % силы, чтобы игрок успел взять управление.
    /// </summary>
    public class LiquidationWave : MonoBehaviour
    {
        // ---- константы TUNE исходника ----
        private const float WaveSpeedPxPerFrame = 0.43f;
        private const float WaveAccel = 0.4f;
        private const float EaseInDistM = 350f;

        /// <summary>Насколько позади игрока волна стартует, в долях ширины кадра (W*0.95).</summary>
        private const float StartLeadScreens = 0.95f;

        private BikeController _bike;
        private Camera _camera;
        private float _eraWave = 1f;

        /// <summary>
        /// Множитель сложности — произведение из формулы исходника (стр. 1773):
        /// волатильность монеты (tickerVolMul) × (1 − 0.07·ЩИТ) × (1 + 0.02·max(0, lev − safeLev)).
        /// Плечо выше потолка железа разгоняет волну — маховик b77.
        /// </summary>
        public float DifficultyMul = 1f;

        /// <summary>
        /// Браузерный метр = 10 px исходника (dist = bike.x/10). Все дистанционные константы
        /// формулы (150, 700, 620, 350) заданы В НИХ, и подставлять сюда реальные метры —
        /// значит замедлить разгон волны в 3.5 раза и не заметить.
        /// </summary>
        public const float BrowserM = 10f * UnitsContract.PxToM;

        private Mesh _mesh;
        private MeshFilter _filter;
        private float _time;

        /// <summary>Мировая X волны. Догнала игрока — ликвидация.</summary>
        public float PositionXM { get; private set; }

        /// <summary>
        /// Мировая X байка. Берётся с ТРАНСФОРМА, а не из BikeState: состояние
        /// публикуется только после первого шага физики, и на кадре создания оно нулевое.
        /// Гейт волны поймал это сразу — волна вставала в −6.5 м вместо позиции спавна,
        /// то есть стартовый отрыв был вычислен от нуля мира, а не от игрока.
        /// </summary>
        private float BikeX => _bike != null ? _bike.transform.position.x : 0f;

        /// <summary>Отрыв в метрах: сколько ещё есть до ликвидации.</summary>
        public float LeadM => BikeX - PositionXM;

        /// <summary>0..1 — насколько близко волна. Для тревоги в HUD и в звуке.</summary>
        public float Danger
        {
            get
            {
                if (_camera == null) return 0f;
                var screenW = _camera.orthographicSize * 2f * _camera.aspect;
                return Mathf.Clamp01(1f - LeadM / Mathf.Max(0.01f, screenW * StartLeadScreens));
            }
        }

        public static LiquidationWave Attach(BikeController bike, Camera cam, Transform world,
            float eraWave = 1f)
        {
            var go = new GameObject("LiquidationWave");
            go.transform.SetParent(world, false);
            var w = go.AddComponent<LiquidationWave>();
            w._bike = bike;
            w._camera = cam;
            w._eraWave = eraWave;
            w._mesh = new Mesh { name = "Wave" };
            w._filter = go.AddComponent<MeshFilter>();
            w._filter.sharedMesh = w._mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Shapes.VertexColorMaterial;
            // Перед всем миром и перед декой, но ЗА байком: волна пожирает картинку,
            // а герой остаётся читаемым до последнего кадра.
            mr.sortingOrder = 8;
            w.Reset();
            return w;
        }

        /// <summary>Ставит волну в стартовую позицию позади игрока.</summary>
        public void Reset()
        {
            var screenW = _camera != null ? _camera.orthographicSize * 2f * _camera.aspect : 8f;
            PositionXM = BikeX - screenW * StartLeadScreens;
        }

        /// <summary>Откат волны назад — награда за событие (порт: памп/флеш дают отрыв).</summary>
        public void PushBack(float screens)
        {
            var screenW = _camera.orthographicSize * 2f * _camera.aspect;
            PositionXM = Mathf.Min(PositionXM, BikeX - screenW * screens);
        }

        private void FixedUpdate()
        {
            if (_bike == null || _bike.Halted) return;

            var d = Mathf.Max(0f, _bike.State.DistanceM) / BrowserM;
            var pxPerFrame = (WaveSpeedPxPerFrame
                              + (Mathf.Min(d / 150f, 4.8f) + Mathf.Max(0f, (d - 700f) / 620f)) * WaveAccel)
                             * _eraWave * DifficultyMul
                             * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(d / EaseInDistM));

            PositionXM += pxPerFrame * UnitsContract.PxPerFrameToMPerS * Time.fixedDeltaTime;

            // Догнала — ликвидация. Это правило заезда, а не физика, поэтому вход внешний.
            if (PositionXM >= BikeX) _bike.Liquidate();
        }

        private void LateUpdate()
        {
            if (_camera == null) return;
            _time += Time.deltaTime;
            Rebuild();
        }

        /// <summary>
        /// Стена дампа: непрозрачная масса слева, градиентный подпор, рваный светящийся
        /// фронт. Фронт дрожит двумя несоизмеримыми синусами — как в исходнике: ровная
        /// линия читалась бы стеной уровня, а не наступающей ликвидацией.
        /// </summary>
        private void Rebuild()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            var camPos = _camera.transform.position;
            var halfH = _camera.orthographicSize;
            var halfW = halfH * _camera.aspect;
            var y0 = camPos.y - halfH * 1.4f;
            var y1 = camPos.y + halfH * 1.4f;
            var left = camPos.x - halfW * 1.6f;

            // Рисуем только когда фронт вошёл в кадр с запасом — иначе меш пуст.
            if (PositionXM > left)
            {
                var mass = new Color(18 / 255f, 2 / 255f, 8 / 255f, 0.84f);
                Quad(v, c, t, left, PositionXM, y0, y1, mass, mass);

                // Градиентный подпор перед фронтом: 150 px исходника.
                var glowW = 150f * UnitsContract.PxToM;
                var g0 = new Color(150 / 255f, 16 / 255f, 36 / 255f, 0f);
                var g1 = new Color(220 / 255f, 34 / 255f, 58 / 255f, 0.55f);
                Quad(v, c, t, PositionXM - glowW, PositionXM, y0, y1, g0, g1);
            }

            // Фронт: рваная светящаяся линия. Двойной штрих (подложка + ядро) вместо
            // размытия — тот же приём, которым исходник снимал цену shadowBlur.
            var step = 15f * UnitsContract.PxToM;
            var prev = Vector2.zero;
            var first = true;
            for (var y = y0; y <= y1; y += step)
            {
                var jx = PositionXM
                         + Mathf.Sin(y * 0.06f / UnitsContract.PxToM + _time * 18f) * 11f * UnitsContract.PxToM
                         + Mathf.Sin(y * 0.14f / UnitsContract.PxToM + _time * 12f) * 6f * UnitsContract.PxToM;
                var p = new Vector2(jx, y);
                if (!first)
                {
                    Shapes.AddBar(v, c, t, prev, p, 9f * UnitsContract.PxToM,
                        new Color(1f, 48 / 255f, 80 / 255f, 0.30f));
                    Shapes.AddBar(v, c, t, prev, p, 3f * UnitsContract.PxToM,
                        new Color(1f, 74 / 255f, 96 / 255f, 0.95f));
                }
                prev = p;
                first = false;
            }

            _mesh.Clear();
            _mesh.SetVertices(v);
            _mesh.SetColors(c);
            _mesh.SetTriangles(t, 0);
            _mesh.RecalculateBounds();
            _filter.sharedMesh = _mesh;
        }

        private static void Quad(List<Vector3> v, List<Color> c, List<int> t,
            float x0, float x1, float y0, float y1, Color cl, Color cr)
        {
            var i0 = v.Count;
            v.Add(new Vector3(x0, y0, 0f)); c.Add(Shapes.V(cl));
            v.Add(new Vector3(x0, y1, 0f)); c.Add(Shapes.V(cl));
            v.Add(new Vector3(x1, y1, 0f)); c.Add(Shapes.V(cr));
            v.Add(new Vector3(x1, y0, 0f)); c.Add(Shapes.V(cr));
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
        }
    }
}
