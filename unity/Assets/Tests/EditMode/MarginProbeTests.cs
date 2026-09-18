using System;
using NUnit.Framework;

namespace BangsEdge.Simulation.Tests
{
    /// <summary>
    /// The release-margin probe clones the live simulation at the moment of release and runs it
    /// ahead with the cursor fixed to find when a BANG would have happened. These tests pin the
    /// contract: the clone is bit-identical and independent, the probe never touches the source,
    /// and its answer equals what the source itself does when stepped the same way.
    /// </summary>
    public class MarginProbeTests
    {
        private static (BangSimulation sim, Mulberry32 rng, double nowMs) RunCenterUntil(int seed, Func<BangSimulation, bool> stop, int maxSteps)
        {
            var rng = new Mulberry32((uint)seed);
            var sim = new BangSimulation(rng, null);
            sim.CursorX = 960;
            sim.CursorY = 540;
            sim.StartRound(0);
            double now = 0;
            for (int step = 1; step <= maxSteps; step++)
            {
                now += GameConfig.FIXED_DT * 1000;
                sim.Step(GameConfig.FIXED_DT, now);
                if (stop(sim)) break;
            }
            return (sim, rng, now);
        }

        private static void AssertSameState(BangSimulation a, BangSimulation b, string where)
        {
            Assert.AreEqual(Bits.Checksum(a), Bits.Checksum(b), $"{where} positions/velocities");
            Assert.AreEqual(a.State, b.State, $"{where} State");
            Assert.AreEqual(a.IsPressing, b.IsPressing, $"{where} IsPressing");
            Assert.AreEqual(a.Stage, b.Stage, $"{where} Stage");
            Assert.AreEqual(a.GraceCounter, b.GraceCounter, $"{where} GraceCounter");
            Assert.AreEqual(a.CurrentScore, b.CurrentScore, $"{where} CurrentScore");
            Assert.AreEqual(a.CurrentDensity, b.CurrentDensity, $"{where} CurrentDensity");
            Assert.AreEqual(a.RoundSteps, b.RoundSteps, $"{where} RoundSteps");
            Assert.AreEqual(a.ReachableCount, b.ReachableCount, $"{where} ReachableCount");
            Assert.AreEqual(a.BangPossible, b.BangPossible, $"{where} BangPossible");
            Assert.AreEqual(a.UnreachableFrames, b.UnreachableFrames, $"{where} UnreachableFrames");
            Assert.AreEqual(a.ShakeMagnitude, b.ShakeMagnitude, $"{where} ShakeMagnitude");
            Assert.AreEqual(a.FlashOpacity, b.FlashOpacity, $"{where} FlashOpacity");
            Assert.AreEqual(a.LastMeasureCount, b.LastMeasureCount, $"{where} LastMeasureCount");
            Assert.AreEqual(a.CursorX, b.CursorX, $"{where} CursorX");
            Assert.AreEqual(a.CursorY, b.CursorY, $"{where} CursorY");
            Assert.AreEqual(a.PressStartTime, b.PressStartTime, $"{where} PressStartTime");
            for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++)
            {
                Assert.AreEqual(a.InMeasure[i], b.InMeasure[i], $"{where} InMeasure[{i}]");
                Assert.AreEqual(a.OutOfReach[i], b.OutOfReach[i], $"{where} OutOfReach[{i}]");
            }
        }

        [Test]
        public void Mulberry32CloneContinuesTheSameSequenceIndependently()
        {
            var a = new Mulberry32(4242);
            for (int i = 0; i < 17; i++) a.NextUInt32();
            var b = a.Clone();
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(a.NextUInt32(), b.NextUInt32(), $"value #{i}");
            // advancing one must not move the other
            uint next = a.NextUInt32();
            Assert.AreEqual(next, b.NextUInt32(), "clone lagged behind after the source advanced");
        }

