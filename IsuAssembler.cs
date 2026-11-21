namespace SDRSharp.InmarsatAero
{
    public sealed class IsuItem
    {
        public const int UserDataCapacity = 2 + 63 * 8;

        public int AESID { get; set; }

        public int GESID { get; set; }

        public int QNO { get; set; }

        public int REFNO { get; set; }

        // subsequent signal units still expected
        public int SEQNO { get; set; }

        public int OctetsInLastSSU { get; set; }

        public int Age { get; set; }

        public bool InUse { get; set; }

        public byte[] UserData { get; } = new byte[UserDataCapacity];

        public int Length { get; set; }
    }

    // Joins a user data ISU (type 0x71) with its subsequent signal units (types 0xC0 .. 0xFF)
    public sealed class IsuAssembler : ISignalUnitSink
    {
        private const int PendingCount = 16;
        private const int MaxAge = 10; // 0x71 signal units seen before an incomplete ISU is dropped
        private const byte UserDataIsu = 0x71;

        private readonly IIsuSink _sink;
        private readonly IsuItem[] _pending = new IsuItem[PendingCount];

        public IsuAssembler(IIsuSink sink)
        {
            _sink = sink;
            for (var i = 0; i < PendingCount; i++)
            {
                _pending[i] = new IsuItem();
            }
        }

        public void Reset()
        {
            for (var i = 0; i < PendingCount; i++)
            {
                _pending[i].InUse = false;
            }
        }

        public void SignalUnit(byte[] su, int offset)
        {
            var type = su[offset];
            if (type == UserDataIsu)
            {
                StartIsu(su, offset);
            }
            else if ((type & 0xC0) == 0xC0)
            {
                AddSsu(su, offset);
            }
        }

        private void StartIsu(byte[] su, int offset)
        {
            var aesid = (su[offset + 1] << 16) | (su[offset + 2] << 8) | su[offset + 3];
            var gesid = su[offset + 4];
            var qno = (su[offset + 5] >> 4) & 0x0F;
            var refno = su[offset + 5] & 0x0F;
            var seqno = su[offset + 6] & 0x3F;
            var lastOctets = (su[offset + 7] >> 4) & 0x0F;

            var slot = -1;
            var oldest = 0;
            for (var i = 0; i < PendingCount; i++)
            {
                var item = _pending[i];
                if (!item.InUse)
                {
                    continue;
                }
                if (item.Age < MaxAge)
                {
                    item.Age++;
                }
                else
                {
                    item.InUse = false;
                    continue;
                }
                if (item.AESID == aesid && item.GESID == gesid && item.QNO == qno && item.REFNO == refno)
                {
                    slot = i;
                }
            }

            if (lastOctets > 8)
            {
                return;
            }

            if (slot < 0)
            {
                for (var i = 0; i < PendingCount; i++)
                {
                    if (!_pending[i].InUse)
                    {
                        slot = i;
                        break;
                    }
                    if (_pending[i].Age > _pending[oldest].Age)
                    {
                        oldest = i;
                    }
                }
            }
            if (slot < 0)
            {
                slot = oldest;
            }

            var isu = _pending[slot];
            isu.AESID = aesid;
            isu.GESID = gesid;
            isu.QNO = qno;
            isu.REFNO = refno;
            isu.SEQNO = seqno;
            isu.OctetsInLastSSU = lastOctets;
            isu.Age = 0;
            isu.UserData[0] = su[offset + 8];
            isu.UserData[1] = su[offset + 9];
            isu.Length = 2;
            isu.InUse = seqno > 0;
        }

        private void AddSsu(byte[] su, int offset)
        {
            var seqno = su[offset] & 0x3F;
            var qno = (su[offset + 1] >> 4) & 0x0F;
            var refno = su[offset + 1] & 0x0F;

            IsuItem isu = null;
            for (var i = 0; i < PendingCount; i++)
            {
                var item = _pending[i];
                if (item.InUse && item.QNO == qno && item.REFNO == refno && item.SEQNO == seqno + 1 && (isu == null || item.Age < isu.Age))
                {
                    isu = item;
                }
            }
            if (isu == null)
            {
                return;
            }

            isu.SEQNO = seqno;
            var count = seqno == 0 ? isu.OctetsInLastSSU : 8;
            if (isu.Length + count > IsuItem.UserDataCapacity)
            {
                isu.InUse = false;
                return;
            }
            for (var k = 0; k < count; k++)
            {
                isu.UserData[isu.Length + k] = su[offset + 2 + k];
            }
            isu.Length += count;

            if (seqno == 0)
            {
                isu.InUse = false;
                _sink.Isu(isu);
            }
        }
    }
}
