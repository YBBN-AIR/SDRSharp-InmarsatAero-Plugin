using SDRSharp.Radio;
using System;

namespace SDRSharp.InmarsatAero
{
    // Carrier-to-noise density per channel from one averaged spectrum of the raw IQ (decoder worker).
    // The noise floor is the median bin of the inner band: most of the band is empty, so neighbouring carriers do not
    // move it (a channel's own decimated band is too narrow for that at 10500, where carriers sit 13 kHz apart).
    // C = sum over the carrier's bandwidth of (bin - floor); C/N0 = C / floor x bin width (summed power: no window ENBW).
    // Prototype, synthetic band with neighbours: within 0.1 dB of the true C/N0 for 600, 1200 and 10500.
    // Unlike a decision-domain SNR it does not need the demodulator to lock, and reads nothing on an empty channel.
    public unsafe sealed class WidebandPower : IDisposable
    {
        private const double TargetBinWidth = 80.0; // Hz
        private const int MaxFftSize = 65536;
        private const int MaxFftsPerBlock = 4;
        private const double AverageSeconds = 3.0;
        private const double InnerBand = 0.40; // x sample rate either side for the floor
        private const int FloorEvery = 8; // blocks between floor updates
        private const double CarrierWidth = 1.15; // x the rate's half bandwidth, each side
        private const double Significance = 5.0; // a carrier must stand this many noise standard deviations above the floor
        private const double HannCorrelation = 1.5; // neighbouring Hann bins are correlated: noise of a bin sum grows this much faster

        private readonly double _sampleRate;
        private readonly int _size;
        private readonly double _binWidth;
        private readonly float _alpha;
        private readonly double[] _average;
        private readonly double[] _sorted;
        private readonly int _innerFrom;
        private readonly int _innerTo;
        private UnsafeBuffer _window;
        private UnsafeBuffer _frame;
        private int _ffts;
        private int _blocks;
        private double _floor;

        public WidebandPower(double sampleRate)
        {
            _sampleRate = sampleRate;
            var size = 1024;
            while (size < MaxFftSize && sampleRate / size > TargetBinWidth)
            {
                size *= 2;
            }
            _size = size;
            _binWidth = sampleRate / size;
            var fftsPerSecond = Math.Min(sampleRate / size, MaxFftsPerBlock * sampleRate / 65536.0);
            _alpha = (float)Math.Min(1.0, 1.0 / (AverageSeconds * Math.Max(1.0, fftsPerSecond)));
            _average = new double[size];
            var half = (int)(InnerBand * size);
            _innerFrom = size / 2 - half;
            _innerTo = size / 2 + half;
            _sorted = new double[_innerTo - _innerFrom];
            var w = new float[size];
            for (var i = 0; i < size; i++)
            {
                w[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / size));
            }
            _window = UnsafeBuffer.Create(w);
            _frame = UnsafeBuffer.Create(size, sizeof(Complex));
        }

        // true once the average and the floor are usable
        public bool Ready => _floor > 0;

        public void Reset()
        {
            Array.Clear(_average, 0, _size);
            _ffts = 0;
            _blocks = 0;
            _floor = 0;
        }

        // Raw IQ block: up to MaxFftsPerBlock windows of it go into the average
        public void Process(UnsafeBuffer block, int length)
        {
            var frame = (Complex*)_frame;
            var window = (float*)_window;
            var windows = Math.Min(MaxFftsPerBlock, length / _size);
            var stride = windows > 0 ? length / windows : 0;
            for (var k = 0; k < windows; k++)
            {
                Utils.Memcpy(frame, (Complex*)block + k * stride, _size * sizeof(Complex));
                Fourier.ApplyFFTWindow(frame, window, _size);
                Fourier.ForwardTransform(frame, _size, true);
                var alpha = _ffts < 16 ? 1.0f / (_ffts + 1) : _alpha; // quick start, then the slow average
                for (var i = 0; i < _size; i++)
                {
                    _average[i] += alpha * (frame[i].ModulusSquared() - _average[i]);
                }
                _ffts++;
            }
            if (++_blocks >= FloorEvery && _ffts >= 16)
            {
                _blocks = 0;
                Array.Copy(_average, _innerFrom, _sorted, 0, _sorted.Length);
                Array.Sort(_sorted);
                _floor = _sorted[_sorted.Length / 2];
            }
        }

        // dB-Hz for a carrier of this rate near offset Hz from the raw IQ centre: the strongest carrier-width window
        // within the rate's search range. NaN until Ready, or when nothing stands above the floor.
        public float CarrierToNoise(double offset, AeroRate rate)
        {
            if (_floor <= 0)
            {
                return float.NaN;
            }
            var half = (int)Math.Ceiling(CarrierWidth * rate.HalfBandwidth / _binWidth);
            var search = (int)Math.Ceiling(rate.SearchRange / _binWidth);
            var centre = _size / 2 + (int)Math.Round(offset / _binWidth);
            var from = centre - search - half;
            var to = centre + search + half;
            if (from < 1 || to >= _size - 1)
            {
                return float.NaN;
            }
            // sliding sum of (bin - floor) over 2 x half + 1 bins
            var width = 2 * half + 1;
            var sum = 0.0;
            for (var i = from; i < from + width; i++)
            {
                sum += _average[i] - _floor;
            }
            var best = sum;
            for (var i = from + width; i <= to; i++)
            {
                sum += (_average[i] - _floor) - (_average[i - width] - _floor);
                if (sum > best)
                {
                    best = sum;
                }
            }
            // the strongest window over pure noise is still positive: report only what clearly stands above it
            // (std of a width-bin sum of exponentially averaged bins ~ floor x sqrt(1.5 x width x alpha / 2))
            var sigma = _floor * Math.Sqrt(HannCorrelation * width * _alpha / 2.0);
            if (best <= Significance * sigma)
            {
                return float.NaN;
            }
            return (float)(10.0 * Math.Log10(best / _floor * _binWidth));
        }

        public void Dispose()
        {
            if (_window != null)
            {
                _window.Dispose();
                _window = null;
            }
            if (_frame != null)
            {
                _frame.Dispose();
                _frame = null;
            }
        }
    }
}
