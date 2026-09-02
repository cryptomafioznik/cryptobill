using System.Collections.Generic;
using ChartRunner.Bike;
using ChartRunner.Meta;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// МОНЕТЫ ($) — правило исходника, не моё: 30 % head-узлов свечи несут монету
    /// (buildTickerTrack, coin:(head&&random<0.30)), монета висит на 30 px над линией,
    /// радиус сбора 36 px × (1 + 0.40·МАГНИТ) (стр. 1705). Сбор кормит PUMP (+0.04).
    /// Золото — единственное золото в кадре, и это буквально деньги заезда.
    /// </summary>
    public class CoinField : MonoBehaviour
    {
        private struct Coin { public Vector2 Pos; public bool Taken; public float TakenAt; }

        private readonly List<Coin> _coins = new List<Coin>();
        private BikeController _bike;
        private Mesh _mesh;
        private MeshFilter _filter;
        private float _time;

        public int Collected { get; private set; }
        public int JustCollected { get; private set; }

        private static readonly Color Gold = new Color(1f, 0.84f, 0.36f, 1f);

        public static CoinField Attach(TickerTrack track, TerrainSampler terrain, BikeController bike, Transform world)
        {
            var go = new GameObject("CoinField");
            go.transform.SetParent(world, false);
            var f = go.AddComponent<CoinField>();
            f._bike = bike;
            f._mesh = new Mesh { name = "Coins" };
            f._filter = go.AddComponent<MeshFilter>();
            f._filter.sharedMesh = f._mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Shapes.Emissive(1.7f);
            mr.sortingOrder = 7;
            var k = UnitsContract.PxToM;
            foreach (var n in track.CoinNodes)
            {
                var x = n * TickerTrack.StepPx * k;
                f._coins.Add(new Coin { Pos = new Vector2(x, terrain.HeightAt(x) + 30f * k) });
            }
            return f;
        }

        private void LateUpdate()
        {
            _time += Time.deltaTime;
            JustCollected = 0;
            if (_bike == null) return;

            var bikePos = new Vector2(_bike.State.PositionXM, _bike.State.PositionYM);
            var magR = 36f * UnitsContract.PxToM * (1f + 0.40f * Economy.UpgLvl("mag"));

            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (var i = 0; i < _coins.Count; i++)
            {
                var coin = _coins[i];
                var dx = coin.Pos.x - bikePos.x;
                if (dx < -6f || dx > 26f) continue;

                if (!coin.Taken)
                {
                    if ((coin.Pos - bikePos).sqrMagnitude < magR * magR && !_bike.Halted)
                    {
                        coin.Taken = true; coin.TakenAt = _time; _coins[i] = coin;
                        Collected++; JustCollected++;
                        continue;
                    }
                    var ph = _time * 3.2f + coin.Pos.x * 0.7f;
                    var w = 0.16f + 0.10f * Mathf.Abs(Mathf.Cos(ph));
                    var p = coin.Pos + new Vector2(0f, Mathf.Sin(_time * 2f + coin.Pos.x) * 0.06f);
                    Diamond(v, c, t, p, w, 0.26f, Gold);
                }
                else if (_time - coin.TakenAt < 0.35f)
                {
                    var k = (_time - coin.TakenAt) / 0.35f;
                    Ring(v, c, t, coin.Pos, 0.2f + k * 0.75f, new Color(Gold.r, Gold.g, Gold.b, (1f - k) * 0.7f));
                }
            }
            _mesh.Clear(); _mesh.SetVertices(v); _mesh.SetColors(c); _mesh.SetTriangles(t, 0);
            _mesh.RecalculateBounds(); _filter.sharedMesh = _mesh;
        }

        public void ResetRun()
        {
            for (var i = 0; i < _coins.Count; i++) { var c = _coins[i]; c.Taken = false; _coins[i] = c; }
            Collected = 0;
        }

        private static void Diamond(List<Vector3> v, List<Color> c, List<int> t, Vector2 p, float w, float h, Color col)
        {
            var i0 = v.Count;
            v.Add(new Vector3(p.x, p.y + h, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(p.x + w, p.y, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(p.x, p.y - h, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(p.x - w, p.y, 0f)); c.Add(Shapes.V(col));
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
        }

        private static void Ring(List<Vector3> v, List<Color> c, List<int> t, Vector2 p, float r, Color col)
        {
            const int seg = 12; var inner = r * 0.78f; var i0 = v.Count;
            for (var i = 0; i < seg; i++)
            {
                var a = i / (float)seg * Mathf.PI * 2f; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                v.Add(new Vector3(p.x + d.x * r, p.y + d.y * r, 0f)); c.Add(Shapes.V(col));
                v.Add(new Vector3(p.x + d.x * inner, p.y + d.y * inner, 0f)); c.Add(Shapes.V(col));
            }
            for (var i = 0; i < seg; i++)
            {
                var o0 = i0 + i * 2; var n0 = o0 + 1; var o1 = i0 + (i * 2 + 2) % (seg * 2); var n1 = i0 + (i * 2 + 3) % (seg * 2);
                t.Add(o0); t.Add(o1); t.Add(n0); t.Add(n0); t.Add(o1); t.Add(n1);
            }
        }
    }
}
