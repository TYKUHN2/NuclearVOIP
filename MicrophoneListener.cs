using System;
using System.Threading;
using UnityEngine;

namespace NuclearVOIP
{
    internal class MicrophoneListener: MonoBehaviour
    {
        class MicrophoneError(): Exception("microphone initialization failure");

        private AudioClip? audioClip;
        private int pos = 0;

        public readonly SampleStream stream = new(48000);

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
            Update(); // Just one more in case we have more data
            Microphone.End(null);
            audioClip = null;
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
