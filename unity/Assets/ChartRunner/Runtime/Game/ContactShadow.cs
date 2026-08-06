using System.Collections.Generic;
using ChartRunner.Bike;
using ChartRunner.Track;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Тени под колёсами. Их задача не украшение, а ПРИВЯЗКА К ЗЕМЛЕ.
    ///
    /// Без контактной тени объект в 2D читается парящим: глазу нечем определить, где он по
    /// глубине относительно поверхности. Особенно это бьёт по этой игре — прыжок и езда по
    /// земле различаются только тенью, а различить их игрок обязан мгновенно.
    ///
    /// Тень причинная: она лежит на поверхности под колесом (высота берётся у того же
    /// сэмплера, что и физика), а с высотой отрыва становится шире и слабее — как ведёт себя
    /// полутень от протяжённого источника. Поэтому по тени читается ВЫСОТА полёта, а не
    /// только факт отрыва.
    /// </summary>
    public class ContactShadow : MonoBehaviour
    {
        private TerrainSampler _terrain;
        private Transform _rear;
        private Transform _front;
        private Transform _rearShadow;
        private Transform _frontShadow;
        private float _wheelR;

        /// <summary>Высота отрыва, при которой тень исчезает совсем, метры.</summary>
        public float FadeHeightM = 4.0f;

        public static ContactShadow Attach(BikeController controller, TerrainSampler terrain,
            Transform world)
        {
            var rig = controller.GetComponent<BikeRig>();
            var go = new GameObject("ContactShadows");
            go.transform.SetParent(world, false);
            var s = go.AddComponent<ContactShadow>();
            s._terrain = terrain;
            s._rear = rig.RearWheel.transform;
            s._front = rig.FrontWheel.transform;
            s._wheelR = rig.Profile.wheelRadiusM;
            s._rearShadow = Blob(go.transform, "RearShadow");
            s._frontShadow = Blob(go.transform, "FrontShadow");
            return s;
        }

        /// <summary>
        /// Пятно тени. Мягкость — градиентом вершинного цвета от центра к краю: у меша нет
        /// размытия, но убывающая альфа даёт ту же читаемость и стоит один draw call.
        /// </summary>
        private static Transform Blob(Transform parent, string name)
        {
            const int seg = 20;
            var v = new List<Vector3> { Vector3.zero };
            var c = new List<Color> { Shapes.V(new Color(0f, 0f, 0f, 0.55f)) };
            var t = new List<int>();
            for (var i = 0; i < seg; i++)
            {
                var a = i / (float)seg * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.28f, 0f));
                c.Add(Shapes.V(new Color(0f, 0f, 0f, 0f)));
            }
            for (var i = 0; i < seg; i++)
            {
                t.Add(0); t.Add(1 + i); t.Add(1 + (i + 1) % seg);
            }
            var go = Shapes.Create(name, parent, Shapes.Build(name, v, c, t), -5);
            return go.transform;
        }

        private void LateUpdate()
        {
            Place(_rearShadow, _rear);
            Place(_frontShadow, _front);
        }

        private void Place(Transform shadow, Transform wheel)
        {
            if (shadow == null || wheel == null) return;

            var x = wheel.position.x;
            var groundY = _terrain.HeightAt(x);
            // Высота НИЗА колеса над землёй: именно она определяет отрыв, а не высота оси.
            var gap = Mathf.Max(0f, wheel.position.y - _wheelR - groundY);
            var k = Mathf.Clamp01(1f - gap / FadeHeightM);

            shadow.position = new Vector3(x, groundY + 0.02f, 0.1f);
            // Тень поворачивается по склону — иначе на подъёме она врезается в грунт.
            shadow.rotation = Quaternion.Euler(0f, 0f, _terrain.SlopeAt(x) * Mathf.Rad2Deg);
            // Шире и слабее с высотой: полутень растёт, контакт теряется.
            var w = _wheelR * (1.05f + gap * 0.30f);
            shadow.localScale = new Vector3(w, w * Mathf.Lerp(0.55f, 1f, k), 1f) * Mathf.Max(0.001f, k);
        }
    }
}
