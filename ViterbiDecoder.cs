namespace SDRSharp.InmarsatAero
{
    // K = 7, rate 1/2, polynomials 109 and 79 (shift register newest bit in the LSB, first output from 109)
    public sealed class ViterbiDecoder
    {
        private const int States = 64;
        private const int Depth = 64; // trellis steps of look-ahead before a bit is decided
        private const int HistoryLength = 128;
        private const int Polynomial0 = 109;
        private const int Polynomial1 = 79;

        private static readonly int[] Expected0 = MakeExpected(Polynomial0);
        private static readonly int[] Expected1 = MakeExpected(Polynomial1);

        private readonly int[] _metrics = new int[States];
        private readonly int[] _newMetrics = new int[States];
        private readonly ulong[] _decisions = new ulong[HistoryLength];
        private int _head;
        private int _filled;

        public void Reset()
        {
            for (var i = 0; i < States; i++)
            {
                _metrics[i] = 0;
            }
            _head = 0;
            _filled = 0;
        }

        // soft: positive = 1, magnitude = confidence; pairs = trellis steps; output: one decided bit (0 / 1) per byte, in step order
        public int Process(sbyte[] soft, int offset, int pairs, byte[] output, int outputOffset)
        {
            var metrics = _metrics;
            var newMetrics = _newMetrics;
            var decisions = _decisions;
            var head = _head;
            var filled = _filled;
            var produced = 0;

            for (var p = 0; p < pairs; p++)
            {
                int y0 = soft[offset + 2 * p];
                int y1 = soft[offset + 2 * p + 1];
                ulong decision = 0;
                var best = int.MinValue;
                var bestState = 0;

                for (var ns = 0; ns < States; ns++)
                {
                    var bit = ns & 1;
                    var ps0 = ns >> 1;
                    var ps1 = ps0 | 32;
                    var reg0 = (ps0 << 1) | bit;
                    var reg1 = (ps1 << 1) | bit;
                    var m0 = metrics[ps0] + Expected0[reg0] * y0 + Expected1[reg0] * y1;
                    var m1 = metrics[ps1] + Expected0[reg1] * y0 + Expected1[reg1] * y1;
                    int m;
                    if (m1 > m0)
                    {
                        m = m1;
                        decision |= 1UL << ns;
                    }
                    else
                    {
                        m = m0;
                    }
                    newMetrics[ns] = m;
                    if (m > best)
                    {
                        best = m;
                        bestState = ns;
                    }
                }

                for (var s = 0; s < States; s++)
                {
                    metrics[s] = newMetrics[s] - best;
                }

                decisions[head] = decision;

                if (filled < Depth)
                {
                    filled++;
                }

                if (filled == Depth)
                {
                    var state = bestState;
                    var index = head;
                    for (var k = 0; k < Depth - 1; k++)
                    {
                        var hi = (int)((decisions[index] >> state) & 1);
                        state = (state >> 1) | (hi << 5);
                        if (index > 0)
                        {
                            index--;
                        }
                        else
                        {
                            index = HistoryLength - 1;
                        }
                    }
                    output[outputOffset + produced] = (byte)(state & 1);
                    produced++;
                }

                if (head < HistoryLength - 1)
                {
                    head++;
                }
                else
                {
                    head = 0;
                }
            }

            _head = head;
            _filled = filled;
            return produced;
        }

        private static int[] MakeExpected(int polynomial)
        {
            var table = new int[2 * States];
            for (var reg = 0; reg < 2 * States; reg++)
            {
                var v = reg & polynomial;
                var parity = 0;
                while (v != 0)
                {
                    parity ^= v & 1;
                    v >>= 1;
                }
                table[reg] = parity == 1 ? 1 : -1;
            }
            return table;
        }
    }
}
