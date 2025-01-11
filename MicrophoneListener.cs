using System;
using UnityEngine;

namespace NuclearVOIP
{
    internal class MicrophoneListener: MonoBehaviour, OutStream<float>
    {
        class MicrophoneError(): Exception("microphone initialization failure");

        private AudioClip? audioClip;
        private int pos = 0;
        private readonly SampleStream stream = new(48000);

        public event Action<StreamArgs<float>>? OnData;
        public int frequency = 48000;
        public MicrophoneListener()
        {
            stream.OnData += (args) => { OnData?.Invoke(args); };
        }

        public float Read()
        {
            return stream.Read();
        }

        public float[]? Read(int num)
        {
            return stream.Read(num);
        }

        public bool Empty()
        {
            return stream.Empty();
        }

        public int Count()
        {
            return stream.Count();
        }

        public void Pipe(InStream<float>? other)
        {
            stream.Pipe(other);
        }

        private void Awake()
        {
            this.enabled = false;
        }

        private void OnEnable()
        {
            audioClip = Microphone.Start(null, true, 5, stream.frequency);
            if (audioClip == null)
            {
                throw new MicrophoneError();
            }
            pos = 0;
        }

        private void OnDisable()
        {
            Update(); // Just one more in case we have more data
            Microphone.End(null);
            audioClip = null;
        }

        private void OnDestroy()
        {
            OnDisable();
        }

        private void Update()
        {
            if (audioClip == null)
                return;

            int mPos = Microphone.GetPosition(null);
            if (mPos != pos)
            {
                int toRead = pos > mPos ? audioClip.samples - pos + mPos : mPos - pos;
                float[] buf = new float[toRead];

                audioClip.GetData(buf, pos);
                pos = mPos;

                stream.Write(buf);
            }
        }
    }
}
