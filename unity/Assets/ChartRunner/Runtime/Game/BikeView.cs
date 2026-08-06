using System.Collections.Generic;
using ChartRunner.Bike;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Видимый байк с райдером — ПАРАМЕТРИЧЕСКАЯ ГЕОМЕТРИЯ, не картинка.
    ///
    /// Главное решение здесь — райдер приколочен к байку ПРИЧИННО, а не нарисован в позе:
    /// перенос веса двигает ТАЗ, а руки остаются на руле и ноги на подножках, потому что
    /// конечности каждый кадр досчитываются двухкостным IK. Поэтому «вес назад» видно как
    /// движение тела, а не как смену спрайта, и игрок читает своё действие в кадре.
    ///
    /// Это прямой урок cutout-рига: две попытки собрать райдера из PNG-частей развалились,
    /// потому что вилка и маятник растягивались между повёрнутым пивотом и неповёрнутой осью.
    /// У геометрии этой проблемы нет — точки крепления вычисляются, а не подгоняются.
    ///
    /// Размеры сняты с колёсной базы 1.47 м, то есть с той же геометрии, что и физика.
    /// Если менять пропорции — менять здесь, а не масштабом Transform: масштаб врёт
    /// про толщину линий.
    /// </summary>
    public class BikeView : MonoBehaviour
    {
        // ---- палитра: КОНТРАЖУР ----
        //
        // Солнце низко и ПОЗАДИ игрока (SkyView), поэтому герой обращён к камере теневой
        // стороной. Отсюда два следствия, и оба причинные, а не декоративные:
        //   1. собственные цвета байка и райдера почти чёрные — на свету они не находятся;
        //   2. весь цвет уходит в ОБВОДКУ по верхним и задним кромкам, потому что именно
        //      их задевает свет, идущий из-за спины.
        // Это и есть тот контраст, которого не хватало первому кадру: герой — тёмный силуэт
        // на самом светлом месте кадра, с тонкой горячей линией по краю.
        public Color FrameColor = new Color(0.055f, 0.055f, 0.075f, 1f);
        public Color FrameMid = new Color(0.10f, 0.10f, 0.13f, 1f);
        public Color TyreColor = new Color(0.035f, 0.035f, 0.045f, 1f);
        public Color RimColor = new Color(0.20f, 0.20f, 0.25f, 1f);
        public Color RiderColor = new Color(0.075f, 0.075f, 0.10f, 1f);

        /// <summary>Горячая кромка от низкого солнца сзади. Главный носитель силуэта.</summary>
        public Color RimLight = new Color(1f, 0.72f, 0.38f, 1f);

        /// <summary>Холодный отражённый свет неба сверху — вторая, слабая грань.</summary>
        public Color SkyBounce = new Color(0.46f, 0.56f, 0.78f, 1f);

        /// <summary>Толщина обводки, метры. На 12.5 % высоты экрана это ~2 px — предел различимости.</summary>
        public float RimWidthM = 0.045f;

        private BikeController _controller;
        private BikeRig _rig;
        private Transform _riderHost;
        private MeshFilter _riderFilter;
        private Mesh _riderMesh;
        private MeshFilter _linkageFilter;
        private Mesh _linkageMesh;

        private float _halfWb;
        private float _wheelR;

        // Сглаженная поза: физический перенос веса уже отрампован (0.55 с), но телу нужен
        // ещё чуть более мягкий ход, иначе поза дёргается на кадрах контакта.
        private float _poseShift;

        public static BikeView Attach(BikeController controller)
        {
            var rig = controller.GetComponent<BikeRig>();
            var view = controller.gameObject.AddComponent<BikeView>();
            view._controller = controller;
            view._rig = rig;
            view.BuildStatic();
            return view;
        }

        private void BuildStatic()
        {
            var p = _rig.Profile;
            _halfWb = p.halfWheelbaseM;
            _wheelR = p.wheelRadiusM;

            BuildWheel(_rig.RearWheel.transform, _wheelR, "RearWheelView");
            BuildWheel(_rig.FrontWheel.transform, _wheelR, "FrontWheelView");
            BuildFrame(transform);

            var linkHost = new GameObject("LinkageView");
            linkHost.transform.SetParent(transform, false);
            _linkageMesh = new Mesh { name = "Linkage" };
            var lmf = linkHost.AddComponent<MeshFilter>();
            lmf.sharedMesh = _linkageMesh;
            var lmr = linkHost.AddComponent<MeshRenderer>();
            lmr.sharedMaterial = Shapes.VertexColorMaterial;
            // Между колёсами (10) и рамой (11): подвеска уходит ЗА раму и ПЕРЕД колесом.
            lmr.sortingOrder = 10;
            _linkageFilter = lmf;

            var host = new GameObject("RiderView");
            host.transform.SetParent(transform, false);
            _riderHost = host.transform;
            _riderMesh = new Mesh { name = "Rider" };
            var mf = host.AddComponent<MeshFilter>();
            mf.sharedMesh = _riderMesh;
            var mr = host.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Shapes.VertexColorMaterial;
            mr.sortingOrder = 12;
            _riderFilter = mf;
        }

        // ================= колесо =================

        private void BuildWheel(Transform host, float radius, string name)
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            // Покрышка: кольцо. Ширина протектора — видимая доля радиуса, иначе колесо
            // на конечном размере читается сплошным пятном.
            AddRing(v, c, t, radius, radius * 0.72f, TyreColor, 26);

            // Грунтозацепы: короткие штрихи по ободу. Их задача — сделать ВРАЩЕНИЕ видимым.
            // Без них колесо крутится незаметно, и игрок не читает пробуксовку.
            for (var i = 0; i < 10; i++)
            {
                var a = i / 10f * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Shapes.AddBar(v, c, t, d * (radius * 0.74f), d * (radius * 0.99f), radius * 0.14f,
                    new Color(0.20f, 0.22f, 0.26f, 1f));
            }

            // Обод и спицы.
            AddRing(v, c, t, radius * 0.70f, radius * 0.63f, RimColor, 22);
            for (var i = 0; i < 6; i++)
            {
                var a = i / 6f * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Shapes.AddBar(v, c, t, d * (radius * 0.10f), d * (radius * 0.66f), radius * 0.045f,
                    new Color(0.52f, 0.57f, 0.64f, 1f));
            }
            Shapes.AddDisc(v, c, t, Vector2.zero, radius * 0.16f, RimColor);

            var go = Shapes.Create(name, host, Shapes.Build(name, v, c, t), 10);
            go.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        }

        private static void AddRing(List<Vector3> v, List<Color> c, List<int> t,
            float outer, float inner, Color color, int segments)
        {
            var i0 = v.Count;
            for (var i = 0; i < segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                var dx = Mathf.Cos(a);
                var dy = Mathf.Sin(a);
                v.Add(new Vector3(dx * outer, dy * outer, 0f));
                v.Add(new Vector3(dx * inner, dy * inner, 0f));
                c.Add(Shapes.V(color));
                c.Add(Shapes.V(color));
            }
            for (var i = 0; i < segments; i++)
            {
                var o0 = i0 + i * 2;
                var in0 = i0 + i * 2 + 1;
                var o1 = i0 + (i * 2 + 2) % (segments * 2);
                var in1 = i0 + (i * 2 + 3) % (segments * 2);
                t.Add(o0); t.Add(o1); t.Add(in0);
                t.Add(in0); t.Add(o1); t.Add(in1);
            }
        }

        // ================= рама =================

        // Опорные точки байка в локальных метрах шасси (нуль — середина между осями,
        // y = 0 на линии осей). Сняты с пропорций YZ250F под базу 1.47 м.
        private Vector2 RearAxle => new Vector2(-_halfWb, 0f);
        private Vector2 FrontAxle => new Vector2(_halfWb, 0f);
        private Vector2 SwingPivot => new Vector2(-0.10f, 0.17f);
        private Vector2 Peg => new Vector2(-0.17f, 0.20f);
        private Vector2 SeatBack => new Vector2(-0.62f, 0.60f);
        private Vector2 SeatFront => new Vector2(-0.10f, 0.63f);
        private Vector2 SteerHead => new Vector2(0.47f, 0.66f);
        private Vector2 Bar => new Vector2(0.40f, 0.95f);

        private void BuildFrame(Transform host)
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            var dark = FrameColor;
            var mid = FrameMid;

            // ---- тело байка: почти чёрное, потому что оно в тени ----
            //
            // МАЯТНИКА И ВИЛКИ ЗДЕСЬ НЕТ. Они пересобираются каждый кадр в RebuildLinkage,
            // потому что целятся в ФАКТИЧЕСКОЕ положение колёс, а колёса ходят на подвеске.
            // Нарисовать их от фиксированных точек шасси — это ровно тот дефект, на котором
            // дважды развалился cutout-риг: «вилка и маятник растягивались между повёрнутым
            // пивотом и неповёрнутой осью». Здесь точка крепления ВЫЧИСЛЯЕТСЯ, а не задаётся.

            // Двигатель и рама — сплошной объём, читается блоком на любом размере.
            Shapes.AddFan(v, c, t, new[]
            {
                new Vector2(-0.30f, 0.10f), new Vector2(0.16f, 0.12f), new Vector2(0.30f, 0.42f),
                new Vector2(0.02f, 0.52f), new Vector2(-0.28f, 0.44f)
            }, dark);

            // Бак и сиденье: длинная горизонталь силуэта.
            Shapes.AddFan(v, c, t, new[]
            {
                SeatBack, SeatFront, new Vector2(0.22f, 0.72f), SteerHead,
                new Vector2(0.20f, 0.56f), new Vector2(-0.16f, 0.50f), new Vector2(-0.58f, 0.50f)
            }, dark);

            // Щитки — «уши» силуэта: по ним байк узнаётся мотоциклом, а не прямоугольником.
            Shapes.AddFan(v, c, t, new[]
            {
                new Vector2(-0.58f, 0.56f), new Vector2(-0.94f, 0.74f),
                new Vector2(-0.98f, 0.63f), new Vector2(-0.58f, 0.46f)
            }, mid);
            Shapes.AddFan(v, c, t, new[]
            {
                new Vector2(0.44f, 0.74f), new Vector2(0.90f, 0.88f),
                new Vector2(0.92f, 0.77f), new Vector2(0.48f, 0.64f)
            }, mid);

            // Рулевая колонка и руль.
            Shapes.AddBar(v, c, t, SteerHead, Bar, 0.075f, mid);
            Shapes.AddBar(v, c, t, Bar + new Vector2(-0.16f, 0f), Bar + new Vector2(0.13f, 0.02f),
                0.06f, mid);

            // Подножка.
            Shapes.AddBar(v, c, t, SwingPivot, Peg, 0.05f, mid);
            Shapes.AddBar(v, c, t, Peg + new Vector2(-0.10f, 0f), Peg + new Vector2(0.10f, 0f),
                0.05f, mid);

            // Выхлоп.
            Shapes.AddBar(v, c, t, new Vector2(0.10f, 0.30f), new Vector2(-0.72f, 0.52f), 0.08f, mid);

            // ---- ГОРЯЧАЯ КРОМКА ----
            //
            // Солнце низко и позади (слева по ходу), поэтому свет задевает ЗАДНИЕ и ВЕРХНИЕ
            // контуры. Обводка идёт только по ним — не по всему контуру: обводка по кругу
            // читается как наклейка, обводка по одной стороне читается как свет.
            var rw = RimWidthM;
            Shapes.AddBar(v, c, t, new Vector2(-0.94f, 0.74f), new Vector2(-0.58f, 0.56f), rw, RimLight);
            Shapes.AddBar(v, c, t, new Vector2(-0.58f, 0.56f), SeatBack, rw, RimLight);
            Shapes.AddBar(v, c, t, SeatBack, SeatFront, rw, RimLight);
            Shapes.AddBar(v, c, t, SeatFront, new Vector2(0.22f, 0.72f), rw * 0.85f, RimLight);
            Shapes.AddBar(v, c, t, new Vector2(0.44f, 0.76f), new Vector2(0.90f, 0.90f),
                rw * 0.8f, RimLight);
            // Обводки по маятнику здесь НЕТ намеренно: она проходила внутри силуэта и на
            // конечном размере читалась палкой поперёк байка, а не светом по краю.

            // Холодная грань от неба сверху — вторая, слабее. Она отделяет руль от фона.
            Shapes.AddBar(v, c, t, Bar + new Vector2(-0.16f, 0.03f), Bar + new Vector2(0.13f, 0.05f),
                rw * 0.7f, SkyBounce);
            Shapes.AddBar(v, c, t, new Vector2(0.22f, 0.74f), SteerHead + new Vector2(0f, 0.03f),
                rw * 0.6f, SkyBounce);

            Shapes.Create("FrameView", host, Shapes.Build("FrameView", v, c, t), 11);
        }

        // ================= райдер =================

        private void LateUpdate()
        {
            if (_controller == null || _riderFilter == null) return;

            var st = _controller.State;
            // Поза следует за физическим переносом веса, а не за кнопкой: то, что видит
            // игрок, обязано быть тем, что реально приложено к телу.
            _poseShift = Mathf.MoveTowards(_poseShift, st.WeightShift, Time.deltaTime * 4.5f);
            RebuildLinkage();
            RebuildRider(_poseShift, st);
        }

        /// <summary>
        /// Маятник и вилка, нацеленные в ФАКТИЧЕСКИЕ оси колёс.
        ///
        /// Колёса — отдельные тела на WheelJoint2D, они ходят по подвеске независимо от
        /// шасси. Поэтому их положение берётся из мира и переводится в локальные координаты
        /// шасси КАЖДЫЙ кадр. Следствие, ради которого всё и делается: ход подвески видно.
        /// Маятник поворачивается на кочке, вилка складывается на приземлении — то есть
        /// игрок читает нагрузку, а не догадывается о ней.
        /// </summary>
        private void RebuildLinkage()
        {
            if (_linkageFilter == null || _rig == null) return;

            var rear = (Vector2)transform.InverseTransformPoint(_rig.RearWheel.position);
            var front = (Vector2)transform.InverseTransformPoint(_rig.FrontWheel.position);

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            var mid = FrameMid;

            // Маятник: пивот → фактическая задняя ось.
            Shapes.AddBar(v, c, t, SwingPivot, rear, 0.11f, mid);
            Shapes.AddDisc(v, c, t, SwingPivot, 0.06f, mid, 10);

            // Вилка: рулевая колонка → фактическая передняя ось. Угол наклона вилки
            // получается сам из двух точек, поэтому rake не нужно задавать отдельным
            // числом и незачем держать его в синхроне вручную.
            Shapes.AddBar(v, c, t, SteerHead, front, 0.085f, mid);
            Shapes.AddBar(v, c, t, SteerHead + new Vector2(0.02f, -0.02f),
                Vector2.Lerp(SteerHead, front, 0.55f), 0.13f, FrameColor);

            // Горячая кромка по задней стороне маятника — единственная обводка здесь:
            // остальные линии подвески проходят внутри силуэта.
            Shapes.AddBar(v, c, t, SwingPivot + new Vector2(0f, 0.055f),
                rear + new Vector2(0f, 0.055f), RimWidthM * 0.8f, RimLight);

            _linkageMesh.Clear();
            _linkageMesh.SetVertices(v);
            _linkageMesh.SetColors(c);
            _linkageMesh.SetTriangles(t, 0);
            _linkageMesh.RecalculateBounds();
            _linkageFilter.sharedMesh = _linkageMesh;
        }

        private void RebuildRider(float shift, Bike.BikeState st)
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            // ТАЗ — единственная точка, которую двигает игрок. Всё остальное следует.
            // Вперёд (положительный shift) = над баком; назад = за сиденье.
            var hipNeutral = new Vector2(-0.20f, 0.92f);
            var hip = hipNeutral + new Vector2(shift * 0.40f, -Mathf.Abs(shift) * 0.10f);

            // Плечи следуют за тазом частично: корпус наклоняется, а не переносится целиком.
            var shoulder = hip + new Vector2(0.34f + shift * 0.10f, 0.52f - Mathf.Abs(shift) * 0.06f);

            // Руки заканчиваются на руле ВСЕГДА — это и есть связь тела с байком.
            var hand = Bar + new Vector2(0.02f, 0.03f);
            // Ноги заканчиваются на подножке ВСЕГДА.
            var foot = Peg + new Vector2(0f, 0.06f);

            // Двухкостный IK: локоть выгибается назад, колено вперёд.
            var elbow = Solve(shoulder, hand, 0.30f, 0.30f, -1f);
            var knee = Solve(hip, foot, 0.42f, 0.42f, +1f);

            var head = shoulder + new Vector2(0.14f, 0.20f);

            // Дальняя сторона тела ещё темнее ближней. Двух планов хватает, чтобы силуэт
            // читался объёмным, и это дешевле любого освещения.
            var far = new Color(RiderColor.r * 0.55f, RiderColor.g * 0.55f, RiderColor.b * 0.60f, 1f);
            Shapes.AddBar(v, c, t, hip + new Vector2(-0.04f, 0f), knee + new Vector2(-0.05f, 0f), 0.15f, far);
            Shapes.AddBar(v, c, t, knee + new Vector2(-0.05f, 0f), foot + new Vector2(-0.05f, 0f), 0.12f, far);
            Shapes.AddBar(v, c, t, shoulder + new Vector2(-0.03f, 0f), elbow + new Vector2(-0.04f, 0f), 0.11f, far);
            Shapes.AddBar(v, c, t, elbow + new Vector2(-0.04f, 0f), hand + new Vector2(-0.04f, 0f), 0.095f, far);

            // Корпус.
            Shapes.AddFan(v, c, t, new[]
            {
                hip + new Vector2(-0.13f, -0.02f), hip + new Vector2(0.13f, 0.02f),
                shoulder + new Vector2(0.14f, 0.02f), shoulder + new Vector2(-0.12f, -0.02f)
            }, RiderColor);
            // Спина ловит свет целиком: она обращена к солнцу. Это же и читаемый указатель
            // того, куда ушёл вес — линия спины наклоняется вместе с тазом.
            Shapes.AddBar(v, c, t, hip + new Vector2(-0.155f, -0.02f),
                shoulder + new Vector2(-0.145f, 0.02f), RimWidthM * 1.25f, RimLight);

            // Ближние конечности.
            Shapes.AddBar(v, c, t, hip, knee, 0.16f, RiderColor);
            Shapes.AddBar(v, c, t, knee, foot, 0.125f, RiderColor);
            Shapes.AddDisc(v, c, t, knee, 0.085f, RiderColor, 10);
            Shapes.AddBar(v, c, t, shoulder, elbow, 0.115f, RiderColor);
            Shapes.AddBar(v, c, t, elbow, hand, 0.10f, RiderColor);
            Shapes.AddDisc(v, c, t, elbow, 0.065f, RiderColor, 10);
            Shapes.AddDisc(v, c, t, shoulder, 0.10f, RiderColor, 12);

            // Ботинок и перчатка — маленькие, но именно они «пришивают» райдера к байку.
            Shapes.AddFan(v, c, t, new[]
            {
                foot + new Vector2(-0.08f, -0.05f), foot + new Vector2(0.13f, -0.05f),
                foot + new Vector2(0.13f, 0.04f), foot + new Vector2(-0.08f, 0.05f)
            }, new Color(0.10f, 0.11f, 0.14f, 1f));
            Shapes.AddDisc(v, c, t, hand, 0.065f, new Color(0.10f, 0.11f, 0.14f, 1f), 10);

            // Шлем: козырёк задаёт направление взгляда, поэтому байк читается едущим вправо.
            Shapes.AddDisc(v, c, t, head, 0.150f, RiderColor, 14);
            Shapes.AddFan(v, c, t, new[]
            {
                head + new Vector2(0.05f, 0.06f), head + new Vector2(0.27f, 0.03f),
                head + new Vector2(0.27f, -0.03f), head + new Vector2(0.05f, -0.02f)
            }, RiderColor);

            // Кромка по затылку и макушке — самая яркая точка героя. Голова на фоне неба:
            // если она не отделена от фона, силуэт распадается именно здесь.
            for (var i = 0; i < 9; i++)
            {
                var a0 = Mathf.PI * (0.28f + i * 0.10f);
                var a1 = Mathf.PI * (0.28f + (i + 1) * 0.10f);
                Shapes.AddBar(v, c, t,
                    head + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.150f,
                    head + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.150f,
                    RimWidthM * 1.2f, RimLight);
            }
            // Плечо и бедро — вторая и третья по важности точки контура.
            Shapes.AddBar(v, c, t, shoulder + new Vector2(-0.13f, 0.085f),
                shoulder + new Vector2(0.05f, 0.115f), RimWidthM, RimLight);

            _riderMesh.Clear();
            _riderMesh.SetVertices(v);
            _riderMesh.SetColors(c);
            _riderMesh.SetTriangles(t, 0);
            _riderMesh.RecalculateBounds();
            _riderFilter.sharedMesh = _riderMesh;
        }

        /// <summary>
        /// Двухкостный IK. bendSign задаёт, в какую сторону выгибается сустав — иначе
        /// решение неоднозначно и колено начинает щёлкать между двумя позами.
        /// </summary>
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
    }
}
