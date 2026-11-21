using System;
using System.Threading;

namespace SDRSharp.InmarsatAero
{
    // Decoder worker only: owns the manual channels (from the panel's list) and the auto channels (from the carrier scan).
    // Changes are incremental, so adding or removing one channel leaves the others tracking. The scan names each
    // carrier's rate and frequency from its line pair, so an auto channel starts at that rate (the probe path, which
    // scores 600 against 1200 and 8400 against 10500 itself, is kept for a channel started without a known rate).
    // No frame sync within TrialTime: ignored for BlockTime. An auto channel without sync for DropTime is dropped. The channel array for the panel is published by one assignment.
    public sealed class ChannelManager : IDisposable
    {
        private const int MaxAuto = 24;
        private const int MaxBlocked = 64;
        private const long ProbeTime = 15000; // ms to find an Aero line pair
        private const long TrialTime = 30000; // ms from the line pair to frame sync
        private const long VoiceBlockTime = 3600000; // ms a C channel (8400 line pair) is ignored
        private const long DropTime = 120000; // ms without frame sync before an auto channel is dropped
        private const long BlockTime = 600000; // ms a carrier that synced at no rate is ignored
        private const long RejectTime = 3600000; // ms a carrier removed from the panel is ignored
        private const long NarrowTolerance = 1500; // Hz: a scan result this close to a 600 / 1200 channel is that channel
        private const long WideTolerance = 6000; // Hz: same, when either side is 10500
        private const long SettleTime = 5000; // ms of frame sync before an auto channel is fine-tuned
        private const long RetuneInterval = 10000; // ms between fine-tunes of one channel
        private const double RetuneThreshold = 15; // Hz of error before a fine-tune

        private const int VoiceRate = 8400;
        private static readonly int[] NarrowProbe = { 600 };
        private static readonly int[] WideProbe = { VoiceRate };

        private readonly IAcarsSink _sink;
        private readonly int _blockLength;
        private readonly AutoEntry[] _auto = new AutoEntry[MaxAuto];
        private readonly long[] _blockedFrequency = new long[MaxBlocked];
        private readonly long[] _blockedUntil = new long[MaxBlocked];
        private AeroChannel[] _manual = new AeroChannel[0];
        private int _autoCount;
        private double _sampleRate;
        private volatile AeroChannel[] _published = new AeroChannel[0];
        private volatile int _version;
        private readonly float[] _offsets = new float[InmarsatAeroProcessor.MaxChannels];
        private double _receiverOffset;
        private int _receiverCount;

        public ChannelManager(IAcarsSink sink, int blockLength)
        {
            _sink = sink;
            _blockLength = blockLength;
        }

        // Manual channels first, then auto channels
        public AeroChannel[] Channels => _published;

        // Changes whenever the channel set changes (wraps)
        public int Version => _version;

        public int AutoCount => _autoCount;

        // Median carrier offset of the manual channels in frame sync = receiver tuning error (Hz); 0 when none
        public double ReceiverOffset => Volatile.Read(ref _receiverOffset);

        // Manual channels in frame sync behind ReceiverOffset
        public int ReceiverCount => _receiverCount;

        // Input rate changed: every channel rebuilds its DSP (keeps the channel objects)
        public void Configure(double sampleRate)
        {
            _sampleRate = sampleRate;
            for (var i = 0; i < _manual.Length; i++)
            {
                _manual[i].Configure(sampleRate, _blockLength);
            }
            for (var i = 0; i < _autoCount; i++)
            {
                _auto[i].Channel.Configure(sampleRate, _blockLength);
            }
        }

        // The panel's list changed: keep matching channels, create the new ones, dispose the removed ones
        public void SetManual(ChannelSpec[] specs)
        {
            var old = _manual;
            var manual = new AeroChannel[specs.Length];
            for (var i = 0; i < specs.Length; i++)
            {
                AeroChannel found = null;
                for (var k = 0; k < old.Length; k++)
                {
                    if (old[k] != null && old[k].Spec.Frequency == specs[i].Frequency && old[k].Spec.BitRate == specs[i].BitRate)
                    {
                        found = old[k];
                        old[k] = null;
                        break;
                    }
                }
                if (found == null)
                {
                    found = new AeroChannel(specs[i], _sink);
                    found.Configure(_sampleRate, _blockLength);
                }
                manual[i] = found;
            }
            for (var k = 0; k < old.Length; k++)
            {
                if (old[k] != null)
                {
                    old[k].Dispose();
                }
            }
            _manual = manual;

            // a manual channel replaces an auto channel on the same carrier
            for (var i = _autoCount - 1; i >= 0; i--)
            {
                if (NearManual(_auto[i].Frequency, _auto[i].Channel.Spec.BitRate))
                {
                    RemoveAutoAt(i);
                }
            }
            Publish();
        }

