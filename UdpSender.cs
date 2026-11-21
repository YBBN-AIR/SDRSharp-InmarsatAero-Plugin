using SDRSharp.Radio;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SDRSharp.InmarsatAero
{
    // One datagram per record. The decoder queues records in a FIFO (2-byte length, then the bytes); this worker
    // resolves the destination and sends, so a slow DNS lookup or a network error never stalls the decoder.
    public unsafe sealed class UdpSender : IDisposable
    {
        private const int MaxBacklog = 256 * 1024; // bytes queued before new records are dropped
        private const int MaxDatagram = 65507;

        private readonly FifoStream _fifo = new FifoStream(BlockMode.BlockingRead);
        private readonly byte[] _datagram = new byte[MaxDatagram];
        private readonly byte[] _header = new byte[2];
        private Thread _thread;
        private Socket _socket;
        private volatile string _host = string.Empty;
        private volatile int _port;
        private volatile bool _configChanged = true;
        private volatile string _status = "Not started";
        private volatile int _sent;
        private volatile int _dropped;

        public string Host
        {
            get { return _host; }
            set
            {
                if (_host != value)
                {
                    _host = value;
                    _configChanged = true;
                }
            }
        }

        public int Port
        {
            get { return _port; }
            set
            {
                if (_port != value)
                {
                    _port = value;
                    _configChanged = true;
                }
            }
        }

        public string Status => _status;

        // records sent / dropped since Start (saturating)
        public int Sent => _sent;

        public int Dropped => _dropped;

        public void Start()
        {
            if (_thread != null)
            {
                Stop();
            }
            _sent = 0;
            _dropped = 0;
            _configChanged = true;
            _status = "Waiting for a message";
            _fifo.Open();
            _thread = new Thread(SenderProc);
            _thread.Name = "Inmarsat Aero UDP";
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Stop()
        {
            if (_thread == null)
            {
                return;
            }
            _fifo.Close();
            _thread.Join();
            _thread = null;
            CloseSocket();
        }

        // Producer (decoder worker): never waits
        public void Send(byte[] record, int length)
        {
            if (length <= 0 || length > MaxDatagram)
            {
                return;
            }
            if (_fifo.Length + length + 2 > MaxBacklog)
            {
                if (_dropped < int.MaxValue)
                {
                    _dropped++;
                }
                return;
            }
            _header[0] = (byte)(length & 0xFF);
            _header[1] = (byte)(length >> 8);
            fixed (byte* headerPtr = _header)
            {
                _fifo.Write(headerPtr, 2);
            }
            fixed (byte* recordPtr = record)
            {
                _fifo.Write(recordPtr, length);
            }
        }

        public void Dispose()
        {
            Stop();
            _fifo.Dispose();
        }

        private void SenderProc()
        {
            var lengthBytes = new byte[2];
            while (true)
            {
                if (!ReadExactly(lengthBytes, 2))
                {
                    return;
                }
                var length = lengthBytes[0] | (lengthBytes[1] << 8);
                if (!ReadExactly(_datagram, length))
                {
                    return;
                }

                if (_configChanged)
                {
                    _configChanged = false;
                    Connect();
                }
                if (_socket == null)
                {
                    continue;
                }

                try
                {
                    _socket.Send(_datagram, 0, length, SocketFlags.None);
                    if (_sent < int.MaxValue)
                    {
                        _sent++;
                    }
                }
                catch (SocketException ex)
                {
                    _status = "Send failed: " + ex.Message;
                }
            }
        }

        private bool ReadExactly(byte[] buffer, int count)
        {
            var done = 0;
            fixed (byte* ptr = buffer)
            {
                while (done < count)
                {
                    var n = _fifo.Read(ptr, done, count - done);
                    if (n <= 0)
                    {
                        return false;
                    }
                    done += n;
                }
            }
            return true;
        }

        private void Connect()
        {
            CloseSocket();
            var host = _host;
            var port = _port;
            if (host.Length == 0 || port <= 0 || port > 65535)
            {
                _status = "No destination";
                return;
            }
            try
            {
                IPAddress address;
                if (!IPAddress.TryParse(host, out address))
                {
                    var addresses = Dns.GetHostAddresses(host);
                    address = null;
                    for (var i = 0; i < addresses.Length; i++)
                    {
                        if (addresses[i].AddressFamily == AddressFamily.InterNetwork)
                        {
                            address = addresses[i];
                            break;
                        }
                    }
                    if (address == null && addresses.Length > 0)
                    {
                        address = addresses[0];
                    }
                    if (address == null)
                    {
                        _status = "Cannot resolve " + host;
                        return;
                    }
                }
                _socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                _socket.Connect(new IPEndPoint(address, port));
                _status = "Sending to " + address + ":" + port;
            }
            catch (SocketException ex)
            {
                CloseSocket();
                _status = "Cannot reach " + host + ": " + ex.Message;
            }
        }

        private void CloseSocket()
        {
            if (_socket != null)
            {
                _socket.Dispose();
                _socket = null;
            }
        }
    }
}
