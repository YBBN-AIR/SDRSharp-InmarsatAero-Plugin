using System;

namespace SDRSharp.InmarsatAero
{
    // C channel (8400 bps voice) carriers seen by the scan: not decoded, only logged. Worker thread feeds it after
    // each scan compile; the panel reads the event ring by sequence number (like the message ring) and the totals.
    // A carrier is "started" when the scan first finds it and "ended" when the scan has not found it for EndTime.
    // The scan's average fades over ~17 s, so a strong carrier keeps being found for a while after it stops:
    // durations run long by up to about a minute for strong calls.
    public sealed class VoiceLog
    {
        public const int EventSlots = 64;
        public const int SequenceModulus = 1 << 30;
        public const byte Started = 1;
        public const byte Ended = 2;

        private const int MaxActive = 32;
        private const int MaxFrequencies = 64;
        private const long EndTime = 10000; // ms not found before a carrier counts as ended
        private const long Tolerance = 100; // Hz: the same carrier

        public sealed class Event
        {
            public int Sequence = -1;
            public byte Kind;
            public long Frequency;
            public long UtcTicks;
            public int Seconds; // Ended: how long it was found
            public int FrequencyCount; // calls seen on this frequency so far
            public int TotalCount; // calls seen on all frequencies so far
            public float Score;
        }

        private readonly long[] _activeFrequency = new long[MaxActive];
        private readonly long[] _activeFirst = new long[MaxActive];
        private readonly long[] _activeLast = new long[MaxActive];
        private readonly float[] _activePeak = new float[MaxActive];
        private int _activeCount;
        private readonly long[] _knownFrequency = new long[MaxFrequencies];
        private readonly int[] _knownCount = new int[MaxFrequencies];
        private int _knownUsed;
        private readonly Event[] _events = new Event[EventSlots];
        private int _nextSequence;
        private volatile int _latest = -1;
        private volatile int _total;
        private volatile int _active;

        public VoiceLog()
        {
            for (var i = 0; i < EventSlots; i++)
            {
                _events[i] = new Event();
            }
        }

        // Panel: newest event sequence (-1 = none), and the slot holding a sequence
        public int Latest => _latest;

        public Event Slot(int sequence)
        {
            return _events[sequence % EventSlots];
        }

        // Calls seen since the plugin started, and carriers present now
        public int Total => _total;

        public int Active => _active;

        // Worker: after a scan compile
        public void OnScan(CarrierScanner scanner, long rawCenter, double receiverOffset, long now)
        {
            for (var v = 0; v < scanner.VoiceCount; v++)
            {
                var frequency = (long)Math.Round(rawCenter + scanner.VoiceOffset(v) - receiverOffset);
                var score = scanner.VoiceScore(v);
                var found = -1;
                for (var a = 0; a < _activeCount; a++)
                {
                    if (Math.Abs(_activeFrequency[a] - frequency) < Tolerance)
                    {
                        found = a;
                        break;
                    }
                }
                if (found >= 0)
                {
                    _activeLast[found] = now;
                    _activePeak[found] = Math.Max(_activePeak[found], score);
                    continue;
                }
                if (_activeCount >= MaxActive)
                {
                    continue;
                }
                _activeFrequency[_activeCount] = frequency;
                _activeFirst[_activeCount] = now;
                _activeLast[_activeCount] = now;
                _activePeak[_activeCount] = score;
                _activeCount++;
                _total = _total < int.MaxValue ? _total + 1 : _total;
                Publish(Started, frequency, 0, Count(frequency), score);
            }

            for (var a = _activeCount - 1; a >= 0; a--)
            {
                if (now - _activeLast[a] < EndTime)
                {
                    continue;
                }
                var seconds = (int)((_activeLast[a] - _activeFirst[a]) / 1000);
                Publish(Ended, _activeFrequency[a], seconds, CountOf(_activeFrequency[a]), _activePeak[a]);
                _activeCount--;
                _activeFrequency[a] = _activeFrequency[_activeCount];
                _activeFirst[a] = _activeFirst[_activeCount];
                _activeLast[a] = _activeLast[_activeCount];
                _activePeak[a] = _activePeak[_activeCount];
            }
            _active = _activeCount;
        }

        // adds a call on this frequency; returns its count
        private int Count(long frequency)
        {
            for (var i = 0; i < _knownUsed; i++)
            {
                if (Math.Abs(_knownFrequency[i] - frequency) < Tolerance)
                {
                    return ++_knownCount[i];
                }
            }
            var slot = _knownUsed < MaxFrequencies ? _knownUsed++ : LeastCounted();
            _knownFrequency[slot] = frequency;
            _knownCount[slot] = 1;
            return 1;
        }

        private int CountOf(long frequency)
        {
            for (var i = 0; i < _knownUsed; i++)
            {
                if (Math.Abs(_knownFrequency[i] - frequency) < Tolerance)
                {
                    return _knownCount[i];
                }
            }
            return 0;
        }

        private int LeastCounted()
        {
            var least = 0;
            for (var i = 1; i < MaxFrequencies; i++)
            {
                if (_knownCount[i] < _knownCount[least])
                {
                    least = i;
                }
            }
            return least;
        }

        private void Publish(byte kind, long frequency, int seconds, int frequencyCount, float score)
        {
            var sequence = _nextSequence;
            var e = _events[sequence % EventSlots];
            e.Kind = kind;
            e.Frequency = frequency;
            e.UtcTicks = DateTime.UtcNow.Ticks;
            e.Seconds = seconds;
            e.FrequencyCount = frequencyCount;
            e.TotalCount = _total;
            e.Score = score;
            e.Sequence = sequence;
            _latest = sequence;
            _nextSequence = sequence < SequenceModulus - 1 ? sequence + 1 : 0;
        }
    }
}
