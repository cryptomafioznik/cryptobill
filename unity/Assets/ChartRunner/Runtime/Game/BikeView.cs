using System.Collections.Generic;
using ChartRunner.Bike;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Видимый байк: СПРАЙТЫ исходника + ЖИВОЙ райдер на двухкостном IK.
    ///
    /// Байк и колёса — арт браузерной игры, выгнанный её же функциями (bikeSprites,
    /// КРОСС 250) в 4× разрешении. Это валидированная годами картинка; рисовать вместо
    /// неё новую геометрию — ошибка того же класса, что выброшенная схема btn4.
    ///
    /// Правило «картинка входит в игру только РАЗОБРАННОЙ» соблюдено: корпус и каждое
    /// колесо — отдельные части на физических костях. Колёса вращаются и ходят по
    /// подвеске физикой (они отдельные тела на WheelJoint2D), поэтому ход подвески
    /// виден: колесо-спрайт едет относительно вилки, запечённой в корпус, — ровно как
    /// в исходнике (cmpF/cmpR).
    ///
    /// РАЙДЕР — не спрайт, а код: конечности каждый кадр досчитываются IK от таза до
    /// руля и подножек (порт скелета b155). Перенос веса двигает таз, всё остальное
    /// следует — игрок читает своё действие в кадре. Экип — порт b241/b243: райдер
    /// СВЕТЛЫЙ доминантный (белое джерси пробивается на тёмных байках), цвет байка
    /// живёт только в акцентах.
    /// </summary>
    public class BikeView : MonoBehaviour
    {
        // ---- палитра райдера (b241/b243) ----
        private static readonly Color Jersey = Hex(0xEE, 0xF2, 0xF7);
        private static readonly Color JerseyFar = Hex(0xC4, 0xCD, 0xDC);
        private static readonly Color Pants = Hex(0x25, 0x2B, 0x37);
        private static readonly Color PantsFar = Hex(0x16, 0x1B, 0x24);
        private static readonly Color Glove = Hex(0x2A, 0x31, 0x40);
        private static readonly Color Boot = Hex(0x1A, 0x1F, 0x2A);
        private static readonly Color GearPad = Hex(0xC2, 0xCC, 0xDB);
        private static readonly Color Visor = Hex(0x14, 0x18, 0x1C);
        /// <summary>Акцент байка/скина на экипе райдера (b243) — из Economy.</summary>
        private Color Accent = new Color(80 / 255f, 1f, 170 / 255f, 1f);

        private BikeController _controller;
        private BikeRig _rig;
        private MeshFilter _riderFilter;
        private Mesh _riderMesh;

        private float _poseShift;
        private float _air;

        // ---- геометрия исходника ----
        private const float K = UnitsContract.PxToM;
        /// <summary>Оси спрайта на ±27 px, физика на ±26 px: корпус поджимается на их отношение.</summary>
        private const float FitScale = 26f / 27f;
        /// <summary>Origin спрайта был на 15.5 px выше линии осей (CgAboveAxle исходника).</summary>
        private const float OriginLiftM = 15.5f * K * FitScale;

        /// <summary>Точка (px исходника, ось Y вниз) → локальные метры шасси (ось Y вверх).</summary>
        private static Vector2 P(float px, float py)
        {
            return new Vector2(px * K * FitScale, OriginLiftM - py * K * FitScale);
        }

        public static BikeView Attach(BikeController controller)
        {
            var view = controller.gameObject.AddComponent<BikeView>();
            view._controller = controller;
            view._rig = controller.GetComponent<BikeRig>();
            view.BuildStatic();
            return view;
        }

        private void BuildStatic()
        {
            var p = _rig.Profile;
            Accent = Meta.Economy.AccentColor();

            // Спрайты ВЫБРАННОГО байка и скина (b143/b176): выгнаны из браузерной игры
            // её же функциями — 7 байков × 4 скина, имя = индекс + скин.
            var bike = Meta.Economy.SelBike;
            var skin = Meta.Economy.CurrentSkin;
            var tag = bike + (string.IsNullOrEmpty(skin) ? "" : "-" + skin);
            var wheelR = Meta.Economy.WheelSpriteR(Meta.Economy.Bikes[bike].Type);

            // Колёса: спрайты на ФИЗИЧЕСКИХ телах колёс — вращение и ход подвески
            // приходят из решателя, а не из анимации.
            AttachSprite(_rig.RearWheel.transform, "Art/wheel-" + tag + "-rear", 9,
                p.wheelRadiusM / (wheelR * K));
            AttachSprite(_rig.FrontWheel.transform, "Art/wheel-" + tag + "-front", 10,
                p.wheelRadiusM / (wheelR * K));

            // Корпус — на шасси, поднят так, чтобы оси спрайта легли на оси физики.
            var body = AttachSprite(transform, "Art/bike-" + tag + "-body", 11, FitScale);
            if (body != null) body.transform.localPosition = new Vector3(0f, OriginLiftM, -0.01f);

            var host = new GameObject("RiderView");
            host.transform.SetParent(transform, false);
            _riderMesh = new Mesh { name = "Rider" };
            _riderFilter = host.AddComponent<MeshFilter>();
            _riderFilter.sharedMesh = _riderMesh;
            var mr = host.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Shapes.VertexColorMaterial;
            mr.sortingOrder = 12;
        }

        private GameObject AttachSprite(Transform host, string resource, int order, float scale)
        {
            var sprite = Resources.Load<Sprite>(resource);
            if (sprite == null)
            {
                // Молчать нельзя: без спрайта герой невидим, а сборка «зелёная».
                Debug.LogError("BikeView: спрайт не найден в Resources: " + resource);
                return null;
            }
            var go = new GameObject(resource);
            go.transform.SetParent(host, false);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return go;
        }

        // ================= райдер =================

        private void LateUpdate()
        {
            if (_controller == null || _riderFilter == null) return;

            var st = _controller.State;
            _poseShift = Mathf.MoveTowards(_poseShift, st.WeightShift, Time.deltaTime * 4.5f);
            // Стойка в воздухе: привстаёт (b155 dynAir). Плавно, не рывком по кадру отрыва.
            _air = Mathf.MoveTowards(_air, st.IsGrounded ? 0f : 1f, Time.deltaTime * 3.5f);
            RebuildRider(_poseShift, _air);
        }

        /// <summary>
        /// Скелет b155 в позе dirt: таз → торс под углом → руки на руль, ноги на пеги.
        /// Все опорные точки — координаты СПРАЙТА, поэтому кисти лежат на нарисованном
        /// руле, а стопы на нарисованных подножках без подгонки на глаз.
        /// </summary>
        private void RebuildRider(float shift, float air)
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            // ПОЗА ПО ТИПУ БАЙКА (PS исходника, стр. 4985-4988): dirt = MX-стойка,
            // bike = педали, scooter/moped = городская посадка прямее, sport = глубокий тук.
            // riderShiftPx 2.5 — микро-сдвиг переноса веса (b148: райдер жёстко на байке).
            var type = Meta.Economy.Bikes[Mathf.Clamp(Meta.Economy.SelBike, 0, Meta.Economy.Bikes.Length - 1)].Type;
            float px0, py0, ta0, bx, by, f1x, f2x;
            switch (type)
            {
                case "bike": px0 = -8f; py0 = -11f; ta0 = 0.30f; bx = 16f; by = -15f; f1x = -2f; f2x = 1.5f; break;
                case "scooter": case "moped": px0 = -4f; py0 = -8f; ta0 = 0.10f; bx = 10f; by = -15f; f1x = -6f; f2x = -2.5f; break;
                case "sport": px0 = -5f; py0 = -6.5f; ta0 = 0.86f; bx = 15f; by = -11f; f1x = -7f; f2x = -3.5f; break;
                default: px0 = -6f; py0 = -8.5f; ta0 = 0.34f; bx = 21f; by = -18f; f1x = -2f; f2x = 1.5f; break;
            }
            var hipPx = px0 + shift * 2.5f - 2f * air;
            var hipPy = py0 - 4f * air;
            var ta = ta0 + shift * 0.10f - 0.10f * air;

            var hip = P(hipPx, hipPy);
            var shoulder = P(hipPx + Mathf.Sin(ta) * 13f, hipPy - Mathf.Cos(ta) * 13f);
            var grip = P(bx + 0.3f, by - 1.6f);
            var pegFar = P(f1x, 8f);
            var pegNear = P(f2x, 8f);

            // Длины сегментов исходника: руки 7.6/8.2, ноги 9/9.6 px.
            var armL1 = 7.6f * K; var armL2 = 8.2f * K;
            var legL1 = 9.0f * K; var legL2 = 9.6f * K;

            var farSh = shoulder + new Vector2(-1.2f * K, 0f);
            var farHip = hip + new Vector2(-1.5f * K, 0f);
            var elbowF = Solve(farSh, grip + new Vector2(-1.5f * K, 0f), armL1, armL2, -1f);
            var kneeF = Solve(farHip, pegFar, legL1, legL2, +1f);
            var elbowN = Solve(shoulder, grip, armL1, armL2, -1f);
            var kneeN = Solve(hip, pegNear, legL1, legL2, +1f);

            // ---- дальняя сторона (до торса) ----
            Limb(v, c, t, farSh, elbowF, grip + new Vector2(-1.5f * K, 0f),
                4.2f * K, 3.8f * K, JerseyFar, Glove);
            Limb(v, c, t, farHip, kneeF, pegFar, 5.0f * K, 4.4f * K, PantsFar, Boot);

            // ---- торс: белое джерси, трапеция таз→плечи ----
            var up = (shoulder - hip).normalized;
            var side = new Vector2(-up.y, up.x);
            Quad(v, c, t,
                hip - side * 4.6f * K, hip + side * 4.6f * K,
                shoulder + side * 4.2f * K, shoulder - side * 4.2f * K, Jersey);
            // Акцент-полоса по спине — цвет байка на экипе (b243).
            Shapes.AddBar(v, c, t, hip - side * 3.4f * K, shoulder - side * 3.2f * K,
                1.4f * K, new Color(Accent.r, Accent.g, Accent.b, 0.85f));
            // Тень под грудью — объём корпуса без света.
            Shapes.AddBar(v, c, t, hip + side * 2.4f * K, shoulder + side * 2.8f * K,
                1.6f * K, new Color(0f, 0f, 0.02f, 0.18f));

            // ---- ближняя нога: тёмные штаны, наколенник, ботинок ----
            Limb(v, c, t, hip, kneeN, pegNear, 5.5f * K, 4.8f * K, Pants, Boot);
            Shapes.AddDisc(v, c, t, kneeN, 2.4f * K, GearPad, 10);
            // Ботинок: явный блок на пеге.
            Quad(v, c, t,
                pegNear + new Vector2(-3.4f * K, -1.6f * K), pegNear + new Vector2(4.4f * K, -1.6f * K),
                pegNear + new Vector2(4.4f * K, 1.8f * K), pegNear + new Vector2(-3.4f * K, 1.8f * K),
                Boot);

            // ---- ближняя рука: джерси, перчатка на грипсе ----
            Limb(v, c, t, shoulder, elbowN, grip, 4.8f * K, 4.2f * K, Jersey, Glove);
            Shapes.AddDisc(v, c, t, grip, 2.2f * K, Glove, 8);

            // ---- шлем: белый фулфейс с козырьком и визором (rcHead) ----
            var head = P(hipPx + Mathf.Sin(ta) * 13f + 2.2f, hipPy - Mathf.Cos(ta) * 13f - 8.5f);
            var hr = 6.2f * K;
            // шея
            Shapes.AddBar(v, c, t, shoulder, head, 3.4f * K, Jersey);
            Shapes.AddDisc(v, c, t, head, hr, Hex(0xF4, 0xF7, 0xFB), 16);
            // козырёк вперёд-вверх — силуэтная подпись кросса
            Shapes.AddFan(v, c, t, new[]
            {
                head + new Vector2(hr * 0.2f, hr * 0.86f),
                head + new Vector2(hr * 1.5f, hr * 0.62f),
                head + new Vector2(hr * 1.2f, hr * 0.3f),
                head + new Vector2(hr * 0.1f, hr * 0.5f)
            }, Hex(0xE8, 0xED, 0xF5));
            // визор: тёмное окно, смотрит вперёд
            Shapes.AddFan(v, c, t, new[]
            {
                head + new Vector2(hr * 0.05f, hr * 0.42f),
                head + new Vector2(hr * 0.95f, hr * 0.22f),
                head + new Vector2(hr * 0.88f, hr * -0.24f),
                head + new Vector2(hr * 0.02f, hr * -0.10f)
            }, Visor);
            // акцент-дуга по затылку
            for (var i = 0; i < 5; i++)
            {
                var a0 = Mathf.PI * (0.55f + i * 0.09f);
                var a1 = Mathf.PI * (0.55f + (i + 1) * 0.09f);
                Shapes.AddBar(v, c, t,
                    head + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * (hr - 1.2f * K),
                    head + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * (hr - 1.2f * K),
                    1.4f * K, new Color(Accent.r, Accent.g, Accent.b, 0.9f));
            }

            _riderMesh.Clear();
            _riderMesh.SetVertices(v);
            _riderMesh.SetColors(c);
            _riderMesh.SetTriangles(t, 0);
            _riderMesh.RecalculateBounds();
            _riderFilter.sharedMesh = _riderMesh;
        }

        /// <summary>Конечность: два сегмента убывающей толщины + скругление сустава.</summary>
        private static void Limb(List<Vector3> v, List<Color> c, List<int> t,
            Vector2 root, Vector2 joint, Vector2 end, float w1, float w2, Color col, Color tip)
        {
            Shapes.AddBar(v, c, t, root, joint, w1, col);
            Shapes.AddBar(v, c, t, joint, end, w2, col);
            Shapes.AddDisc(v, c, t, joint, w1 * 0.52f, col, 8);
            Shapes.AddDisc(v, c, t, root, w1 * 0.55f, col, 8);
            Shapes.AddDisc(v, c, t, end, w2 * 0.5f, tip, 8);
        }

        private static void Quad(List<Vector3> v, List<Color> c, List<int> t,
            Vector2 a, Vector2 b, Vector2 d, Vector2 e, Color col)
        {
            var i0 = v.Count;
            v.Add(a); c.Add(Shapes.V(col));
            v.Add(b); c.Add(Shapes.V(col));
            v.Add(d); c.Add(Shapes.V(col));
            v.Add(e); c.Add(Shapes.V(col));
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1);
            t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
        }

        private static Vector2 Solve(Vector2 root, Vector2 target, float l1, float l2, float bendSign)
        {
            var delta = target - root;
            var d = delta.magnitude;
            var min = Mathf.Abs(l1 - l2) + 1e-3f;
            var max = l1 + l2 - 1e-3f;
            d = Mathf.Clamp(d, min, max);
            if (delta.sqrMagnitude < 1e-8f) delta = new Vector2(0f, -1f);
            var u = delta.normalized;

            var a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            var h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            var n = new Vector2(-u.y, u.x) * bendSign;
            return root + u * a + n * h;
        }

        private static Color Hex(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }
    }
}
