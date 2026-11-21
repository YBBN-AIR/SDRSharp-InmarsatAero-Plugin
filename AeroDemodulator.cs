using SDRSharp.Radio;
using System;

namespace SDRSharp.InmarsatAero
{
    // Coherent OQPSK demodulator for the Aero P channel at SamplesPerBit x bit rate.
    // A-BPSK (600 / 1200 bps) is demodulated as OQPSK at the bit rate and differentially decoded;
    // A-QPSK (10500 bps) gives the arm decisions directly. Output: one soft bit per bit, positive = 1.
    // Acquire: carrier offset from the spectrum of the squared signal (lines at 2 x offset +/- bit rate / 2).
    // Track: carrier removed by an NCO, matched filter, Gardner timing on the arms at bit-rate strobes,
    // decision-directed QPSK carrier loop on (arm, previous arm) pairs, log-domain AGC.
    public unsafe sealed class AeroDemodulator : IDisposable
    {
        private const int AcquisitionFFTSize = 8192;
        private const int AcquisitionSpectra = 2;
        private const float AcquisitionThreshold = 25.0f; // weaker line / mean; measured: noise <= 10, a plain tone <= 10.5, Aero at Eb/N0 4 dB >= 178
        private const double CarrierLoopBandwidth = 0.02; // x bit rate, 600 / 1200 until frame sync
        private const double TimingLoopBandwidth = 0.01; // x bit rate, 600 / 1200 until frame sync
        private const double QpskCarrierLoopBandwidth = 0.005; // x bit rate, 10500 (prototype: 0.02 fails below Eb/N0 3 dB, 0.005 decodes at 1 dB)
        private const double QpskTimingLoopBandwidth = 0.0025; // x bit rate, 10500
        private const double LockedCarrierLoopBandwidth = 0.005; // x bit rate, 600 / 1200 after frame sync (prototype: +0.45 dB decision SNR)
        private const double LockedTimingLoopBandwidth = 0.0025; // x bit rate, 600 / 1200 after frame sync
        // 10500 adaptive loops, two stages, entered only while the measured carrier drift is small.
        // Real recordings, good SUs of 416 frames: 1545.320 MHz (Es/N0 -2.8 dB) wide 0, stage 1 155;
        // 1545.345 / 1545.320 MHz (next morning) stage 1 166 / 145, stage 2 195 / 176.
        // Synthetic drift tolerance (weak carrier): stage 1 holds past 3 Hz/s, stage 2 to 0.3 Hz/s (fails at 0.5),
        // carrier 0.0005 already loses SUs at 0.05 Hz/s. That receiver drifted 0.02 Hz/s.
        private const double QpskNarrowCarrierLoopBandwidth = 0.0015; // x bit rate, stage 1
        private const double QpskFineCarrierLoopBandwidth = 0.0008; // x bit rate, stage 2
        private const double QpskNarrowTimingLoopBandwidth = 0.00125; // x bit rate, both stages (0.001-0.0015 measured the same)
        private const int QpskNarrowAfterBits = 3000; // of tracking before stage 1
        private const double DriftWindowSeconds = 2.0; // carrier frequency averaged over this, drift = change between windows
        private const double NarrowMaxDrift = 1.0; // Hz/s: wide -> stage 1 below this
        private const double FineMaxDrift = 0.15; // Hz/s: stage 1 -> stage 2 below this
        private const double FineLeaveDrift = 0.25; // Hz/s: stage 2 -> stage 1 above this
        private const double WidenDrift = 3.0; // Hz/s: any stage -> wide above this
        private const double LoopDamping = 0.707;
        private const double FastAGCRate = 0.05; // log-gain step per bit, first FastAGCBits bits after acquisition
        private const double SlowAGCRate = 0.005;
        private const int FastAGCBits = 256;
        private const double MaxAGCGain = 1e12;
        private const double MaxTimingAdjust = 0.1; // x bit period per strobe
        private const double MaxTimingIntegral = 2e-4; // x bit period: clock offset the loop may hold (200 ppm); 0.01 let the loop run away and slip bits at low SNR
        private const double MaxResidualFrequency = 0.02; // x bit rate the carrier loop may hold
        private const double QualityAlpha = 0.002; // per bit
        private const float SoftScale = 64.0f; // soft value per unit arm amplitude
        private const double NudgeThreshold = 2.0; // Hz: a resync search this far from the loop's carrier moves the NCO
        private const float RunnerUpRatio = 1.6f; // the named rate's line pair must beat the other rates' by this
        private const double ResyncRange = 0.4; // x bit rate either side of the loop's carrier: a resync never jumps to a neighbour

