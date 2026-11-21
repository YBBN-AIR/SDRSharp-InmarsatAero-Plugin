using SDRSharp.Radio;
using System;
using System.Threading;

namespace SDRSharp.InmarsatAero
{
    // RawIQ hook -> circular buffer -> decoder worker; the worker runs every channel (manual list + auto scan) on each block.
    // Results go to the panel (message ring) and to UDP (JAERO JSON).
    public unsafe sealed class InmarsatAeroProcessor : IIQProcessor, IAcarsSink, IUnitSink, IDisposable
    {
        public const int MaxChannels = 32;
        public const int MessageSlotCount = 64;
        public const int SequenceModulus = 1 << 30; // multiple of MessageSlotCount

        private const int BlockLength = 65536; // multiple of 16 (DownConverter)
        private const int SlotCount = 16;

        private readonly JaeroJsonWriter _jsonWriter = new JaeroJsonWriter();
        private readonly UdpSender _udpSender = new UdpSender();
        private readonly UdpSender _udpSender2 = new UdpSender(); // second destination: own queue and thread
        private readonly AcarsMessage[] _messageSlots = new AcarsMessage[MessageSlotCount];
        private double _sampleRate;
        private long _centerFrequency;
        private volatile bool _enabled;
        private volatile bool _rebuildNeeded = true;
        private volatile bool _gap;
        private volatile bool _hasInput;
        private volatile ChannelSpec[] _channelSpecs = new ChannelSpec[0];
        private volatile ChannelManager _manager;
        private volatile CarrierScanner _scanner;
        private volatile bool _autoScan;
        private long _rejectRequest;
        private long _scanCenter;
        private WidebandPower _wideband; // decoder worker only
        private long _widebandCenter;
        private int _widebandBlocks;
        private volatile string _stationId = string.Empty;
        private volatile bool _udpEnabled;
        private volatile bool _udp2Enabled;
        private volatile string _failure;
        private volatile int _messageSequence = -1;
        private volatile int _rebuilds;
        private ChannelSpec[] _builtSpecs;
        private ComplexCircularBuffer _ring;
        private UnsafeBuffer _block;
        private Thread _workerThread;
        private int _nextSequence;

        public InmarsatAeroProcessor()
        {
            SystemInfo = new SystemTable(Aircraft);
            for (var i = 0; i < MessageSlotCount; i++)
            {
                _messageSlots[i] = new AcarsMessage();
            }
        }

