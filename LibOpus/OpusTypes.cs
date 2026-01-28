using System;
using System.Runtime.InteropServices;

namespace LibOpus
{
    public static class OpusTypes
    {
        internal static readonly int[] GOOD_FREQS = [8000, 12000, 16000, 24000, 48000];

        public enum Modes
        {
            VOIP = 2048,
            AUDIO = 2049,
            LOWDELAY = 2051
        }

        public enum Signal
        {
            AUTO = -1000,
            VOICE = 3001,
            MUSIC = 3002
        }

        public enum FEC
        {
            DISABLED,
            AGGRESSIVE,
            RELAXED
        }

        public enum Bandwidth
        { 
            NARROW = 1101,
            MEDIUM = 1102,
            WIDE = 1103,
            SUPERWIDE = 1104,
            FULL = 1105
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
        public static extern int opus_encoder_ctl(IntPtr encoder, int req, out int val); // get variant



        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_decoder_get_size(int channels);

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_decoder_init(IntPtr decoder, int freq, int channels);

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_decode_float(IntPtr decoder, byte[]? data, int length, float[] pcm, int frame_size, int fec);

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_decoder_ctl(IntPtr decoder, int req, int val); // vararg not fun

        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_decoder_ctl(IntPtr decoder, int req, out int val); // get variant


        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int opus_packet_has_lbrr(byte[] packet, int len);


        [DllImport("opus.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr opus_strerror(int err);



        public class OpusException(int code): Exception("libopus: " + Marshal.PtrToStringAnsi(opus_strerror(code)))
        {
        }
    }
}
