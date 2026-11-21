using System;

namespace SDRSharp.InmarsatAero
{
    // Aircraft seen on the P channels: AES ID (ICAO address), last event, registration learned from ACARS.
    // Fed on the decoder worker (one call per good signal unit / message, no allocation); read by the panel under
    // the same lock. Signal unit types with the AES ID in octets 1-3 and the GES ID in octet 4 (as user data, 0x71;
    // the published MH370 logs show 0x62 laid out the same way): 0x11 log-on confirm, 0x15 log-on/log-off acknowledge,
    // 0x51 T channel assignment, 0x53 reservation forthcoming, 0x61 request for acknowledge, 0x62 acknowledge.
    public sealed class AircraftTracker
    {
        public const int Capacity = 512;
        public const long ExpiryMs = 3600000; // aircraft not heard for this long drop off

        public const byte EventLogOnConfirm = 0x11;
        public const byte EventLogOn = 0x15;
        public const byte EventTAssignment = 0x51;
        public const byte EventReservation = 0x53;
        public const byte EventRequestAck = 0x61;
        public const byte EventAcknowledge = 0x62;
        public const byte EventUserData = 0x71;

        public sealed class Entry
        {
            public int Aes;
            public int Ges;
            public readonly byte[] Registration = new byte[AcarsMessage.RegistrationCapacity];
            public int RegistrationLength;
            public long LastSeen; // Environment.TickCount64
            public long LastUtcTicks;
            public byte Event;
            public bool Acars; // Event 0x71 carried an ACARS message: Label0 / Label1 / Block
            public byte Label0;
            public byte Label1;
            public byte Block;
            public long Frequency;
            public int Units;
            public bool Used;
            public long AssignedFrequency; // Hz, from the P/R channel control after its log-on confirm (0 = none)
            public int AssignedRate;

            public void CopyTo(Entry other)
            {
                other.Aes = Aes;
                other.Ges = Ges;
                Array.Copy(Registration, other.Registration, Registration.Length);
                other.RegistrationLength = RegistrationLength;
                other.LastSeen = LastSeen;
                other.LastUtcTicks = LastUtcTicks;
                other.Event = Event;
                other.Acars = Acars;
                other.Label0 = Label0;
                other.Label1 = Label1;
                other.Block = Block;
                other.Frequency = Frequency;
                other.Units = Units;
                other.Used = Used;
                other.AssignedFrequency = AssignedFrequency;
                other.AssignedRate = AssignedRate;
            }
        }

        private readonly object _lock = new object();
        private readonly Entry[] _entries = new Entry[Capacity];
        private readonly int[] _otherTypes = new int[256];
        private int _version;

        public AircraftTracker()
        {
            for (var i = 0; i < Capacity; i++)
            {
                _entries[i] = new Entry();
            }
        }

        // Changes on every update (wraps)
        public int Version => _version;

        public static bool IsAddressed(byte type)
        {
            return type == EventLogOnConfirm || type == EventLogOn || type == EventTAssignment || type == EventReservation
                || type == EventRequestAck || type == EventAcknowledge || type == EventUserData;
        }

        public static string EventName(byte type)
        {
            switch (type)
            {
                case EventLogOnConfirm:
                    return "log-on confirm";

                case EventLogOn:
                    return "log-on/off ack";

                case EventTAssignment:
                    return "T channel assignment";

                case EventReservation:
                    return "reservation forthcoming";

                case EventRequestAck:
                    return "request for ack";

                case EventAcknowledge:
                    return "acknowledge";

                case EventUserData:
                    return "user data";

                default:
                    return "0x" + type.ToString("X2");
            }
        }

