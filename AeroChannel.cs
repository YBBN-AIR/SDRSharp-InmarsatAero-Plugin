using SDRSharp.Radio;
using System;
using System.Threading;

namespace SDRSharp.InmarsatAero
{
    public enum ChannelState
    {
        Idle,
        RateTooLow,
        OutOfBand,
        Searching,
        NoFrameSync,
        FrameSync
    }

    // One carrier taken out of the raw IQ: SDR# DDC (DownConverter: shift to 0 Hz + decimation by 2^n), spur notcher, resampling to the
    // demodulator rate, demodulator, frame decoder, ISU assembler, ACARS parser. Runs on the decoder worker.
    public unsafe sealed class AeroChannel : IAcarsSink, ISignalUnitSink, IDisposable
    {
        private const int ReacquireFrames = 6; // frame periods without a unique word before the carrier search restarts (the resync nudge comes first)
        private const double SlowResyncTime = 2.5; // s without a unique word before the background resync search runs, 600 / 1200
        private const double FastResyncTime = 1.0; // s, 10500
        private const double DecimationMargin = 2.0; // decimated rate kept above this x the rate's minimum input rate
        private const double UsableBandwidth = 0.45; // x input rate, either side of the centre

        private readonly ChannelSpec _spec;
        private readonly AeroRate _rate;
        private readonly IAcarsSink _sink;
        private readonly IUnitSink _units;
        private readonly AeroFrameDecoder _frameDecoder;
        private readonly IsuAssembler _isuAssembler;
        private readonly AcarsParser _acarsParser;
        private DownConverter _downConverter;
        private SpurNotcher _spurNotcher;
        private IQResampler _resampler;
        private AeroDemodulator _demodulator;
        private UnsafeBuffer _work;
        private UnsafeBuffer _resampled;
        private double _inputRate;
        private double _appliedOffset = double.NaN;
        private long _tuneShift; // Hz the DDC sits above Spec.Frequency (auto fine-tune)
        private int[] _probeRates;
        private volatile float _carrierToNoise = float.NaN;
        private volatile int _state;

        public AeroChannel(ChannelSpec spec, IAcarsSink sink)
        {
            _spec = spec;
            _rate = AeroRate.FromBitRate(spec.BitRate);
            _sink = sink;
            _acarsParser = new AcarsParser(this);
            _isuAssembler = new IsuAssembler(_acarsParser);
            _frameDecoder = new AeroFrameDecoder(this);
            _units = sink as IUnitSink;
            _frameDecoder.Configure(_rate);
        }

        public ChannelSpec Spec => _spec;

        // Where the channel is tuned: Spec.Frequency plus any fine-tune
        public long TunedFrequency => _spec.Frequency + Interlocked.Read(ref _tuneShift);

        // Worker, auto channels: re-tune so the carrier sits at target Hz of offset, without losing lock
        public bool Recentre(double target)
        {
            var demodulator = _demodulator;
            if (demodulator == null || State != ChannelState.FrameSync)
            {
                return false;
            }
            var shift = (long)Math.Round(demodulator.CarrierOffset - target);
            if (shift == 0)
            {
                return false;
            }
            Interlocked.Add(ref _tuneShift, shift);
            demodulator.ShiftCarrier(shift); // the DDC follows on the next Process
            return true;
        }

        // Auto scan: score these rates too during acquisition (see AeroDemodulator.SetProbe); set before Configure
        public void SetProbe(int[] alternateRates)
        {
            _probeRates = alternateRates;
            var demodulator = _demodulator;
            if (demodulator != null)
            {
                demodulator.SetProbe(alternateRates);
            }
        }

        public int DetectedRate
        {
            get
            {
                var demodulator = _demodulator;
                return demodulator != null ? demodulator.DetectedRate : 0;
            }
        }

        public float DetectedOffset
        {
            get
            {
                var demodulator = _demodulator;
                return demodulator != null ? demodulator.DetectedOffset : 0.0f;
            }
        }

        // 10500: adaptive loop stage, 0 wide, 1 narrow, 2 fine
        public int LoopStage
        {
            get
            {
                var demodulator = _demodulator;
                return demodulator != null ? demodulator.LoopStage : 0;
            }
        }

        public bool LoopsNarrow => LoopStage > 0;

        public int Nudges
        {
            get
            {
                var demodulator = _demodulator;
                return demodulator != null ? demodulator.Nudges : 0;
            }
        }