        private readonly AeroRate _rate;
        private readonly double _sampleRate;
        private readonly double _samplesPerBit;
        private readonly double _wideCarrierKp;
        private readonly double _wideCarrierKi;
        private readonly double _wideTimingKp;
        private readonly double _wideTimingKi;
        private readonly double _lockedCarrierKp;
        private readonly double _lockedCarrierKi;
        private readonly double _lockedTimingKp;
        private readonly double _lockedTimingKi;
        private double _carrierKp;
        private double _carrierKi;
        private double _timingKp;
        private double _timingKi;
        private bool _locked;
        private readonly float[] _acquisitionPower = new float[AcquisitionFFTSize];
        private ComplexFilter _acquisitionFilter;
        private ComplexFilter _matchedFilter;
        private UnsafeBuffer _acquisitionBuffer;
        private UnsafeBuffer _fftBuffer;
        private sbyte[] _soft = new sbyte[0];

        private bool _tracking;
        private int _acquisitionFill;
        private int _acquisitionSpectra;
        private double _carrierEstimate;
        private double _ncoPhase;
        private double _ncoStep;
        private double _timeToStrobe;
        private Complex _previous;
        private Complex _z1;
        private Complex _z2;
        private int _arm;
        private double _a1;
        private double _phi;
        private double _omega;
        private double _qpskNarrowCarrierKp;
        private double _qpskNarrowCarrierKi;
        private double _qpskNarrowTimingKp;
        private double _qpskNarrowTimingKi;
        private double _qpskFineCarrierKp;
        private double _qpskFineCarrierKi;
        private volatile int _qpskStage; // 0 wide, 1 narrow, 2 fine
        private long _trackedBits;
        private double _driftSum; // Hz x bits in the current window
        private long _driftCount;
        private double _driftPreviousMean = double.NaN;
        private volatile float _drift = float.NaN; // Hz/s
        private double _timingIntegral;
        private double _agc;
        private int _agcBits;
        private double _diffPrevious;
        private int _diffParity;
        private double _meanMagnitude;
        private double _meanSquare;

        private volatile bool _isTracking;
        private volatile float _carrierOffset;
        private volatile float _snr;
        private volatile float _level;
        private volatile float _acquisitionScore;
        private volatile float _acquisitionEstimate;
        private int[] _probeRates;
        private volatile int _detectedRate;
        private volatile float _detectedOffset;
        private bool _resync;
        private volatile int _nudges;

        public AeroDemodulator(AeroRate rate, double sampleRate)
        {
            _rate = rate;
            _sampleRate = sampleRate;
            _samplesPerBit = sampleRate / rate.BitRate;

            LoopGains(rate.IsDifferential ? CarrierLoopBandwidth : QpskCarrierLoopBandwidth, out _wideCarrierKp, out _wideCarrierKi);
            LoopGains(rate.IsDifferential ? TimingLoopBandwidth : QpskTimingLoopBandwidth, out _wideTimingKp, out _wideTimingKi);
            LoopGains(LockedCarrierLoopBandwidth, out _lockedCarrierKp, out _lockedCarrierKi);
            LoopGains(LockedTimingLoopBandwidth, out _lockedTimingKp, out _lockedTimingKi);
            LoopGains(QpskNarrowCarrierLoopBandwidth, out _qpskNarrowCarrierKp, out _qpskNarrowCarrierKi);
            LoopGains(QpskNarrowTimingLoopBandwidth, out _qpskNarrowTimingKp, out _qpskNarrowTimingKi);
            LoopGains(QpskFineCarrierLoopBandwidth, out _qpskFineCarrierKp, out _qpskFineCarrierKi);
            UseWideLoops();

            var spb = rate.SamplesPerBit;
            var acquisitionKernel = FilterBuilder.MakeComplexKernel(sampleRate, 8 * spb, 2.0 * (rate.SearchRange + rate.HalfBandwidth), 0, WindowType.BlackmanHarris4);
            _acquisitionFilter = new ComplexFilter(acquisitionKernel);
            _matchedFilter = new ComplexFilter(MakeMatchedKernel(rate, sampleRate));
            _fftBuffer = UnsafeBuffer.Create(AcquisitionFFTSize, sizeof(Complex));
            Reacquire();
        }

