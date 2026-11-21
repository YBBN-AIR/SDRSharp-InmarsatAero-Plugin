namespace SDRSharp.InmarsatAero
{
    public sealed class AeroRate
    {
        public const int InterleaverRows = 64;

        public static readonly AeroRate Rate600 = new AeroRate(600, 32, 0, 1152, 6, 8, 0.75, 600.0);
        public static readonly AeroRate Rate1200 = new AeroRate(1200, 32, 0, 1152, 9, 8, 0.75, 1200.0);
        public static readonly AeroRate Rate10500 = new AeroRate(10500, 64, 178, 4992, 78, 4, 0.5, 2500.0);

        private AeroRate(int bitRate, int uniqueWordBits, int dummyBits, int dataBits, int columns, int samplesPerBit, double halfBandwidth, double searchRange)
        {
            BitRate = bitRate;
            UniqueWordBits = uniqueWordBits;
            HeaderBits = 16;
            DummyBits = dummyBits;
            DataBits = dataBits;
            Columns = columns;
            SamplesPerBit = samplesPerBit;
            HalfBandwidth = halfBandwidth * bitRate;
            SearchRange = searchRange;
        }

        // bps
        public int BitRate { get; }

        // 32: one 0xE15AE893 word (A-BPSK); 64: the word on each OQPSK arm, interleaved (A-QPSK)
        public int UniqueWordBits { get; }

        public int HeaderBits { get; }

        public int DummyBits { get; }

        // coded bits per frame
        public int DataBits { get; }

        public int Columns { get; }

        public int SamplesPerBit { get; }

        // Hz, one side of the signal's main lobe
        public double HalfBandwidth { get; }

        // Hz, +/- range of the coarse carrier search
        public double SearchRange { get; }

        public bool IsDifferential => UniqueWordBits == 32;

        public int FrameBits => UniqueWordBits + HeaderBits + DummyBits + DataBits;

        public int BlockBits => InterleaverRows * Columns;

        public int InfoBits => DataBits / 2;

        public int SignalUnitsPerFrame => InfoBits / 96;

        public double ProcessingRate => (double)SamplesPerBit * BitRate;

        // S/s: PreIF is flat to +/-0.4 x its rate (docs 02, 2.4)
        public double MinimumInputRate => (HalfBandwidth + SearchRange) / 0.4;

        public static AeroRate FromBitRate(int bitRate)
        {
            switch (bitRate)
            {
                case 1200:
                    return Rate1200;

                case 10500:
                    return Rate10500;

                default:
                    return Rate600;
            }
        }
    }
}