        [TestCase(11)]
        [TestCase(12345)]
        [TestCase(777)]
        public void CloneContinuesBitIdenticallyAndLeavesSourceUntouched(int seed)
        {
            var (sim, rng, now) = RunCenterUntil(seed, s => s.CurrentDangerRatio >= 0.7, 900);
            Assert.AreEqual(GameState.Attracting, sim.State, "precondition: still attracting at 70% danger");

            string before = Bits.Checksum(sim);
            var clone = sim.CloneForProbe(rng.Clone());
            Assert.AreEqual(before, Bits.Checksum(sim), "cloning must not modify the source");
            Assert.AreNotSame(sim.X, clone.X, "clone must own its arrays");
            AssertSameState(sim, clone, "right after clone");

            double n = now;
            for (int i = 1; i <= 300; i++)
            {
                n += GameConfig.FIXED_DT * 1000;
                sim.Step(GameConfig.FIXED_DT, n);
                clone.Step(GameConfig.FIXED_DT, n);
                AssertSameState(sim, clone, $"step +{i}");
                if (sim.State == GameState.Bang) break;
            }
            Assert.AreEqual(GameState.Bang, sim.State, "the source should have fired within 5 s of 70% danger");
            Assert.AreEqual(sim.LastBangSteps, clone.LastBangSteps, "LastBangSteps");
        }

        [Test]
        public void CloneRequiresARandomSource()
        {
            var sim = new BangSimulation(new Mulberry32(1), null);
            Assert.Throws<ArgumentNullException>(() => sim.CloneForProbe(null));
        }

        [TestCase(11)]
        [TestCase(12345)]
        [TestCase(777)]
        [TestCase(2024)]
        public void ProbePredictsTheStepTheSourceWouldBang(int seed)
        {
            var (sim, rng, now) = RunCenterUntil(seed, s => s.CurrentDangerRatio >= 0.8, 900);
            Assert.AreEqual(GameState.Attracting, sim.State, "precondition: still attracting at 80% danger");
            string before = Bits.Checksum(sim);

            var probe = new MarginProbe();
            probe.Begin(sim, rng.Clone(), now);
            Assert.IsTrue(probe.IsRunning, "running after Begin");
            Assert.IsFalse(probe.IsDone, "not done after Begin");

            int calls = 0;
            while (!probe.IsDone)
            {
                int advanced = probe.Advance(30);
                Assert.That(advanced, Is.GreaterThan(0).And.LessThanOrEqualTo(30), "Advance returns the steps it took");
                calls++;
                Assert.Less(calls, 100, "probe never finished");
            }
            Assert.IsFalse(probe.IsRunning, "not running once done");
            Assert.AreEqual(0, probe.Advance(10), "Advance after done is a no-op");
            Assert.AreEqual(before, Bits.Checksum(sim), "probing must not modify the source");
            Assert.AreEqual(GameState.Attracting, sim.State, "probing must not change the source state");

            int stepsToBang = -1;
            double n = now;
            for (int i = 1; i <= MarginProbe.MAX_STEPS; i++)
            {
                n += GameConfig.FIXED_DT * 1000;
                sim.Step(GameConfig.FIXED_DT, n);
                if (sim.State == GameState.Bang)
                {
                    stepsToBang = i;
                    break;
                }
            }
            Assert.That(stepsToBang, Is.GreaterThan(0), "source should bang within the probe horizon from 80% danger");
            Assert.AreEqual(stepsToBang, probe.StepsToBang, "probe must predict the source's own bang step");
            Assert.AreEqual(stepsToBang, probe.StepsAdvanced, "probe stops on the bang step");
            Assert.AreEqual(stepsToBang * GameConfig.FIXED_DT, probe.SecondsToBang, 1e-12, "SecondsToBang");
        }

