using UnityEngine;

namespace NuclearVOIP
{
    internal class StreamPlayer: MonoBehaviour
    {
        private StreamClip? clip;
        private AudioSource? source;

        public readonly OpusDecoder decoder = new();

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            clip = gameObject.AddComponent<StreamClip>();

            clip.OnReady += () => { source.Play(); };
            clip.OnDry += () => { source.Pause(); };

            decoder.Pipe(clip);
        }

        void OnDestroy()
        {
            Destroy(clip);
            Destroy(source);
        }
    }
}
