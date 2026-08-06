using System.Collections.Generic;
using ChartRunner.Bike;
using ChartRunner.Track;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// Пыль из-под заднего колеса. Это НЕ эффект, а обратная связь по газу.
    ///
    /// Проблема, которую она решает, механическая, а не декоративная: пробуксовка —
    /// центральная величина этой физики (тяга ограничена трением в пятне контакта), и до
    /// сих пор игрок не видел её никак. Он не мог отличить «газ работает» от «газ крутит
    /// колесо впустую», а именно это различие и есть содержание дозировки.
    ///
    /// Поэтому количество и скорость выброса пропорциональны ИЗМЕРЕННОЙ пробуксовке
    /// (BikeState.RearSlip), а не факту нажатия газа. Врать в эту сторону нельзя: пыль,
    /// нарисованная по кнопке, показывала бы намерение игрока вместо состояния байка.
    /// </summary>
    public class WheelDust : MonoBehaviour
    {
        private const int Capacity = 28;

        private struct Puff
        {
            public Vector2 Pos;
            public Vector2 Vel;
            public float Age;
            public float Life;
            public float Size;
        }

        private readonly Puff[] _puffs = new Puff[Capacity];
        private int _next;
        private float _emitAccum;

        private BikeController _controller;
        private BikeRig _rig;
        private TerrainSampler _terrain;
        private Mesh _mesh;
        private MeshFilter _filter;

        /// <summary>Пробуксовка, ниже которой пыли нет вовсе.</summary>
        public float SlipThreshold = 0.22f;

        public Color DustColor = new Color(0.85f, 0.62f, 0.42f, 1f);

        public static WheelDust Attach(BikeController controller, TerrainSampler terrain,
            Transform world)
        {
            var go = new GameObject("WheelDust");
            go.transform.SetParent(world, false);
            var d = go.AddComponent<WheelDust>();
            d._controller = controller;
            d._rig = controller.GetComponent<BikeRig>();
            d._terrain = terrain;
            d._mesh = new Mesh { name = "Dust" };
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = d._mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Shapes.VertexColorMaterial;
            // Позади байка, но перед рельефом: пыль поднимается над землёй, а не под неё.
            mr.sortingOrder = 6;
            d._filter = mf;
            return d;
        }

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            var st = _controller.State;
            var slip = st.RearSlip;
            var grounded = st.GroundedWheelCount > 0;

            if (grounded && slip > SlipThreshold && st.ThrottleApplied > 0.05f)
            {
                // Темп выброса растёт с пробуксовкой: слабый срыв — редкие клубы,
                // полный — сплошной шлейф.
                var rate = Mathf.Lerp(6f, 34f, Mathf.InverseLerp(SlipThreshold, 1f, slip));
                _emitAccum += rate * dt;
                while (_emitAccum >= 1f)
                {
                    _emitAccum -= 1f;
                    Emit(slip, st.SpeedMPerS);
                }
            }
            else
            {
                _emitAccum = 0f;
            }

            Rebuild(dt);
        }

        private void Emit(float slip, float speed)
        {
            var wheelPos = (Vector2)_rig.RearWheel.position;
            var x = wheelPos.x;
            var groundY = _terrain.HeightAt(x);
            var slopeDir = new Vector2(Mathf.Cos(_terrain.SlopeAt(x)), Mathf.Sin(_terrain.SlopeAt(x)));

            // Выброс НАЗАД вдоль поверхности: колесо срывает грунт против хода.
            var back = -slopeDir;
            // Детерминированный разброс от номера частицы: Random в рантайме сделал бы
            // одинаковые прогоны разными, и по кадрам нельзя было бы сравнивать.
            var jitter = (_next % 7) / 7f - 0.5f;

            _puffs[_next] = new Puff
            {
                Pos = new Vector2(x, groundY + 0.08f),
                Vel = back * (1.4f + speed * 0.28f) + new Vector2(jitter * 0.9f, 0.9f + jitter * 0.5f),
                Age = 0f,
                Life = 0.42f + 0.30f * slip,
                Size = 0.18f + 0.22f * slip
            };
            _next = (_next + 1) % Capacity;
        }

        private void Rebuild(float dt)
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            for (var i = 0; i < Capacity; i++)
            {
                if (_puffs[i].Life <= 0f) continue;
                _puffs[i].Age += dt;
                if (_puffs[i].Age >= _puffs[i].Life) { _puffs[i].Life = 0f; continue; }

                var k = _puffs[i].Age / _puffs[i].Life;
                _puffs[i].Pos += _puffs[i].Vel * dt;
                // Замедление и оседание: клуб теряет скорость и растёт, как настоящая пыль.
                _puffs[i].Vel *= 1f - Mathf.Min(0.9f, 2.6f * dt);
                _puffs[i].Vel += new Vector2(0f, -1.1f * dt);

                var r = _puffs[i].Size * (0.6f + 1.5f * k);
                var a = (1f - k) * (1f - k) * 0.42f;
                Shapes.AddDisc(v, c, t, _puffs[i].Pos, r,
                    new Color(DustColor.r, DustColor.g, DustColor.b, a), 8);
            }

            _mesh.Clear();
            if (v.Count > 0)
            {
                _mesh.SetVertices(v);
                _mesh.SetColors(c);
                _mesh.SetTriangles(t, 0);
                _mesh.RecalculateBounds();
            }
            _filter.sharedMesh = _mesh;
        }
    }
}
