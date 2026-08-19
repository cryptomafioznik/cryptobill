using System.Collections.Generic;
using ChartRunner.Bike;
using ChartRunner.Track;
using ChartRunner.Tuning;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// МОНЕТЫ ПО ТРАССЕ — то, ради чего игрок выбирает линию, а не просто держит газ.
    ///
    /// Зачем: во всех разобранных играх жанра между «ехать» и «ехать хорошо» стоит
    /// собираемое. В Hill Climb Racing это монеты на рельефе, в Alto's — они же плюс
    /// цепочки трюков. Без них трасса читается коридором: любая линия одинаково верна.
    ///
    /// Правило проекта «золото = деньги» соблюдено: монеты — единственный золотой
    /// объект в кадре, и это буквально деньги заезда.
    ///
    /// РАЗМЕЩЕНИЕ ПРИЧИННОЕ, А НЕ РОВНЫМ ШАГОМ. Монета встаёт там, где есть ВЫБОР:
    ///  - над гребнем подъёма — забрать можно, только неся скорость;
    ///  - в яме — забрать можно, только не срезав по верху;
    ///  - дуга над разрывом — приз за прыжок.
    /// Монета, поставленная равномерно, награждает за отсутствие решения.
    /// </summary>
    public class CoinField : MonoBehaviour
    {
        private struct Coin
        {
            public Vector2 Pos;
            public bool Taken;
            public float TakenAt;
        }

        private readonly List<Coin> _coins = new List<Coin>();
        private BikeController _bike;
        private Mesh _mesh;
        private MeshFilter _filter;
        private float _time;

        /// <summary>Собрано за заезд.</summary>
        public int Collected { get; private set; }

        /// <summary>Сколько собрано только что — для звука и всплывающего текста.</summary>
        public int JustCollected { get; private set; }

        private const float PickupRadiusM = 0.85f;
        private static readonly Color Gold = new Color(1f, 0.84f, 0.36f, 1f);

        public static CoinField Attach(TrackProfile track, TerrainSampler terrain,
            BikeController bike, Transform world, int seed)
        {
            var go = new GameObject("CoinField");
            go.transform.SetParent(world, false);
            var f = go.AddComponent<CoinField>();
            f._bike = bike;
            f._mesh = new Mesh { name = "Coins" };
            f._filter = go.AddComponent<MeshFilter>();
            f._filter.sharedMesh = f._mesh;
            var mr = go.AddComponent<MeshRenderer>();
            // Монеты светятся: это данные (деньги), а не декор.
            mr.sharedMaterial = Shapes.Emissive(1.7f);
            mr.sortingOrder = 7;
            f.Place(track, terrain, seed);
            return f;
        }

        private void Place(TrackProfile track, TerrainSampler terrain, int seed)
        {
            var stepM = track.nodeStepPx * UnitsContract.PxToM;
            var endM = track.EndM;
            var st = (uint)(seed == 0 ? 1 : seed);
            float Rand()
            {
                st ^= st << 13; st ^= st >> 17; st ^= st << 5;
                return (st & 0xFFFFFF) / 16777216f;
            }

            // Идём по узлам и смотрим ФОРМУ впереди: решение о монете принимается
            // рельефом, а не счётчиком.
            for (var x = 6f; x < endM - 4f; x += stepM)
            {
                var y0 = terrain.HeightAt(x);
                var y1 = terrain.HeightAt(x + stepM);
                var y2 = terrain.HeightAt(x + stepM * 2f);
                var slope = (y1 - y0) / stepM;
                var curve = (y2 - y1) - (y1 - y0);

                // Гребень: подъём переходит в спуск. Приз за пронесённую скорость —
                // кладём цепочку по дуге вылета.
                if (slope > 0.25f && curve < -0.12f && Rand() > 0.35f)
                {
                    for (var k = 0; k < 4; k++)
                    {
                        var cx = x + stepM * (0.6f + k * 0.55f);
                        var arc = 1.1f + Mathf.Sin(k / 3f * Mathf.PI) * 1.5f;
                        Add(new Vector2(cx, terrain.HeightAt(cx) + arc));
                    }
                    x += stepM * 3f;
                    continue;
                }

                // Яма: спуск переходит в подъём. Приз за то, что не срезал поверху.
                if (slope < -0.25f && curve > 0.12f && Rand() > 0.45f)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        var cx = x + stepM * (0.5f + k * 0.6f);
                        Add(new Vector2(cx, terrain.HeightAt(cx) + 0.9f));
                    }
                    x += stepM * 2f;
                    continue;
                }

                // Ровный участок: редкая одиночная монета, чтобы дорога не пустовала.
                if (Mathf.Abs(slope) < 0.12f && Rand() > 0.82f)
                    Add(new Vector2(x, y0 + 1.2f));
            }
        }

        private void Add(Vector2 p) => _coins.Add(new Coin { Pos = p, Taken = false });

        private void LateUpdate()
        {
            _time += Time.deltaTime;
            JustCollected = 0;
            if (_bike == null) return;

            var bikePos = new Vector2(_bike.State.PositionXM, _bike.State.PositionYM);

            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();

            for (var i = 0; i < _coins.Count; i++)
            {
                var coin = _coins[i];
                // Далёкие монеты не считаем и не рисуем: кадр видит ~20 м.
                var dx = coin.Pos.x - bikePos.x;
                if (dx < -6f || dx > 26f) continue;

                if (!coin.Taken)
                {
                    if ((coin.Pos - bikePos).sqrMagnitude < PickupRadiusM * PickupRadiusM
                        && !_bike.Halted)
                    {
                        coin.Taken = true;
                        coin.TakenAt = _time;
                        _coins[i] = coin;
                        Collected++;
                        JustCollected++;
                        RunGoals.AddCoins(1);
                        continue;
                    }

                    // Монета: ромб, дышащий по вертикали — вращающийся жетон читается
                    // на этом размере именно так, а не полной геометрией.
                    var ph = _time * 3.2f + coin.Pos.x * 0.7f;
                    var w = 0.16f + 0.10f * Mathf.Abs(Mathf.Cos(ph));
                    var h = 0.26f;
                    var p = coin.Pos + new Vector2(0f, Mathf.Sin(_time * 2f + coin.Pos.x) * 0.06f);
                    Diamond(v, c, t, p, w, h, Gold);
                }
                else if (_time - coin.TakenAt < 0.35f)
                {
                    // Вспышка подбора: расширяется и гаснет — подтверждение действия.
                    var k = (_time - coin.TakenAt) / 0.35f;
                    var r = 0.2f + k * 0.75f;
                    var a = (1f - k) * 0.7f;
                    Ring(v, c, t, coin.Pos, r, new Color(Gold.r, Gold.g, Gold.b, a));
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(v);
            _mesh.SetColors(c);
            _mesh.SetTriangles(t, 0);
            _mesh.RecalculateBounds();
            _filter.sharedMesh = _mesh;
        }

        /// <summary>Обнуляет собранное и возвращает монеты на трассу — новая попытка.</summary>
        public void ResetRun()
        {
            for (var i = 0; i < _coins.Count; i++)
            {
                var c = _coins[i];
                c.Taken = false;
                _coins[i] = c;
            }
            Collected = 0;
        }

        private static void Diamond(List<Vector3> v, List<Color> c, List<int> t,
            Vector2 p, float w, float h, Color col)
        {
            var i0 = v.Count;
            v.Add(new Vector3(p.x, p.y + h, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(p.x + w, p.y, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(p.x, p.y - h, 0f)); c.Add(Shapes.V(col));
            v.Add(new Vector3(p.x - w, p.y, 0f)); c.Add(Shapes.V(col));
            t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2);
            t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
        }

        private static void Ring(List<Vector3> v, List<Color> c, List<int> t,
            Vector2 p, float r, Color col)
        {
            const int seg = 12;
            var inner = r * 0.78f;
            var i0 = v.Count;
            for (var i = 0; i < seg; i++)
            {
                var a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                v.Add(new Vector3(p.x + d.x * r, p.y + d.y * r, 0f)); c.Add(Shapes.V(col));
                v.Add(new Vector3(p.x + d.x * inner, p.y + d.y * inner, 0f)); c.Add(Shapes.V(col));
            }
            for (var i = 0; i < seg; i++)
            {
                var o0 = i0 + i * 2;
                var n0 = i0 + i * 2 + 1;
                var o1 = i0 + (i * 2 + 2) % (seg * 2);
                var n1 = i0 + (i * 2 + 3) % (seg * 2);
                t.Add(o0); t.Add(o1); t.Add(n0);
                t.Add(n0); t.Add(o1); t.Add(n1);
            }
        }
    }
}