        public sbyte[] SoftBits => _soft;

        // Display values, read on the panel's timer
        public bool IsTracking => _isTracking;

        // Hz relative to the VFO frequency
        public float CarrierOffset => _carrierOffset;

        // dB, arm decision power over its variance
        public float SNR => _snr;

        // dBFS, approximate carrier level: the AGC scales the arm amplitude to 1, so the input amplitude is about 1 / gain
        public float Level => _level;

        public float AcquisitionScore => _acquisitionScore;

        // Hz, best carrier offset of the last search (taken only when AcquisitionScore reaches the threshold)
        public float AcquisitionEstimate => _acquisitionEstimate;

        // Auto scan: also score these bit rates' line pairs during acquisition; if one beats this rate the demodulator
        // reports it in DetectedRate (and does not track), so the caller can restart at that rate.
        public void SetProbe(int[] alternateRates)
        {
            _probeRates = alternateRates;
        }

        // Bit rate whose line pair the last successful search found (0 = none yet)
        public int DetectedRate => _detectedRate;

        // Hz, carrier offset that search found
        public float DetectedOffset => _detectedOffset;

        // Worker: while tracking without frame sync, keep re-running the line-pair search in the background and
        // move the NCO to its carrier when it is more than NudgeThreshold away (timing and AGC are kept)
        public bool Resync
        {
            get { return _resync; }
            set
            {
                if (value == _resync)
                {
                    return;
                }
                _resync = value;
                if (_tracking)
                {
                    ClearSearch();
                }
            }
        }

        // 10500: adaptive loop stage, 0 wide, 1 narrow, 2 fine (see QpskNarrow* / QpskFine*)
        public int LoopStage => _qpskStage;

        public bool LoopsNarrow => _qpskStage > 0;

        // Measured carrier drift, Hz/s (NaN until two windows are in)
        public float Drift => _drift;

        // NCO moves made by the resync search (wraps)
        public int Nudges => _nudges;

        // Frame sync state from the frame decoder: 600 / 1200 narrow their loops while it holds (10500: no gain measured, stays wide)
        public void SetLocked(bool locked)
        {
            if (locked == _locked || !_rate.IsDifferential)
            {
                return;
            }
            _locked = locked;
            if (locked)
            {
                _carrierKp = _lockedCarrierKp;
                _carrierKi = _lockedCarrierKi;
                _timingKp = _lockedTimingKp;
                _timingKi = _lockedTimingKi;
            }
            else
            {
                UseWideLoops();
            }
        }

        // Worker: the DDC moved by hz, so the carrier moves by -hz at baseband; keeps the loops locked
        public void ShiftCarrier(double hz)
        {
            if (!_tracking)
            {
                return;
            }
            _carrierEstimate -= hz;
            _ncoStep = -2.0 * Math.PI * _carrierEstimate / _sampleRate;
            _carrierOffset = (float)(_carrierOffset - hz);
            // a deliberate move is not drift
            _driftSum -= hz * _driftCount;
            _driftPreviousMean -= hz;
        }

        public void Reacquire()
        {
            UseWideLoops();
            _tracking = false;
            _isTracking = false;
            ClearSearch();
        }

        private void ClearSearch()
        {
            _acquisitionFill = 0;
            _acquisitionSpectra = 0;
            Array.Clear(_acquisitionPower, 0, AcquisitionFFTSize);
        }

