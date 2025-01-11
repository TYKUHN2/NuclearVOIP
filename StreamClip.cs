using System;
using UnityEngine;

namespace NuclearVOIP
{
    internal class StreamClip: InStream<float>
    {
        private readonly SampleStream buffer = new(48000);
        private const int bufferSamples = (int)((48000 * 0.02) * 3); // 3, 20ms packets at 48khz
        private bool ready = false;

        public readonly AudioClip clip;

        //private int pos = 0;
        //private int avail = 0;

        public event Action? OnReady;
        public event Action? OnDry;

        public StreamClip(string name) {
            clip = AudioClip.Create(name, 24000, 1, 48000, true, OnRead);
        }

        public void Write(float sample) // Very inefficient, don't use this
        {
            Write([sample]);
        }

        public void Write(float[] samples)
        {
            /*float[]? samples = parent.Read(parent.Count());
            if (samples == null)
                return;

            int rPos;
            int nPos;
            do
            {
                rPos = pos;
                nPos = rPos + samples.Length;
                if (nPos > clip.samples)
                    nPos -= clip.samples;
            } while (Interlocked.CompareExchange(ref pos, nPos, rPos) == rPos);

            clip.SetData(samples, rPos);

            Interlocked.Add(ref avail, samples.Length);
            if (avail >= bufferSamples && !ready)
            {
                ready = true;
                OnReady?.Invoke();
            }*/

            buffer.Write(samples);

            if (buffer.Count() > bufferSamples && !ready)
            {
                ready = true;
                OnReady?.Invoke();
            }
        }

        private void OnRead(float[] data)
        {
            Array.Clear(data, 0, data.Length);

            float[]? samples = buffer.Read(Math.Min(data.Length, buffer.Count()));
            if (samples == null)
            {
                ready = false;
                OnDry?.Invoke();
                return;
            }

            samples.CopyTo(data, 0);
        }
    }
}
