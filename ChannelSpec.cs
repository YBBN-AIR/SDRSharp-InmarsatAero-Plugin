namespace SDRSharp.InmarsatAero
{
    public sealed class ChannelSpec
    {
        public ChannelSpec(long frequency, int bitRate) : this(frequency, bitRate, false)
        {
        }

        public ChannelSpec(long frequency, int bitRate, bool isAuto)
        {
            Frequency = frequency;
            BitRate = AeroRate.FromBitRate(bitRate).BitRate;
            IsAuto = isAuto;
        }

        // Hz, display domain (same as ISharpControl.Frequency)
        public long Frequency { get; }

        // bps: 600, 1200 or 10500
        public int BitRate { get; }

        // true: found by the carrier scan (temporary, never saved)
        public bool IsAuto { get; }
    }
}
