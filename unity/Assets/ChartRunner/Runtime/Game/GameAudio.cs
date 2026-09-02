using System;
using System.Collections.Generic;
using ChartRunner.Meta;
using UnityEngine;

namespace ChartRunner.Game
{
    /// <summary>
    /// ЗВУК — порт процедурного WebAudio исходника (b152/b164/b179): ни одного файла,
    /// весь звук синтезируется в OnAudioFilterRead. Это и есть «как было»: блипы событий
    /// (blip), движок по типу байка (ENG_PROF: осц-банк + шум выхлопа → перегруз →
    /// лоупасс, открывающийся газом), ветер полёта, рокот волны сзади ∝ близости,
    /// генеративный синтвейв-луп 123 BPM (пад/бас/арп/бочка/хэт), мастер-компрессор.
    ///
    /// Реализация: один AudioSource с DSP-коллбэком. Голоса — простые осцилляторы
    /// с экспоненциальной огибающей и однополюсным лоупассом; этого достаточно, чтобы
    /// звучать как исходник, и ноль ассетов в билде.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio I { get; private set; }

        // ---- параметры движка по типу байка (ENG_PROF) ----
        private struct EngProf { public float F0, Fv, H2, H3, Lp0, Lpv, Gv, GThr, Cap; }
        private static readonly Dictionary<string, EngProf> Prof = new Dictionary<string, EngProf>
        {
            { "bike", new EngProf { F0 = 26, Fv = 1.2f, H2 = 2.01f, H3 = 1.006f, Lp0 = 800, Lpv = 30, Gv = 0.0022f, GThr = 0.008f, Cap = 0.07f } },
            { "scooter", new EngProf { F0 = 95, Fv = 10.5f, H2 = 2.02f, H3 = 2.98f, Lp0 = 700, Lpv = 70, Gv = 0.010f, GThr = 0.05f, Cap = 0.30f } },
            { "moped", new EngProf { F0 = 82, Fv = 10, H2 = 2.02f, H3 = 2.98f, Lp0 = 640, Lpv = 75, Gv = 0.010f, GThr = 0.05f, Cap = 0.30f } },
            { "dirt", new EngProf { F0 = 40, Fv = 8, H2 = 2.01f, H3 = 1.006f, Lp0 = 360, Lpv = 95, Gv = 0.011f, GThr = 0.05f, Cap = 0.32f } },
            { "sport", new EngProf { F0 = 58, Fv = 11, H2 = 3.0f, H3 = 1.5f, Lp0 = 520, Lpv = 130, Gv = 0.012f, GThr = 0.06f, Cap = 0.34f } },
        };

        private enum Wave { Sine, Square, Saw, Tri, Noise }

        private class Voice
        {
            public Wave W; public float Freq, Freq2, Phase, Gain, Attack, Dur, T; public float Cut, Lp; public bool Kick;
        }

        private readonly object _lock = new object();
        private readonly List<Voice> _voices = new List<Voice>();
        private int _sr = 48000;

        // ---- живые параметры (главный поток → DSP) ----
        private volatile float _speedPxF;   // скорость байка в px/кадр исходника
        private volatile bool _throttle, _boost, _playing, _airborne;
        private volatile float _airK, _danger, _rush;
        private string _bikeType = "dirt";

        // состояние DSP
        private float _e1, _e2, _e3, _eLp, _eGain, _windG, _waveG, _nz, _wLp;
        private float _mGain, _mInt, _mDang, _mMood, _mDroneP;
        private double _mNext; private int _mStep, _mArpI;
        private uint _rng = 2463534242u;

        // ---- музыка (b1106) ----
        private const float Bpm = 123f;
        private static readonly (int r, int m)[] Prog = { (0, 0), (-4, 1), (3, 1), (-2, 1) };
        private static readonly int[] Arp = { 1, 0, 1, 1, 0, 1, 0, 1, 1, 0, 1, 0, 1, 1, 0, 1 };
        private static float MF(float s) => 110f * Mathf.Pow(2f, s / 12f);
        private double _time;

        public static void Ensure()
        {
            if (I != null) return;
            var go = new GameObject("GameAudio");
            DontDestroyOnLoad(go);
            I = go.AddComponent<GameAudio>();
            var src = go.AddComponent<AudioSource>();
            src.loop = true; src.playOnAwake = true; src.spatialBlend = 0f; src.volume = 1f;
            src.clip = AudioClip.Create("silence", 4800, 1, 48000, false); // носитель для фильтра
            src.Play();
            I._sr = AudioSettings.outputSampleRate;
        }

        // ================= API (порт sfx*) =================

        private void Blip(float freq, float dur, Wave w, float vol)
        {
            if (Economy.Muted) return;
            lock (_lock) _voices.Add(new Voice { W = w, Freq = freq, Freq2 = freq, Dur = dur, Gain = vol, Attack = 0.012f, Cut = 20000f });
        }

