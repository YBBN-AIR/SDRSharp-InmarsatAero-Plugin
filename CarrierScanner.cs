using SDRSharp.Radio;
using System;

namespace SDRSharp.InmarsatAero
{
    // Line-pair carrier scan over the raw IQ. A short Hann-windowed FFT stepped a quarter of its length splits the band
    // into overlapping channels ~10 kHz apart (the quarter step makes the per-channel phase correction a quarter turn
    // per step). Each channel's output is squared and its spectrum averaged with a fade; an Aero carrier leaves a line
    // pair at 2 x offset +/- bit rate / 2 there, which names the rate and the carrier to a fraction of a hertz, even
    // below the noise in its own bandwidth. Channels finish their segments at staggered times so the long FFTs spread
    // over the blocks. All buffers are allocated in the constructor.
    // Prototype (synthetic band): 0 false finds in 30 s; 10500 / 8400 / 6 dB carriers on the first look (1.7 s),
    // 1200 at 0 dB within ~3 s, 600 at 1-3 dB intermittently after ~6 s; IQ images and slow-rate ghosts beside strong
    // carriers dropped.
    public unsafe sealed class CarrierScanner : IDisposable
    {
        public const int MaxCandidates = 64;
        public const int VoiceRate = 8400; // C channel line pair: found, not decoded

        private const double ChannelSpacing = 9766.0; // Hz target: a 10500 carrier anywhere between two channels fits one
        private const int MaxChannels = 160; // nearest the centre; ~770 KB each
        private const int MaxSegment = 65536; // channel samples per squared-spectrum FFT (~1.7 s)
        private const double SegmentSeconds = 1.7;
        private const float Fade = 0.9f; // per segment: ~17 s memory
        private const float FirstThreshold = 60.0f; // one segment averaged (or reported at once at any time)
        private const float SlowThreshold = 5.0f; // 600 / 1200, averaged: noise <= ~4.7
        private const float WideThreshold = 25.0f; // 10500 / 8400, averaged: noise <= ~8, a 0 dB 10500 carrier ~280
        private const float RunnerUpRatio = 1.6f; // over the next rate's threshold-normalised score
        private const double RepeatTolerance = 3.0; // Hz: a find must repeat this close on the next look
        private const double WindowMargin = 500.0; // Hz beyond half the spacing each channel searches
        private const double UsableBandwidth = 0.45; // x input rate either side, as AeroChannel
        private const double EdgeMargin = 6000.0; // Hz: channels this far inside the usable band
        private const int ResultBlocks = 8; // compile results every this many blocks
        private const double SlowTolerance = 20.0; // Hz: same carrier / alias, 600 / 1200
        private const double WideTolerance = 100.0; // Hz, 10500 / 8400
        private const float AliasRatio = 2.0f; // an alias is this much weaker than the carrier it copies
        private const float StrongRatio = 20.0f; // finds this much weaker within StrongSpan of a carrier are its ghosts
        private const double StrongSpan = 15000.0;
        private const float ImageRatio = 4.0f; // a find mirrored about the centre by one this much stronger is its image
        private const double ImageTolerance = 100.0;

        private static readonly int[] Rates = { 600, 1200, 10500, VoiceRate };

        private readonly double _sampleRate;
        private readonly int _n; // bank FFT size
        private readonly int _hop;
        private readonly double _spacing;
        private readonly double _outRate;
        private readonly int _segment;
        private readonly double _window;
        private readonly int _count; // scanned channels
        private readonly int[] _index; // FFT index of each scanned channel (rearranged, DC at _n / 2)
        private readonly int[] _quarter; // channel number mod 4
        private readonly double[] _centre; // Hz from the raw centre
        private readonly UnsafeBuffer[] _segments;
        private readonly float[][] _spectra;
        private readonly float[] _weight;
        private readonly int[] _fill;
        private readonly bool[] _primed; // false until the first (partial, staggered) segment is discarded
        private readonly int[] _prevRate;
        private readonly double[] _prevOffset;
        private readonly int[] _findRate; // current confirmed find per channel (0 = none)
        private readonly double[] _findOffset;
        private readonly float[] _findScore;
        private UnsafeBuffer _bankWindow;
        private UnsafeBuffer _frame;
        private UnsafeBuffer _history;
        private UnsafeBuffer _work;
        private int _historyFill;
        private int _phase; // bank steps mod 4
        private int _blocks;

