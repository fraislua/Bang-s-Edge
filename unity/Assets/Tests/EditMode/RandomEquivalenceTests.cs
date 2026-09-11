using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace BangsEdge.Simulation.Tests
{
    /// <summary>Loads golden data written by tools/sim-harness/golden.js from the real JS version.</summary>
    internal static class Golden
    {
        public static T Load<T>(string fileName)
        {
            var path = Path.Combine(Application.dataPath, "Tests", "EditMode", "Golden", fileName);
            Assert.That(File.Exists(path), $"Golden file missing: {path}. Run: node tools/sim-harness/golden.js");
            return JsonUtility.FromJson<T>(File.ReadAllText(path));
        }
    }

    [Serializable]
    internal class RngGolden
    {
        public long seed;
        public long[] raw;
    }

    public class RandomEquivalenceTests
    {
        [Test]
        public void Mulberry32MatchesJavaScriptBitForBit()
        {
            var golden = Golden.Load<RngGolden>("rng.json");
            Assert.That(golden.raw.Length, Is.GreaterThan(0));

            var rng = new Mulberry32((uint)golden.seed);
            for (int i = 0; i < golden.raw.Length; i++)
                Assert.AreEqual((uint)golden.raw[i], rng.NextUInt32(), $"value #{i}");
        }

        [Test]
        public void NextDoubleIsRawDividedByTwoToThe32()
        {
            var a = new Mulberry32(7);
            var b = new Mulberry32(7);
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(a.NextUInt32() / 4294967296.0, b.NextDouble(), 0.0);
        }
    }
}