        public void Coin() { Blip(940, 0.09f, Wave.Tri, 0.2f); Blip(1400, 0.07f, Wave.Tri, 0.11f); }
        public void Flip() => Blip(260, 0.2f, Wave.Saw, 0.16f);
        public void Near() => Blip(1250, 0.12f, Wave.Sine, 0.16f);
        public void Land(float k) => Blip(150 - 45 * k, 0.09f + 0.06f * k, Wave.Square, 0.2f + 0.15f * k);
        public void Crash() { Blip(80, 0.45f, Wave.Saw, 0.38f); Blip(120, 0.3f, Wave.Square, 0.22f); }
        public void Lev() { Blip(680, 0.08f, Wave.Square, 0.16f); Blip(1020, 0.08f, Wave.Square, 0.12f); }
        public void Click() => Blip(540, 0.05f, Wave.Square, 0.16f);
        public void Air() { Blip(420, 0.18f, Wave.Sine, 0.18f); Blip(700, 0.14f, Wave.Sine, 0.12f); }
        public void RankUp()
        {
            // восходящая фанфара: 0,4,7,12,16 полутона через 72 мс
            var steps = new[] { 0, 4, 7, 12, 16 };
            for (var i = 0; i < steps.Length; i++)
            {
                var f = 392f * Mathf.Pow(2f, steps[i] / 12f);
                lock (_lock) _voices.Add(new Voice { W = Wave.Tri, Freq = f, Freq2 = f, Dur = 0.2f, Gain = 0.17f, Attack = 0.012f, Cut = 20000f, T = -i * 0.072f });
            }
        }

        /// <summary>Состояние заезда для движка/ветра/рокота — раз в кадр из PlaySession.</summary>
        public void SetRun(bool playing, float speedMPerS, bool throttle, bool boost, bool airborne, float airK, float danger, float rush, string bikeType)
        {
            _playing = playing;
            _speedPxF = speedMPerS / Tuning.UnitsContract.PxPerFrameToMPerS;
            _throttle = throttle; _boost = boost; _airborne = airborne; _airK = airK; _danger = danger; _rush = rush;
            _bikeType = bikeType ?? "dirt";
        }

        // ================= DSP =================