        private readonly double[] _offsets = new double[MaxCandidates];
        private readonly int[] _rates = new int[MaxCandidates];
        private readonly float[] _scores = new float[MaxCandidates];
        private readonly double[] _voiceOffsets = new double[MaxCandidates];
        private readonly float[] _voiceScores = new float[MaxCandidates];
        private int _voiceCount;
        private readonly int[] _order;
        private int _results;
        private volatile int _lastCount = -1;
        private volatile int _images;
        private volatile int _voice;

        public CarrierScanner(double sampleRate, int maxLength)
        {
            _sampleRate = sampleRate;
            var n = 8;
            while (n < 4096 && sampleRate / n > ChannelSpacing * 1.5)
            {
                n *= 2;
            }
            _n = n;
            _hop = n / 4;
            _spacing = sampleRate / n;
            _outRate = sampleRate / _hop;
            var segment = 4096;
            while (segment < MaxSegment && segment / _outRate < SegmentSeconds)
            {
                segment *= 2;
            }
            _segment = segment;
            _window = _spacing / 2 + WindowMargin;

            // channels nearest the centre first, inside the usable band
            var limit = UsableBandwidth * sampleRate - EdgeMargin;
            var count = 0;
            var index = new int[Math.Min(MaxChannels, n)];
            var quarter = new int[index.Length];
            var centre = new double[index.Length];
            for (var d = 0; d <= n / 2 && count < index.Length; d++)
            {
                for (var side = 0; side < 2 && count < index.Length; side++)
                {
                    if (d == 0 && side == 1)
                    {
                        continue;
                    }
                    var k = side == 0 ? d : -d;
                    if (k < -n / 2 || k >= n / 2 || Math.Abs(k * _spacing) > limit)
                    {
                        continue;
                    }
                    index[count] = n / 2 + k;
                    quarter[count] = ((k % 4) + 4) % 4;
                    centre[count] = k * _spacing;
                    count++;
                }
            }
            _count = count;
            _index = index;
            _quarter = quarter;
            _centre = centre;

            _segments = new UnsafeBuffer[count];
            _spectra = new float[count][];
            for (var c = 0; c < count; c++)
            {
                _segments[c] = UnsafeBuffer.Create(_segment, sizeof(Complex));
                _spectra[c] = new float[_segment];
            }
            _weight = new float[count];
            _fill = new int[count];
            _primed = new bool[count];
            _prevRate = new int[count];
            _prevOffset = new double[count];
            _findRate = new int[count];
            _findOffset = new double[count];
            _findScore = new float[count];
            _order = new int[count];

            var w = new float[n];
            for (var i = 0; i < n; i++)
            {
                w[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / n));
            }
            _bankWindow = UnsafeBuffer.Create(w);
            _frame = UnsafeBuffer.Create(n, sizeof(Complex));
            _history = UnsafeBuffer.Create(maxLength + n, sizeof(Complex));
            _work = UnsafeBuffer.Create(_segment, sizeof(Complex));
            Reset();
        }

        // Results of the last compile (worker thread)
        public int Count => _results;

        // Hz from the raw IQ centre
        public double Offset(int index)
        {
            return _offsets[index];
        }

        // 600, 1200 or 10500
        public int Rate(int index)
        {
            return _rates[index];
        }

        public float Score(int index)
        {
            return _scores[index];
        }

        // Display (any thread): carriers reported by the last compile, -1 = none yet
        public int LastCount => _lastCount;

        // C channels (8400 line pair) of the last compile (worker thread): Hz from the raw IQ centre, score
        public int VoiceCount => _voiceCount;

        public double VoiceOffset(int index)
        {
            return _voiceOffsets[index];
        }

        public float VoiceScore(int index)
        {
            return _voiceScores[index];
        }

        // Finds dropped as IQ images in the last compile
        public int LastImages => _images;

        // C channels (voice) seen in the last compile: not decoded
        public int LastVoice => _voice;

        // Hz either side of the centre the scan covers
        public double Coverage => _count > 0 ? Math.Abs(_centre[_count - 1]) + _window : 0;

        // Discards everything averaged (the centre moved); channels restart staggered
        public void Reset()
        {
            for (var c = 0; c < _count; c++)
            {
                Array.Clear(_spectra[c], 0, _segment);
                _weight[c] = 0;
                _fill[c] = (int)((long)c * _segment / Math.Max(1, _count));
                _primed[c] = _fill[c] == 0;
                _prevRate[c] = 0;
                _findRate[c] = 0;
            }
            _historyFill = 0;
            _phase = 0;
            _blocks = 0;
            _results = 0;
            _voiceCount = 0;
            _lastCount = -1;
        }

