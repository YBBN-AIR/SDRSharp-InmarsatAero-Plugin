namespace SDRSharp.InmarsatAero
{
    // P channel frame: unique word, 16 header bits, dummy bits (10500 only), interleaved rate 1/2 coded data.
    // Data path: 64 x N block deinterleaver, Viterbi, descrambler (reset each frame), 12-byte signal units with CRC-16.
    public sealed class AeroFrameDecoder
    {
        private const uint UniqueWord = 0xE15AE893;
        private const int SyncTolerance = 2; // bit errors per 32-bit word while searching (600 / 1200: a false hit about every 2 h of noise)
        private const int DualSyncTolerance = 3; // per arm while searching at 10500: both arms must hit on consecutive bits
        private const int LockedTolerance = 6; // bit errors per 32-bit word inside the expected window; the word shifted 1..4 bits is >= 14 away
        private const int SyncWindow = 2; // bits either side of the expected unique word end
        private const int MaxMissedWords = 4; // frames kept on the flywheel before sync is dropped
        private const int StatisticsFrames = 16;
        private const int SignalUnitBytes = 12;
        private const int HardHistory = 4096; // >= one block of trellis steps (10500: 2496) + the Viterbi decision delay
        private const int EncoderFill = 6; // re-encoded bits skipped at the start of a frame (register not yet known)

        private static readonly byte[] ScramblerSequence = MakeScramblerSequence(5000);

        private readonly ISignalUnitSink _sink;
        private readonly ViterbiDecoder _viterbi = new ViterbiDecoder();
        private readonly int[] _permutation = new int[AeroRate.InterleaverRows];
        private readonly int[] _okPerFrame = new int[StatisticsFrames];
        private readonly int[] _badPerFrame = new int[StatisticsFrames];
        private readonly bool[] _invert = new bool[2];
        private readonly uint[] _armWord = new uint[2];
        private readonly byte[] _hard = new byte[HardHistory]; // received hard decisions per trellis step: bit 0 / bit 1
        private readonly int[] _errorsPerFrame = new int[StatisticsFrames];
        private readonly int[] _bitsPerFrame = new int[StatisticsFrames];
        private int _stepIn; // trellis steps given to the Viterbi decoder this frame
        private int _stepOut; // decided bits taken from it this frame
        private int _encoder; // re-encoder register, newest bit in the LSB
        private int _frameErrors;
        private int _frameBits;
        private volatile int _channelErrors;
        private volatile int _channelBits;
        private AeroRate _rate;
        private sbyte[] _block;
        private sbyte[] _coded;
        private byte[] _decoded;
        private byte[] _info;
        private bool _synced;
        private int _counter;
        private int _arm;
        private bool _armHitPrevious;
        private uint _word;
        private int _missedWords;
        private int _infoIndex;
        private int _statisticsIndex;
        private int _bitsSinceSync;
        private volatile int _signalUnitsOk;
        private volatile int _signalUnitsBad;
        private volatile bool _isSynced;

        public AeroFrameDecoder(ISignalUnitSink sink)
        {
            _sink = sink;
            for (var i = 0; i < AeroRate.InterleaverRows; i++)
            {
                _permutation[i] = (i * 27) % AeroRate.InterleaverRows;
            }
        }

        // Read on the panel's timer: display values
        public bool IsSynced => _isSynced;

        public int SignalUnitsOk => _signalUnitsOk;

        public int SignalUnitsBad => _signalUnitsBad;

        // bits received since the last unique word (saturates at int.MaxValue / 2)
        public int BitsSinceSync => _bitsSinceSync;

        // Coded bits the Viterbi decoder corrected, and coded bits checked, over the last StatisticsFrames frames that
        // had a good CRC (the decoder's output is then right, so re-encoding it gives the bits that were sent)
        public int ChannelErrors => _channelErrors;

        public int ChannelBits => _channelBits;

        public void Configure(AeroRate rate)
        {
            _rate = rate;
            _block = new sbyte[rate.BlockBits];
            _coded = new sbyte[rate.BlockBits];
            _decoded = new byte[rate.BlockBits];
            _info = new byte[rate.InfoBits / 8];
            for (var i = 0; i < StatisticsFrames; i++)
            {
                _okPerFrame[i] = 0;
                _badPerFrame[i] = 0;
            }
            _signalUnitsOk = 0;
            _signalUnitsBad = 0;
            for (var i = 0; i < StatisticsFrames; i++)
            {
                _errorsPerFrame[i] = 0;
                _bitsPerFrame[i] = 0;
            }
            _channelErrors = 0;
            _channelBits = 0;
            Reset();
        }

        public void Reset()
        {
            _synced = false;
            _isSynced = false;
            _counter = 0;
            _missedWords = 0;
            _armHitPrevious = false;
            _word = 0;
            _armWord[0] = 0;
            _armWord[1] = 0;
            _invert[0] = false;
            _invert[1] = false;
            _bitsSinceSync = 0;
            StartData();
        }

        public void Process(sbyte[] soft, int count)
        {
            var rate = _rate;
            var frameBits = rate.FrameBits;
            var dataStart = rate.HeaderBits + rate.DummyBits;
            var dataEnd = dataStart + rate.DataBits;
            var blockBits = rate.BlockBits;
            var dual = !rate.IsDifferential;

            for (var i = 0; i < count; i++)
            {
                var value = soft[i];
                var bit = value > 0 ? 1u : 0u;
                var arm = _arm;

                if (_bitsSinceSync < int.MaxValue / 2)
                {
                    _bitsSinceSync++;
                }

                var expected = _synced && _counter >= frameBits - 1 - SyncWindow && _counter <= frameBits - 1 + SyncWindow;
                var allowed = !_synced || expected;
                var tolerance = _synced ? LockedTolerance : dual ? DualSyncTolerance : SyncTolerance;
                var found = false;
                if (dual)
                {
                    _armWord[arm] = (_armWord[arm] << 1) | bit;
                    var distance = BitCount(_armWord[arm] ^ UniqueWord);
                    var hit = distance <= tolerance || distance >= 32 - tolerance;
                    if (allowed && hit && _armHitPrevious)
                    {
                        _invert[arm] = distance >= 32 - tolerance;
                        var other = 1 - arm;
                        _invert[other] = BitCount(_armWord[other] ^ UniqueWord) >= 32 - tolerance;
                        found = true;
                    }
                    _armHitPrevious = hit;
                }
                else
                {
                    _word = (_word << 1) | bit;
                    var distance = BitCount(_word ^ UniqueWord);
                    if (allowed && (distance <= tolerance || distance >= 32 - tolerance))
                    {
                        var inverted = distance >= 32 - tolerance;
                        _invert[0] = inverted;
                        _invert[1] = inverted;
                        found = true;
                    }
                }

                if (!found && _synced && _counter >= dataStart && _counter < dataEnd)
                {
                    var index = (_counter - dataStart) % blockBits;
                    _block[index] = _invert[arm] ? (sbyte)(-value) : value;
                    if (index == blockBits - 1)
                    {
                        DecodeBlock();
                    }
                }

                if (found)
                {
                    if (!_synced)
                    {
                        StartData();
                    }
                    _synced = true;
                    _isSynced = true;
                    _missedWords = 0;
                    _counter = 0;
                    _bitsSinceSync = 0;
                    _armHitPrevious = false;
                }
                else if (_synced && _counter == frameBits - 1 + SyncWindow)
                {
                    if (_missedWords < MaxMissedWords)
                    {
                        _missedWords++;
                        _counter = SyncWindow;
                    }
                    else
                    {
                        _synced = false;
                        _isSynced = false;
                        _counter = 0;
                    }
                }
                else if (_counter < frameBits + SyncWindow)
                {
                    _counter++;
                }

                _arm = 1 - arm;
            }
        }

        private void StartData()
        {
            _viterbi.Reset();
            _infoIndex = 0;
            _stepIn = 0;
            _stepOut = 0;
            _encoder = 0;
            _frameErrors = 0;
            _frameBits = 0;
            if (_info != null)
            {
                for (var i = 0; i < _info.Length; i++)
                {
                    _info[i] = 0;
                }
            }
        }

        private void DecodeBlock()
        {
            var columns = _rate.Columns;
            var rows = AeroRate.InterleaverRows;
            var k = 0;
            for (var j = 0; j < columns; j++)
            {
                for (var r = 0; r < rows; r++)
                {
                    _coded[k] = _block[_permutation[r] * columns + j];
                    k++;
                }
            }

            var pairs = _rate.BlockBits / 2;
            for (var p = 0; p < pairs; p++)
            {
                _hard[(_stepIn + p) & (HardHistory - 1)] = (byte)((_coded[2 * p] > 0 ? 1 : 0) | (_coded[2 * p + 1] > 0 ? 2 : 0));
            }
            _stepIn += pairs;

            var produced = _viterbi.Process(_coded, 0, pairs, _decoded, 0);
            var infoBits = _rate.InfoBits;
            for (var n = 0; n < produced; n++)
            {
                // re-encode the decided bit and compare with what was received at that step
                _encoder = ((_encoder << 1) | _decoded[n]) & 127;
                if (_stepOut >= EncoderFill)
                {
                    var sent = Parity(_encoder & 109) | (Parity(_encoder & 79) << 1);
                    var diff = sent ^ _hard[_stepOut & (HardHistory - 1)];
                    _frameErrors += (diff & 1) + (diff >> 1);
                    _frameBits += 2;
                }
                _stepOut++;

                var index = _infoIndex;
                var bit = _decoded[n] ^ ScramblerSequence[index];
                if (bit != 0)
                {
                    _info[index >> 3] |= (byte)(1 << (index & 7));
                }
                if (index < infoBits - 1)
                {
                    _infoIndex = index + 1;
                }
                else
                {
                    _infoIndex = 0;
                    FrameComplete();
                }
            }
        }

        private void FrameComplete()
        {
            var ok = 0;
            var bad = 0;
            var units = _rate.SignalUnitsPerFrame;
            for (var k = 0; k < units; k++)
            {
                var offset = k * SignalUnitBytes;
                if (CheckCrc(_info, offset))
                {
                    ok++;
                    _sink.SignalUnit(_info, offset);
                }
                else
                {
                    bad++;
                }
            }

            for (var i = 0; i < _info.Length; i++)
            {
                _info[i] = 0;
            }

            _errorsPerFrame[_statisticsIndex] = ok > 0 ? _frameErrors : 0;
            _bitsPerFrame[_statisticsIndex] = ok > 0 ? _frameBits : 0;
            _frameErrors = 0;
            _frameBits = 0;
            _okPerFrame[_statisticsIndex] = ok;
            _badPerFrame[_statisticsIndex] = bad;
            if (_statisticsIndex < StatisticsFrames - 1)
            {
                _statisticsIndex++;
            }
            else
            {
                _statisticsIndex = 0;
            }
            var sumOk = 0;
            var sumBad = 0;
            for (var i = 0; i < StatisticsFrames; i++)
            {
                sumOk += _okPerFrame[i];
                sumBad += _badPerFrame[i];
            }
            _signalUnitsOk = sumOk;
            _signalUnitsBad = sumBad;
            var sumErrors = 0;
            var sumBits = 0;
            for (var i = 0; i < StatisticsFrames; i++)
            {
                sumErrors += _errorsPerFrame[i];
                sumBits += _bitsPerFrame[i];
            }
            _channelErrors = sumErrors;
            _channelBits = sumBits;
        }

        private static int Parity(int v)
        {
            v ^= v >> 4;
            v ^= v >> 2;
            v ^= v >> 1;
            return v & 1;
        }

        // CRC-16, polynomial 0x1021 bit-reversed (0x8408), LSB first, initial 0xFFFF, inverted; stored low byte first
        public static bool CheckCrc(byte[] data, int offset)
        {
            var crc = (ushort)0xFFFF;
            var sum = 0;
            for (var i = 0; i < 10; i++)
            {
                int b = data[offset + i];
                sum += b;
                for (var k = 0; k < 8; k++)
                {
                    var feedback = (crc ^ (b >> k)) & 1;
                    crc = (ushort)(crc >> 1);
                    if (feedback != 0)
                    {
                        crc = (ushort)(crc ^ 0x8408);
                    }
                }
            }
            crc = (ushort)~crc;
            var received = (ushort)(data[offset + 10] | (data[offset + 11] << 8));
            if (received == 0 && sum == 0)
            {
                return true;
            }
            return crc == received;
        }

        private static int BitCount(uint v)
        {
            return System.Numerics.BitOperations.PopCount(v);
        }

        // 15-bit LFSR, output = s0 ^ s14, initial state 110100101011001
        private static byte[] MakeScramblerSequence(int length)
        {
            var state = new byte[] { 1, 1, 0, 1, 0, 0, 1, 0, 1, 0, 1, 1, 0, 0, 1 };
            var sequence = new byte[length];
            for (var a = 0; a < length; a++)
            {
                var v = (byte)(state[0] ^ state[14]);
                sequence[a] = v;
                for (var i = state.Length - 1; i > 0; i--)
                {
                    state[i] = state[i - 1];
                }
                state[0] = v;
            }
            return sequence;
        }
    }
}
