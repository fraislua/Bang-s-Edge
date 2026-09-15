using System;

namespace BangsEdge.Simulation
{
    public sealed class RenderInterpolator
    {
        private readonly int _capacity;
        private double[] _prevX;
        private double[] _prevY;
        private bool _hasPrevious;

        public RenderInterpolator(int capacity)
        {
            _capacity = capacity;
            _prevX = new double[_capacity];
            _prevY = new double[_capacity];
            _hasPrevious = false;
        }

        public void CapturePrevious(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
        {
            int count = Math.Min(_capacity, x.Length);
            if (y.Length < count)
            {
                count = y.Length;
            }

            for (int i = 0; i < count; i++)
            {
                _prevX[i] = x[i];
                _prevY[i] = y[i];
            }

            _hasPrevious = true;
        }

        public void Reset()
        {
            _hasPrevious = false;
        }

        public void Sample(double alpha, ReadOnlySpan<double> currentX, ReadOnlySpan<double> currentY,
            Span<float> outX, Span<float> outY)
        {
            double clampedAlpha = Math.Clamp(alpha, 0.0, 1.0);

            int count = _capacity;
            if (currentX.Length < count)
            {
                count = currentX.Length;
            }
            if (currentY.Length < count)
            {
                count = currentY.Length;
            }
            if (outX.Length < count)
            {
                count = outX.Length;
            }
            if (outY.Length < count)
            {
                count = outY.Length;
            }

            for (int i = 0; i < count; i++)
            {
                double prev = _prevX[i];
                double current = currentX[i];

                if (_hasPrevious)
                {
                    outX[i] = (float)(prev + (current - prev) * clampedAlpha);
                    outY[i] = (float)(_prevY[i] + (currentY[i] - _prevY[i]) * clampedAlpha);
                }
                else
                {
                    outX[i] = (float)current;
                    outY[i] = (float)currentY[i];
                }
            }
        }
    }
}