        // block: raw IQ; returns true when the results were compiled
        public bool Process(UnsafeBuffer block, int length)
        {
            var history = (Complex*)_history;
            Utils.Memcpy(history + _historyFill, (Complex*)block, length * sizeof(Complex));
            var available = _historyFill + length;
            var frame = (Complex*)_frame;
            var window = (float*)_bankWindow;
            var position = 0;
            while (position + _n <= available)
            {
                Utils.Memcpy(frame, history + position, _n * sizeof(Complex));
                Fourier.ApplyFFTWindow(frame, window, _n);
                Fourier.ForwardTransform(frame, _n, true);
                for (var c = 0; c < _count; c++)
                {
                    // channel k turns k quarter circles per step: undo it so each channel sits at baseband
                    var v = frame[_index[c]];
                    Complex turned;
                    switch ((_quarter[c] * _phase) & 3)
                    {
                        case 0:
                            turned = v;
                            break;

                        case 1:
                            turned = new Complex(v.Imag, -v.Real);
                            break;

                        case 2:
                            turned = new Complex(-v.Real, -v.Imag);
                            break;

                        default:
                            turned = new Complex(-v.Imag, v.Real);
                            break;
                    }
                    var segment = (Complex*)_segments[c];
                    segment[_fill[c]] = turned;
                    if (++_fill[c] == _segment)
                    {
                        _fill[c] = 0;
                        FinishSegment(c);
                    }
                }
                _phase = (_phase + 1) & 3;
                position += _hop;
            }
            var left = available - position;
            if (left > 0 && position > 0)
            {
                Utils.Memmove(history, history + position, left * sizeof(Complex));
            }
            _historyFill = left;

            if (++_blocks < ResultBlocks)
            {
                return false;
            }
            _blocks = 0;
            Compile();
            return true;
        }

        public void Dispose()
        {
            if (_segments != null)
            {
                for (var c = 0; c < _segments.Length; c++)
                {
                    if (_segments[c] != null)
                    {
                        _segments[c].Dispose();
                        _segments[c] = null;
                    }
                }
            }
            DisposeBuffer(ref _bankWindow);
            DisposeBuffer(ref _frame);
            DisposeBuffer(ref _history);
            DisposeBuffer(ref _work);
        }

