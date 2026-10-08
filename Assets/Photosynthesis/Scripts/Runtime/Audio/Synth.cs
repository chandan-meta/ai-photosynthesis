using System;

namespace Photosynthesis
{
    /// Procedural audio renderer — every SFX, music loop and ambience bed as float PCM at 44.1 kHz.
    /// Pure C# (no UnityEngine calls) so AudioDirector can render on a worker thread. Loops are rendered
    /// longer than their length and the overhang is cross-faded into the head (Fold), so they loop seamlessly.
    public static class Synth
    {
        public const int SR = 44100;
        const float TAU = 6.28318530718f;

        // ================================================================== primitives
        public struct Rng
        {
            uint s;
            public Rng(uint seed) { s = seed == 0 ? 0x9E3779B9u : seed; }
            /// -1..1
            public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s >> 8) * (2f / 16777216f) - 1f; }
            public float Range(float a, float b) => a + (Next() * 0.5f + 0.5f) * (b - a);
            public int Index(int n) => Math.Min(n - 1, (int)Range(0f, n));
        }

        /// Chamberlin state-variable filter (lp / bp / hp outputs). q = damping (1/Q).
        struct SVF
        {
            public float lp, bp, hp;
            public void Run(float x, float fc, float q)
            {
                float f = 2f * MathF.Sin(MathF.PI * Math.Clamp(fc, 20f, 7000f) / SR);
                lp += f * bp; hp = x - lp - q * bp; bp += f * hp;
            }
        }

        struct Pink
        {
            float b0, b1, b2;
            public float Run(float w)
            {
                b0 = 0.99765f * b0 + w * 0.0990460f; b1 = 0.96300f * b1 + w * 0.2965164f; b2 = 0.57000f * b2 + w * 1.0526913f;
                return (b0 + b1 + b2 + w * 0.1848f) * 0.22f;
            }
        }

        /// Compact Freeverb-style reverb (6 damped combs + 3 all-passes). spread detunes the right channel.
        sealed class Verb
        {
            static readonly int[] C = { 1116, 1188, 1277, 1356, 1422, 1491 }, A = { 556, 441, 341 };
            readonly float[][] cb = new float[6][], ab = new float[3][];
            readonly int[] ci = new int[6], ai = new int[3];
            readonly float[] cs = new float[6];
            readonly float fb, damp;
            public Verb(float room, float damp, int spread)
            {
                fb = room; this.damp = damp;
                for (int i = 0; i < 6; i++) cb[i] = new float[C[i] + spread];
                for (int i = 0; i < 3; i++) ab[i] = new float[A[i] + spread];
            }
            public float Run(float x)
            {
                x *= 0.1f; float o = 0f;
                for (int i = 0; i < 6; i++)
                {
                    var b = cb[i]; int k = ci[i]; float y = b[k];
                    cs[i] = y * (1f - damp) + cs[i] * damp;
                    b[k] = x + cs[i] * fb; ci[i] = ++k == b.Length ? 0 : k; o += y;
                }
                for (int i = 0; i < 3; i++)
                {
                    var b = ab[i]; int k = ai[i]; float bo = b[k];
                    b[k] = o + bo * 0.5f; ai[i] = ++k == b.Length ? 0 : k; o = bo - o;
                }
                return o;
            }
        }

        static int I(float sec) => (int)(sec * SR);
        public static float Hz(float midi) => 440f * MathF.Pow(2f, (midi - 69f) / 12f);
        static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3f - 2f * x); }
        static float AD(float t, float a, float d) => t < 0f ? 0f : t < a ? t / a : MathF.Exp(-(t - a) / d);
        static float Hash(int i, uint seed) { uint h = (uint)i * 374761393u + seed * 668265263u; h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; }
        /// Smooth value noise 0..1.
        static float VNoise(float x, uint seed) { int i = (int)MathF.Floor(x); float f = x - i; f = f * f * (3f - 2f * f); float a = Hash(i, seed); return a + (Hash(i + 1, seed) - a) * f; }
        static float Blep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1f; }
            if (t > 1f - dt) { t = (t - 1f) / dt; return t * t + t + t + 1f; }
            return 0f;
        }
        /// Band-limited (polyBLEP) saw; p = phase 0..1, dt = f / SR.
        static float Saw(ref float p, float dt) { float v = 2f * p - 1f - Blep(p, dt); p += dt; if (p >= 1f) p -= 1f; return v; }
        static void Pan(float pan, out float gl, out float gr) { gl = MathF.Sqrt(0.5f * (1f - pan)); gr = MathF.Sqrt(0.5f * (1f + pan)); }

        /// Sine voice, exponential glide f0→f1 (time constant `glide`), attack/decay envelope, optional 2nd/3rd harmonics.
        static void Tone(float[] b, float t0, float f0, float f1, float amp, float atk, float dec, float glide = 0.05f, float h2 = 0f, float h3 = 0f, float len = -1f)
        {
            if (len < 0f) len = atk + dec * 6f;
            int i0 = I(t0), i1 = Math.Min(b.Length, i0 + I(len)); float ph = 0f, p2 = 0f, p3 = 0f;
            for (int i = i0; i < i1; i++)
            {
                float t = (i - i0) / (float)SR, f = f1 + (f0 - f1) * MathF.Exp(-t / glide);
                ph += f / SR; if (ph >= 1f) ph -= 1f;
                float x = MathF.Sin(TAU * ph);
                if (h2 != 0f) { p2 += 2f * f / SR; if (p2 >= 1f) p2 -= 1f; x += h2 * MathF.Sin(TAU * p2); }
                if (h3 != 0f) { p3 += 3f * f / SR; if (p3 >= 1f) p3 -= 1f; x += h3 * MathF.Sin(TAU * p3); }
                b[i] += x * amp * AD(t, atk, dec) * Math.Min(1f, (i1 - i) / 220f);
            }
        }

        static readonly float[] BR = { 1f, 2f, 2.76f, 4.07f, 5.43f }, BA = { 1f, 0.32f, 0.26f, 0.11f, 0.06f }, BD = { 1f, 0.6f, 0.45f, 0.3f, 0.2f };
        /// Glassy bell (inharmonic partials). R == null → mono into L.
        static void Bell(float[] L, float[] R, float t0, float f, float amp, float decay, float pan = 0f)
        {
            int i0 = I(t0); if (i0 < 0 || i0 >= L.Length) return;
            Pan(pan, out float gl, out float gr);
            for (int p = 0; p < BR.Length; p++)
            {
                float fp = f * BR[p]; if (fp > 15000f) break;
                int i1 = Math.Min(L.Length, i0 + I(decay * BD[p] * 4.6f));   // partial is at 1 % when cut
                float inc = fp / SR, ph = 0f, e = amp * BA[p], k = MathF.Exp(-1f / (decay * BD[p] * SR));
                for (int i = i0; i < i1; i++)
                {
                    ph += inc; if (ph >= 1f) ph -= 1f; e *= k;
                    float x = MathF.Sin(TAU * ph) * e * Math.Min(1f, (i - i0) / 90f) * Math.Min(1f, (i1 - i) / 400f);
                    if (R == null) L[i] += x; else { L[i] += x * gl; R[i] += x * gr; }
                }
            }
        }

        /// Filtered noise: env(t) amplitude, fc(t) cutoff, mode 0 lowpass · 1 bandpass · 2 highpass.
        static void Noise(float[] b, float t0, float len, Func<float, float> env, Func<float, float> fc, float q, int mode, float amp, uint seed)
        {
            var r = new Rng(seed); var f = new SVF();
            int i0 = I(t0), i1 = Math.Min(b.Length, i0 + I(len));
            for (int i = i0; i < i1; i++)
            {
                float t = (i - i0) / (float)SR;
                f.Run(r.Next(), fc(t), q);
                b[i] += (mode == 0 ? f.lp : mode == 1 ? f.bp : f.hp) * env(t) * amp;
            }
        }

        /// Karplus–Strong pluck (soft nylon/pizzicato).
        static void Pluck(float[] L, float[] R, float t0, float f, float amp, float pan, ref Rng rng, float dur = 1.6f, float bright = 0.45f)
        {
            int i0 = I(t0); if (i0 < 0 || i0 >= L.Length) return;
            int p = Math.Max(2, (int)MathF.Round(SR / f)); var d = new float[p]; float lp = 0f, mean = 0f;
            for (int i = 0; i < p; i++) { lp += bright * (rng.Next() - lp); d[i] = lp; mean += lp; }
            mean /= p; for (int i = 0; i < p; i++) d[i] = (d[i] - mean) * 2.2f;
            Pan(pan, out float gl, out float gr);
            int len = Math.Min(L.Length - i0, I(dur)), k = 0;
            for (int i = 0; i < len; i++)
            {
                int nx = k + 1 == p ? 0 : k + 1; float x = d[k]; d[k] = (d[k] + d[nx]) * 0.4985f; k = nx;
                x *= amp * Math.Min(1f, (len - i) / 900f) * Math.Min(1f, i / 30f);
                L[i0 + i] += x * gl; R[i0 + i] += x * gr;
            }
        }

        /// Soft mallet (marimba-ish sine + inharmonic overtone).
        static void Mallet(float[] L, float[] R, float t0, float f, float amp, float pan)
        {
            int i0 = I(t0); if (i0 < 0 || i0 >= L.Length) return;
            int len = Math.Min(L.Length - i0, I(1.3f));
            Pan(pan, out float gl, out float gr);
            float p1 = 0f, p2 = 0f, e1 = 1f, e2 = 1f, k1 = MathF.Exp(-1f / (0.45f * SR)), k2 = MathF.Exp(-1f / (0.07f * SR));
            for (int i = 0; i < len; i++)
            {
                p1 += f / SR; if (p1 >= 1f) p1 -= 1f; p2 += f * 3.93f / SR; if (p2 >= 1f) p2 -= 1f; e1 *= k1; e2 *= k2;
                float x = (MathF.Sin(TAU * p1) * e1 + 0.3f * MathF.Sin(TAU * p2) * e2) * amp * Math.Min(1f, i / 100f) * Math.Min(1f, (len - i) / 600f);
                L[i0 + i] += x * gl; R[i0 + i] += x * gr;
            }
        }

        /// Bubble / drop: short sine with a fast upward glide.
        static void Bubble(float[] L, float[] R, float t0, float f, float amp, float pan, float dec = 0.04f)
        {
            int i0 = I(t0); if (i0 < 0 || i0 >= L.Length) return;
            int len = Math.Min(L.Length - i0, I(dec * 6f));
            Pan(pan, out float gl, out float gr); float ph = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR, fr = f * (1.75f - 0.75f * MathF.Exp(-t / 0.018f));
                ph += fr / SR; if (ph >= 1f) ph -= 1f;
                float x = MathF.Sin(TAU * ph) * amp * MathF.Exp(-t / dec) * Math.Min(1f, i / 40f);
                if (R == null) L[i0 + i] += x; else { L[i0 + i] += x * gl; R[i0 + i] += x * gr; }
            }
        }

        /// Distant bird chirp: downward glide with fast vibrato.
        static void Chirp(float[] L, float[] R, float t0, float f, float dur, float amp, float pan)
        {
            int i0 = I(t0); if (i0 < 0 || i0 >= L.Length) return;
            int len = Math.Min(L.Length - i0, I(dur));
            Pan(pan, out float gl, out float gr); float ph = 0f;
            for (int i = 0; i < len; i++)
            {
                float u = i / (float)len, t = i / (float)SR;
                float fr = f * (1.15f - 0.35f * u) * (1f + 0.06f * MathF.Sin(TAU * 32f * t));
                ph += fr / SR; if (ph >= 1f) ph -= 1f;
                float x = MathF.Sin(TAU * ph) * amp * MathF.Sin(MathF.PI * u);
                L[i0 + i] += x * gl; R[i0 + i] += x * gr;
            }
        }

        static void VerbMono(float[] b, float wet, float room = 0.82f, float damp = 0.3f)
        {
            var v = new Verb(room, damp, 0);
            for (int i = 0; i < b.Length; i++) b[i] += v.Run(b[i]) * wet;
        }

        static void VerbStereo(float[] L, float[] R, float[] sendL, float[] sendR, float wet, float room = 0.86f, float damp = 0.3f)
        {
            var vl = new Verb(room, damp, 0); var vr = new Verb(room, damp, 23);
            for (int i = 0; i < L.Length; i++)
            {
                float l = vl.Run(sendL != null ? sendL[i] : L[i]) * wet, r = vr.Run(sendR != null ? sendR[i] : R[i]) * wet;
                L[i] += l; R[i] += r;
            }
        }

        static float[] Peak(float[] b, float peak)
        {
            float m = 0f; for (int i = 0; i < b.Length; i++) m = Math.Max(m, Math.Abs(b[i]));
            float g = m > 1e-6f ? peak / m : 0f;
            for (int i = 0; i < b.Length; i++) b[i] *= g * Math.Min(1f, (b.Length - i) / 441f);   // 10 ms tail fade
            return b;
        }

        /// Normalise to an RMS target, never letting the peak exceed `cap`.
        static void Level(float[] b, float rms, float cap)
        {
            double s = 0; float m = 0f;
            for (int i = 0; i < b.Length; i++) { s += b[i] * b[i]; m = Math.Max(m, Math.Abs(b[i])); }
            if (m < 1e-6f) return;
            float g = rms / (float)Math.Sqrt(s / b.Length); if (m * g > cap) g = cap / m;
            for (int i = 0; i < b.Length; i++) b[i] *= g;
        }

        /// Seamless loop: output = loopFrames; the `tail` overhang is equal-power cross-faded into the head.
        static float[] Fold(float[] L, float[] R, int loopFrames, int tailFrames)
        {
            var o = new float[loopFrames * 2];
            for (int i = 0; i < loopFrames; i++)
            {
                float l = L[i], r = R[i];
                if (i < tailFrames)
                {
                    float w = i / (float)tailFrames * MathF.PI * 0.5f, a = MathF.Sin(w), c = MathF.Cos(w);
                    l = l * a + L[loopFrames + i] * c; r = r * a + R[loopFrames + i] * c;
                }
                o[2 * i] = l; o[2 * i + 1] = r;
            }
            return o;
        }

        // ================================================================== SFX (mono)
        public static float[] MakeSfx(Sfx s)
        {
            var r = new Rng(977u + (uint)s * 7919u);
            float[] b;
            switch (s)
            {
                case Sfx.UiHover:
                    b = new float[I(0.35f)];
                    Tone(b, 0f, 2400f, 2400f, 1f, 0.002f, 0.02f);
                    Tone(b, 0f, 6624f, 6624f, 0.25f, 0.001f, 0.011f);
                    VerbMono(b, 0.25f); return Peak(b, 0.16f);
                case Sfx.UiClick:
                    b = new float[I(0.4f)];
                    Noise(b, 0f, 0.02f, t => MathF.Exp(-t / 0.003f), t => 5000f, 0.7f, 2, 0.6f, 11);
                    Tone(b, 0f, 1700f, 1350f, 0.8f, 0.001f, 0.03f, 0.02f);
                    Tone(b, 0f, 760f, 700f, 0.4f, 0.002f, 0.045f);
                    VerbMono(b, 0.2f); return Peak(b, 0.3f);
                case Sfx.UiSelect:
                    b = new float[I(1f)];
                    Bell(b, null, 0f, Hz(88), 0.6f, 0.22f); Bell(b, null, 0.07f, Hz(95), 0.5f, 0.28f);
                    VerbMono(b, 0.3f); return Peak(b, 0.4f);
                case Sfx.Unlock:
                    {
                        b = new float[I(2.8f)];
                        int[] up = { 84, 88, 91, 96, 100 };
                        for (int i = 0; i < up.Length; i++) Bell(b, null, i * 0.085f, Hz(up[i]), 0.55f - i * 0.05f, 0.9f);
                        Tone(b, 0f, Hz(60), Hz(60), 0.25f, 0.04f, 0.7f, 1f, 0.2f); Tone(b, 0f, Hz(67), Hz(67), 0.18f, 0.06f, 0.7f);
                        Noise(b, 0.05f, 1.4f, t => Smooth(t / 0.3f) * MathF.Exp(-t / 0.45f), t => 6500f, 0.8f, 2, 0.06f, 12);
                        VerbMono(b, 0.45f); return Peak(b, 0.7f);
                    }
                case Sfx.Success:
                    {
                        b = new float[I(3f)];
                        int[] ch = { 60, 64, 67, 71, 74 };
                        for (int i = 0; i < ch.Length; i++) Tone(b, i * 0.014f, Hz(ch[i]), Hz(ch[i]), 0.22f, 0.012f, 0.9f, 1f, 0.3f, 0.12f);
                        Bell(b, null, 0.05f, Hz(84), 0.3f, 0.8f); Bell(b, null, 0.12f, Hz(91), 0.2f, 0.7f);
                        VerbMono(b, 0.4f); return Peak(b, 0.62f);
                    }
                case Sfx.Wrong:
                    b = new float[I(1.3f)];
                    Tone(b, 0f, Hz(57), Hz(57) * 0.985f, 0.6f, 0.02f, 0.2f, 0.3f, 0.25f, 0f, 0.55f);
                    Tone(b, 0.2f, Hz(53), Hz(53) * 0.97f, 0.6f, 0.02f, 0.28f, 0.4f, 0.25f, 0f, 0.75f);
                    VerbMono(b, 0.18f); return Peak(b, 0.42f);
                case Sfx.Scan:
                    {
                        b = new float[I(1.9f)];
                        Noise(b, 0f, 1.35f, t => Smooth(t / 0.08f) * Smooth((1.35f - t) / 0.3f), t => 500f * MathF.Pow(10f, Math.Min(t / 1.1f, 1f)), 0.3f, 1, 0.9f, 21);
                        float ph = 0f; int n = I(1.25f);
                        for (int i = 0; i < n; i++)
                        {
                            float t = i / (float)SR, f = 700f * MathF.Pow(2600f / 700f, Math.Min(t / 1.1f, 1f));
                            ph += f / SR; if (ph >= 1f) ph -= 1f;
                            b[i] += MathF.Sin(TAU * ph) * 0.22f * (0.55f + 0.45f * MathF.Sin(TAU * 22f * t)) * Smooth(t / 0.05f) * Smooth((1.25f - t) / 0.2f);
                        }
                        for (int i = 0; i < 7; i++) { float f = r.Range(2200f, 4200f); Tone(b, r.Range(0.1f, 1.2f), f, f, 0.12f, 0.002f, 0.018f); }
                        VerbMono(b, 0.2f); return Peak(b, 0.5f);
                    }
                case Sfx.Beep:
                    b = new float[I(0.35f)];
                    Tone(b, 0f, 1760f, 1760f, 0.8f, 0.004f, 10f, 1f, 0.12f, 0f, 0.13f);
                    VerbMono(b, 0.1f); return Peak(b, 0.4f);
                case Sfx.TargetLock:
                    b = new float[I(0.9f)];
                    Tone(b, 0f, 1318.5f, 1318.5f, 0.6f, 0.003f, 10f, 1f, 0f, 0f, 0.065f);
                    Tone(b, 0.085f, 1760f, 1760f, 0.6f, 0.003f, 10f, 1f, 0f, 0f, 0.07f);
                    Tone(b, 0.19f, 880f, 880f, 0.7f, 0.002f, 0.12f, 1f, 0f, 0.33f);
                    Noise(b, 0.19f, 0.01f, t => MathF.Exp(-t / 0.002f), t => 6000f, 0.7f, 2, 0.5f, 22);
                    VerbMono(b, 0.15f); return Peak(b, 0.48f);
                case Sfx.AnalysisComplete:
                    {
                        b = new float[I(2.2f)];
                        int[] up = { 84, 88, 91 };
                        for (int i = 0; i < 3; i++) Tone(b, i * 0.1f, Hz(up[i]), Hz(up[i]), 0.45f, 0.003f, 10f, 1f, 0.1f, 0f, 0.07f);
                        int[] ch = { 72, 76, 79, 84 };
                        for (int i = 0; i < ch.Length; i++) Bell(b, null, 0.32f + i * 0.02f, Hz(ch[i]), 0.25f, 0.7f);
                        Noise(b, 0.3f, 1f, t => Smooth(t / 0.1f) * MathF.Exp(-t / 0.35f), t => 6500f, 0.8f, 2, 0.04f, 23);
                        VerbMono(b, 0.35f); return Peak(b, 0.55f);
                    }
                case Sfx.Whoosh:
                    {
                        b = new float[I(1.4f)]; const float D = 0.95f;
                        Func<float, float> env = t => { float u = MathF.Sin(MathF.PI * MathF.Pow(Math.Min(t / D, 1f), 0.8f)); return u * u; };
                        Noise(b, 0f, D, env, t => 300f * MathF.Pow(8f, MathF.Sin(MathF.PI * Math.Min(t / D, 1f))), 0.45f, 1, 1f, 31);
                        Noise(b, 0f, D, env, t => 180f, 0.9f, 0, 0.5f, 32);
                        VerbMono(b, 0.15f); return Peak(b, 0.55f);
                    }
                case Sfx.Materialize:
                    {
                        b = new float[I(3.4f)];
                        int[] pent = { 74, 76, 78, 81, 83, 86, 88, 90, 93, 95, 98 };
                        for (int i = 0; i < 42; i++)
                        {
                            float t = 1.9f * MathF.Sqrt(r.Range(0f, 1f));
                            Bell(b, null, t, Hz(pent[r.Index(pent.Length)]), r.Range(0.05f, 0.12f) * (0.4f + 0.6f * t / 1.9f), r.Range(0.25f, 0.6f));
                        }
                        Noise(b, 0f, 2.2f, t => Smooth(t / 1.6f) * (t > 1.6f ? MathF.Exp(-(t - 1.6f) / 0.25f) : 1f), t => 800f * MathF.Pow(7f, Math.Min(t / 1.6f, 1f)), 0.5f, 1, 0.25f, 41);
                        Tone(b, 0f, Hz(50), Hz(50), 0.3f, 1.4f, 0.5f, 1f, 0.2f, 0f, 2.3f);
                        Tone(b, 0.1f, Hz(57), Hz(57), 0.2f, 1.3f, 0.5f, 1f, 0f, 0f, 2.2f);
                        int[] bloom = { 86, 90, 93, 98 };
                        for (int i = 0; i < bloom.Length; i++) Bell(b, null, 1.6f + i * 0.03f, Hz(bloom[i]), 0.3f, 1f);
                        VerbMono(b, 0.45f); return Peak(b, 0.62f);
                    }
                case Sfx.WaterAbsorb:
                    b = new float[I(1.3f)];
                    Tone(b, 0f, 380f, 1150f, 0.7f, 0.003f, 0.07f, 0.03f);
                    Tone(b, 0f, 170f, 230f, 0.35f, 0.01f, 0.12f, 0.08f);
                    for (int i = 0; i < 6; i++) Bubble(b, null, r.Range(0.07f, 0.55f), r.Range(600f, 1400f), r.Range(0.15f, 0.32f), 0f, r.Range(0.025f, 0.045f));
                    VerbMono(b, 0.22f); return Peak(b, 0.55f);
                case Sfx.WaterRush:
                    {
                        b = new float[I(3.4f)];
                        Func<float, float> env = t => Smooth(t / 0.5f) * Smooth((3f - t) / 1.2f);
                        Noise(b, 0f, 3f, t => env(t) * (0.8f + 0.2f * MathF.Sin(TAU * 0.9f * t)), t => 1100f, 0.8f, 0, 0.9f, 51);
                        Noise(b, 0f, 3f, t => env(t) * (0.7f + 0.3f * MathF.Sin(TAU * 7.3f * t)), t => 420f * (1f + 0.3f * MathF.Sin(TAU * 1.7f * t)), 0.5f, 1, 0.6f, 52);
                        for (int i = 0; i < 10; i++) Bubble(b, null, r.Range(0.2f, 2.6f), r.Range(250f, 700f), 0.1f, 0f, 0.05f);
                        VerbMono(b, 0.2f); return Peak(b, 0.5f);
                    }
                case Sfx.EnergyBuild:
                    {
                        b = new float[I(3f)]; const float D = 2.3f; var f = new SVF(); float p1 = 0f, p2 = 0f, p3 = 0f; int n = I(2.5f);
                        for (int i = 0; i < n; i++)
                        {
                            float t = i / (float)SR, u = Math.Min(t / D, 1f), fr = 110f * MathF.Pow(4f, MathF.Pow(u, 1.3f));
                            float x = Saw(ref p1, fr / SR) + Saw(ref p2, fr * 1.007f / SR) + 0.6f * Saw(ref p3, fr * 2.003f / SR);
                            f.Run(x, 300f + 5500f * u * u, 0.6f);
                            float trem = 1f - 0.35f * (0.5f + 0.5f * MathF.Sin(TAU * (4f * t + 7f * t * t / D)));
                            b[i] += f.lp * trem * MathF.Pow(u, 1.4f) * Smooth((2.5f - t) / 0.15f) * 0.5f;
                        }
                        Noise(b, 0f, 2.4f, t => MathF.Pow(Math.Min(t / D, 1f), 2f) * Smooth((2.45f - t) / 0.15f), t => 6000f, 0.7f, 2, 0.08f, 61);
                        VerbMono(b, 0.25f); return Peak(b, 0.6f);
                    }
                case Sfx.SunImpact:
                    {
                        b = new float[I(4.6f)];
                        float ph = 0f; int n = I(3f);
                        for (int i = 0; i < n; i++)
                        {
                            float t = i / (float)SR, fr = 38f + 75f * MathF.Exp(-t / 0.12f);
                            ph += fr / SR; if (ph >= 1f) ph -= 1f;
                            b[i] += MathF.Sin(TAU * ph) * Math.Min(1f, t / 0.004f) * MathF.Exp(-t / 0.9f);
                        }
                        Noise(b, 0f, 2.5f, t => Math.Min(1f, t / 0.003f) * MathF.Exp(-t / 0.35f), t => 260f * MathF.Exp(-t / 0.6f) + 60f, 0.7f, 0, 1.2f, 62);
                        var sh = new float[b.Length];
                        int[] tail = { 81, 85, 88, 93, 97 };
                        for (int i = 0; i < tail.Length; i++) Bell(sh, null, 0.04f + i * 0.06f, Hz(tail[i]), 0.22f, 1.6f);
                        Noise(sh, 0.02f, 2.6f, t => Smooth(t / 0.1f) * MathF.Exp(-t / 0.8f), t => 6500f, 0.7f, 2, 0.05f, 63);
                        VerbMono(sh, 0.6f, 0.88f);
                        for (int i = 0; i < b.Length; i++) b[i] += sh[i];
                        return Peak(b, 0.9f);
                    }
                case Sfx.ChloroplastActivate:
                    {
                        b = new float[I(2.4f)];
                        var f = new SVF(); float p = 0f; int n = I(1.4f);
                        for (int i = 0; i < n; i++) { float t = i / (float)SR; f.Run(Saw(ref p, 110f / SR), 600f, 0.7f); b[i] += f.lp * 0.3f * AD(t, 0.25f, 0.5f) * Math.Min(1f, (n - i) / 400f); }
                        int[] ar = { 67, 71, 74, 79, 83 };
                        for (int i = 0; i < ar.Length; i++) Tone(b, i * 0.055f, Hz(ar[i]), Hz(ar[i]), 0.28f, 0.003f, 0.22f, 1f, 0f, 0.33f);
                        Bell(b, null, 0.3f, Hz(91), 0.3f, 0.9f); Bell(b, null, 0.36f, Hz(98), 0.15f, 0.7f);
                        VerbMono(b, 0.35f); return Peak(b, 0.55f);
                    }
                case Sfx.GlucoseDiscover:
                    {
                        b = new float[I(4.2f)];
                        int[] fm9 = { 77, 81, 84, 88, 91, 96 };
                        for (int i = 0; i < fm9.Length; i++) Bell(b, null, 0.05f + i * 0.07f, Hz(fm9[i]), 0.3f, 1.6f);
                        int[] pad = { 53, 60, 69, 76 };
                        for (int i = 0; i < pad.Length; i++) Tone(b, 0f, Hz(pad[i]), Hz(pad[i]), 0.14f, 0.5f, 1.4f, 1f, 0.15f);
                        int[] sp = { 89, 91, 93, 96, 98, 100, 103 };
                        for (int i = 0; i < 20; i++) Bell(b, null, r.Range(0.3f, 1.8f), Hz(sp[r.Index(sp.Length)]), 0.06f, 0.5f);
                        VerbMono(b, 0.5f); return Peak(b, 0.68f);
                    }
                case Sfx.OxygenRelease:
                    b = new float[I(2.4f)];
                    for (int i = 0; i < 8; i++) Bubble(b, null, 0.05f + i * 0.15f + r.Range(-0.05f, 0.05f), r.Range(480f, 900f), r.Range(0.25f, 0.4f), 0f, 0.045f);
                    Noise(b, 0f, 1.6f, t => Smooth(t / 0.3f) * MathF.Exp(-t / 0.7f), t => 3800f, 0.6f, 1, 0.12f, 71);
                    VerbMono(b, 0.3f); return Peak(b, 0.48f);
                case Sfx.Sparkle:
                    {
                        b = new float[I(1.9f)];
                        int[] p = { 93, 95, 97, 100, 102, 105, 107 };
                        for (int i = 0; i < 12; i++) Bell(b, null, r.Range(0f, 0.6f), Hz(p[r.Index(p.Length)]), r.Range(0.1f, 0.25f), r.Range(0.2f, 0.45f));
                        VerbMono(b, 0.4f); return Peak(b, 0.45f);
                    }
                case Sfx.FinaleCadence:
                    {   // the Resolution music (F major) lands: Csus4 → F(add9) pad, a rising bell arpeggio and a low F, long tail
                        b = new float[I(8.5f)];
                        int[] sus = { 48, 55, 60, 65, 67 };
                        foreach (var m in sus) Tone(b, 0f, Hz(m), Hz(m), 0.1f, 0.35f, 0.8f, 1f, 0.12f, 0f, 1.7f);
                        int[] res = { 41, 53, 60, 65, 67, 69, 72 };
                        foreach (var m in res) Tone(b, 1.4f, Hz(m), Hz(m), m < 50 ? 0.16f : 0.09f, 0.7f, 2.4f, 1f, 0.15f, 0f, 6.2f);
                        int[] bell = { 77, 81, 84, 88, 89 };
                        for (int i = 0; i < bell.Length; i++) Bell(b, null, 1.45f + i * 0.11f, Hz(bell[i]), 0.2f, 2.4f);
                        VerbMono(b, 0.5f, 0.9f); return Peak(b, 0.7f);
                    }
                default: // Pop
                    b = new float[I(0.35f)];
                    Tone(b, 0f, 280f, 950f, 0.9f, 0.001f, 0.035f, 0.012f);
                    Noise(b, 0f, 0.006f, t => 1f, t => 4000f, 0.8f, 2, 0.2f, 81);
                    VerbMono(b, 0.1f); return Peak(b, 0.45f);
            }
        }

        // ================================================================== music (stereo loops)
        sealed class Song
        {
            public int[][] chords; public float chordSec = 5f, bpm = 90f, cutoff = 1500f, lfo = 0.07f, lfoDepth = 0.35f, detune = 0.09f, saw = 0.45f, sub = 0.3f;
            public int[] arp; public float arpDiv = 2f, arpAmp = 0.3f; public int arpOct = 12; public bool mallet;
            public float sparkle, bells, shimmer, pulse, shaker, verb = 0.35f; public uint seed = 1;
        }

        static Song SongFor(MusicState s)
        {
            switch (s)
            {
                case MusicState.NewWorld: // awe — Dmaj9 · Bm11 · Gmaj7#11 · A(add9)
                    return new Song { chords = new[] { new[] { 50, 57, 61, 64, 66 }, new[] { 47, 54, 57, 62, 64 }, new[] { 43, 50, 54, 59, 61 }, new[] { 45, 52, 59, 61, 64 } },
                        chordSec = 5f, cutoff = 1700f, saw = 0.5f, sparkle = 0.5f, bells = 0.1f, shimmer = 0.05f, verb = 0.45f, seed = 11 };
                case MusicState.Investigation: // curious, playful — Am9 · Fmaj7 · C6/9 · G6, plucked arpeggio + soft shaker
                    return new Song { chords = new[] { new[] { 45, 52, 55, 59, 60 }, new[] { 41, 48, 52, 57 }, new[] { 48, 55, 57, 62, 64 }, new[] { 43, 50, 52, 59 } },
                        bpm = 96f, chordSec = 5f, cutoff = 1100f, saw = 0.35f, arp = new[] { 0, 3, 2, 4, -1, 3, 1, 5 }, arpDiv = 2f, arpAmp = 0.32f,
                        shaker = 0.05f, sparkle = 0.1f, verb = 0.3f, seed = 22 };
                case MusicState.Microscopic: // mysterious, glassy — Ebmaj7#11 · Cm(add9) · Abmaj7 · Bbsus2
                    return new Song { chords = new[] { new[] { 51, 58, 62, 67, 69 }, new[] { 48, 55, 62, 63 }, new[] { 44, 51, 55, 60 }, new[] { 46, 53, 60, 65 } },
                        chordSec = 6f, cutoff = 850f, lfo = 0.11f, lfoDepth = 0.55f, saw = 0.25f, sub = 0.4f, sparkle = 0.35f, shimmer = 0.04f, verb = 0.5f, seed = 33 };
                case MusicState.WaterJourney: // flowing — E · C#m7 · Amaj9 · B6sus, rolling mallets
                    return new Song { chords = new[] { new[] { 40, 47, 56, 59, 64 }, new[] { 49, 56, 59, 64 }, new[] { 45, 52, 56, 59, 61 }, new[] { 47, 54, 64, 68 } },
                        bpm = 104f, chordSec = 8f * 60f / 104f, cutoff = 1500f, saw = 0.4f, arp = new[] { 0, 1, 2, 3, 4, 3, 2, 1 }, arpDiv = 4f, arpAmp = 0.16f, mallet = true,
                        pulse = 0.15f, sparkle = 0.2f, verb = 0.35f, seed = 44 };
                case MusicState.SunlightReveal: // radiant — C · D/C · Em7 · Fmaj7#11
                    return new Song { chords = new[] { new[] { 48, 55, 60, 64, 67 }, new[] { 48, 62, 66, 69 }, new[] { 52, 59, 62, 67 }, new[] { 41, 48, 64, 69, 71 } },
                        chordSec = 5f, cutoff = 2600f, saw = 0.65f, sparkle = 0.6f, bells = 0.12f, shimmer = 0.08f, verb = 0.45f, seed = 55 };
                case MusicState.Reaction: // energetic sparkle — Am · F · C · G, pumping pad, 16th plucks
                    return new Song { chords = new[] { new[] { 45, 52, 57, 60, 64 }, new[] { 41, 48, 57, 60, 65 }, new[] { 48, 55, 60, 64, 67 }, new[] { 43, 50, 59, 62, 67 } },
                        bpm = 110f, chordSec = 8f * 60f / 110f, cutoff = 2000f, saw = 0.5f, arp = new[] { 0, 2, 4, 5, 4, 2, 3, 1 }, arpDiv = 4f, arpAmp = 0.22f,
                        pulse = 0.45f, shaker = 0.06f, sparkle = 1.4f, verb = 0.3f, seed = 66 };
                case MusicState.Resolution: // warm, satisfied — Fmaj7 · Am7 · Bbmaj9 · Csus4
                    return new Song { chords = new[] { new[] { 41, 48, 52, 57, 60 }, new[] { 45, 52, 55, 60 }, new[] { 46, 53, 57, 62 }, new[] { 48, 55, 60, 65 } },
                        bpm = 70f, chordSec = 5f, cutoff = 1300f, saw = 0.35f, arp = new[] { 4, -1, 2, -1, 3, -1, -1, -1 }, arpDiv = 1f, arpAmp = 0.2f, mallet = true,
                        bells = 0.09f, sparkle = 0.3f, verb = 0.4f, seed = 77 };
                default: // FreeExplore — calm: Gmaj7 · Em9 · Cmaj7 · D6
                    return new Song { chords = new[] { new[] { 43, 50, 54, 59 }, new[] { 40, 47, 55, 62, 66 }, new[] { 48, 55, 59, 64 }, new[] { 50, 57, 59, 66 } },
                        bpm = 72f, chordSec = 5.5f, cutoff = 1200f, saw = 0.3f, arp = new[] { 0, -1, 2, -1, 4, -1, -1, 3 }, arpDiv = 1f, arpAmp = 0.2f,
                        bells = 0.05f, sparkle = 0.25f, verb = 0.42f, seed = 88 };
            }
        }

        /// Interleaved stereo loop for a music state.
        public static float[] MakeMusic(MusicState state)
        {
            var s = SongFor(state);
            int n = s.chords.Length; float loop = n * s.chordSec, tail = 4f;
            int lf = I(loop), tf = I(tail), frames = lf + tf;
            var padL = new float[frames]; var padR = new float[frames]; var L = new float[frames]; var R = new float[frames];
            var rng = new Rng(s.seed);
            float atk = Math.Min(1.6f, s.chordSec * 0.32f);
            int total = n + (int)MathF.Ceiling(tail / s.chordSec) + 1;
            for (int k = 0; k < total; k++)
            {
                int[] ch = s.chords[k % n];
                float t0 = k * s.chordSec - 0.3f, t1 = (k + 1) * s.chordSec - 0.3f;
                int i0 = Math.Clamp(I(t0), 0, frames), i1 = Math.Clamp(I(t1 + 2.6f), 0, frames);
                float vg = 0.2f / MathF.Sqrt(ch.Length);
                for (int v = 0; v < ch.Length; v++)
                {
                    float f = Hz(ch[v]), d1 = f * MathF.Pow(2f, s.detune / 12f) / SR, d2 = f * MathF.Pow(2f, -s.detune / 12f) / SR, d3 = f / SR;
                    float p1 = rng.Range(0f, 1f), p2 = rng.Range(0f, 1f), p3 = rng.Range(0f, 1f), p4 = 0f;
                    float sh = v >= ch.Length - 2 ? s.shimmer : 0f; bool root = v == 0 && s.sub > 0f;
                    for (int i = i0; i < i1; i++)
                    {
                        float t = i / (float)SR, e = Smooth((t - t0) / atk);
                        if (t > t1) e *= MathF.Exp(-(t - t1) / 0.55f);
                        float a = Saw(ref p1, d1), b = Saw(ref p2, d2), body = MathF.Sin(TAU * p3) * (1f - s.saw);
                        p3 += d3; if (p3 >= 1f) p3 -= 1f;
                        padL[i] += (a * s.saw + body) * e * vg; padR[i] += (b * s.saw + body) * e * vg;
                        if (root) { p4 += d3 * 0.5f; if (p4 >= 1f) p4 -= 1f; float x = MathF.Sin(TAU * p4) * s.sub * e * 0.5f; L[i] += x; R[i] += x; }
                        if (sh > 0f) { float x = MathF.Sin(TAU * 4f * p3) * sh * e * (0.6f + 0.4f * MathF.Sin(t * 1.3f + v)); L[i] += x * 0.8f; R[i] += x; }
                    }
                }
                if (s.bells > 0f)
                    for (int j = 0; j < 3; j++) Bell(L, R, k * s.chordSec + 0.05f + j * 0.11f, Hz(ch[ch.Length - 3 + j] + 24), s.bells, 1.8f, -0.4f + j * 0.4f);
            }
            // pad: slow filter sweep (+ optional rhythmic pump)
            var fl = new SVF(); var fr = new SVF(); float beat = 60f / s.bpm;
            for (int i = 0; i < frames; i++)
            {
                float t = i / (float)SR, fc = s.cutoff * (1f + s.lfoDepth * MathF.Sin(TAU * s.lfo * t)), g = 1f;
                if (s.pulse > 0f) { float ph = (t % beat) / beat; g = 1f - s.pulse * MathF.Exp(-ph * 5f) * Smooth(ph * 40f); }
                fl.Run(padL[i], fc, 0.85f); fr.Run(padR[i], fc * 1.03f, 0.85f);
                L[i] += fl.lp * g; R[i] += fr.lp * g;
            }
            if (s.arp != null)
            {
                float step = beat / s.arpDiv; int bar = (int)(s.arpDiv * 4f);
                for (int k = 0; k * step < loop + tail - 0.05f; k++)
                {
                    int idx = s.arp[k % s.arp.Length]; if (idx < 0) continue;
                    float t = k * step + 0.02f + rng.Range(-0.004f, 0.004f);
                    int[] ch = s.chords[(int)(t / s.chordSec) % n];
                    int note = ch[idx % ch.Length] + s.arpOct + (idx >= ch.Length ? 12 : 0);
                    float acc = k % bar == 0 ? 1f : k % 2 == 0 ? 0.8f : 0.62f, pan = k % 2 == 0 ? -0.3f : 0.3f;
                    if (s.mallet) Mallet(L, R, t, Hz(note), s.arpAmp * acc, pan);
                    else Pluck(L, R, t, Hz(note), s.arpAmp * acc, pan, ref rng);
                }
            }
            int sparks = (int)(s.sparkle * (loop + tail));
            for (int j = 0; j < sparks; j++)
            {
                float t = rng.Range(0f, loop + tail - 0.5f);
                int[] ch = s.chords[(int)(t / s.chordSec) % n];
                int note = ch[rng.Index(ch.Length)] + (rng.Next() > 0f ? 36 : 24);
                while (Hz(note) > 5000f) note -= 12;
                Bell(L, R, t, Hz(note), rng.Range(0.025f, 0.055f), rng.Range(0.9f, 1.8f), rng.Range(-0.8f, 0.8f));
            }
            if (s.shaker > 0f)
            {
                float hp = 0f; int sl = I(0.08f);
                for (int k = 1; k * beat * 0.5f < loop + tail; k += 2)
                {
                    int i0 = I(k * beat * 0.5f);
                    for (int i = 0; i < sl && i0 + i < frames; i++)
                    {
                        float w = rng.Next(); hp += 0.5f * (w - hp);
                        float x = (w - hp) * MathF.Exp(-i / (0.022f * SR)) * s.shaker * Math.Min(1f, i / 40f);
                        L[i0 + i] += x * 0.8f; R[i0 + i] += x;
                    }
                }
            }
            VerbStereo(L, R, null, null, s.verb);
            var o = Fold(L, R, lf, tf); Level(o, 0.13f, 0.9f); return o;
        }

        // ================================================================== ambience beds (stereo loops)
        public static bool HasBed(string bed) => bed == "chamber" || bed == "micro" || bed == "cell" || bed == "soil" || bed == "xylem" || bed == "dark";

        public static float[] MakeAmbience(string bed)
        {
            const float loop = 16f, tail = 2.5f;
            int lf = I(loop), tf = I(tail), n = lf + tf;
            var L = new float[n]; var R = new float[n]; var wl = new float[n]; var wr = new float[n];
            uint seed = (uint)bed.Length * 131u + bed[0];
            var r = new Rng(seed);
            float wet = 0.3f, room = 0.84f, rms = 0.06f;
            switch (bed)
            {
                case "chamber": // airy room tone + leaf rustle + distant birds
                    {
                        var pl = new Pink(); var pr = new Pink(); var fl = new SVF(); var fr = new SVF(); var rl = new SVF(); var rr = new SVF();
                        var nl = new Rng(seed + 1); var nr = new Rng(seed + 2);
                        for (int i = 0; i < n; i++)
                        {
                            float t = i / (float)SR, sw = 0.75f + 0.25f * VNoise(t * 0.18f, 3);
                            fl.Run(pl.Run(nl.Next()), 650f, 1.1f); fr.Run(pr.Run(nr.Next()), 700f, 1.1f);
                            L[i] += fl.lp * 0.9f * sw; R[i] += fr.lp * 0.9f * sw;
                            float gl = VNoise(t * 0.6f, 11), gr = VNoise(t * 0.6f + 37f, 11); gl *= gl * gl * gl; gr *= gr * gr * gr;
                            float fl2 = 0.6f + 0.4f * VNoise(t * 14f, 19);
                            rl.Run(nl.Next(), 3200f + 900f * gl, 0.6f); rr.Run(nr.Next(), 3400f, 0.6f);
                            float xl = rl.bp * gl * fl2 * 0.5f, xr = rr.bp * gr * fl2 * 0.5f;
                            L[i] += xl; R[i] += xr; wl[i] += xl * 0.5f; wr[i] += xr * 0.5f;
                        }
                        for (int k = 0; k < 6; k++)
                        {
                            float t0 = r.Range(0.4f, loop + tail - 1.5f), pan = r.Range(-0.8f, 0.8f), f0 = r.Range(2600f, 4200f);
                            int notes = 2 + r.Index(4); float t = t0;
                            for (int j = 0; j < notes; j++, t += r.Range(0.09f, 0.16f)) Chirp(wl, wr, t, f0 * r.Range(0.85f, 1.15f), r.Range(0.05f, 0.11f), r.Range(0.05f, 0.09f), pan);
                        }
                        wet = 0.45f; rms = 0.05f;
                    }
                    break;
                case "micro":
                case "cell": // low biological hum + gentle bubbles (+ organelle glugs in the cell)
                    {
                        bool cell = bed == "cell";
                        float b0 = cell ? 65.4f : 55f; var fl = new SVF(); var fr = new SVF(); var nl = new Rng(seed + 3); var nr = new Rng(seed + 4);
                        float[] ph = new float[4], ph2 = new float[4]; float[] amp = { 0.25f, 0.14f, 0.06f, 0.03f };
                        for (int i = 0; i < n; i++)
                        {
                            float t = i / (float)SR, am = 0.75f + 0.25f * MathF.Sin(TAU * (cell ? 0.18f : 0.08f) * t);
                            float hl = 0f, hr = 0f;
                            for (int h = 0; h < 4; h++)
                            {
                                ph[h] += b0 * (h + 1) * 1.003f / SR; if (ph[h] >= 1f) ph[h] -= 1f;
                                ph2[h] += (b0 * (h + 1) + 0.3f) / SR; if (ph2[h] >= 1f) ph2[h] -= 1f;
                                hl += MathF.Sin(TAU * ph[h]) * amp[h]; hr += MathF.Sin(TAU * ph2[h]) * amp[h];
                            }
                            float wash = 0.6f + 0.4f * VNoise(t * 0.2f, 5);
                            fl.Run(nl.Next(), 350f, 1f); fr.Run(nr.Next(), 380f, 1f);
                            L[i] += (hl * am + fl.lp * 0.5f * wash) * 0.8f; R[i] += (hr * am + fr.lp * 0.5f * wash) * 0.8f;
                        }
                        int count = cell ? 22 : 14;
                        for (int k = 0; k < count; k++)
                        {
                            float t = r.Range(0.1f, loop + tail - 0.3f), pan = r.Range(-0.9f, 0.9f), f = cell ? r.Range(220f, 700f) : r.Range(380f, 1100f), a = r.Range(0.05f, 0.1f);
                            Bubble(L, R, t, f, a, pan, r.Range(0.05f, 0.09f)); Bubble(wl, wr, t, f, a, pan, 0.06f);
                        }
                        if (cell)
                            for (int k = 0; k < 4; k++) Glug(L, R, r.Range(0.5f, loop + tail - 1f), r.Range(90f, 140f), 0.12f, r.Range(-0.5f, 0.5f));
                        wet = 0.35f; rms = 0.06f;
                    }
                    break;
                case "soil": // deep earthy rumble + drips in a cave-like space
                    {
                        var nl = new Rng(seed + 5); var nr = new Rng(seed + 6); var fl = new SVF(); var fr = new SVF(); float bl = 0f, br = 0f, ps = 0f;
                        for (int i = 0; i < n; i++)
                        {
                            float t = i / (float)SR;
                            bl = bl * 0.995f + nl.Next() * 0.05f; br = br * 0.995f + nr.Next() * 0.05f;
                            fl.Run(bl, 140f, 1f); fr.Run(br, 150f, 1f);
                            ps += 36f / SR; if (ps >= 1f) ps -= 1f;
                            float sub = MathF.Sin(TAU * ps) * 0.18f * (0.7f + 0.3f * MathF.Sin(TAU * 0.1f * t));
                            L[i] += fl.lp * 1.6f + sub; R[i] += fr.lp * 1.6f + sub;
                        }
                        for (int k = 0; k < 2; k++)
                        {
                            float t0 = r.Range(1f, loop - 2f);
                            Noise(L, t0, 1.6f, t => MathF.Sin(MathF.PI * Math.Min(t / 1.6f, 1f)), t => 200f, 0.4f, 1, 0.25f, seed + 10 + (uint)k);
                            Noise(R, t0 + 0.05f, 1.6f, t => MathF.Sin(MathF.PI * Math.Min(t / 1.6f, 1f)), t => 190f, 0.4f, 1, 0.25f, seed + 20 + (uint)k);
                        }
                        for (int k = 0; k < 6; k++)
                        {
                            float t = r.Range(0.3f, loop + tail - 0.5f), f = r.Range(1400f, 2500f), pan = r.Range(-0.8f, 0.8f), a = r.Range(0.09f, 0.14f), d = r.Range(0.09f, 0.16f);
                            Bubble(L, R, t, f, a * 0.6f, pan, d); Bubble(wl, wr, t, f, a, pan, d);
                        }
                        wet = 0.5f; room = 0.88f; rms = 0.07f;
                    }
                    break;
                case "dark": // the lights are out (Clue 3 → skylight): a low, slow hum and faint ticks of the dormant factory, never silence
                    {
                        float[] ph = new float[3], fq = { 41.2f, 61.7f, 82.4f }, am = { 0.32f, 0.12f, 0.05f };
                        var nl = new Rng(seed + 5); var fl = new SVF();
                        for (int i = 0; i < n; i++)
                        {
                            float t = i / (float)SR, sw = 0.7f + 0.3f * MathF.Sin(TAU * 0.06f * t), h = 0f;
                            for (int k = 0; k < 3; k++) { ph[k] += fq[k] / SR; if (ph[k] >= 1f) ph[k] -= 1f; h += MathF.Sin(TAU * ph[k]) * am[k]; }
                            fl.Run(nl.Next(), 220f, 1f);
                            float x = (h * sw + fl.lp * 0.25f) * 0.8f;
                            L[i] += x; R[i] += x * 0.96f;
                        }
                        for (int k = 0; k < 10; k++)
                        {
                            float t = r.Range(0.2f, loop + tail - 0.3f), pan = r.Range(-0.7f, 0.7f);
                            Bubble(wl, wr, t, r.Range(900f, 1500f), 0.03f, pan, 0.02f);   // faint ticks, mostly reverb
                        }
                        wet = 0.4f; rms = 0.045f;
                    }
                    break;
                default: // "xylem": flowing water rush + gurgles + low hum
                    {
                        var nl = new Rng(seed + 7); var nr = new Rng(seed + 8); var al = new SVF(); var ar = new SVF(); var bl = new SVF(); var br = new SVF(); float ph = 0f;
                        for (int i = 0; i < n; i++)
                        {
                            float t = i / (float)SR, surge = 0.75f + 0.25f * VNoise(t * 0.7f, 9), fl = 1f + 0.25f * MathF.Sin(TAU * 7.3f * t);
                            float w1 = nl.Next(), w2 = nr.Next();
                            al.Run(w1, 1000f, 0.8f); ar.Run(w2, 1050f, 0.8f); bl.Run(w1, 420f * fl, 0.5f); br.Run(w2, 440f * fl, 0.5f);
                            ph += 70f / SR; if (ph >= 1f) ph -= 1f; float hum = MathF.Sin(TAU * ph) * 0.1f;
                            L[i] += (al.lp * 0.5f + bl.bp * 0.3f) * surge + hum; R[i] += (ar.lp * 0.5f + br.bp * 0.3f) * surge + hum;
                        }
                        for (int k = 0; k < 18; k++)
                        {
                            float t = r.Range(0.1f, loop + tail - 0.3f), f = r.Range(250f, 700f), pan = r.Range(-0.9f, 0.9f);
                            Bubble(L, R, t, f, 0.08f, pan, 0.05f); Bubble(wl, wr, t, f, 0.06f, pan, 0.05f);
                        }
                        wet = 0.25f; rms = 0.08f;
                    }
                    break;
            }
            VerbStereo(L, R, wl, wr, wet, room);
            var o = Fold(L, R, lf, tf); Level(o, rms, 0.7f); return o;
        }

        /// Low wet "glug" (organelle movement): sine with a downward pitch drop.
        static void Glug(float[] L, float[] R, float t0, float f, float amp, float pan)
        {
            int i0 = I(t0); if (i0 < 0 || i0 >= L.Length) return;
            int len = Math.Min(L.Length - i0, I(0.6f)); Pan(pan, out float gl, out float gr); float ph = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR; ph += f * (1f + 0.5f * MathF.Exp(-t / 0.05f)) / SR; if (ph >= 1f) ph -= 1f;
                float x = MathF.Sin(TAU * ph) * amp * MathF.Exp(-t / 0.18f) * Math.Min(1f, i / 200f);
                L[i0 + i] += x * gl; R[i0 + i] += x * gr;
            }
        }
    }
}