        // samples: Complex at the rate given to the constructor; overwritten. Returns the number of soft bits in SoftBits.
        public int Process(UnsafeBuffer samples, int length)
        {
            var needed = (int)(length / _samplesPerBit) + 8;
            if (_soft.Length < needed)
            {
                _soft = new sbyte[needed];
            }

            var buffer = (Complex*)samples;
            var produced = 0;
            var position = 0;
            while (position < length)
            {
                if (_tracking)
                {
                    if (_resync)
                    {
                        // the search reads the samples before Track removes the carrier in place
                        var p = position;
                        while (p < length && _tracking)
                        {
                            bool complete;
                            p += Accumulate(buffer + p, length - p, out complete);
                            if (complete)
                            {
                                ResyncSearch();
                            }
                        }
                    }
                    var before = produced;
                    produced = Track(buffer + position, length - position, produced);
                    position = length;
                    if (!_rate.IsDifferential)
                    {
                        AdaptLoops(produced - before);
                    }
                }
                else
                {
                    position += Acquire(buffer + position, length - position);
                }
            }
            return produced;
        }

        public void Dispose()
        {
            if (_acquisitionFilter != null)
            {
                _acquisitionFilter.Dispose();
                _acquisitionFilter = null;
            }
            if (_matchedFilter != null)
            {
                _matchedFilter.Dispose();
                _matchedFilter = null;
            }
            if (_acquisitionBuffer != null)
            {
                _acquisitionBuffer.Dispose();
                _acquisitionBuffer = null;
            }
            if (_fftBuffer != null)
            {
                _fftBuffer.Dispose();
                _fftBuffer = null;
            }
        }

        // Returns the samples consumed
        private int Acquire(Complex* input, int length)
        {
            bool complete;
            var count = Accumulate(input, length, out complete);
            if (!complete)
            {
                return count;
            }

            float score;
            var estimate = EstimateCarrier(_acquisitionPower, _rate.BitRate, 0, _rate.SearchRange, out score);
            var rate = _rate.BitRate;
            var runnerUp = 0.0f;
            var probes = _probeRates;
            if (probes != null)
            {
                for (var i = 0; i < probes.Length; i++)
                {
                    float probeScore;
                    var probeEstimate = EstimateCarrier(_acquisitionPower, probes[i], 0, _rate.SearchRange, out probeScore);
                    if (probeScore > score)
                    {
                        runnerUp = score;
                        score = probeScore;
                        estimate = probeEstimate;
                        rate = probes[i];
                    }
                    else if (probeScore > runnerUp)
                    {
                        runnerUp = probeScore;
                    }
                }
            }
            if (score < RunnerUpRatio * runnerUp)
            {
                score = 0; // no clear winner between the rates: look again
            }
            Array.Clear(_acquisitionPower, 0, AcquisitionFFTSize);
            _acquisitionScore = score;
            _acquisitionEstimate = (float)estimate;
            if (score >= AcquisitionThreshold)
            {
                _detectedOffset = (float)estimate;
                _detectedRate = rate;
                if (rate == _rate.BitRate)
                {
                    StartTracking(estimate);
                }
            }
            return count;
        }

        // Tracking without frame sync: the carrier the loop holds may be a false lock beside the real one
        private void ResyncSearch()
        {
            float score;
            var range = Math.Min(_rate.SearchRange, ResyncRange * _rate.BitRate);
            var estimate = EstimateCarrier(_acquisitionPower, _rate.BitRate, _carrierOffset, range, out score);
            Array.Clear(_acquisitionPower, 0, AcquisitionFFTSize);
            _acquisitionScore = score;
            _acquisitionEstimate = (float)estimate;
            if (score >= AcquisitionThreshold && Math.Abs(estimate - _carrierOffset) > NudgeThreshold)
            {
                _carrierEstimate = estimate;
                _ncoStep = -2.0 * Math.PI * estimate / _sampleRate;
                _omega = 0;
                _carrierOffset = (float)estimate;
                _nudges = _nudges < int.MaxValue ? _nudges + 1 : 0;
                UseWideLoops(); // the loop was on the wrong carrier: start wide, measure the drift afresh
            }
        }

