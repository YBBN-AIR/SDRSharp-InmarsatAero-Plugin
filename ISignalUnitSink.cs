namespace SDRSharp.InmarsatAero
{
    public interface ISignalUnitSink
    {
        // su: 12-byte signal unit (10 data bytes + CRC) that passed its CRC, at su[offset .. offset + 11]
        void SignalUnit(byte[] su, int offset);
    }

    public interface IIsuSink
    {
        // A complete initial signal unit with all its subsequent signal units; valid only during the call
        void Isu(IsuItem isu);
    }

    public interface IAcarsSink
    {
        // valid only during the call
        void Message(AcarsMessage message);
    }

    public interface IUnitSink
    {
        // every signal unit that passed its CRC, with the channel's frequency; valid only during the call
        void Unit(byte[] su, int offset, long frequency);
    }
}
