using System.Collections.Generic;
using ChartRunner.Meta;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// «СОК» исходника, которого не хватало ощущению: всплывающие тексты у байка (floatPop),
    /// золотой фонтан фиксации (celebStep b134), ачивки-тосты (b181), обучающие подсказки
    /// (tut b108). Всё — порт, ни одной новой механики: это обратная связь на то, что уже
    /// произошло, и без неё игрок не видит, за что его наградили.
    /// </summary>
    public class Juice : MonoBehaviour
    {
        public static Juice I { get; private set; }

        private struct Float { public Vector2 Pos; public string Text; public Color Col; public float Life; }
        private readonly List<Float> _floats = new List<Float>();

        private struct Gold { public Vector2 Pos, Vel; public float Life, R; }
        private readonly List<Gold> _gold = new List<Gold>();
        private Mesh _mesh; private MeshFilter _filter;
        private Camera _cam;

        // ---- ачивки (b181): разовые, очередь тостов ----
        private static readonly HashSet<string> _ach = new HashSet<string>();
        private readonly Queue<string> _toasts = new Queue<string>();
        private string _toast = ""; private float _toastUntil;

        // ---- подсказки (b108 tut): раз и навсегда ----
        private static readonly HashSet<string> _seenTut = new HashSet<string>();
        private string _tut = ""; private float _tutUntil;

        private uint _rng = 88172645u;
        private float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / 16777216f; }

        public static Juice Attach(Camera cam, Transform world)
        {
            var go = new GameObject("Juice");
            go.transform.SetParent(world, false);
            I = go.AddComponent<Juice>();
            I._cam = cam;
            I._mesh = new Mesh { name = "Gold" };
            I._filter = go.AddComponent<MeshFilter>();
            I._filter.sharedMesh = I._mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Shapes.Emissive(1.8f);
            mr.sortingOrder = 14;
            Load();
            return I;
        }

        private static void Load()
        {
            _ach.Clear(); _seenTut.Clear();
            foreach (var s in PlayerPrefs.GetString("cr_ach", "").Split(';')) if (s.Length > 0) _ach.Add(s);
            foreach (var s in PlayerPrefs.GetString("cr_tut", "").Split(';')) if (s.Length > 0) _seenTut.Add(s);
        }

        /// <summary>floatPop: короткий текст, всплывающий над байком.</summary>
        public void FloatPop(Vector2 worldPos, string text, Color col)
        {
            _floats.Add(new Float { Pos = worldPos, Text = text, Col = col, Life = 1f });
        }

        /// <summary>celebStep: непрерывное извержение золота из байка.</summary>
        public void GoldFountain(Vector2 pos, int n)
        {
            for (var i = 0; i < n; i++)
                _gold.Add(new Gold
                {
                    Pos = pos + new Vector2((Rand() - 0.5f) * 1.2f, 0.2f),
                    Vel = new Vector2((Rand() - 0.5f) * 3.5f, 3f + Rand() * 5f),
                    Life = 1f, R = 0.05f + Rand() * 0.07f
                });
        }

        /// <summary>achv: разовое достижение → тост.</summary>
        public void Achv(string id, string text)
        {
            if (_ach.Contains(id)) return;
            _ach.Add(id);
            PlayerPrefs.SetString("cr_ach", string.Join(";", _ach)); PlayerPrefs.Save();
            _toasts.Enqueue(text);
            if (GameAudio.I != null) GameAudio.I.RankUp();
        }

        /// <summary>tut: обучающая подсказка, показывается один раз за всю жизнь установки.</summary>
        public bool Tut(string id, string text)
        {
            if (_seenTut.Contains(id)) return false;
            _seenTut.Add(id);
            PlayerPrefs.SetString("cr_tut", string.Join(";", _seenTut)); PlayerPrefs.Save();
            _tut = text; _tutUntil = Time.unscaledTime + 4.5f;
            return true;
        }

        public static void ResetAll()
        {
            _ach.Clear(); _seenTut.Clear();
            PlayerPrefs.DeleteKey("cr_ach"); PlayerPrefs.DeleteKey("cr_tut"); PlayerPrefs.Save();
        }

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            for (var i = _floats.Count - 1; i >= 0; i--)
            {
                var f = _floats[i]; f.Life -= dt * 0.9f; f.Pos += new Vector2(0f, 0.9f * dt);
                if (f.Life <= 0f) _floats.RemoveAt(i); else _floats[i] = f;
            }
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (var i = _gold.Count - 1; i >= 0; i--)
            {
                var g = _gold[i];
                g.Pos += g.Vel * dt; g.Vel += new Vector2(0f, -9.8f * dt); g.Vel *= 1f - 0.9f * dt; g.Life -= dt * 1.2f;
                if (g.Life <= 0f) { _gold.RemoveAt(i); continue; }
                _gold[i] = g;
                Shapes.AddDisc(v, c, t, g.Pos, g.R * (0.6f + 0.4f * g.Life), new Color(1f, 0.84f, 0.36f, g.Life), 6);
            }
            _mesh.Clear(); _mesh.SetVertices(v); _mesh.SetColors(c); _mesh.SetTriangles(t, 0); _mesh.RecalculateBounds();
            _filter.sharedMesh = _mesh;

            if (Time.unscaledTime >= _toastUntil && _toasts.Count > 0) { _toast = _toasts.Dequeue(); _toastUntil = Time.unscaledTime + 2.6f; }
        }

        /// <summary>Рисуется из OnGUI сессии (матрица 430×932 уже стоит).</summary>
        public void DrawGUI(GUIStyle floatStyle, GUIStyle toastStyle, GUIStyle tutStyle, System.Action<string, float, float, Color, GUIStyle, float> chip)
        {
            if (_cam == null) return;
            var scale = 430f / UnityEngine.Screen.width;
            foreach (var f in _floats)
            {
                var sp = _cam.WorldToScreenPoint(new Vector3(f.Pos.x, f.Pos.y, 0f));
                var x = sp.x * scale; var y = (UnityEngine.Screen.height - sp.y) * scale;
                var a = Mathf.Clamp01(f.Life * 1.6f);
                floatStyle.normal.textColor = new Color(f.Col.r, f.Col.g, f.Col.b, a);
                GUI.Label(new Rect(x - 80f, y - 10f, 160f, 22f), f.Text, floatStyle);
            }
            if (Time.unscaledTime < _toastUntil && _toast.Length > 0)
                chip("★ " + Loc.T("ДОСТИЖЕНИЕ") + ": " + _toast, 215f, 110f, new Color(1f, 0.84f, 0.47f), toastStyle, 1f);
            if (Time.unscaledTime < _tutUntil && _tut.Length > 0)
            {
                var a = Mathf.Clamp01((_tutUntil - Time.unscaledTime) * 2f);
                chip(_tut, 215f, 932f * 0.22f, new Color(0.59f, 0.8f, 1f), tutStyle, a);
            }
        }
    }
}