        // Adds |FFT(filtered x^2)|^2 to _acquisitionPower; complete = AcquisitionSpectra summed. Returns the samples consumed.
        private int Accumulate(Complex* input, int length, out bool complete)
        {
            complete = false;
            var count = Math.Min(length, AcquisitionFFTSize - _acquisitionFill);
            if (_acquisitionBuffer == null || _acquisitionBuffer.Length < count)
            {
                if (_acquisitionBuffer != null)
                {
                    _acquisitionBuffer.Dispose();
                }
                _acquisitionBuffer = UnsafeBuffer.Create(Math.Max(count, AcquisitionFFTSize), sizeof(Complex));
            }

            var work = (Complex*)_acquisitionBuffer;
            Utils.Memcpy(work, input, count * sizeof(Complex));
            _acquisitionFilter.Process(work, count);

            var fft = (Complex*)_fftBuffer;
            var fill = _acquisitionFill;
            for (var i = 0; i < count; i++)
            {
                fft[fill + i] = work[i] * work[i];
            }
            fill += count;

            if (fill < AcquisitionFFTSize)
            {
                _acquisitionFill = fill;
                return count;
            }

            _acquisitionFill = 0;
            Fourier.ForwardTransform(fft, AcquisitionFFTSize, true);
            var power = _acquisitionPower;
            for (var i = 0; i < AcquisitionFFTSize; i++)
            {
                power[i] += fft[i].ModulusSquared();
            }

            if (_acquisitionSpectra < AcquisitionSpectra - 1)
            {
                _acquisitionSpectra++;
                return count;
            }

            _acquisitionSpectra = 0;
            complete = true;
            return count;
        }

