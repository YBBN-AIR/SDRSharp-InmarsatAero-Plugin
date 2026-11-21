namespace SDRSharp.InmarsatAero
{
    // ISU user data -> ACARS block (ARINC 618 over Inmarsat Aero: FF FF SOH mode reg[7] ack label[2] blkid STX/ETX text ETX|ETB BCS DEL),
    // odd parity on every character; multi-block messages (ETB) are joined while the block id steps A..Z
    public sealed class AcarsParser : IIsuSink
    {
        private const int FragmentCount = 8;
        private const int MaxFragmentAge = 30; // messages seen before an unfinished multi-block message is dropped
        private const byte Stx = 0x02;
        private const byte Etx = 0x83; // ETX with its parity bit
        private const byte Etb = 0x97; // ETB with its parity bit
        private const byte Del = 0x7F;

        private static readonly byte[] HexDigits = { (byte)'0', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6', (byte)'7', (byte)'8', (byte)'9', (byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E', (byte)'F' };
        private static readonly byte[] DelText = { (byte)'<', (byte)'D', (byte)'E', (byte)'L', (byte)'>' };

        private readonly IAcarsSink _sink;
        private readonly AcarsMessage _message = new AcarsMessage();
        private readonly AcarsMessage[] _fragments = new AcarsMessage[FragmentCount];
        private readonly int[] _fragmentAge = new int[FragmentCount];
        private readonly bool[] _fragmentInUse = new bool[FragmentCount];

        public AcarsParser(IAcarsSink sink)
        {
            _sink = sink;
            for (var i = 0; i < FragmentCount; i++)
            {
                _fragments[i] = new AcarsMessage();
            }
        }

        public void Reset()
        {
            for (var i = 0; i < FragmentCount; i++)
            {
                _fragmentInUse[i] = false;
            }
        }

        public void Isu(IsuItem isu)
        {
            if (isu.AESID == 0)
            {
                return;
            }

            var data = isu.UserData;
            var length = isu.Length;
            var m = _message;
            m.AESID = isu.AESID;
            m.GESID = isu.GESID;
            m.QNO = isu.QNO;
            m.REFNO = isu.REFNO;
            m.Mode = 0;
            m.Ack = 0;
            m.Label0 = 0;
            m.Label1 = 0;
            m.BlockId = 0;
            m.MoreToCome = false;
            m.RegistrationLength = 0;
            m.TextLength = 0;

            var isAcars = length > 16 && data[0] == 0xFF && data[1] == 0xFF && (data[15] == Etx || data[15] == Stx);
            if (!isAcars)
            {
                m.NonAcars = true;
                var n = 0;
                for (var i = 0; i < length && n < AcarsMessage.TextCapacity - 1; i++)
                {
                    m.Text[n] = HexDigits[data[i] >> 4];
                    m.Text[n + 1] = HexDigits[data[i] & 0x0F];
                    n += 2;
                }
                m.TextLength = n;
                _sink.Message(m);
                return;
            }

            m.NonAcars = false;
            m.Mode = (byte)(data[3] & 0x7F);
            m.Ack = (byte)(data[11] & 0x7F);
            m.Label0 = (byte)(data[12] & 0x7F);
            m.Label1 = (byte)(data[13] & 0x7F);
            m.BlockId = (byte)(data[14] & 0x7F);
            m.MoreToCome = data[length - 4] == Etb;

            var leadingDots = true;
            for (var k = 4; k < 11; k++)
            {
                if (!OddParity(data[k]))
                {
                    return;
                }
                var c = (byte)(data[k] & 0x7F);
                if (leadingDots && c == (byte)'.')
                {
                    continue;
                }
                leadingDots = false;
                m.Registration[m.RegistrationLength] = c;
                m.RegistrationLength++;
            }

            if (data[15] == Stx)
            {
                var n = 0;
                for (var k = 16; k < length - 4; k++)
                {
                    if (!OddParity(data[k]))
                    {
                        return;
                    }
                    var c = (byte)(data[k] & 0x7F);
                    if (c == Del)
                    {
                        if (n + DelText.Length <= AcarsMessage.TextCapacity)
                        {
                            for (var j = 0; j < DelText.Length; j++)
                            {
                                m.Text[n + j] = DelText[j];
                            }
                            n += DelText.Length;
                        }
                    }
                    else if (n < AcarsMessage.TextCapacity)
                    {
                        m.Text[n] = c;
                        n++;
                    }
                }
                m.TextLength = n;
            }

            Defragment(m);
        }

        private void Defragment(AcarsMessage m)
        {
            var match = -1;
            for (var i = 0; i < FragmentCount; i++)
            {
                if (!_fragmentInUse[i])
                {
                    continue;
                }
                if (_fragmentAge[i] < MaxFragmentAge)
                {
                    _fragmentAge[i]++;
                }
                else
                {
                    _fragmentInUse[i] = false;
                    continue;
                }
                var f = _fragments[i];
                var nextBlockId = (((f.BlockId + 1) - 'A') % 26) + 'A';
                if (f.MoreToCome && f.AESID == m.AESID && f.GESID == m.GESID && f.Mode == m.Mode && f.Ack == m.Ack && f.Label0 == m.Label0 && f.Label1 == m.Label1 && SameRegistration(f, m) && nextBlockId == m.BlockId)
                {
                    match = i;
                }
            }

            if (match < 0)
            {
                if (!m.MoreToCome)
                {
                    _sink.Message(m);
                    return;
                }
                var slot = 0;
                for (var i = 0; i < FragmentCount; i++)
                {
                    if (!_fragmentInUse[i])
                    {
                        slot = i;
                        break;
                    }
                    if (_fragmentAge[i] > _fragmentAge[slot])
                    {
                        slot = i;
                    }
                }
                _fragments[slot].CopyFrom(m);
                _fragmentAge[slot] = 0;
                _fragmentInUse[slot] = true;
                return;
            }

            var fragment = _fragments[match];
            var room = AcarsMessage.TextCapacity - fragment.TextLength;
            var count = m.TextLength < room ? m.TextLength : room;
            System.Buffer.BlockCopy(m.Text, 0, fragment.Text, fragment.TextLength, count);
            fragment.TextLength += count;
            fragment.BlockId = m.BlockId;
            fragment.MoreToCome = m.MoreToCome;
            _fragmentAge[match] = 0;
            if (!m.MoreToCome)
            {
                _fragmentInUse[match] = false;
                _sink.Message(fragment);
            }
        }

        private static bool SameRegistration(AcarsMessage a, AcarsMessage b)
        {
            if (a.RegistrationLength != b.RegistrationLength)
            {
                return false;
            }
            for (var i = 0; i < a.RegistrationLength; i++)
            {
                if (a.Registration[i] != b.Registration[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static bool OddParity(byte b)
        {
            return (System.Numerics.BitOperations.PopCount(b) & 1) == 1;
        }
    }
}