        public double SampleRate
        {
            get { return _sampleRate; }
            set
            {
                // only a real change rebuilds the channels: the host may set the same rate again (a rebuild restarts every carrier search)
                if (Interlocked.Exchange(ref _sampleRate, value) != value)
                {
                    _rebuildNeeded = true;
                }
            }
        }

        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (value)
                {
                    if (!_enabled)
                    {
                        Start();
                        _enabled = true;
                    }
                }
                else
                {
                    _enabled = false;
                    if (_workerThread != null)
                    {
                        Stop();
                    }
                }
            }
        }

        // Frequency at 0 Hz of the raw IQ, Hz = CenterFrequency + IFOffset (measured: a channel at f decoded only with this centre)
        // (written on the UI thread from PropertyChanged, read by the worker)
        public long CenterFrequency
        {
            get { return Interlocked.Read(ref _centerFrequency); }
            set { Interlocked.Exchange(ref _centerFrequency, value); }
        }

        // The channel list; a new array is published by one assignment and never changed afterwards
        public ChannelSpec[] ChannelSpecs
        {
            get { return _channelSpecs; }
            set { _channelSpecs = value == null ? new ChannelSpec[0] : value; }
        }

        // Scan the whole band and start temporary channels on Aero carriers
        public bool AutoScan
        {
            get { return _autoScan; }
            set { _autoScan = value; }
        }

        public string StationId
        {
            get { return _stationId; }
            set { _stationId = value == null ? string.Empty : value.Trim(); }
        }

        // Panel option, kept here for the plugin's settings
        public bool AutoCenter { get; set; }

        public bool UdpEnabled
        {
            get { return _udpEnabled; }
            set { _udpEnabled = value; }
        }

        public string UdpHost
        {
            get { return _udpSender.Host; }
            set { _udpSender.Host = value == null ? string.Empty : value.Trim(); }
        }

        public int UdpPort
        {
            get { return _udpSender.Port; }
            set { _udpSender.Port = value; }
        }

        // Display values, read on the panel's timer
        public string Failure => _failure;

        public bool HasInput => _hasInput;

        // channel rebuilds since enabled (saturating)
        public int Rebuilds => _rebuilds;

        private static readonly AeroChannel[] NoChannels = new AeroChannel[0];

        // The channels the worker runs: manual first, then auto (published on every change)
        public AeroChannel[] Channels
        {
            get
            {
                var manager = _manager;
                return manager != null ? manager.Channels : NoChannels;
            }
        }

        // Changes whenever Channels changes
        public int ChannelsVersion
        {
            get
            {
                var manager = _manager;
                return manager != null ? manager.Version : -1;
            }
        }

        // Receiver tuning error from the manual channels in frame sync (Hz), and how many are behind it
        public double ReceiverOffset
        {
            get
            {
                var manager = _manager;
                return manager != null ? manager.ReceiverOffset : 0;
            }
        }

        public int ReceiverCount
        {
            get
            {
                var manager = _manager;
                return manager != null ? manager.ReceiverCount : 0;
            }
        }

        // Regions the last scan skipped as IQ images
        public int ScanImages
        {
            get
            {
                var scanner = _scanner;
                return scanner != null ? scanner.LastImages : 0;
            }
        }

        // C channels (voice) seen by the last scan
        public int ScanVoice
        {
            get
            {
                var scanner = _scanner;
                return scanner != null ? scanner.LastVoice : 0;
            }
        }

        // Hz either side of the centre the scan covers
        public double ScanCoverage
        {
            get
            {
                var scanner = _scanner;
                return scanner != null ? scanner.Coverage : 0;
            }
        }

        // Carriers found by the last scan, -1 = no scan yet / scan off
        public int ScanCount
        {
            get
            {
                var scanner = _scanner;
                return scanner != null ? scanner.LastCount : -1;
            }
        }

        // Panel: drop an auto channel and ignore its carrier for a while (the worker applies it)
        public void RejectAuto(long frequency)
        {
            Interlocked.Exchange(ref _rejectRequest, frequency);
        }

        // Second UDP output (same JSON)
        public bool Udp2Enabled
        {
            get { return _udp2Enabled; }
            set { _udp2Enabled = value; }
        }

        public string Udp2Host
        {
            get { return _udpSender2.Host; }
            set { _udpSender2.Host = value == null ? string.Empty : value.Trim(); }
        }

        public int Udp2Port
        {
            get { return _udpSender2.Port; }
            set { _udpSender2.Port = value; }
        }

        public string Udp2Status => _udpSender2.Status;

        public int Udp2Sent => _udpSender2.Sent;

        public int Udp2Dropped => _udpSender2.Dropped;

        public string UdpStatus => _udpSender.Status;

        public int UdpSent => _udpSender.Sent;

        public int UdpDropped => _udpSender.Dropped;

        // -1 = no message yet; otherwise in [0, SequenceModulus), slot = sequence % MessageSlotCount
        public int MessageSequence => _messageSequence;

        public AcarsMessage GetMessageSlot(int index)
        {
            return _messageSlots[index];
        }

        public void Process(Complex* buffer, int length)
        {
            if (!_ring.Write(buffer, length, false))
            {
                _gap = true;
            }
        }

        // Decoder worker: one call per decoded message
        // Aircraft heard on the P channels, and the optional registration / type database
        public AircraftTracker Aircraft { get; } = new AircraftTracker();

        public AircraftDatabase Database { get; } = new AircraftDatabase();

        // Satellite / GES / channel-assignment broadcasts (System tab)
        public SystemTable SystemInfo { get; }

        // Aircraft tab filter (panel setting, saved by the plugin)
        public bool MilitaryOnly { get; set; }


        private string _databasePath = "";

        // Setting it starts loading the file in the background
        public string DatabasePath
        {
            get { return _databasePath; }
            set
            {
                _databasePath = value ?? "";
                Database.Load(_databasePath);
            }
        }

        public void Unit(byte[] su, int offset, long frequency)
        {
            Aircraft.Unit(su, offset, frequency);
            SystemInfo.Unit(su, offset, frequency);
        }

        public void Message(AcarsMessage message)
        {
            Aircraft.Message(message);
            var sequence = _nextSequence;
            var slot = _messageSlots[sequence % MessageSlotCount];
            slot.CopyFrom(message);
            slot.Sequence = sequence;
            slot.Created = Environment.TickCount64;
            _messageSequence = sequence;
            if (_nextSequence < SequenceModulus - 1)
            {
                _nextSequence++;
            }
            else
            {
                _nextSequence = 0;
            }

            var udp1 = _udpEnabled;
            var udp2 = _udp2Enabled && _udpSender2.Host.Length > 0;
            if (udp1 || udp2)
            {
                var length = _jsonWriter.Write(slot, _stationId);
                if (udp1)
                {
                    _udpSender.Send(_jsonWriter.Buffer, length);
                }
                if (udp2)
                {
                    _udpSender2.Send(_jsonWriter.Buffer, length);
                }
            }
        }

        public void Dispose()
        {
            Enabled = false;
            _udpSender.Dispose();
            _udpSender2.Dispose();
            if (_block != null)
            {
                _block.Dispose();
                _block = null;
            }
        }

        private void Start()
        {
            if (_workerThread != null)
            {
                Stop();
            }
            _failure = null;
            _hasInput = false;
            _rebuilds = 0;
            _rebuildNeeded = true;
            _gap = false;
            _builtSpecs = null;
            _ring = new ComplexCircularBuffer(BlockLength, SlotCount);
            _manager = new ChannelManager(this, BlockLength);
            Interlocked.Exchange(ref _rejectRequest, 0);
            if (_block == null)
            {
                _block = UnsafeBuffer.Create(BlockLength, sizeof(Complex));
            }
            _udpSender.Start();
            _udpSender2.Start();
            _workerThread = new Thread(WorkerProc);
            _workerThread.Name = "Inmarsat Aero Decoder";
            _workerThread.Start();
        }

        private void Stop()
        {
            _ring.Close();
            _workerThread.Join();
            _workerThread = null;
            _ring.Dispose();
            _udpSender.Stop();
            _udpSender2.Stop();
            var manager = _manager;
            _manager = null;
            manager.Dispose();
            DisposeScanner();
            DisposeWideband();
            _hasInput = false;
        }

        private void DisposeWideband()
        {
            var wideband = _wideband;
            _wideband = null;
            if (wideband != null)
            {
                wideband.Dispose();
            }
        }

        private void DisposeScanner()
        {
            var scanner = _scanner;
            _scanner = null;
            if (scanner != null)
            {
                scanner.Dispose();
            }
        }

        private void WorkerProc()
        {
            var ring = _ring;
            var block = _block;

            while (true)
            {
                var slot = ring.Acquire();
                if (slot == null)
                {
                    return;
                }

                // the slot goes back to the producer before the channels run
                Utils.Memcpy((Complex*)block, slot, BlockLength * sizeof(Complex));
                ring.Release();

                try
                {
                    var manager = _manager;
                    var now = Environment.TickCount64;

                    // allocation happens only in this section: input rate, list, scan option or channel set changed
                    if (_rebuildNeeded)
                    {
                        _rebuildNeeded = false;
                        _gap = false;
                        var sampleRate = Interlocked.CompareExchange(ref _sampleRate, 0, 0);
                        manager.Configure(sampleRate);
                        DisposeScanner();
                        DisposeWideband();
                        if (_rebuilds < int.MaxValue)
                        {
                            _rebuilds++;
                        }
                    }
                    var specs = _channelSpecs;
                    if (specs != _builtSpecs)
                    {
                        manager.SetManual(specs);
                        _builtSpecs = specs;
                    }
                    if (_wideband == null)
                    {
                        _wideband = new WidebandPower(Interlocked.CompareExchange(ref _sampleRate, 0, 0));
                        _widebandCenter = CenterFrequency;
                    }
                    if (_autoScan && _scanner == null)
                    {
                        _scanner = new CarrierScanner(Interlocked.CompareExchange(ref _sampleRate, 0, 0), BlockLength);
                        _scanCenter = CenterFrequency;
                    }
                    else if (!_autoScan && _scanner != null)
                    {
                        DisposeScanner();
                        manager.ClearAuto();
                    }
                    var reject = Interlocked.Exchange(ref _rejectRequest, 0);
                    if (reject != 0)
                    {
                        manager.RejectAuto(reject, now);
                    }

                    var channels = manager.Channels;
                    if (_gap)
                    {
                        _gap = false;
                        for (var i = 0; i < channels.Length; i++)
                        {
                            channels[i].Gap();
                        }
                    }

                    var centerFrequency = CenterFrequency;
                    for (var i = 0; i < channels.Length; i++)
                    {
                        channels[i].Process(block, BlockLength, centerFrequency);
                    }

                    // carrier-to-noise per channel from the shared wideband spectrum (no lock needed)
                    var wideband = _wideband;
                    if (centerFrequency != _widebandCenter)
                    {
                        _widebandCenter = centerFrequency;
                        wideband.Reset();
                    }
                    wideband.Process(block, BlockLength);
                    if (++_widebandBlocks >= 4)
                    {
                        _widebandBlocks = 0;
                        for (var i = 0; i < channels.Length; i++)
                        {
                            var channel = channels[i];
                            channel.CarrierToNoise = wideband.CarrierToNoise(channel.TunedFrequency - centerFrequency, channel.Rate);
                        }
                    }

                    var scanner = _scanner;
                    if (scanner != null)
                    {
                        if (centerFrequency != _scanCenter)
                        {
                            _scanCenter = centerFrequency;
                            scanner.Reset();
                        }
                        else if (scanner.Process(block, BlockLength))
                        {
                            manager.OnScan(scanner, centerFrequency, now);
                        }
                    }
                    manager.Supervise(now);
                    _hasInput = true;
                }
                catch (Exception ex)
                {
                    _failure = ex.Message;
                    _enabled = false;
                    return;
                }
            }
        }
    }
}
