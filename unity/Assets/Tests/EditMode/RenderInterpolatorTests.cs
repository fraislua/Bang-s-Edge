using NUnit.Framework;

namespace BangsEdge.Simulation.Tests
{
    public class RenderInterpolatorTests
    {
        [Test]
        public void Sample_WithoutCapture_ReturnsCurrentUnblended()
        {
            var interp = new RenderInterpolator(2);
            double[] cx = { 10.0, 20.0 };
            double[] cy = { 30.0, 40.0 };
            float[] ox = new float[2];
            float[] oy = new float[2];

            interp.Sample(0.5, cx, cy, ox, oy);

            Assert.AreEqual(10f, ox[0]);
            Assert.AreEqual(20f, ox[1]);
            Assert.AreEqual(30f, oy[0]);
            Assert.AreEqual(40f, oy[1]);
        }

        [Test]
        public void Sample_AfterCapture_AlphaZero_ReturnsPrevious()
        {
            var interp = new RenderInterpolator(1);
            interp.CapturePrevious(new double[] { 0.0 }, new double[] { 0.0 });
            float[] ox = new float[1];
            float[] oy = new float[1];

            interp.Sample(0.0, new double[] { 100.0 }, new double[] { 200.0 }, ox, oy);

            Assert.AreEqual(0f, ox[0]);
            Assert.AreEqual(0f, oy[0]);
        }

        [Test]
        public void Sample_AfterCapture_AlphaOne_ReturnsCurrent()
        {
            var interp = new RenderInterpolator(1);
            interp.CapturePrevious(new double[] { 0.0 }, new double[] { 0.0 });
            float[] ox = new float[1];
            float[] oy = new float[1];

            interp.Sample(1.0, new double[] { 100.0 }, new double[] { 200.0 }, ox, oy);

            Assert.AreEqual(100f, ox[0]);
            Assert.AreEqual(200f, oy[0]);
        }

        [Test]
        public void Sample_AfterCapture_AlphaHalf_ReturnsMidpoint()
        {
            var interp = new RenderInterpolator(1);
            interp.CapturePrevious(new double[] { 0.0 }, new double[] { 10.0 });
            float[] ox = new float[1];
            float[] oy = new float[1];

            interp.Sample(0.5, new double[] { 100.0 }, new double[] { 30.0 }, ox, oy);

            Assert.AreEqual(50f, ox[0]);
            Assert.AreEqual(20f, oy[0]);
        }

        [TestCase(-0.5, 0f)]
        [TestCase(1.5, 100f)]
        public void Sample_ClampsAlphaOutsideZeroOne(double alpha, float expectedX)
        {
            var interp = new RenderInterpolator(1);
            interp.CapturePrevious(new double[] { 0.0 }, new double[] { 0.0 });
            float[] ox = new float[1];
            float[] oy = new float[1];

            interp.Sample(alpha, new double[] { 100.0 }, new double[] { 100.0 }, ox, oy);

            Assert.AreEqual(expectedX, ox[0]);
        }

        [Test]
        public void Sample_AfterReset_ReturnsCurrentUnblended()
        {
            var interp = new RenderInterpolator(1);
            interp.CapturePrevious(new double[] { 0.0 }, new double[] { 0.0 });
            interp.Reset();
            float[] ox = new float[1];
            float[] oy = new float[1];

            interp.Sample(0.5, new double[] { 42.0 }, new double[] { 42.0 }, ox, oy);

            Assert.AreEqual(42f, ox[0]);
            Assert.AreEqual(42f, oy[0]);
        }
    }
}