        [Test]
        public void ProbeFromAnUnreachablePositionReportsNoBangWithinHorizon()
        {
            // At the far left edge fewer than REQUIRED_PARTICLES are within reach, so a bang is impossible.
            var rng = new Mulberry32(5);
            var sim = new BangSimulation(rng, null);
            sim.CursorX = 30;
            sim.CursorY = 540;
            sim.StartRound(0);
            double now = 0;
            for (int step = 1; step <= 60; step++)
            {
                now += GameConfig.FIXED_DT * 1000;
                sim.Step(GameConfig.FIXED_DT, now);
            }

            var probe = new MarginProbe();
            probe.Begin(sim, rng.Clone(), now);
            while (!probe.IsDone) probe.Advance(100);

            Assert.AreEqual(-1, probe.StepsToBang, "no bang within MAX_STEPS");
            Assert.AreEqual(MarginProbe.MAX_STEPS, probe.StepsAdvanced, "ran the whole horizon");
            Assert.Less(probe.SecondsToBang, 0.0, "SecondsToBang is negative when unknown");
        }

        [Test]
        public void BeginOutsideAttractingIsImmediatelyDoneWithoutBang()
        {
            var rng = new Mulberry32(9);
            var sim = new BangSimulation(rng, null); // Ready, not pressing
            var probe = new MarginProbe();
            probe.Begin(sim, rng.Clone(), 0);
            Assert.IsTrue(probe.IsDone);
            Assert.IsFalse(probe.IsRunning);
            Assert.AreEqual(-1, probe.StepsToBang);
            Assert.AreEqual(0, probe.Advance(10));
        }

        [TestCase(11)]
        [TestCase(12345)]
        public void ConfirmRoundRecordsMeasureCountAndGraceStep(int seed)
        {
            var (sim, rng, now) = RunCenterUntil(seed, s => s.GraceCounter >= 2, 1200);
            Assert.AreEqual(2, sim.GraceCounter, "precondition: stopped on the 2nd grace step");
            Assert.AreEqual(GameState.Attracting, sim.State);

            int count = sim.LastMeasureCount;
            Assert.That(count, Is.GreaterThanOrEqualTo(GameConfig.REQUIRED_PARTICLES), "over the threshold means at least REQUIRED_PARTICLES inside the measure circle");
            Assert.That(count, Is.LessThanOrEqualTo(GameConfig.TOTAL_PARTICLES));
            int inMeasure = 0;
            for (int i = 0; i < GameConfig.TOTAL_PARTICLES; i++) if (sim.InMeasure[i]) inMeasure++;
            Assert.AreEqual(inMeasure, count, "LastMeasureCount equals the number of InMeasure flags");
            Assert.That(Math.Abs(sim.CurrentScore - count * 9999.0 / GameConfig.TOTAL_PARTICLES), Is.LessThan(1.0), "score is the measure count scaled to 9999");

            sim.ConfirmRound();
            Assert.AreEqual(GameState.Resolved, sim.State);
            Assert.AreEqual(count, sim.FinalMeasureCount, "FinalMeasureCount is frozen at release");
            Assert.AreEqual(2, sim.ReleaseGraceCounter, "ReleaseGraceCounter is the grace step at release");
            Assert.AreEqual(2, sim.GraceCounter, "ConfirmRound leaves GraceCounter as it was (JS parity)");

            sim.StartRound(now);
            Assert.AreEqual(0, sim.FinalMeasureCount, "StartRound clears FinalMeasureCount");
            Assert.AreEqual(0, sim.ReleaseGraceCounter, "StartRound clears ReleaseGraceCounter");
            Assert.AreEqual(0, sim.LastMeasureCount, "StartRound clears LastMeasureCount");
        }

        [Test]
        public void ReleaseBelowThresholdRecordsZeroGrace()
        {
            var (sim, rng, now) = RunCenterUntil(11, s => s.CurrentDangerRatio >= 0.5, 900);
            Assert.AreEqual(0, sim.GraceCounter);
            int count = sim.LastMeasureCount;
            Assert.That(count, Is.LessThan(GameConfig.REQUIRED_PARTICLES));
            sim.ConfirmRound();
            Assert.AreEqual(count, sim.FinalMeasureCount);
            Assert.AreEqual(0, sim.ReleaseGraceCounter);
        }
    }
}
