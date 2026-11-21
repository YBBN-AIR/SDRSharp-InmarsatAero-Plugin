using System;
using System.Text;

namespace SDRSharp.InmarsatAero
{
    // System information the satellites broadcast on the P channels (mostly the Psmc log-on channels), following
    // JAERO's decoding (MIT, see THIRD-PARTY-NOTICES.txt); octet N below is su[offset + N - 1], octet 1 = type.
    //   0x0C satellite identification: satellite ID = (octet3 & 0x03) << 4 | octet4 >> 4, sequence = octet3 >> 2,
    //        longitude = octet6 x 1.5 deg (> 180 = West), Psmc1 / Psmc2 = octets 7-8 / 9-10 (bit 15 = spot beam)
    //   0x05 GES Psmc and Rsmc channels: GES = octet4, three 16-bit channels in octets 5-10; octet3 & 0x03 says
    //        which: 0-1 Psmc (RX) + Rsmc0-1, 2 Rsmc2-4, 3 Rsmc5-7 (Rsmc are aircraft transmit, +101.5 MHz)
    //   0x40 P/R channel control: GES = octet5, bit rate code = octet8 >> 4, Pd = octets 9-10 (bit 15 = spot beam)
    //   channel numbers: 1510 MHz + channel x 2.5 kHz
    // A 0x40 that directly follows a 0x11 log-on confirm on the same channel is that aircraft's assigned P channel.
    // Fed on the decoder worker; read by the panel under the same lock.
    public sealed class SystemTable
    {
        private const int MaxSatellites = 64;
        private const int MaxAssignments = 128;
        private const int MaxChannels = 64; // channels whose previous unit is remembered for the 0x11 -> 0x40 pairing
        private const double Raster = 0.0025; // MHz per channel number
        private const double Base = 1510.0; // MHz
        private const double TransmitOffset = 101.5; // MHz, Rsmc

        private readonly object _lock = new object();
        private readonly AircraftTracker _aircraft;

        private readonly bool[] _satUsed = new bool[MaxSatellites];
        private readonly double[] _satLongitude = new double[MaxSatellites];
        private readonly double[] _satPsmc1 = new double[MaxSatellites];
        private readonly double[] _satPsmc2 = new double[MaxSatellites];
        private readonly bool[] _satSpot1 = new bool[MaxSatellites];
        private readonly bool[] _satSpot2 = new bool[MaxSatellites];
        private readonly int[] _satSequence = new int[MaxSatellites];
        private readonly long[] _satHeardOn = new long[MaxSatellites];
        private readonly long[] _satUtc = new long[MaxSatellites];

        private readonly bool[] _gesUsed = new bool[256];
        private readonly double[] _gesPsmc = new double[256];
        private readonly double[,] _gesRsmc = new double[256, 8];
        private readonly long[] _gesUtc = new long[256];

        private readonly int[] _asgGes = new int[MaxAssignments];
        private readonly long[] _asgFrequency = new long[MaxAssignments]; // Hz
        private readonly int[] _asgRate = new int[MaxAssignments];
        private readonly bool[] _asgSpot = new bool[MaxAssignments];
        private readonly int[] _asgCount = new int[MaxAssignments];
        private readonly int[] _asgLogOns = new int[MaxAssignments];
        private readonly long[] _asgUtc = new long[MaxAssignments];
        private int _asgUsed;

        private readonly long[] _chanFrequency = new long[MaxChannels];
        private readonly byte[] _chanLastType = new byte[MaxChannels];
        private readonly int[] _chanLastAes = new int[MaxChannels];
        private int _chanUsed;

        private int _version;

        public SystemTable(AircraftTracker aircraft)
        {
            _aircraft = aircraft;
        }

        // Changes on every update (wraps)
        public int Version => _version;

        public static int RateFromCode(int code)
        {
            switch (code)
            {
                case 0: return 600;
                case 1: return 1200;
                case 2: return 2400;
                case 3: return 4800;
                case 4: return 6000;
                case 5: return 5250;
                case 6: return 10500;
                case 7: return 8400;
                case 9: return 21000;
                default: return 0;
            }
        }

