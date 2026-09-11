namespace BangsEdge.Simulation
{
    /// <summary>Source of uniform doubles in [0, 1), standing in for JavaScript's Math.random.</summary>
    public interface IRandomSource
    {
        double NextDouble();
    }

    /// <summary>
    /// Mulberry32, bit-for-bit identical to the JavaScript version in tools/sim-harness/golden.js.
    /// The simulation draws from it in the same order as script.js draws from Math.random, so a C#
    /// run and a Node run of the real script.js can be compared from the same seed.
    /// </summary>
    public sealed class Mulberry32 : IRandomSource
    {
        private uint _state;

        public Mulberry32(uint seed)
        {
            _state = seed;
        }

        public uint NextUInt32()
        {
            unchecked
            {
                _state += 0x6D2B79F5;
                uint t = (_state ^ (_state >> 15)) * (_state | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return t ^ (t >> 14);
            }
        }

        public double NextDouble()
        {
            return NextUInt32() / 4294967296.0;
        }
    }
}
