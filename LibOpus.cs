using AOT;
using System;
using System.Runtime.InteropServices;

namespace NuclearVOIP
{
    internal static class LibOpus
    {
        public enum Modes
        {
            VOIP = 2048,
            AUDIO = 2049,
            LOWDELAY = 2051
        }

        public enum EncoderCtl
        {
            SET_BITRATE = 4002,
            SET_INBAND_FEC = 4012,
            SET_PACKET_LOSS_PERC = 4014,
            SET_DTX = 4016,
            SET_SIGNAL = 4024,
            GET_LOOKAHEAD = 4027
        }

        public enum Signal
        {
            AUTO = -1000,
            VOICE = 3001,
            MUSIC = 3002
        }

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_encoder_get_size(int channels);

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_encoder_init(IntPtr encoder, int freq, int channels, int mode);

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_encode_float(IntPtr encoder, float[] pcm, int frame_size, byte[] data, int max_data_bytes);

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_encoder_ctl(IntPtr encoder, int req, int val); // vararg not fun

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_encoder_ctl(IntPtr encoder, int req, ref int val); // get varient

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr opus_strerror(int err);
    }
}
