using SDRSharp.Radio;

namespace SDRSharp.InmarsatAero
{
    // Automatic narrowband spur excision for one channel, in the FFT domain (SDR# WeightedOverlapAddProcessor).
    // Bin power is averaged over AverageTime; a bin more than ThresholdRatio above the mean of the bins InnerBins..OuterBins
    // away on both sides is a spur (an Aero carrier is smooth at ~5 Hz resolution). A spur seen on two consecutive
    // updates is zeroed with its neighbours; it is released after two clear updates. Measured (prototype, 600 bps,
    // Eb/N0 4 dB): spur at -4 dB of the carrier 150 Hz away 13 -> 222 of 264 SUs; no spur 252 -> 252.
    public unsafe sealed class SpurNotcher : WeightedOverlapAddProcessor
    {
        private const double BinWidth = 5.0; // Hz, target resolution
        private const double AverageTime = 1.0; // s
        private const double UpdateTime = 0.25; // s
        private const double ThresholdRatio = 7.94; // 9 dB
        private const int InnerBins = 3;
        private const int OuterBins = 24;
        private const int MaxNotchedFraction = 16; // at most fftSize / 16 bins notched

        private readonly int _size;
        private readonly float _alpha;
        private readonly int _updateCalls;
        private readonly int _warmupCalls;
        private readonly double[] _average;
        private readonly double[] _prefix;
        private readonly float[] _gain;
        private readonly bool[] _notched;
        private readonly int[] _seen;
        private readonly int[] _clear;
        private int _calls;
        private int _sinceUpdate;
        private volatile int _spurCount;

        public SpurNotcher(double sampleRate) : this(sampleRate, FftSizeFor(sampleRate))
        {
        }

        private SpurNotcher(double sampleRate, int fftSize) : base(fftSize, fftSize / 8)
        {
            _size = fftSize;
            var hopTime = fftSize / 8 / sampleRate;
            _alpha = (float)(hopTime / AverageTime);
            _updateCalls = System.Math.Max(1, (int)(UpdateTime / hopTime));
            _warmupCalls = (int)(AverageTime / hopTime) + 1;
            _average = new double[fftSize];
            _prefix = new double[fftSize + 1];
            _gain = new float[fftSize];
            _notched = new bool[fftSize];
            _seen = new int[fftSize];
            _clear = new int[fftSize];
            for (var i = 0; i < fftSize; i++)
            {
                _gain[i] = 1.0f;
            }
        }

        // Spurs currently notched (display value)
        public int SpurCount => _spurCount;

        protected override void ProcessFft(Complex* buffer, int length)
        {
            var average = _average;
            var gain = _gain;
            var first = _calls == 0;
            for (var i = 0; i < length; i++)
            {
                var power = buffer[i].ModulusSquared();
                if (first)
                {
                    average[i] = power;
                }
                else
                {
                    average[i] += _alpha * (power - average[i]);
                }
            }

            if (_calls < _warmupCalls)
            {
                _calls++;
            }
            else if (_sinceUpdate < _updateCalls - 1)
            {
                _sinceUpdate++;
            }
            else
            {
                _sinceUpdate = 0;
                UpdateMask();
            }

            for (var i = 0; i < length; i++)
            {
                buffer[i] *= gain[i];
            }
        }

        private void UpdateMask()
        {
            var n = _size;
            var average = _average;
            var prefix = _prefix;
            prefix[0] = 0;
            for (var i = 0; i < n; i++)
            {
                prefix[i + 1] = prefix[i] + average[i];
            }

            var count = 0;
            for (var k = 0; k < n; k++)
            {
                var reference = (RangeSum(k + InnerBins, OuterBins - InnerBins + 1) + RangeSum(k - OuterBins, OuterBins - InnerBins + 1)) / (2 * (OuterBins - InnerBins + 1));
                var hit = average[k] > ThresholdRatio * reference;
                if (hit)
                {
                    _seen[k] = _seen[k] < 2 ? _seen[k] + 1 : 2;
                    _clear[k] = 0;
                }
                else
                {
                    _seen[k] = 0;
                    _clear[k] = _clear[k] < 2 ? _clear[k] + 1 : 2;
                }
                var notch = _seen[k] >= 2 || (_notched[k] && _clear[k] < 2);
                _notched[k] = notch;
                if (notch)
                {
                    count++;
                }
            }

            for (var i = 0; i < n; i++)
            {
                _gain[i] = 1.0f;
            }
            var spurs = 0;
            if (count <= n / MaxNotchedFraction)
            {
                for (var k = 0; k < n; k++)
                {
                    if (!_notched[k])
                    {
                        continue;
                    }
                    if (!_notched[(k + n - 1) % n])
                    {
                        spurs++;
                    }
                    _gain[(k + n - 1) % n] = 0.0f;
                    _gain[k] = 0.0f;
                    _gain[(k + 1) % n] = 0.0f;
                }
            }
            _spurCount = spurs;
        }

        // Sum of average[start .. start + count - 1], indexes taken modulo the FFT size (count < size)
        private double RangeSum(int start, int count)
        {
            var n = _size;
            var a = ((start % n) + n) % n;
            var end = a + count;
            if (end <= n)
            {
                return _prefix[end] - _prefix[a];
            }
            return _prefix[n] - _prefix[a] + _prefix[end - n];
        }

        private static int FftSizeFor(double sampleRate)
        {
            var size = 64;
            while (size < sampleRate / BinWidth && size < 65536)
            {
                size *= 2;
            }
            return size;
        }
    }
}
