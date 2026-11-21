using System;

namespace SDRSharp.InmarsatAero
{
    // JAERO "JSONdump" record (the format acarsdec / acars_router / acarshub read as IMSL), plus "freq" in MHz
    public sealed class JaeroJsonWriter
    {
        public const int BufferCapacity = 6 * AcarsMessage.TextCapacity + 1024;

        private const string AppName = "JAERO";
        private static readonly string AppVersion = "SDR# Inmarsat Aero " + PluginVersion.Text;

        private static readonly byte[] HexDigits = { (byte)'0', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6', (byte)'7', (byte)'8', (byte)'9', (byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E', (byte)'F' };

        private readonly byte[] _buffer = new byte[BufferCapacity];
        private readonly byte[] _digits = new byte[20];
        private int _position;

        public byte[] Buffer => _buffer;

        // Returns the record length in Buffer. stationId: empty = no "station"; m.Frequency 0 = no "freq"
        public int Write(AcarsMessage m, string stationId)
        {
            var frequency = m.Frequency;
            var now = DateTime.UtcNow;
            var unixTicks = now.Ticks - DateTime.UnixEpoch.Ticks;
            var seconds = unixTicks / TimeSpan.TicksPerSecond;
            var microseconds = (unixTicks % TimeSpan.TicksPerSecond) / 10 / 1000 * 1000;

            _position = 0;
            Raw("{\"app\":{\"name\":\"");
            Raw(AppName);
            Raw("\",\"ver\":\"");
            Raw(AppVersion);
            Raw("\"},\"isu\":{");
            if (!m.NonAcars)
            {
                Raw("\"acars\":{\"mode\":\"");
                Character(m.Mode);
                Raw("\",\"ack\":\"");
                Character(m.Ack == 0x15 ? (byte)'!' : m.Ack);
                Raw("\",\"blk_id\":\"");
                Character(m.BlockId);
                Raw("\",\"label\":\"");
                Character(m.Label0);
                Character(m.Label1 == 0x7F ? (byte)'d' : m.Label1);
                Raw("\",\"reg\":\"");
                for (var i = 0; i < m.RegistrationLength; i++)
                {
                    Character(m.Registration[i]);
                }
                Raw("\"");
                if (m.TextLength > 0)
                {
                    Raw(",\"msg_text\":\"");
                    for (var i = 0; i < m.TextLength; i++)
                    {
                        Character(m.Text[i]);
                    }
                    Raw("\"");
                }
                Raw("},");
            }
            Raw("\"refno\":\"");
            Hex(m.REFNO, 2);
            Raw("\",\"qno\":\"");
            Hex(m.QNO, 2);
            Raw("\",\"src\":{\"type\":\"Ground Earth Station\",\"addr\":\"");
            Hex(m.GESID, 2);
            Raw("\"},\"dst\":{\"type\":\"Aircraft Earth Station\",\"addr\":\"");
            Hex(m.AESID, 6);
            Raw("\"}},\"t\":{\"sec\":");
            Number(seconds);
            Raw(",\"usec\":");
            Number(microseconds);
            Raw("}");
            if (stationId.Length > 0)
            {
                Raw(",\"station\":\"");
                for (var i = 0; i < stationId.Length; i++)
                {
                    var c = stationId[i];
                    Character(c < 0x80 ? (byte)c : (byte)'?');
                }
                Raw("\"");
            }
            if (frequency > 0)
            {
                Raw(",\"freq\":");
                Number(frequency / 1000000);
                Raw(".");
                var fraction = (frequency % 1000000) / 100;
                _buffer[_position] = (byte)('0' + fraction / 1000);
                _buffer[_position + 1] = (byte)('0' + fraction / 100 % 10);
                _buffer[_position + 2] = (byte)('0' + fraction / 10 % 10);
                _buffer[_position + 3] = (byte)('0' + fraction % 10);
                _position += 4;
            }
            Raw(",\"level\":");
            Fixed1(m.Level);
            Raw(",\"snr\":");
            Fixed1(m.SNR);
            Raw(",\"offset\":");
            Fixed1(m.CarrierOffset);
            if (!float.IsNaN(m.EbN0))
            {
                Raw(",\"ebn0\":");
                Fixed1(m.EbN0);
                Raw(",\"margin\":");
                Fixed1(m.Margin);
            }
            if (!float.IsNaN(m.Ber))
            {
                Raw(",\"ber\":");
                Fixed4(m.Ber);
            }
            Raw("}");
            return _position;
        }

        private void Raw(string s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                _buffer[_position] = (byte)s[i];
                _position++;
            }
        }

        // ASCII character as JSON string content
        private void Character(byte c)
        {
            if (c == (byte)'"' || c == (byte)'\\')
            {
                _buffer[_position] = (byte)'\\';
                _buffer[_position + 1] = c;
                _position += 2;
            }
            else if (c == (byte)'\n')
            {
                _buffer[_position] = (byte)'\\';
                _buffer[_position + 1] = (byte)'n';
                _position += 2;
            }
            else if (c == (byte)'\r')
            {
                _buffer[_position] = (byte)'\\';
                _buffer[_position + 1] = (byte)'r';
                _position += 2;
            }
            else if (c < 0x20 || c >= 0x7F)
            {
                Raw("\\u00");
                _buffer[_position] = HexDigits[(c >> 4) & 0x0F];
                _buffer[_position + 1] = HexDigits[c & 0x0F];
                _position += 2;
            }
            else
            {
                _buffer[_position] = c;
                _position++;
            }
        }

        private void Hex(int value, int digits)
        {
            for (var i = digits - 1; i >= 0; i--)
            {
                _buffer[_position] = HexDigits[(value >> (4 * i)) & 0x0F];
                _position++;
            }
        }

        // One decimal place; non-finite values written as 0
        private void Fixed1(float value)
        {
            if (!float.IsFinite(value))
            {
                value = 0;
            }
            var tenths = (long)Math.Round(value * 10.0);
            if (tenths < 0)
            {
                _buffer[_position] = (byte)'-';
                _position++;
                tenths = -tenths;
            }
            Number(tenths / 10);
            _buffer[_position] = (byte)'.';
            _buffer[_position + 1] = (byte)('0' + tenths % 10);
            _position += 2;
        }

        // 0 <= value < 1, four decimals
        private void Fixed4(float value)
        {
            if (!float.IsFinite(value) || value < 0)
            {
                value = 0;
            }
            var units = Math.Min(9999L, (long)Math.Round(value * 10000.0));
            _buffer[_position] = (byte)'0';
            _buffer[_position + 1] = (byte)'.';
            _buffer[_position + 2] = (byte)('0' + units / 1000);
            _buffer[_position + 3] = (byte)('0' + units / 100 % 10);
            _buffer[_position + 4] = (byte)('0' + units / 10 % 10);
            _buffer[_position + 5] = (byte)('0' + units % 10);
            _position += 6;
        }

        private void Number(long value)
        {
            if (value < 0)
            {
                _buffer[_position] = (byte)'-';
                _position++;
                value = -value;
            }
            var n = 0;
            do
            {
                _digits[n] = (byte)('0' + value % 10);
                value /= 10;
                n++;
            }
            while (value != 0);
            for (var i = n - 1; i >= 0; i--)
            {
                _buffer[_position] = _digits[i];
                _position++;
            }
        }
    }
}