        // Worker: a good signal unit (12 bytes at su[offset])
        public void Unit(byte[] su, int offset, long frequency)
        {
            var type = su[offset];
            if (type >= 0xC0)
            {
                return; // subsequent signal unit: no address
            }
            lock (_lock)
            {
                if (!IsAddressed(type))
                {
                    if (_otherTypes[type] < int.MaxValue)
                    {
                        _otherTypes[type]++;
                    }
                    return;
                }
                var aes = (su[offset + 1] << 16) | (su[offset + 2] << 8) | su[offset + 3];
                if (aes == 0 || aes == 0xFFFFFF)
                {
                    return;
                }
                var e = FindOrAdd(aes);
                e.Ges = su[offset + 4];
                if (type != EventUserData || !e.Acars)
                {
                    e.Event = type;
                    e.Acars = false;
                }
                Touch(e, frequency);
                if (e.Units < int.MaxValue)
                {
                    e.Units++;
                }
            }
        }

        // Worker: a decoded message (ACARS or not)
        public void Message(AcarsMessage m)
        {
            if (m.AESID == 0)
            {
                return;
            }
            lock (_lock)
            {
                var e = FindOrAdd(m.AESID);
                e.Ges = m.GESID;
                e.Event = EventUserData;
                e.Acars = !m.NonAcars;
                if (!m.NonAcars)
                {
                    e.Label0 = m.Label0;
                    e.Label1 = m.Label1;
                    e.Block = m.BlockId;
                    if (m.RegistrationLength > 0)
                    {
                        Array.Copy(m.Registration, e.Registration, m.RegistrationLength);
                        e.RegistrationLength = m.RegistrationLength;
                    }
                }
                Touch(e, m.Frequency);
            }
        }

        // Worker (from SystemTable): the P channel a log-on confirm assigned to this aircraft
        public void Assign(int aes, long frequency, int rate)
        {
            lock (_lock)
            {
                var e = FindOrAdd(aes);
                e.AssignedFrequency = frequency;
                e.AssignedRate = rate;
                _version = _version < int.MaxValue ? _version + 1 : 0;
            }
        }

        // Panel: copies the aircraft heard within ExpiryMs into rows (at least Capacity long), newest first
        public int Copy(Entry[] rows)
        {
            var now = Environment.TickCount64;
            var n = 0;
            lock (_lock)
            {
                for (var i = 0; i < Capacity; i++)
                {
                    var e = _entries[i];
                    if (e.Used && now - e.LastSeen <= ExpiryMs)
                    {
                        e.CopyTo(rows[n]);
                        n++;
                    }
                }
            }
            // newest first (insertion sort on the panel's copy)
            for (var i = 1; i < n; i++)
            {
                var v = rows[i];
                var j = i;
                while (j > 0 && rows[j - 1].LastSeen < v.LastSeen)
                {
                    rows[j] = rows[j - 1];
                    j--;
                }
                rows[j] = v;
            }
            return n;
        }

        // Panel: counts of signal unit types that carry no aircraft address (index = type)
        public void CopyOtherTypes(int[] counts)
        {
            lock (_lock)
            {
                Array.Copy(_otherTypes, counts, 256);
            }
        }

        private Entry FindOrAdd(int aes)
        {
            Entry free = null;
            Entry oldest = null;
            for (var i = 0; i < Capacity; i++)
            {
                var e = _entries[i];
                if (e.Used)
                {
                    if (e.Aes == aes)
                    {
                        return e;
                    }
                    if (oldest == null || e.LastSeen < oldest.LastSeen)
                    {
                        oldest = e;
                    }
                }
                else if (free == null)
                {
                    free = e;
                }
            }
            var slot = free ?? oldest;
            slot.Used = true;
            slot.Aes = aes;
            slot.RegistrationLength = 0;
            slot.Acars = false;
            slot.Units = 0;
            slot.AssignedFrequency = 0;
            slot.AssignedRate = 0;
            return slot;
        }

        private void Touch(Entry e, long frequency)
        {
            e.LastSeen = Environment.TickCount64;
            e.LastUtcTicks = DateTime.UtcNow.Ticks;
            e.Frequency = frequency;
            _version = _version < int.MaxValue ? _version + 1 : 0;
        }
    }
}