        // power: |FFT(x^2)|^2 with DC at the centre bin; returns the carrier offset in Hz
        // bitRate: the line pair sits at 2 x offset +/- bitRate / 2; searches offsets within range Hz of centreHz
        private double EstimateCarrier(float[] power, int bitRate, double centreHz, double range, out float score)
        {
            var binHz = _sampleRate / AcquisitionFFTSize;
            var lineOffset = (int)Math.Round(bitRate * 0.5 / binHz);
            var maxBin = (int)(2.0 * range / binHz);
            var centreBin = (int)Math.Round(2.0 * centreHz / binHz);
            var centre = AcquisitionFFTSize / 2;
            var best = -1.0;
            var bestBin = 0;
            var sum = 0.0;
            var n = 0;
            for (var k = centreBin - maxBin; k <= centreBin + maxBin; k++)
            {
                var upper = centre + k + lineOffset;
                var lower = centre + k - lineOffset;
                if (upper < 0 || upper >= AcquisitionFFTSize || lower < 0 || lower >= AcquisitionFFTSize)
                {
                    continue;
                }
                var s = (double)Math.Min(power[upper], power[lower]);
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

        private void StartTracking(double estimate)
        {
            _carrierEstimate = estimate;
            _ncoStep = -2.0 * Math.PI * estimate / _sampleRate;
            _ncoPhase = 0;
            _timeToStrobe = _samplesPerBit;
            _previous = new Complex(0, 0);
            _z1 = new Complex(0, 0);
            _z2 = new Complex(0, 0);
            _arm = 0;
            _a1 = 0;
            _phi = 0;
            _omega = 0;
            _timingIntegral = 0;
            _agc = 1.0;
            _agcBits = 0;
            _diffPrevious = 0;
            _diffParity = 0;
            _meanMagnitude = 0;
            _meanSquare = 0;
            _carrierOffset = (float)estimate;
            UseWideLoops();
            _tracking = true;
            _isTracking = true;
        }

        private int Track(Complex* buffer, int length, int produced)
        {
            var ncoPhase = _ncoPhase;
            var ncoStep = _ncoStep;
            for (var i = 0; i < length; i++)
            {
                buffer[i] = buffer[i] * Complex.FromAngle(ncoPhase);
                ncoPhase += ncoStep;
                if (ncoPhase > Math.PI)
                {
                    ncoPhase -= 2.0 * Math.PI;
                }
                else if (ncoPhase < -Math.PI)
                {
                    ncoPhase += 2.0 * Math.PI;
                }
            }
            _ncoPhase = ncoPhase;

            _matchedFilter.Process(buffer, length);

            var soft = _soft;
            var differential = _rate.IsDifferential;
            var samplesPerBit = _samplesPerBit;
            var timeToStrobe = _timeToStrobe;
            var previous = _previous;
            var z1 = _z1;
            var z2 = _z2;
            var arm = _arm;
            var a1 = _a1;
            var phi = _phi;
            var omega = _omega;
            var timingIntegral = _timingIntegral;
            var agc = _agc;
            var agcBits = _agcBits;
            var diffPrevious = _diffPrevious;
            var diffParity = _diffParity;
            var meanMagnitude = _meanMagnitude;
            var meanSquare = _meanSquare;
            var maxOmega = 2.0 * Math.PI * MaxResidualFrequency;

            for (var i = 0; i < length; i++)
            {
                var v = buffer[i];
                timeToStrobe -= 1.0;
                if (timeToStrobe <= 0.0)
                {
                    var fraction = (float)(1.0 + timeToStrobe);
                    var z = previous + (v - previous) * fraction;
                    z = z * Complex.FromAngle(-phi) * (float)agc;

                    double a;
                    double mid;
                    double old;
                    if (arm == 0)
                    {
                        a = z.Real;
                        mid = z1.Real;
                        old = z2.Real;
                    }
                    else
                    {
                        a = z.Imag;
                        mid = z1.Imag;
                        old = z2.Imag;
                    }

                    var timingError = mid * (a - old);
                    timingIntegral += _timingKi * timingError;
                    timingIntegral = Math.Max(-MaxTimingIntegral, Math.Min(MaxTimingIntegral, timingIntegral));
                    var timingAdjust = Math.Max(-MaxTimingAdjust, Math.Min(MaxTimingAdjust, _timingKp * timingError + timingIntegral));
                    timeToStrobe += samplesPerBit - timingAdjust * samplesPerBit;

                    double wr;
                    double wi;
                    if (arm == 0)
                    {
                        wr = a;
                        wi = a1;
                    }
                    else
                    {
                        wr = a1;
                        wi = a;
                    }
                    var phaseError = Math.Tanh(2.0 * wr) * wi - Math.Tanh(2.0 * wi) * wr;
                    omega += _carrierKi * phaseError;
                    omega = Math.Max(-maxOmega, Math.Min(maxOmega, omega));
                    phi += omega + _carrierKp * phaseError;
                    if (phi > Math.PI)
                    {
                        phi -= 2.0 * Math.PI;
                    }
                    else if (phi < -Math.PI)
                    {
                        phi += 2.0 * Math.PI;
                    }

                    var magnitude = Math.Abs(a);
                    var agcRate = agcBits < FastAGCBits ? FastAGCRate : SlowAGCRate;
                    if (agcBits < FastAGCBits)
                    {
                        agcBits++;
                    }
                    agc *= Math.Exp(-agcRate * Math.Log(Math.Max(magnitude, 1e-12)));
                    if (agc > MaxAGCGain)
                    {
                        agc = MaxAGCGain;
                    }

                    meanMagnitude += QualityAlpha * (magnitude - meanMagnitude);
                    meanSquare += QualityAlpha * (a * a - meanSquare);

                    double decision;
                    if (differential)
                    {
                        decision = -a * diffPrevious;
                        if (diffParity == 1)
                        {
                            decision = -decision;
                        }
                        diffParity = 1 - diffParity;
                        diffPrevious = a;
                    }
                    else
                    {
                        decision = a;
                    }

                    var s = decision * SoftScale;
                    if (s > 127.0)
                    {
                        s = 127.0;
                    }
                    else if (s < -127.0)
                    {
                        s = -127.0;
                    }
                    if (produced < soft.Length)
                    {
                        soft[produced] = (sbyte)s;
                        produced++;
                    }

                    a1 = a;
                    z2 = z1;
                    z1 = z;
                    arm = 1 - arm;
                }
                previous = v;
            }

            if (!double.IsFinite(timeToStrobe) || !double.IsFinite(phi) || !double.IsFinite(agc) || !double.IsFinite(meanSquare) || !float.IsFinite(previous.Real) || !float.IsFinite(previous.Imag))
            {
                Reacquire();
                return produced;
            }

            _timeToStrobe = timeToStrobe;
            _previous = previous;
            _z1 = z1;
            _z2 = z2;
            _arm = arm;
            _a1 = a1;
            _phi = phi;
            _omega = omega;
            _timingIntegral = timingIntegral;
            _agc = agc;
            _agcBits = agcBits;
            _diffPrevious = diffPrevious;
            _diffParity = diffParity;
            _meanMagnitude = meanMagnitude;
            _meanSquare = meanSquare;

            var variance = meanSquare - meanMagnitude * meanMagnitude;
            if (variance > 0 && meanMagnitude > 0)
            {
                _snr = (float)(10.0 * Math.Log10(meanMagnitude * meanMagnitude / variance));
            }
            _carrierOffset = (float)(_carrierEstimate + omega * _rate.BitRate / (2.0 * Math.PI));
            _level = (float)(-20.0 * Math.Log10(agc));
            return produced;
        }

        // 10500: average the loop's carrier frequency over DriftWindowSeconds; narrow the loops after QpskNarrowAfterBits
        // while the drift between windows stays under NarrowMaxDrift, widen again above WidenDrift
        private void AdaptLoops(int bits)
        {
            if (bits <= 0)
            {
                return;
            }
            _trackedBits += bits;
            _driftSum += _carrierOffset * (double)bits;
            _driftCount += bits;
            var window = (long)(DriftWindowSeconds * _rate.BitRate);
            if (_driftCount < window)
            {
                return;
            }
            var mean = _driftSum / _driftCount;
            if (!double.IsNaN(_driftPreviousMean))
            {
                var drift = (mean - _driftPreviousMean) / ((double)_driftCount / _rate.BitRate);
                _drift = (float)drift;
                var size = Math.Abs(drift);
                if (_qpskStage > 0 && size > WidenDrift)
                {
                    SetStage(0);
                }
                else if (_qpskStage == 2 && size > FineLeaveDrift)
                {
                    SetStage(1);
                }
                else if (_qpskStage == 1 && size < FineMaxDrift)
                {
                    SetStage(2);
                }
                else if (_qpskStage == 0 && _trackedBits >= QpskNarrowAfterBits && size < NarrowMaxDrift)
                {
                    SetStage(1);
                }
            }
            _driftPreviousMean = mean;
            _driftSum = 0;
            _driftCount = 0;
        }

        private void SetStage(int stage)
        {
            _qpskStage = stage;
            if (stage == 0)
            {
                _carrierKp = _wideCarrierKp;
                _carrierKi = _wideCarrierKi;
                _timingKp = _wideTimingKp;
                _timingKi = _wideTimingKi;
                return;
            }
            _carrierKp = stage == 2 ? _qpskFineCarrierKp : _qpskNarrowCarrierKp;
            _carrierKi = stage == 2 ? _qpskFineCarrierKi : _qpskNarrowCarrierKi;
            _timingKp = _qpskNarrowTimingKp;
            _timingKi = _qpskNarrowTimingKi;
        }

        private void ResetDrift()
        {
            _qpskStage = 0;
            _trackedBits = 0;
            _driftSum = 0;
            _driftCount = 0;
            _driftPreviousMean = double.NaN;
            _drift = float.NaN;
        }

        private void UseWideLoops()
        {
            ResetDrift();
            _locked = false;
            _carrierKp = _wideCarrierKp;
            _carrierKi = _wideCarrierKi;
            _timingKp = _wideTimingKp;
            _timingKi = _wideTimingKi;
        }

        // Second-order loop, normalized bandwidth per update, detector gain 1
        private static void LoopGains(double bandwidth, out double kp, out double ki)
        {
            var theta = bandwidth / (LoopDamping + 1.0 / (4.0 * LoopDamping));
            var d = 1.0 + 2.0 * LoopDamping * theta + theta * theta;
            kp = 4.0 * LoopDamping * theta / d;
            ki = 4.0 * theta * theta / d;
        }

        // 600 / 1200: half-sine over two bits (MSK pulse); 10500: root raised cosine, roll-off 1, arm symbol rate = bit rate / 2
        private static Complex[] MakeMatchedKernel(AeroRate rate, double sampleRate)
        {
            var spb = rate.SamplesPerBit;
            float[] taps;
            if (rate.IsDifferential)
            {
                taps = new float[2 * spb + 1];
                for (var i = 0; i < taps.Length; i++)
                {
                    taps[i] = (float)Math.Sin(Math.PI * i / (2.0 * spb));
                }
            }
            else
            {
                taps = FilterBuilder.MakeRootRaisedCosineKernel(16 * spb + 1, sampleRate, rate.BitRate * 0.5, 1.0);
            }
            FilterBuilder.Normalize(taps);
            var kernel = new Complex[taps.Length];
            for (var i = 0; i < taps.Length; i++)
            {
                kernel[i] = new Complex(taps[i], 0);
            }
            return kernel;
        }
    }
}
