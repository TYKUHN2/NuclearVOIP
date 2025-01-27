using System;
using UnityEngine;

namespace NuclearVOIP
{
    [RequireComponent(typeof(AudioSource))]
    internal class StreamClip: MonoBehaviour, InStream<float>
    {
        private readonly SampleStream buffer = new(48000);
        private const int bufferSamples = (int)((48000 * 0.02) * 3); // 3, 20ms packets at 48khz

        public event Action? OnReady;
        public event Action? OnDry;

        private void Awake()
        {
            enabled = false;
        }

        public void Write(float sample) // Very inefficient, don't use this
        {
            Write([sample]);
        }

        public void Write(float[] samples)
        {
            buffer.Write(samples);

            if (buffer.Count() > bufferSamples && !enabled)
            {
                enabled = true;
                OnReady?.Invoke();
            }
        }

        // Runs on a different thread, suddenly really happy I made the Streams threadsafe.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            int perChannel = data.Length / channels;
            int count = buffer.Count();

            if (count < perChannel)
            {
                Plugin.Logger.LogDebug("StreamClip Dry");

                enabled = false;
                OnDry?.Invoke();

                if (count == 0)
                    return;
            }

            float[]? samples = buffer.Read(Math.Min(perChannel, count));

            if (samples == null)
            {
                Plugin.Logger.LogDebug("StreamClip Dry");

                enabled = false;
                OnDry?.Invoke();
                return;
            }

            for (int i = 0; i < samples.Length; i++)
            {
                int offset = i * channels;

                for (int j = 0; j < channels; j++)
                    data[offset + j] += samples[i];
            }
        }
    }
}
