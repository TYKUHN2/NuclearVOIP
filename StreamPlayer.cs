using UnityEngine;

namespace NuclearVOIP
{
    internal class StreamPlayer: MonoBehaviour
    {
        private readonly StreamClip<byte[]> clip;
        private AudioSource? source;

        public readonly OpusDecoder decoder = new();

        public StreamPlayer()
        {
            clip = new StreamClip<byte[]>(decoder, "NuclearVOIP.StreamPlayer");
        }

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.clip = clip.clip;

            clip.OnReady += () => { source.Play(); };
            clip.OnDry += () => { source.Pause(); };
        }
    }
}
