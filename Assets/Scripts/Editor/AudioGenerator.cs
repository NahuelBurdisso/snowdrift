using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Snowdrift.Editor
{
    // Procedurally synthesizes the three gameplay audio clips as 16-bit PCM 44.1 kHz WAVs:
    //   Assets/Audio/wind.wav  — ~4 s loopable low-passed brown noise (speed wind loop)
    //   Assets/Audio/carve.wav — ~2 s loopable high-passed hiss (snow carve loop)
    //   Assets/Audio/thump.wav — ~0.35 s landing thump (sine sweep 120→60 Hz + noise attack)
    //
    // Idempotent: overwrites existing files. Run via:
    //   Unity.exe -batchmode -projectPath . -executeMethod Snowdrift.Editor.AudioGenerator.GenerateAll -quit
    // ProjectSetup.Step5_BuildMainScene wires these clips into GameFeel's AudioSources,
    // so rebuild the scene after generating.
    public static class AudioGenerator
    {
        const int SampleRate = 44100;

        public static void GenerateAll()
        {
            Directory.CreateDirectory("Assets/Audio");

            WriteWav("Assets/Audio/wind.wav",  GenerateWind(4f));
            WriteWav("Assets/Audio/carve.wav", GenerateCarve(2f));
            WriteWav("Assets/Audio/thump.wav", GenerateThump(0.35f));
            WriteWav("Assets/Audio/music.wav", GenerateMusic());
            WriteWav("Assets/Audio/crash.wav", GenerateCrash(0.6f));
            WriteWav("Assets/Audio/chime.wav", GenerateChime(0.5f));

            AssetDatabase.Refresh();

            // Loop-friendly import settings
            ConfigureImporter("Assets/Audio/wind.wav");
            ConfigureImporter("Assets/Audio/carve.wav");
            ConfigureImporter("Assets/Audio/thump.wav");
            ConfigureImporter("Assets/Audio/music.wav");
            ConfigureImporter("Assets/Audio/crash.wav");
            ConfigureImporter("Assets/Audio/chime.wav");

            Debug.Log("[AudioGenerator] Generated wind, carve, thump, music, crash, chime");
        }

        static void ConfigureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        // ── Synthesis ──────────────────────────────────────────────────────────

        // Brown noise (integrated white) through a gentle one-pole low-pass; slow
        // amplitude wobble so the loop doesn't read as a flat hiss. Loop seam is
        // crossfaded over the last 0.25 s.
        static float[] GenerateWind(float seconds)
        {
            int n = (int)(SampleRate * seconds);
            var raw = new float[n];
            var rng = new System.Random(12345);
            float brown = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                brown = Mathf.Clamp(brown + white * 0.02f, -1f, 1f) * 0.999f;
                lp += 0.08f * (brown - lp); // ~600 Hz one-pole low-pass
                float t = (float)i / SampleRate;
                float wobble = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.35f * t)
                                     * Mathf.Sin(2f * Mathf.PI * 0.13f * t + 1.7f);
                raw[i] = lp * wobble;
            }
            Normalize(raw, 0.7f);
            CrossfadeLoop(raw, (int)(SampleRate * 0.25f));
            return raw;
        }

        // High-passed white noise hiss — reads as edge-on-snow scrape when looped.
        static float[] GenerateCarve(float seconds)
        {
            int n = (int)(SampleRate * seconds);
            var raw = new float[n];
            var rng = new System.Random(54321);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.15f * (white - lp);
                float hp = white - lp; // high-pass = white minus low-pass
                float t = (float)i / SampleRate;
                float grain = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 7f * t);
                raw[i] = hp * grain;
            }
            Normalize(raw, 0.55f);
            CrossfadeLoop(raw, (int)(SampleRate * 0.15f));
            return raw;
        }

        // Landing thump: 120→60 Hz sine sweep with exponential decay, plus a short
        // low-passed noise burst on the attack for the "snow compression" texture.
        static float[] GenerateThump(float seconds)
        {
            int n = (int)(SampleRate * seconds);
            var raw = new float[n];
            var rng = new System.Random(99999);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / seconds; // 0..1
                float freq = Mathf.Lerp(120f, 60f, u);
                phase += 2f * Mathf.PI * freq / SampleRate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-9f * u);

                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.12f * (white - lp);
                float attack = lp * Mathf.Exp(-40f * u) * 0.8f;

                raw[i] = body + attack;
            }
            Normalize(raw, 0.9f);
            // Tiny fade-in/out to kill clicks
            Fade(raw, (int)(SampleRate * 0.004f));
            return raw;
        }

        // Ambient music loop: four-chord pad progression (Am - F - C - G), each chord
        // 2.4 s, soft sine partials with per-chord attack/release envelope plus an
        // octave-down root for warmth. 9.6 s total, seam crossfaded for looping.
        static float[] GenerateMusic()
        {
            const float chordSeconds = 2.4f;
            float[][] chords =
            {
                new[] { 220.00f, 261.63f, 329.63f }, // Am
                new[] { 174.61f, 220.00f, 261.63f }, // F
                new[] { 261.63f, 329.63f, 392.00f }, // C
                new[] { 196.00f, 246.94f, 293.66f }, // G
            };
            int perChord = (int)(SampleRate * chordSeconds);
            int n = perChord * chords.Length;
            var raw = new float[n];

            for (int c = 0; c < chords.Length; c++)
            {
                var chord = chords[c];
                float root = chord[0] * 0.5f; // octave-down warmth
                for (int i = 0; i < perChord; i++)
                {
                    float t = (float)i / SampleRate;
                    float u = (float)i / perChord;
                    // Attack 0.4 s, release last 0.5 s — chords swell into each other
                    float env = Mathf.Clamp01(t / 0.4f) * Mathf.Clamp01((chordSeconds - t) / 0.5f);
                    float s = Mathf.Sin(2f * Mathf.PI * root * t) * 0.5f;
                    for (int v = 0; v < chord.Length; v++)
                    {
                        // Slight detune per voice for chorus shimmer
                        float f = chord[v] * (1f + 0.0015f * Mathf.Sin(2f * Mathf.PI * 0.3f * t + v * 2.1f));
                        s += Mathf.Sin(2f * Mathf.PI * f * t) * 0.35f;
                    }
                    // Gentle tremolo so the pad breathes
                    s *= 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.25f * (c * chordSeconds + t));
                    raw[c * perChord + i] = s * env;
                }
            }
            Normalize(raw, 0.45f);
            CrossfadeLoop(raw, (int)(SampleRate * 0.3f));
            return raw;
        }

        // Crash impact: heavy low-passed noise burst with fast decay plus an
        // 80 -> 40 Hz sine drop for body.
        static float[] GenerateCrash(float seconds)
        {
            int n = (int)(SampleRate * seconds);
            var raw = new float[n];
            var rng = new System.Random(24680);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float u = t / seconds;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.10f * (white - lp);
                float noise = lp * Mathf.Exp(-7f * u);

                float freq = Mathf.Lerp(80f, 40f, u);
                phase += 2f * Mathf.PI * freq / SampleRate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-6f * u) * 0.7f;

                raw[i] = noise + body;
            }
            Normalize(raw, 0.9f);
            Fade(raw, (int)(SampleRate * 0.004f));
            return raw;
        }

        // Clean-trick chime: two bell partials (A5 then D6) with exponential decay,
        // second note delayed 0.09 s for a quick ascending "ding-ding".
        static float[] GenerateChime(float seconds)
        {
            int n = (int)(SampleRate * seconds);
            var raw = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float s = Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-8f * t);
                if (t > 0.09f)
                {
                    float t2 = t - 0.09f;
                    s += Mathf.Sin(2f * Mathf.PI * 1174.66f * t2) * Mathf.Exp(-8f * t2) * 0.9f;
                }
                raw[i] = s;
            }
            Normalize(raw, 0.6f);
            Fade(raw, (int)(SampleRate * 0.003f));
            return raw;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        static void Normalize(float[] samples, float peak)
        {
            float max = 0f;
            for (int i = 0; i < samples.Length; i++) max = Mathf.Max(max, Mathf.Abs(samples[i]));
            if (max < 1e-6f) return;
            float k = peak / max;
            for (int i = 0; i < samples.Length; i++) samples[i] *= k;
        }

        // Equal-power crossfade of the tail into the head so the loop seam is silent.
        static void CrossfadeLoop(float[] samples, int fadeLen)
        {
            int n = samples.Length;
            fadeLen = Mathf.Min(fadeLen, n / 4);
            for (int i = 0; i < fadeLen; i++)
            {
                float u = (float)i / fadeLen;
                float a = Mathf.Sqrt(1f - u); // tail weight
                float b = Mathf.Sqrt(u);      // head weight
                int tail = n - fadeLen + i;
                samples[tail] = samples[tail] * a + samples[i] * b;
            }
        }

        static void Fade(float[] samples, int fadeLen)
        {
            int n = samples.Length;
            fadeLen = Mathf.Min(fadeLen, n / 2);
            for (int i = 0; i < fadeLen; i++)
            {
                float u = (float)i / fadeLen;
                samples[i] *= u;
                samples[n - 1 - i] *= u;
            }
        }

        static void WriteWav(string path, float[] samples)
        {
            int n = samples.Length;
            int byteRate = SampleRate * 2; // mono, 16-bit
            int dataSize = n * 2;

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var w = new BinaryWriter(fs);

            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataSize);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);              // fmt chunk size
            w.Write((short)1);        // PCM
            w.Write((short)1);        // mono
            w.Write(SampleRate);
            w.Write(byteRate);
            w.Write((short)2);        // block align
            w.Write((short)16);       // bits per sample
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(dataSize);

            for (int i = 0; i < n; i++)
                w.Write((short)Mathf.Clamp(Mathf.RoundToInt(samples[i] * 32767f), short.MinValue, short.MaxValue));
        }
    }
}