        // Worker: every good signal unit, with the frequency of the channel it came on
        public void Unit(byte[] su, int offset, long frequency)
        {
            var type = su[offset];
            lock (_lock)
            {
                var chan = ChannelSlot(frequency);
                var previousType = _chanLastType[chan];
                var previousAes = _chanLastAes[chan];
                _chanLastType[chan] = type;
                _chanLastAes[chan] = (su[offset + 1] << 16) | (su[offset + 2] << 8) | su[offset + 3];

                switch (type)
                {
                    case 0x0C:
                        Satellite(su, offset, frequency);
                        break;

                    case 0x05:
                        Ges(su, offset);
                        break;

                    case 0x40:
                        Assignment(su, offset, previousType == AircraftTracker.EventLogOnConfirm ? previousAes : 0);
                        break;

                    default:
                        return;
                }
                _version = _version < int.MaxValue ? _version + 1 : 0;
            }
        }

        private void Satellite(byte[] su, int o, long frequency)
        {
            var b3 = su[o + 2];
            var b4 = su[o + 3];
            var id = ((b3 & 0x03) << 4) | (b4 >> 4);
            var ch1 = ((su[o + 6] & 0x7F) << 8) | su[o + 7];
            var ch2 = ((su[o + 8] & 0x7F) << 8) | su[o + 9];
            _satUsed[id] = true;
            _satSequence[id] = b3 >> 2;
            _satLongitude[id] = su[o + 5] * 1.5;
            _satPsmc1[id] = Base + ch1 * Raster;
            _satPsmc2[id] = ch2 != 0 ? Base + ch2 * Raster : 0;
            _satSpot1[id] = (su[o + 6] & 0x80) != 0;
            _satSpot2[id] = (su[o + 8] & 0x80) != 0;
            _satHeardOn[id] = frequency;
            _satUtc[id] = DateTime.UtcNow.Ticks;
        }

        private void Ges(byte[] su, int o)
        {
            var ges = su[o + 3];
            var part = su[o + 2] & 0x03;
            var f1 = Base + ((su[o + 4] << 8) | su[o + 5]) * Raster;
            var f2 = Base + ((su[o + 6] << 8) | su[o + 7]) * Raster + TransmitOffset;
            var f3 = Base + ((su[o + 8] << 8) | su[o + 9]) * Raster + TransmitOffset;
            _gesUsed[ges] = true;
            if (part <= 1)
            {
                _gesPsmc[ges] = f1;
                _gesRsmc[ges, 0] = f2;
                _gesRsmc[ges, 1] = f3;
            }
            else
            {
                var first = part == 2 ? 2 : 5;
                _gesRsmc[ges, first] = f1 + TransmitOffset;
                _gesRsmc[ges, first + 1] = f2;
                _gesRsmc[ges, first + 2] = f3;
            }
            _gesUtc[ges] = DateTime.UtcNow.Ticks;
        }

        private void Assignment(byte[] su, int o, int logOnAes)
        {
            var ges = su[o + 4];
            var rate = RateFromCode(su[o + 7] >> 4);
            var channel = ((su[o + 8] & 0x7F) << 8) | su[o + 9];
            var spot = (su[o + 8] & 0x80) != 0;
            var hz = (long)Math.Round((Base + channel * Raster) * 1e6);

            var slot = -1;
            for (var i = 0; i < _asgUsed; i++)
            {
                if (_asgGes[i] == ges && _asgFrequency[i] == hz && _asgRate[i] == rate)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                slot = _asgUsed < MaxAssignments ? _asgUsed++ : Oldest();
                _asgGes[slot] = ges;
                _asgFrequency[slot] = hz;
                _asgRate[slot] = rate;
                _asgCount[slot] = 0;
                _asgLogOns[slot] = 0;
            }
            _asgSpot[slot] = spot;
            _asgCount[slot]++;
            _asgUtc[slot] = DateTime.UtcNow.Ticks;
            if (logOnAes != 0 && logOnAes != 0xFFFFFF)
            {
                _asgLogOns[slot]++;
                _aircraft.Assign(logOnAes, hz, rate);
            }
        }

