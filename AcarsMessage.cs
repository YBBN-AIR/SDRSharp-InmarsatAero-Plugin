namespace SDRSharp.InmarsatAero
{
    public sealed class AcarsMessage
    {
        public const int RegistrationCapacity = 7;
        public const int TextCapacity = 8192;

        public long Created { get; set; }

        public int Sequence { get; set; }

        // Hz, the channel the message was decoded on
        public long Frequency { get; set; }

        // Channel signal at decode time: dBFS (approximate), dB, Hz
        public float Level { get; set; }

        public float SNR { get; set; }

        public float CarrierOffset { get; set; }

        // From the corrected coded bits (NaN = not enough yet): Eb/N0 per information bit, margin above code failure, dB
        public float EbN0 { get; set; }

        public float Margin { get; set; }

        // Coded-bit error rate before the Viterbi decoder (NaN = not enough yet)
        public float Ber { get; set; }

        public int AESID { get; set; }

        public int GESID { get; set; }

        public int QNO { get; set; }

        public int REFNO { get; set; }

        // true: not an ACARS block; Text holds the ISU user data in upper-case hex
        public bool NonAcars { get; set; }

        public byte Mode { get; set; }

        public byte Ack { get; set; }

        public byte Label0 { get; set; }

        public byte Label1 { get; set; }

        public byte BlockId { get; set; }

        public bool MoreToCome { get; set; }

        public byte[] Registration { get; } = new byte[RegistrationCapacity];

        public int RegistrationLength { get; set; }

        public byte[] Text { get; } = new byte[TextCapacity];

        public int TextLength { get; set; }

        public void CopyFrom(AcarsMessage other)
        {
            Created = other.Created;
            Sequence = other.Sequence;
            Frequency = other.Frequency;
            Level = other.Level;
            SNR = other.SNR;
            CarrierOffset = other.CarrierOffset;
            EbN0 = other.EbN0;
            Margin = other.Margin;
            Ber = other.Ber;
            AESID = other.AESID;
            GESID = other.GESID;
            QNO = other.QNO;
            REFNO = other.REFNO;
            NonAcars = other.NonAcars;
            Mode = other.Mode;
            Ack = other.Ack;
            Label0 = other.Label0;
            Label1 = other.Label1;
            BlockId = other.BlockId;
            MoreToCome = other.MoreToCome;
            System.Buffer.BlockCopy(other.Registration, 0, Registration, 0, other.RegistrationLength);
            RegistrationLength = other.RegistrationLength;
            System.Buffer.BlockCopy(other.Text, 0, Text, 0, other.TextLength);
            TextLength = other.TextLength;
        }
    }
}