        // A scan completed: start an auto channel for each new carrier
        public void OnScan(CarrierScanner scanner, long rawCenter, long now)
        {
            var changed = false;
            for (var c = 0; c < scanner.Count; c++)
            {
                // scan position minus the receiver error = nominal frequency, same convention as the manual list
                var frequency = (long)Math.Round(rawCenter + scanner.Offset(c) - _receiverOffset);
                var rate = scanner.Rate(c);
                if (NearManual(frequency, rate) || NearAuto(frequency, rate) || IsBlocked(frequency, rate, now))
                {
                    continue;
                }
                if (_autoCount >= MaxAuto || _manual.Length + _autoCount >= InmarsatAeroProcessor.MaxChannels)
                {
                    break;
                }
                var entry = new AutoEntry();
                entry.Frequency = frequency;
                entry.TrialStart = now;
                entry.Detected = true; // the scan's line pair named the rate
                entry.LastSync = 0;
                entry.SyncSince = 0;
                entry.LastRetune = 0;
                entry.Channel = NewAutoChannel(frequency, rate, null);
                _auto[_autoCount] = entry;
                _autoCount++;
                changed = true;
            }
            if (changed)
            {
                Publish();
            }
        }

        // Every block: frame sync bookkeeping, rate trials, drops
        public void Supervise(long now)
        {
            UpdateReceiverOffset();
            var changed = false;
            for (var i = _autoCount - 1; i >= 0; i--)
            {
                var entry = _auto[i];
                if (entry.Channel.State == ChannelState.FrameSync)
                {
                    entry.LastSync = now;
                    if (entry.SyncSince == 0)
                    {
                        entry.SyncSince = now;
                    }
                    // put the carrier at the receiver error, like the manual channels: TunedFrequency is then nominal
                    if (now - entry.SyncSince >= SettleTime && now - entry.LastRetune >= RetuneInterval
                        && Math.Abs(entry.Channel.CarrierOffset - _receiverOffset) > RetuneThreshold
                        && entry.Channel.Recentre(_receiverOffset))
                    {
                        entry.LastRetune = now;
                        entry.Frequency = entry.Channel.TunedFrequency;
                    }
                    continue;
                }
                entry.SyncSince = 0;
                if (entry.LastSync == 0)
                {
                    var detected = entry.Channel.DetectedRate;
                    if (!entry.Detected && detected != 0)
                    {
                        if (detected == VoiceRate)
                        {
                            Block(entry.Frequency, now + VoiceBlockTime);
                            RemoveAutoAt(i);
                            changed = true;
                            continue;
                        }
                        entry.Detected = true;
                        entry.TrialStart = now;
                        if (detected != entry.Channel.Spec.BitRate)
                        {
                            // restart at the rate the line pair named, on the carrier it measured
                            var carrier = entry.Channel.TunedFrequency + (long)Math.Round(entry.Channel.DetectedOffset - _receiverOffset);
                            entry.Frequency = (carrier + 5) / 10 * 10;
                            entry.Channel.Dispose();
                            entry.Channel = NewAutoChannel(entry.Frequency, detected, null);
                            changed = true;
                        }
                        continue;
                    }
                    if (now - entry.TrialStart >= (entry.Detected ? TrialTime : ProbeTime))
                    {
                        Block(entry.Frequency, now + BlockTime);
                        RemoveAutoAt(i);
                        changed = true;
                    }
                }
                else if (now - entry.LastSync > DropTime)
                {
                    RemoveAutoAt(i);
                    changed = true;
                }
            }
            if (changed)
            {
                Publish();
            }
        }

        // Removed from the panel (an auto row, or a manual channel): drop any auto channel on it and keep the scan off
        // that carrier for RejectTime
        public void RejectAuto(long frequency, long now)
        {
            for (var i = _autoCount - 1; i >= 0; i--)
            {
                if (_auto[i].Channel.Spec.Frequency == frequency || _auto[i].Frequency == frequency)
                {
                    Block(_auto[i].Frequency, now + RejectTime);
                    RemoveAutoAt(i);
                    Publish();
                    return;
                }
            }
            Block(frequency, now + RejectTime);
        }