        private int Oldest()
        {
            var oldest = 0;
            for (var i = 1; i < MaxAssignments; i++)
            {
                if (_asgUtc[i] < _asgUtc[oldest])
                {
                    oldest = i;
                }
            }
            return oldest;
        }

        private int ChannelSlot(long frequency)
        {
            for (var i = 0; i < _chanUsed; i++)
            {
                if (_chanFrequency[i] == frequency)
                {
                    return i;
                }
            }
            var slot = _chanUsed < MaxChannels ? _chanUsed++ : 0;
            _chanFrequency[slot] = frequency;
            _chanLastType[slot] = 0;
            _chanLastAes[slot] = 0;
            return slot;
        }

        // Panel: the tables as fixed-width text
        public string Report()
        {
            var t = new StringBuilder();
            lock (_lock)
            {
                t.Append("SATELLITES (system table, 0x0C)\r\n");
                t.Append("ID  Longitude  Psmc1 MHz          Psmc2 MHz          Seq  Heard on      Last\r\n");
                var any = false;
                for (var id = 0; id < MaxSatellites; id++)
                {
                    if (!_satUsed[id])
                    {
                        continue;
                    }
                    any = true;
                    var lon = _satLongitude[id];
                    var lonText = lon > 180.0 ? (360.0 - lon).ToString("0.0") + "W" : lon.ToString("0.0") + "E";
                    t.Append(id.ToString().PadRight(4)).Append(lonText.PadRight(11))
                        .Append((_satPsmc1[id].ToString("0.0000") + (_satSpot1[id] ? " spot" : "")).PadRight(19))
                        .Append((_satPsmc2[id] > 0 ? _satPsmc2[id].ToString("0.0000") + (_satSpot2[id] ? " spot" : "") : "-").PadRight(19))
                        .Append(_satSequence[id].ToString().PadRight(5))
                        .Append((_satHeardOn[id] / 1e6).ToString("0.0000").PadRight(14))
                        .Append(Local(_satUtc[id])).Append("\r\n");
                }
                if (!any)
                {
                    t.Append("(none yet: the satellite table is broadcast on the Psmc log-on channels)\r\n");
                }

                t.Append("\r\nGROUND EARTH STATIONS (0x05)\r\n");
                t.Append("GES  Psmc RX MHz  Rsmc TX MHz                                                   Last\r\n");
                any = false;
                for (var g = 0; g < 256; g++)
                {
                    if (!_gesUsed[g])
                    {
                        continue;
                    }
                    any = true;
                    t.Append(g.ToString("X2").PadRight(5)).Append((_gesPsmc[g] > 0 ? _gesPsmc[g].ToString("0.0000") : "-").PadRight(13));
                    var rsmc = new StringBuilder();
                    for (var r = 0; r < 8; r++)
                    {
                        if (_gesRsmc[g, r] > 0)
                        {
                            rsmc.Append(r).Append(':').Append(_gesRsmc[g, r].ToString("0.0000")).Append(' ');
                        }
                    }
                    t.Append(rsmc.ToString().PadRight(62)).Append(Local(_gesUtc[g])).Append("\r\n");
                }
                if (!any)
                {
                    t.Append("(none yet)\r\n");
                }

                t.Append("\r\nP CHANNEL ASSIGNMENTS (P/R channel control, 0x40)\r\n");
                t.Append("GES  Pd MHz     Rate    Spot  Seen   Log-ons  Last\r\n");
                if (_asgUsed == 0)
                {
                    t.Append("(none yet)\r\n");
                }
                for (var i = 0; i < _asgUsed; i++)
                {
                    t.Append(_asgGes[i].ToString("X2").PadRight(5))
                        .Append((_asgFrequency[i] / 1e6).ToString("0.000").PadRight(11))
                        .Append((_asgRate[i] > 0 ? _asgRate[i].ToString() : "?").PadRight(8))
                        .Append((_asgSpot[i] ? "yes" : "").PadRight(6))
                        .Append(_asgCount[i].ToString().PadRight(7))
                        .Append(_asgLogOns[i].ToString().PadRight(9))
                        .Append(Local(_asgUtc[i])).Append("\r\n");
                }
            }
            return t.ToString();
        }

        private static string Local(long utcTicks)
        {
            return new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss");
        }
    }
}
