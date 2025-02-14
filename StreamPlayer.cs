using UnityEngine;

namespace NuclearVOIP
{
    internal class StreamPlayer: MonoBehaviour
    {
        private StreamClip? clip;
        private AudioSource? source;

        public readonly OpusDecoder decoder = new();

        public delegate void Modifier(ref float[] samples);
        public Modifier? curModifier;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            clip = gameObject.AddComponent<StreamClip>();

            clip.OnReady += () => { source.Play(); };
            clip.OnDry += () => { source.Pause(); };

            decoder.OnData += (StreamArgs<float> args) =>
            {
                args.Handle();

                float[] samples = args.data;
                curModifier?.Invoke(ref samples);

                clip.Write(samples);
            };
        }
    }
}