        public void ClearAuto()
        {
            if (_autoCount == 0)
            {
                return;
            }
            for (var i = _autoCount - 1; i >= 0; i--)
            {
                RemoveAutoAt(i);
            }
            Publish();
        }

        public void Dispose()
        {
            for (var i = 0; i < _manual.Length; i++)
            {
                _manual[i].Dispose();
            }
            _manual = new AeroChannel[0];
            for (var i = _autoCount - 1; i >= 0; i--)
            {
                _auto[i].Channel.Dispose();
                _auto[i] = null;
            }
            _autoCount = 0;
            _published = new AeroChannel[0];
        }

        private void UpdateReceiverOffset()
        {
            var n = 0;
            for (var i = 0; i < _manual.Length && n < _offsets.Length; i++)
            {
                if (_manual[i].State == ChannelState.FrameSync)
                {
                    // insertion sort into the fixed buffer
                    var v = _manual[i].CarrierOffset;
                    var k = n;
                    while (k > 0 && _offsets[k - 1] > v)
                    {
                        _offsets[k] = _offsets[k - 1];
                        k--;
                    }
                    _offsets[k] = v;
                    n++;
                }
            }
            if (n > 0)
            {
                var median = (n & 1) == 1 ? _offsets[n / 2] : 0.5 * (_offsets[n / 2 - 1] + _offsets[n / 2]);
                Volatile.Write(ref _receiverOffset, median);
            }
            _receiverCount = n; // with none in sync the last estimate is kept
        }

        private AeroChannel NewAutoChannel(long frequency, int bitRate, int[] probe)
        {
            var channel = new AeroChannel(new ChannelSpec(frequency, bitRate, true), _sink);
            channel.SetProbe(probe);
            channel.Configure(_sampleRate, _blockLength);
            return channel;
        }

        private void RemoveAutoAt(int index)
        {
            _auto[index].Channel.Dispose();
            for (var k = index; k < _autoCount - 1; k++)
            {
                _auto[k] = _auto[k + 1];
            }
            _autoCount--;
            _auto[_autoCount] = null;
        }

        private static long Tolerance(int rateA, int rateB)
        {
            return rateA == 10500 || rateB == 10500 ? WideTolerance : NarrowTolerance;
        }

        private bool NearManual(long frequency, int rate)
        {
            for (var i = 0; i < _manual.Length; i++)
            {
                var spec = _manual[i].Spec;
                if (Math.Abs(spec.Frequency - frequency) < Tolerance(rate, spec.BitRate))
                {
                    return true;
                }
            }
            return false;
        }

        private bool NearAuto(long frequency, int rate)
        {
            for (var i = 0; i < _autoCount; i++)
            {
                if (Math.Abs(_auto[i].Frequency - frequency) < Tolerance(rate, _auto[i].Channel.Spec.BitRate))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsBlocked(long frequency, int rate, long now)
        {
            for (var i = 0; i < MaxBlocked; i++)
            {
                if (_blockedUntil[i] > now && Math.Abs(_blockedFrequency[i] - frequency) < Tolerance(rate, rate))
                {
                    return true;
                }
            }
            return false;
        }

        // reuses an expired slot, else the one expiring soonest
        private void Block(long frequency, long until)
        {
            var slot = 0;
            for (var i = 0; i < MaxBlocked; i++)
            {
                if (_blockedUntil[i] < _blockedUntil[slot])
                {
                    slot = i;
                }
            }
            _blockedFrequency[slot] = frequency;
            _blockedUntil[slot] = until;
        }

        private void Publish()
        {
            var channels = new AeroChannel[_manual.Length + _autoCount];
            for (var i = 0; i < _manual.Length; i++)
            {
                channels[i] = _manual[i];
            }
            for (var i = 0; i < _autoCount; i++)
            {
                channels[_manual.Length + i] = _auto[i].Channel;
            }
            _published = channels;
            _version = _version < int.MaxValue ? _version + 1 : 0;
        }

        private sealed class AutoEntry
        {
            public AeroChannel Channel { get; set; }

            public long Frequency { get; set; }

            // a line pair was found (the rate is settled)
            public bool Detected { get; set; }

            public long TrialStart { get; set; }

            public long LastSync { get; set; }

            public long SyncSince { get; set; }

            public long LastRetune { get; set; }
        }
    }
}