        private static void DisposeBuffer(ref UnsafeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Dispose();
                buffer = null;
            }
        }

        // A channel's segment is full: square, transform, fade it into the average, look for line pairs
        private void FinishSegment(int c)
        {
            if (!_primed[c])
            {
                _primed[c] = true; // the staggered first segment was partial
                return;
            }
            var work = (Complex*)_work;
            var segment = (Complex*)_segments[c];
            for (var i = 0; i < _segment; i++)
            {
                var v = segment[i];
                work[i] = v * v;
            }
            Fourier.ForwardTransform(work, _segment, true);
            var spectrum = _spectra[c];
            for (var i = 0; i < _segment; i++)
            {
                spectrum[i] = spectrum[i] * Fade + work[i].ModulusSquared();
            }
            _weight[c] = _weight[c] * Fade + 1;

            var first = _weight[c] < 1.5f;
            var bestQ = 0.0f;
            var secondQ = 0.0f;
            var bestRate = 0;
            var bestOffset = 0.0;
            var bestScore = 0.0f;
            for (var r = 0; r < Rates.Length; r++)
            {
                float score;
                var offset = LinePair(spectrum, Rates[r], out score);
                var q = score / (first ? FirstThreshold : Rates[r] <= 1200 ? SlowThreshold : WideThreshold);
                if (q > bestQ)
                {
                    secondQ = bestQ;
                    bestQ = q;
                    bestRate = Rates[r];
                    bestOffset = offset;
                    bestScore = score;
                }
                else if (q > secondQ)
                {
                    secondQ = q;
                }
            }

            var passed = bestQ >= 1 && (secondQ < 1 || bestQ >= RunnerUpRatio * secondQ);
            var confirmed = passed
                && (bestScore >= FirstThreshold || (_prevRate[c] == bestRate && Math.Abs(_prevOffset[c] - bestOffset) < RepeatTolerance));
            _prevRate[c] = bestQ >= 1 ? bestRate : 0;
            _prevOffset[c] = bestOffset;
            if (confirmed)
            {
                _findRate[c] = bestRate;
                _findOffset[c] = _centre[c] + bestOffset;
                _findScore[c] = bestScore;
            }
            else
            {
                _findRate[c] = 0;
            }
        }

        // Best carrier offset (Hz from the channel centre) for this bit rate's pair; score = weaker line over the mean
        private double LinePair(float[] spectrum, int bitRate, out float score)
        {
            var size = _segment;
            var binHz = _outRate / size;
            var line = (int)Math.Round(bitRate * 0.5 / binHz);
            var maxBin = (int)(2.0 * _window / binHz);
            var middle = size / 2;
            var best = -1.0f;
            var bestBin = 0;
            var sum = 0.0;
            var n = 0;
            for (var k = -maxBin; k <= maxBin; k++)
            {
                var upper = middle + k + line;
                var lower = middle + k - line;
                if (upper >= size || lower < 0)
                {
                    continue;
                }
                var s = Math.Min(spectrum[upper], spectrum[lower]);
                sum += s;
                n++;
                if (s > best)
                {
                    best = s;
                    bestBin = k;
                }
            }
            score = n > 0 && sum > 0 ? (float)(best * n / sum) : 0.0f;
            return bestBin * binHz * 0.5;
        }

        // Current finds of all channels, strongest first; duplicates, aliases, ghosts beside strong carriers and IQ
        // images dropped; C channels counted, not reported
        private void Compile()
        {
            var m = 0;
            for (var c = 0; c < _count; c++)
            {
                if (_findRate[c] != 0)
                {
                    _order[m++] = c;
                }
            }
            // insertion sort by score, descending (few finds)
            for (var i = 1; i < m; i++)
            {
                var v = _order[i];
                var j = i;
                while (j > 0 && _findScore[_order[j - 1]] < _findScore[v])
                {
                    _order[j] = _order[j - 1];
                    j--;
                }
                _order[j] = v;
            }

            var step = _outRate / 2;
            var accepted = 0; // _order[0 .. accepted) are kept
            var images = 0;
            var voice = 0;
            for (var i = 0; i < m; i++)
            {
                var c = _order[i];
                var rate = _findRate[c];
                var f = _findOffset[c];
                var a = _findScore[c];
                var drop = false;
                for (var j = 0; j < accepted && !drop; j++)
                {
                    var c0 = _order[j];
                    var f0 = _findOffset[c0];
                    var a0 = _findScore[c0];
                    if (rate == _findRate[c0])
                    {
                        var tolerance = rate <= 1200 ? SlowTolerance : WideTolerance;
                        var steps = Math.Round((f - f0) / step);
                        if (Math.Abs(f - f0 - steps * step) < tolerance && (steps == 0 || a0 >= AliasRatio * a))
                        {
                            drop = true; // the same carrier from a neighbouring channel, or an alias of it
                        }
                    }
                    if (!drop && a0 >= StrongRatio * a && Math.Abs(f - f0) < StrongSpan)
                    {
                        drop = true;
                    }
                    if (!drop && a0 >= ImageRatio * a && Math.Abs(f + f0) < ImageTolerance)
                    {
                        drop = true;
                        images++;
                    }
                }
                if (!drop)
                {
                    _order[accepted++] = c;
                }
            }

            _results = 0;
            _voiceCount = 0;
            for (var i = 0; i < accepted && _results < MaxCandidates; i++)
            {
                var c = _order[i];
                var rate = _findRate[c];
                if (rate == VoiceRate)
                {
                    voice++;
                    if (_voiceCount < MaxCandidates)
                    {
                        _voiceOffsets[_voiceCount] = _findOffset[c];
                        _voiceScores[_voiceCount] = _findScore[c];
                        _voiceCount++;
                    }
                    continue;
                }
                var aero = AeroRate.FromBitRate(rate);
                if (Math.Abs(_findOffset[c]) > UsableBandwidth * _sampleRate - aero.HalfBandwidth - aero.SearchRange)
                {
                    continue;
                }
                _offsets[_results] = _findOffset[c];
                _rates[_results] = rate;
                _scores[_results] = _findScore[c];
                _results++;
            }
            _images = images;
            _voice = voice;
            _lastCount = _results;
        }
    }
}