        // Display values, read on the panel's timer
        public ChannelState State => (ChannelState)_state;

        public float CarrierOffset
        {
            get
            {
                var demodulator = _demodulator;
                return demodulator != null ? demodulator.CarrierOffset : 0.0f;
            }
        }

        // Coded-bit error rate the Viterbi decoder corrected (NaN until LinkQuality.MinimumBits are checked)
        public float PreFecBer
        {
            get
            {
                var bits = _frameDecoder.ChannelBits;
                return bits >= LinkQuality.MinimumBits ? (float)_frameDecoder.ChannelErrors / bits : float.NaN;
            }
        }

        // true: so few errors were counted that EbN0 and Margin are lower bounds
        public bool QualityIsLowerBound => _frameDecoder.ChannelErrors < LinkQuality.MinimumErrors;

        // dB per information bit, from the corrected bits (NaN until there are enough)
        public float EbN0
        {
            get
            {
                var ber = QualityBer();
                return float.IsNaN(ber) ? float.NaN : (float)(LinkQuality.EsN0Db(ber, _rate.IsDifferential) + 3.0103);
            }
        }

        // dB above the point where decoding fails
        public float Margin
        {
            get
            {
                var ber = QualityBer();
                return float.IsNaN(ber) ? float.NaN : (float)LinkQuality.MarginDb(ber, _rate.IsDifferential);
            }
        }

        // error rate for the figures: a lower bound's worth of errors when fewer were counted
        private float QualityBer()
        {
            var bits = _frameDecoder.ChannelBits;
            if (bits < LinkQuality.MinimumBits)
            {
                return float.NaN;
            }
            return (float)Math.Max(_frameDecoder.ChannelErrors, LinkQuality.MinimumErrors) / bits;
        }

        public AeroRate Rate => _rate;

        // dB-Hz from the wideband spectrum (processor sets it; NaN = nothing above the noise / not measured yet)
        public float CarrierToNoise
        {
            get { return _carrierToNoise; }
            set { _carrierToNoise = value; }
        }

        // Es/N0 (dB per coded bit) from CarrierToNoise; NaN when unknown
        public float SpectralEsN0
        {
            get
            {
                var cn0 = _carrierToNoise;
                return float.IsNaN(cn0) ? float.NaN : (float)(cn0 - 10.0 * Math.Log10(_rate.BitRate));
            }
        }

        public float SNR
        {
            get
            {
                var demodulator = _demodulator;
                return demodulator != null ? demodulator.SNR : 0.0f;
            }
        }

        public float AcquisitionScore
        {
            get
            {
                var demodulator = _demodulator;
                return demodulator != null ? demodulator.AcquisitionScore : 0.0f;
            }
        }

        public float AcquisitionEstimate
        {
            get
            {
                var demodulator = _demodulator;
                return demodulator != null ? demodulator.AcquisitionEstimate : 0.0f;
            }
        }

        public int SpurCount
        {
            get
            {
                var spurNotcher = _spurNotcher;
                return spurNotcher != null ? spurNotcher.SpurCount : 0;
            }
        }

        public int SignalUnitsOk => _frameDecoder.SignalUnitsOk;

        public int SignalUnitsBad => _frameDecoder.SignalUnitsBad;

        // Worker thread (allocates): input rate in S/s, blockLength = the most samples one Process call gets
        public void Configure(double inputRate, int blockLength)
        {
            DisposeDsp();
            _inputRate = inputRate;
            _appliedOffset = double.NaN;
            _frameDecoder.Reset();
            _isuAssembler.Reset();
            _acarsParser.Reset();

            var ratio = 1;
            while (ratio < 4096 && inputRate / (2 * ratio) >= DecimationMargin * _rate.MinimumInputRate)
            {
                ratio *= 2;
            }
            var decimatedRate = inputRate / ratio;
            if (decimatedRate < _rate.MinimumInputRate)
            {
                _state = (int)ChannelState.RateTooLow;
                return;
            }

            _downConverter = new DownConverter(inputRate, ratio);
            _spurNotcher = new SpurNotcher(decimatedRate);
            _resampler = new IQResampler(decimatedRate, _rate.ProcessingRate);
            var r = _resampler.ActualSampleRate / decimatedRate;
            _work = UnsafeBuffer.Create(blockLength, sizeof(Complex));
            _resampled = UnsafeBuffer.Create((int)Math.Ceiling((blockLength / ratio + 2) * r) + 1, sizeof(Complex));
            _demodulator = new AeroDemodulator(_rate, _resampler.ActualSampleRate);
            _demodulator.SetProbe(_probeRates);
            _state = (int)ChannelState.Searching;
        }

