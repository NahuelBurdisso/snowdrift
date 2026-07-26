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

            AssetDatabase.Refresh();

            // Loop-friendly import settings
            ConfigureImporter("Assets/Audio/wind.wav");
            ConfigureImporter("Assets/Audio/carve.wav");
            ConfigureImporter("Assets/Audio/thump.wav");

            Debug.Log("[AudioGenerator] Generated wind.wav, carve.wav, thump.wav");
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