        private float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / 16777216f * 2f - 1f; }

        private static float Osc(Wave w, float ph)
        {
            switch (w)
            {
                case Wave.Sine: return Mathf.Sin(ph * 6.2831853f);
                case Wave.Square: return ph < 0.5f ? 1f : -1f;
                case Wave.Saw: return 2f * ph - 1f;
                case Wave.Tri: return 4f * Mathf.Abs(ph - 0.5f) - 1f;
                default: return 0f;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            var sr = _sr; var dt = 1f / sr;
            var muted = Economy.Muted;
            var prof = Prof.TryGetValue(_bikeType, out var p) ? p : Prof["dirt"];
            var v = _speedPxF;

            // ---- целевые параметры движка (порт стр. 4484-4489) ----
            var f = prof.F0 + v * prof.Fv + (_throttle ? 16f : 0f);
            var lpF = prof.Lp0 + v * prof.Lpv + (_throttle ? 520f : 0f) + (_boost ? 700f : 0f);
            var eTgt = (_playing && !muted) ? Mathf.Clamp(0.05f + v * prof.Gv + (_throttle ? prof.GThr : 0f) + (_boost ? 0.06f : 0f), 0f, prof.Cap) : 0f;
            var windTgt = (_playing && _airborne) ? Mathf.Min(0.42f, _airK * 0.36f) : 0f;
            var waveTgt = (_playing && !muted) ? Mathf.Pow(Mathf.Clamp01(_danger), 1.5f) * 0.30f : 0f;
            var lpA = Mathf.Clamp01(lpF / sr * 6.28f);

            // ---- музыка: интенсивность/опасность/настроение (musicTick) ----
            var iTgt = muted ? 0f : (_playing ? Mathf.Clamp01(0.22f + _rush * 0.6f + Mathf.Clamp01(v / 16f) * 0.3f) : 0.1f);
            var mTgt = muted ? 0f : 0.44f;
            var step16 = 60.0 / Bpm / 4.0;

            for (var i = 0; i < data.Length; i += channels)
            {
                _time += dt;
                // Планировщик 16-х: шаг вперёд, если пора.
                if (_mNext < _time - 0.5) _mNext = _time + 0.14;
                while (_mNext < _time + 0.02)
                {
                    ScheduleStep(_mStep, (float)(_mNext - _time));
                    _mStep = (_mStep + 1) % 64; _mNext += step16;
                }
                _mInt += (iTgt - _mInt) * 0.00004f;
                _mDang += (_danger - _mDang) * 0.00005f;
                _mGain += (mTgt - _mGain) * 0.00005f;
                _eGain += (eTgt - _eGain) * 0.0004f;
                _windG += (windTgt - _windG) * 0.0005f;
                _waveG += (waveTgt - _waveG) * 0.0003f;

                // ---- движок: 3 осциллятора + шум выхлопа → мягкий перегруз → лоупасс ----
                _e1 += f * dt; _e2 += f * prof.H2 * dt; _e3 += f * prof.H3 * dt;
                if (_e1 >= 1f) _e1 -= 1f; if (_e2 >= 1f) _e2 -= 1f; if (_e3 >= 1f) _e3 -= 1f;
                var n = Rand();
                _nz += (n - _nz) * 0.25f;
                var emix = (Osc(Wave.Saw, _e1) + 0.45f * Osc(Wave.Square, _e2) + 0.6f * Osc(Wave.Saw, _e3) + 0.22f * _nz) * 0.5f;
                var shaped = 9f * emix / (1f + 8f * Mathf.Abs(emix)); // makeDistCurve(8)
                _eLp += (shaped - _eLp) * lpA;
                var s = _eLp * _eGain;

                // ---- ветер (полосовой шум ~520 Гц) и рокот волны (лоупасс 130 Гц) ----
                _wLp += (n - _wLp) * 0.017f; // ~130 Гц
                s += _wLp * _waveG * 1.6f;
                s += (n - _wLp) * 0.09f * _windG;

                // ---- голоса блипов/музыки ----
                var music = 0f;
                lock (_lock)
                {
                    for (var k = _voices.Count - 1; k >= 0; k--)
                    {
                        var vo = _voices[k];
                        vo.T += dt;
                        if (vo.T < 0f) continue;
                        if (vo.T > vo.Dur + 0.04f) { _voices.RemoveAt(k); continue; }
                        var fr = vo.Kick ? Mathf.Lerp(125f, 45f, Mathf.Clamp01(vo.T / 0.11f)) : vo.Freq;
                        vo.Phase += fr * dt; if (vo.Phase >= 1f) vo.Phase -= 1f;
                        var env = vo.T < vo.Attack ? vo.T / vo.Attack
                            : Mathf.Exp(-6.9f * (vo.T - vo.Attack) / Mathf.Max(0.01f, vo.Dur - vo.Attack));
                        var o = vo.W == Wave.Noise ? Rand() : Osc(vo.W, vo.Phase);
                        var a = Mathf.Clamp01(vo.Cut / sr * 6.28f);
                        vo.Lp += (o - vo.Lp) * a;
                        var val = (vo.Cut >= 19000f ? o : vo.Lp) * env * vo.Gain;
                        if (vo.Freq2 < 0f) music += val; else s += val;
                    }
                }
                // дрон опасности (mDrone, sine на MF(-24), gain = mDang*0.12)
                _mDroneP += MF(-24) * dt; if (_mDroneP >= 1f) _mDroneP -= 1f;
                music += Mathf.Sin(_mDroneP * 6.2831853f) * _mDang * 0.12f;
                s += music * _mGain;

                // ---- мастер: компрессор-лимитер (b164) ----
                s *= 0.42f;
                s = s / (1f + 0.6f * Mathf.Abs(s));
                for (var c = 0; c < channels; c++) data[i + c] = s;
            }
        }

        /// <summary>Порт scheduleStep: пад на 1-й, бас на 1/9, арп по маске, бочка/хэт в заезде.</summary>
        private void ScheduleStep(int step, float delay)
        {
            var bar = step / 16; var c = Prog[bar % Prog.Length];
            var notes = new[] { c.r, c.r + (c.m != 0 ? 4 : 3), c.r + 7 };
            var bright = 620f + _mMood * 900f + _mInt * 1500f;
            if (step % 16 == 0)
                for (var k = 0; k < 3; k++)
                    MVoice(Wave.Saw, MF(notes[k] + 12) + (k > 0 ? Mathf.Abs(Rand()) * 1.6f : 0f), delay, 1.9f, 0.04f + _mMood * 0.025f, 700f + _mMood * 700f);
            if (step % 16 == 0 || step % 16 == 8)
            {
                MVoice(Wave.Saw, MF(c.r - 12), delay, 0.32f, 0.15f, 250f + _mInt * 220f);
                MVoice(Wave.Sine, MF(c.r - 12), delay, 0.30f, 0.09f, 400f);
            }
            if (Arp[step % 16] != 0 && _mInt > 0.18f)
            {
                MVoice(Wave.Tri, MF(notes[_mArpI % 3] + 24), delay, 0.15f, 0.045f + _mInt * 0.06f, bright); _mArpI++;
            }
            if (_playing && step % 4 == 0)
                _voices.Add(new Voice { W = Wave.Sine, Kick = true, Freq = 125, Freq2 = -1, Dur = 0.17f, Gain = 0.34f + _mInt * 0.26f, Attack = 0.006f, Cut = 20000f, T = -delay });
            if (_playing && step % 2 == 1 && _mInt > 0.35f)
                _voices.Add(new Voice { W = Wave.Noise, Freq = 0, Freq2 = -1, Dur = 0.05f, Gain = _mInt * 0.26f, Attack = 0.001f, Cut = 20000f, T = -delay });
        }

        private void MVoice(Wave w, float freq, float delay, float dur, float vol, float cut)
        {
            _voices.Add(new Voice { W = w, Freq = freq, Freq2 = -1, Dur = dur, Gain = vol, Attack = 0.014f, Cut = cut, T = -delay });
        }
    }
}