        // block: Complex raw IQ at the input rate, length a multiple of 16 (DownConverter), not modified
        public void Process(UnsafeBuffer block, int length, long centerFrequency)
        {
            var demodulator = _demodulator;
            if (demodulator == null)
            {
                return;
            }

            var offset = (double)(TunedFrequency - centerFrequency);
            if (Math.Abs(offset) > UsableBandwidth * _inputRate - _rate.HalfBandwidth - _rate.SearchRange)
            {
                _state = (int)ChannelState.OutOfBand;
                return;
            }
            if (offset != _appliedOffset)
            {
                // a centre change only re-tunes the DDC: the carrier keeps its baseband offset, so tracking continues
                _appliedOffset = offset;
                _downConverter.Frequency = offset; // DownConverter brings +Frequency to 0 Hz (docs 03, 3.9)
            }

            var work = (Complex*)_work;
            Utils.Memcpy(work, (Complex*)block, length * sizeof(Complex));
            var decimated = _downConverter.Process(work, length);
            _spurNotcher.Process(work, decimated);
            var count = _resampler.Process(work, (Complex*)_resampled, decimated);
            var bits = demodulator.Process(_resampled, count);
            if (bits > 0)
            {
                _frameDecoder.Process(demodulator.SoftBits, bits);
            }
            demodulator.SetLocked(_frameDecoder.IsSynced);
            var resyncBits = Math.Max(_rate.FrameBits, (_rate.IsDifferential ? SlowResyncTime : FastResyncTime) * _rate.BitRate);
            demodulator.Resync = demodulator.IsTracking && !_frameDecoder.IsSynced && _frameDecoder.BitsSinceSync > resyncBits;

            if (demodulator.IsTracking && !_frameDecoder.IsSynced && _frameDecoder.BitsSinceSync > ReacquireFrames * _rate.FrameBits)
            {
                demodulator.Reacquire();
                _frameDecoder.Reset();
            }

            if (!demodulator.IsTracking)
            {
                _state = (int)ChannelState.Searching;
            }
            else if (_frameDecoder.IsSynced)
            {
                _state = (int)ChannelState.FrameSync;
            }
            else
            {
                _state = (int)ChannelState.NoFrameSync;
            }
        }

        // Frame decoder: a good signal unit goes to the assembler and to the aircraft tracker
        public void SignalUnit(byte[] su, int offset)
        {
            _isuAssembler.SignalUnit(su, offset);
            if (_units != null)
            {
                _units.Unit(su, offset, TunedFrequency);
            }
        }

        // The input stream had a gap: the current frame is lost
        public void Gap()
        {
            _frameDecoder.Reset();
        }

        public void Message(AcarsMessage message)
        {
            message.Frequency = TunedFrequency;
            var demodulator = _demodulator;
            if (demodulator != null)
            {
                message.Level = demodulator.Level;
                var spectral = SpectralEsN0;
                message.SNR = float.IsNaN(spectral) ? demodulator.SNR : spectral; // JSON "snr": Es/N0 from the spectrum
                message.CarrierOffset = demodulator.CarrierOffset;
            }
            message.EbN0 = EbN0;
            message.Margin = Margin;
            message.Ber = PreFecBer;
            _sink.Message(message);
        }

        public void Dispose()
        {
            DisposeDsp();
        }

        private void DisposeDsp()
        {
            var demodulator = _demodulator;
            _demodulator = null;
            if (demodulator != null)
            {
                demodulator.Dispose();
            }
            if (_downConverter != null)
            {
                _downConverter.Dispose();
                _downConverter = null;
            }
            if (_spurNotcher != null)
            {
                _spurNotcher.Dispose();
                _spurNotcher = null;
            }
            if (_resampler != null)
            {
                _resampler.Dispose();
                _resampler = null;
            }
            if (_work != null)
            {
                _work.Dispose();
                _work = null;
            }
            if (_resampled != null)
            {
                _resampled.Dispose();
                _resampled = null;
            }
            _state = (int)ChannelState.Idle;
        }
    }
}
