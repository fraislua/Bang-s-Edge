using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BangsEdge.Simulation.Tests
{
    /// <summary>
    /// Golden doubles are IEEE-754 bit patterns in hex. JsonUtility parses decimal doubles
    /// inexactly (one ulp off was observed), so decimals cannot be compared exactly.
    /// </summary>
    internal static class Bits
    {
        public static double D(string hex) => BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64(hex, 16)));

        public static double[] D(string[] hex) => hex.Select(D).ToArray();

        public static string Hex(double v) => unchecked((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("x16");

        /// <summary>Mirror of stateChecksum in tools/sim-harness/golden.js.</summary>
        public static string Checksum(BangSimulation sim)
        {
            ulong acc = 0;
            for (int i = 0; i < sim.X.Length; i++)
            {
                Mix(ref acc, sim.X[i]);
                Mix(ref acc, sim.Y[i]);
                Mix(ref acc, sim.VX[i]);
                Mix(ref acc, sim.VY[i]);
            }
            return acc.ToString("x16");
        }

        private static void Mix(ref ulong acc, double v)
        {
            acc = (acc << 1) | (acc >> 63);
            acc ^= unchecked((ulong)BitConverter.DoubleToInt64Bits(v));
        }
    }

    [Serializable]
    internal class ConstantsGolden
    {
        public string measureArea;
        public string densityMax;
        public string bangThreshold;
        public int requiredParticles;
        public string effectiveRMax;
    }

    [Serializable]
    internal class FrameGolden
    {
        public int step;
        public string cursorX;
        public string cursorY;
        public string gameState;
        public string dangerStage;
        public string currentDensity;
        public string currentDangerRatio;
        public int currentScore;
        public int finalScore;
        public int graceCounter;
        public int reachableCount;
        public bool bangPossible;
        public int unreachableFrames;
        public string shakeMagnitude;
        public string flashOpacity;
        public string[] x;
        public string[] y;
        public string[] vx;
        public string[] vy;
        public bool[] inMeasure;
        public bool[] outOfReach;
    }

    [Serializable]
    internal class RunGolden
    {
        public int seed;
        public string policy;
        public int bangStep;
        public FrameGolden[] frames;
        public string[] checksums;
    }

    [Serializable]
    internal class TrajectoryGolden
    {
        public RunGolden[] runs;
    }

    [Serializable]
    internal class BangRunGolden
    {
        public int seed;
        public int bangStep;
    }

    [Serializable]
    internal class BangTimesGolden
    {
        public string policy;
        public int maxSteps;
        public BangRunGolden[] runs;
    }

    /// <summary>
    /// Compares BangSimulation with the real script.js run under Node from the same seed
    /// (tools/sim-harness/golden.js). The press, the cursor policies and the stepping below
    /// must mirror that harness exactly.
    /// </summary>
    public class SimulationEquivalenceTests
    {
        // Math.exp/cos may differ in the final bit between JavaScript engines and .NET, and this
        // system amplifies differences over time, so close agreement is only required up to here.
        private const int ExactHorizonStep = 60;
        private const double ExactTolerance = 1e-9;

        private static Func<int, (double x, double y)> Policy(string name, int seed)
        {
            switch (name)
            {
                case "center":
                    return _ => (960, 540);
                case "sweep":
                    return step =>
                    {
                        double s = Math.Min(step / 120.0, 1);
                        return (300 + (960 - 300) * s, 300 + (540 - 300) * s);
                    };
                case "microJitter":
                    var p = new Mulberry32((uint)(seed + 1000000));
                    return _ =>
                    {
                        double x = 960 + (p.NextDouble() * 2 - 1) * 3;
                        double y = 540 + (p.NextDouble() * 2 - 1) * 3;
                        return (x, y);
                    };
                default:
                    throw new ArgumentException($"unknown policy {name}");
            }
        }

        /// <summary>Press at step 0, then advance in fixed steps, setting the cursor before each step.</summary>
        private static void RunRound(int seed, string policyName, int maxSteps, Func<int, BangSimulation, bool> onStep)
        {
            var sim = new BangSimulation(new Mulberry32((uint)seed), null);
            var cursorAt = Policy(policyName, seed);

            var (x0, y0) = cursorAt(0);
            sim.CursorX = x0;
            sim.CursorY = y0;
            sim.StartRound(0);
            if (!onStep(0, sim)) return;

            double simNow = 0;
            for (int step = 1; step <= maxSteps; step++)
            {
                var (x, y) = cursorAt(step);
                simNow += GameConfig.FIXED_DT * 1000;
                sim.CursorX = x;
                sim.CursorY = y;
                sim.Step(GameConfig.FIXED_DT, simNow);
                if (!onStep(step, sim)) return;
            }
        }

        [Test]
        public void ConstantsMatchJavaScript()
        {
            var g = Golden.Load<ConstantsGolden>("constants.json");
            Assert.AreEqual(g.measureArea, Bits.Hex(GameConfig.MEASURE_AREA), "MEASURE_AREA bits");
            Assert.AreEqual(g.densityMax, Bits.Hex(GameConfig.DENSITY_MAX), "DENSITY_MAX bits");
            Assert.AreEqual(g.bangThreshold, Bits.Hex(GameConfig.BANG_THRESHOLD), "BANG_THRESHOLD bits");
            Assert.AreEqual(g.requiredParticles, GameConfig.REQUIRED_PARTICLES, "REQUIRED_PARTICLES");

            // JS uses Math.hypot, C# uses Math.Sqrt(x*x + y*y); the last bit may legitimately differ.
            var sim = new BangSimulation(new Mulberry32(1), null);
            TestContext.WriteLine($"EffectiveRMax bits JS={g.effectiveRMax} C#={Bits.Hex(sim.EffectiveRMax)}");
            Assert.AreEqual(Bits.D(g.effectiveRMax), sim.EffectiveRMax, ExactTolerance, "EffectiveRMax");
        }

        [TestCase("trajectory-center.json")]
        [TestCase("trajectory-sweep.json")]
        public void ShortHorizonMatchesJavaScript(string file)
        {
            var golden = Golden.Load<TrajectoryGolden>(file);
            foreach (var run in golden.runs)
            {
                var expected = run.frames.Where(f => f.step <= ExactHorizonStep).ToDictionary(f => f.step);
                int last = expected.Keys.Max();
                RunRound(run.seed, run.policy, last, (step, sim) =>
                {
                    if (expected.TryGetValue(step, out var frame))
                        AssertFrame(frame, sim, $"{file} seed={run.seed} step={step}");
                    return true;
                });
            }
        }

        [TestCase("trajectory-center.json")]
        [TestCase("trajectory-sweep.json")]
        public void BitIdenticalStepsReport(string file)
        {
            // Report-only: the first step whose particle state differs from JS in any bit.
            var golden = Golden.Load<TrajectoryGolden>(file);
            foreach (var run in golden.runs)
            {
                int firstDiff = -1;
                RunRound(run.seed, run.policy, run.checksums.Length - 1, (step, sim) =>
                {
                    if (Bits.Checksum(sim) == run.checksums[step]) return true;
                    firstDiff = step;
                    return false;
                });
                TestContext.WriteLine(firstDiff < 0
                    ? $"{file} seed={run.seed}: bit-identical for all {run.checksums.Length} steps"
                    : $"{file} seed={run.seed}: first step with any differing bit = {firstDiff}");
            }
        }

        [TestCase("trajectory-center.json")]
        [TestCase("trajectory-sweep.json")]
        public void LongHorizonReport(string file)
        {
            // Report-only: how far the runs drift at later checkpoints.
            var golden = Golden.Load<TrajectoryGolden>(file);
            foreach (var run in golden.runs)
            {
                var expected = run.frames.ToDictionary(f => f.step);
                int last = expected.Keys.Max();
                int csBangStep = -1;
                RunRound(run.seed, run.policy, last, (step, sim) =>
                {
                    if (csBangStep < 0 && sim.State == GameState.Bang) csBangStep = step;
                    if (expected.TryGetValue(step, out var f))
                    {
                        double[] fx = Bits.D(f.x), fy = Bits.D(f.y);
                        double maxPos = 0;
                        for (int i = 0; i < fx.Length; i++)
                            maxPos = Math.Max(maxPos, Math.Max(Math.Abs(fx[i] - sim.X[i]), Math.Abs(fy[i] - sim.Y[i])));
                        TestContext.WriteLine($"{file} seed={run.seed} step={step,4}: max |dpos| = {maxPos:E2}, " +
                                              $"state JS={f.gameState} C#={sim.State.ToString().ToUpperInvariant()}");
                    }
                    return true;
                });
                TestContext.WriteLine($"{file} seed={run.seed}: bangStep JS={run.bangStep} C#={csBangStep}");
            }
        }

        [Test]
        public void BangTimesMatchJavaScript()
        {
            var golden = Golden.Load<BangTimesGolden>("bang-times.json");
            var js = new List<int>();
            var cs = new List<int>();
            int same = 0;
            foreach (var run in golden.runs)
            {
                int csBangStep = -1;
                RunRound(run.seed, golden.policy, golden.maxSteps, (step, sim) =>
                {
                    if (sim.State != GameState.Bang) return true;
                    csBangStep = step;
                    return false;
                });
                if (csBangStep == run.bangStep) same++;
                js.Add(run.bangStep);
                cs.Add(csBangStep);
            }

            TestContext.WriteLine($"identical bang step: {same}/{golden.runs.Length}");
            TestContext.WriteLine($"JS never fired: {js.Count(s => s < 0)}, C# never fired: {cs.Count(s => s < 0)}");
            TestContext.WriteLine("histogram (nearest 10 steps)  JS | C#");
            foreach (var bucket in js.Concat(cs).Where(s => s >= 0).Select(Bucket).Distinct().OrderBy(b => b))
                TestContext.WriteLine($"  {bucket,4}: {js.Count(s => s >= 0 && Bucket(s) == bucket),3} | {cs.Count(s => s >= 0 && Bucket(s) == bucket),3}");

            Assert.That(cs.All(s => s >= 0), $"C# never reached BANG in {golden.maxSteps} steps for some seeds");
            double jsMean = js.Where(s => s >= 0).Average() * GameConfig.FIXED_DT;
            double csMean = cs.Average() * GameConfig.FIXED_DT;
            TestContext.WriteLine($"mean JS={jsMean:F3} s C#={csMean:F3} s");
            Assert.AreEqual(jsMean, csMean, 0.3, "mean time to BANG differs by more than 0.3 s");
        }

        private static int Bucket(int step) => (int)Math.Round(step / 10.0, MidpointRounding.AwayFromZero) * 10;

        private static void AssertFrame(FrameGolden f, BangSimulation sim, string where)
        {
            Assert.AreEqual(f.gameState, sim.State.ToString().ToUpperInvariant(), $"{where} gameState");
            Assert.AreEqual(f.dangerStage, sim.Stage.ToString().ToUpperInvariant(), $"{where} dangerStage");
            Assert.AreEqual(f.currentScore, sim.CurrentScore, $"{where} currentScore");
            Assert.AreEqual(f.finalScore, sim.FinalScore, $"{where} finalScore");
            Assert.AreEqual(f.graceCounter, sim.GraceCounter, $"{where} graceCounter");
            Assert.AreEqual(f.reachableCount, sim.ReachableCount, $"{where} reachableCount");
            Assert.AreEqual(f.bangPossible, sim.BangPossible, $"{where} bangPossible");
            Assert.AreEqual(f.unreachableFrames, sim.UnreachableFrames, $"{where} unreachableFrames");
            Assert.AreEqual(Bits.D(f.currentDensity), sim.CurrentDensity, ExactTolerance, $"{where} currentDensity");
            Assert.AreEqual(Bits.D(f.currentDangerRatio), sim.CurrentDangerRatio, ExactTolerance, $"{where} currentDangerRatio");
            Assert.AreEqual(Bits.D(f.shakeMagnitude), sim.ShakeMagnitude, ExactTolerance, $"{where} shakeMagnitude");
            Assert.AreEqual(Bits.D(f.flashOpacity), sim.FlashOpacity, ExactTolerance, $"{where} flashOpacity");

            Assert.AreEqual(f.x.Length, sim.X.Length, $"{where} particle count");
            double[] x = Bits.D(f.x), y = Bits.D(f.y), vx = Bits.D(f.vx), vy = Bits.D(f.vy);
            for (int i = 0; i < x.Length; i++)
            {
                Assert.AreEqual(x[i], sim.X[i], ExactTolerance, $"{where} x[{i}]");
                Assert.AreEqual(y[i], sim.Y[i], ExactTolerance, $"{where} y[{i}]");
                Assert.AreEqual(vx[i], sim.VX[i], ExactTolerance, $"{where} vx[{i}]");
                Assert.AreEqual(vy[i], sim.VY[i], ExactTolerance, $"{where} vy[{i}]");
                Assert.AreEqual(f.inMeasure[i], sim.InMeasure[i], $"{where} inMeasure[{i}]");
                Assert.AreEqual(f.outOfReach[i], sim.OutOfReach[i], $"{where} outOfReach[{i}]");
            }
        }
    }
}
