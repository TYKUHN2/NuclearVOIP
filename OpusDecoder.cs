using System;
using System.Diagnostics;
using System.Linq;
using LibOpus;

namespace NuclearVOIP
{
    internal class OpusDecoder: AbstractTransform<byte[], float>
    {
        private readonly Decoder decoder = new(48000, 1)
        {
            BWE = true,
            Gain = (int)Math.Round(Plugin.Instance.configOutputGain.Value * 256, MidpointRounding.AwayFromZero),
            Complexity = 10
        };

        private bool packetLost = false;

        private byte costi = 0;
        private readonly int[] costs = new int[6];

        protected override float[] Transform(byte[][] data)
        {
            float[][] decoded = new float[data.Length][];
            for (int i = 0; i < data.Length; i++)
                decoded[i] = DoDecode(data[i]);

            return [.. decoded.SelectMany(a => a)];
        }

        private float[] DoDecode(byte[] packet)
        {
            if (packet.Length <= 2) // DTX/Lost packet
            {
                packetLost = true;
                return [];
            } 
            else
            {
                Stopwatch sw = Stopwatch.StartNew();

                float[] prefix;

                if (packetLost && Plugin.Instance.configUseFEC.Value)
                    prefix = decoder.DecodeLoss(packet, 20 /* TODO */);
                else
                    prefix = [];

                float[] decoded = decoder.Decode(packet);

                sw.Stop();

                costs[costi++] = (int)sw.ElapsedMilliseconds;

                if (costi == 6)
                {
                    costi = 0;

                    int avgCost = (int)Math.Ceiling(costs.Average());

                    if (avgCost > 40) // On average we are two or more packets too slow
                        decoder.Complexity -= 2;
                    else if (avgCost > 20) // On average we are one packet too slow
                        decoder.Complexity -= 1;
                }

                return [..prefix, ..decoded];
            }
        }
    }
}
