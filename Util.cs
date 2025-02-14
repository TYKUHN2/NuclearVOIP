using System;
using System.Threading;
using Unity.Mathematics;

namespace NuclearVOIP
{
    internal class Util
    {
        public const double TAU = Math.PI * 2;

        private static readonly System.Random generator = new();
        private static float next = float.NaN;


        /// <summary>
        /// Generates a normal distribution random value. <see href="https://stackoverflow.com/a/218600"/>
        /// </summary>
        /// <returns>Randomized float</returns>
        internal static float Random()
        {
            float was;
            if (!float.IsNaN(was = Interlocked.Exchange(ref next, float.NaN)))
                return was;

            double u1 = 1.0 - generator.NextDouble();
            double u2 = 1.0 - generator.NextDouble();

            double prefix = Math.Sqrt(-2.0 * Math.Log(u1));
            double input = TAU * u2;

            math.sincos(input, out double sin, out double cos);

            next = (float)(prefix * cos);
            Interlocked.MemoryBarrier();

            return (float)(prefix * sin);
        }
    }
}
