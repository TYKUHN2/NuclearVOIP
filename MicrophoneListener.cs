using System;
using UnityEngine;

namespace NuclearVOIP
{
    internal class MicrophoneListener: MonoBehaviour
    {
        class MicrophoneError: Exception
        {}

        private AudioClip? audioClip;
        private int pos = 0;

        public SampleStream stream = new(48000);

        private void Awake()
        {
            this.enabled = false;
        }

        private void OnEnable()
        {
            audioClip = Microphone.Start(null, true, 2, 48000);
            if (audioClip == null)
            {
                throw new MicrophoneError();
            }
            pos = 0;
        }

        private void OnDisable()
        {
            Microphone.End(null);
        }

        private void OnDestroy()
        {
            Microphone.End(null);
        }

        private void FixedUpdate()
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

                Plugin.Instance!.Logger.LogDebug($"Writing microphone frame ({toRead} samples)");
                stream.Write(buf);
            }
        }
    }
}
