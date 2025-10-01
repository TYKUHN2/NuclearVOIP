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


            // Implement jitter buffer by delayig OnDry and OnReady
            clip.OnReady += () => { source.Play(); };
            clip.OnDry += () => {
                Plugin.Logger.LogDebug("Ran Dry");
                source.Pause(); 
            };

            decoder.OnData += args =>
            {
                args.Handle();

                float[] samples = args.data;
                curModifier?.Invoke(ref samples);

                clip.Write(samples);
            };
        }
    }
}
