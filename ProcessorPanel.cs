using SDRSharp.Common;
using System;
using System.Text;
using System.Windows.Forms;

namespace SDRSharp.InmarsatAero
{
    public partial class ProcessorPanel : UserControl
    {
        private const int MaxLogLength = 200000; // characters kept in the message log
        private const long CenterStep = 1000; // Hz, auto centre rounding and nudge step
        private const long DcGuard = 3000; // Hz, no channel this close to the raw IQ centre
        private const int MaxNudgeSteps = 50;
        private const double UsableBandwidth = 0.45; // x input rate either side, as AeroChannel
        private static readonly int[] BitRates = { 600, 1200, 10500 };

        private InmarsatAeroProcessor _processor;
        private ISharpControl _control;
        private long _selectedFrequency = -1;
        private bool _selectedIsAuto;
        private int _shownVersion = -2;
        private readonly AircraftTracker.Entry[] _aircraft = MakeAircraftRows();
        private readonly int[] _otherTypes = new int[256];
        private int _aircraftCount;
        private int _aircraftVersion = -1;
        private long _aircraftShown;
        private int _shownSystem = -1;
        private int _shownSequence = -1;
        private long _lastMessageCreated;
        private bool _hasMessage;
        private string _centerNote;
        private bool _initializing = true;

        public ProcessorPanel(InmarsatAeroProcessor processor, ISharpControl control)
        {
            _processor = processor;
            _control = control;

            InitializeComponent();

            frequencyNumericUpDown.Value = ClampFrequency(_control.Frequency);
            rateComboBox.SelectedIndex = 0;
            stationTextBox.Text = _processor.StationId;
            udpCheckBox.Checked = _processor.UdpEnabled;
            autoCenterCheckBox.Checked = _processor.AutoCenter;
            autoScanCheckBox.Checked = _processor.AutoScan;
            databaseTextBox.Text = _processor.DatabasePath;
            militaryOnlyCheckBox.Checked = _processor.MilitaryOnly;
            hostTextBox.Text = _processor.UdpHost;
            portNumericUpDown.Value = Math.Min(Math.Max(_processor.UdpPort, (int)portNumericUpDown.Minimum), (int)portNumericUpDown.Maximum);
            udp2CheckBox.Checked = _processor.Udp2Enabled;
            udp2HostTextBox.Text = _processor.Udp2Host;
            udp2PortNumericUpDown.Value = Math.Min(Math.Max(_processor.Udp2Port, (int)udp2PortNumericUpDown.Minimum), (int)udp2PortNumericUpDown.Maximum);
            enableCheckBox.Checked = _processor.Enabled;
            RebuildList();
            _initializing = false;

            enableCheckBox_ValueChanged(null, null);
            stationTextBox_TextChanged(null, null);
            udpCheckBox_CheckedChanged(null, null);
            hostTextBox_TextChanged(null, null);
            portNumericUpDown_ValueChanged(null, null);
        }

        public void StopRefresh()
        {
            refreshTimer.Enabled = false;
        }

        private void enableCheckBox_ValueChanged(object sender, EventArgs e)
        {
            _processor.Enabled = enableCheckBox.Checked;
            refreshTimer.Enabled = enableCheckBox.Checked;
            RebuildList();
            ShowStatus();
        }

        private void vfoButton_Click(object sender, EventArgs e)
        {
            frequencyNumericUpDown.Value = ClampFrequency(_control.Frequency);
        }

        private void addButton_Click(object sender, EventArgs e)
        {
            var frequency = (long)frequencyNumericUpDown.Value;
            var bitRate = BitRates[Math.Max(0, rateComboBox.SelectedIndex)];
            var old = _processor.ChannelSpecs;
            var count = 0;
            for (var i = 0; i < old.Length; i++)
            {
                if (old[i].Frequency != frequency)
                {
                    count++;
                }
            }
            if (count >= InmarsatAeroProcessor.MaxChannels || frequency <= 0)
            {
                return;
            }

            var specs = new ChannelSpec[count + 1];
            var keys = new long[count + 1];
            var n = 0;
            for (var i = 0; i < old.Length; i++)
            {
                if (old[i].Frequency != frequency)
                {
                    specs[n] = old[i];
                    keys[n] = old[i].Frequency;
                    n++;
                }
            }
            specs[n] = new ChannelSpec(frequency, bitRate);
            keys[n] = frequency;
            Array.Sort(keys, specs);

            _processor.ChannelSpecs = specs;
            _selectedFrequency = frequency;
            RebuildList();
            AutoCenter();
        }

        private void removeButton_Click(object sender, EventArgs e)
        {
            if (_selectedFrequency < 0)
            {
                return;
            }
            if (_selectedIsAuto)
            {
                // drop the auto channel; the scan ignores that carrier for a while
                _processor.RejectAuto(_selectedFrequency);
                _selectedFrequency = -1;
                return;
            }
            var old = _processor.ChannelSpecs;
            var count = 0;
            for (var i = 0; i < old.Length; i++)
            {
                if (old[i].Frequency != _selectedFrequency)
                {
                    count++;
                }
            }
            var specs = new ChannelSpec[count];
            var n = 0;
            for (var i = 0; i < old.Length; i++)
            {
                if (old[i].Frequency != _selectedFrequency)
                {
                    specs[n] = old[i];
                    n++;
                }
            }
            _processor.ChannelSpecs = specs;
            _processor.RejectAuto(_selectedFrequency); // and the scan must not bring the carrier straight back as "auto"
            _selectedFrequency = -1;
            RebuildList();
            AutoCenter();
        }

        private void channelListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (channelListView.SelectedItems.Count == 0)
            {
                _selectedFrequency = -1;
                return;
            }
            var spec = (ChannelSpec)channelListView.SelectedItems[0].Tag;
            _selectedFrequency = spec.Frequency;
            _selectedIsAuto = spec.IsAuto;
            var tuned = spec.Frequency;
            var channels = _processor.Channels;
            for (var i = 0; i < channels.Length; i++)
            {
                if (channels[i].Spec == spec)
                {
                    tuned = channels[i].TunedFrequency; // Add / Update then makes it a manual channel on the refined frequency
                }
            }
            frequencyNumericUpDown.Value = ClampFrequency(tuned);
            rateComboBox.SelectedIndex = Math.Max(0, Array.IndexOf(BitRates, spec.BitRate));
        }

        private static AircraftTracker.Entry[] MakeAircraftRows()
        {
            var rows = new AircraftTracker.Entry[AircraftTracker.Capacity];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = new AircraftTracker.Entry();
            }
            return rows;
        }

        private void databaseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Aircraft database (tar1090-db aircraft.csv.gz, or a CSV with icao24,r,t columns)";
                dialog.Filter = "Aircraft database (*.csv;*.gz)|*.csv;*.gz|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                _processor.DatabasePath = dialog.FileName;
                databaseTextBox.Text = dialog.FileName;
            }
        }

        // Refreshed on the timer: a new copy when the tracker changed, else every 5 s for the expiry
        private void ShowAircraft()
        {
            var tracker = _processor.Aircraft;
            var now = Environment.TickCount64;
            if (tracker.Version != _aircraftVersion || now - _aircraftShown > 5000)
            {
                _aircraftVersion = tracker.Version;
                _aircraftShown = now;
                var heard = tracker.Copy(_aircraft);
                _aircraftCount = heard;
                if (_processor.MilitaryOnly)
                {
                    // keep the rows the database flags as military, in order
                    var kept = 0;
                    for (var i = 0; i < heard; i++)
                    {
                        string registration;
                        string type;
                        bool military;
                        _processor.Database.Lookup(_aircraft[i].Aes, out registration, out type, out military);
                        if (military)
                        {
                            var row = _aircraft[kept];
                            _aircraft[kept] = _aircraft[i];
                            _aircraft[i] = row;
                            kept++;
                        }
                    }
                    _aircraftCount = kept;
                }
                if (aircraftListView.VirtualListSize != _aircraftCount)
                {
                    aircraftListView.VirtualListSize = _aircraftCount;
                }
                aircraftListView.Invalidate();

                tracker.CopyOtherTypes(_otherTypes);
                var text = new StringBuilder();
                text.Append(_processor.Database.Status).Append(" | ");
                if (_processor.MilitaryOnly)
                {
                    text.Append(_aircraftCount).Append(" military of ").Append(heard).Append(" aircraft in the last hour");
                    if (_processor.Database.Count == 0)
                    {
                        text.Append(" (load the database: the military flag comes from it)");
                    }
                }
                else
                {
                    text.Append(_aircraftCount).Append(" aircraft in the last hour");
                }
                var shown = 0;
                for (var pass = 0; pass < 6; pass++)
                {
                    // the busiest other types, for working out what they are
                    var best = -1;
                    for (var t = 0; t < 256; t++)
                    {
                        if (_otherTypes[t] > 0 && (best < 0 || _otherTypes[t] > _otherTypes[best]))
                        {
                            best = t;
                        }
                    }
                    if (best < 0)
                    {
                        break;
                    }
                    text.Append(shown == 0 ? " | other SU types: " : ", ").Append("0x").Append(best.ToString("X2")).Append(" x").Append(_otherTypes[best]);
                    _otherTypes[best] = 0;
                    shown++;
                }
                aircraftStatusLabel.Text = text.ToString();
            }
        }

        private void militaryOnlyCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _processor.MilitaryOnly = militaryOnlyCheckBox.Checked;
            _aircraftVersion = -1; // re-filter on the next refresh
            ShowAircraft();
        }

        private void aircraftListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex >= _aircraftCount)
            {
                e.Item = new ListViewItem(new[] { "", "", "", "", "", "", "", "", "", "" });
                return;
            }
            var a = _aircraft[e.ItemIndex];
            string dbRegistration;
            string dbType;
            bool military;
            _processor.Database.Lookup(a.Aes, out dbRegistration, out dbType, out military);
            var registration = a.RegistrationLength > 0 ? Encoding.ASCII.GetString(a.Registration, 0, a.RegistrationLength) : dbRegistration ?? "";
            string evt;
            if (a.Event == AircraftTracker.EventUserData && a.Acars)
            {
                evt = "ACARS " + (char)a.Label0 + (char)a.Label1 + " block " + (char)a.Block;
            }
            else
            {
                evt = AircraftTracker.EventName(a.Event);
            }
            e.Item = new ListViewItem(new[]
            {
                a.Aes.ToString("X6"),
                registration,
                dbType ?? "",
                military ? "MIL" : "",
                IcaoCountry.Of(a.Aes),
                new DateTime(a.LastUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss"),
                evt,
                a.AssignedFrequency > 0 ? (a.AssignedFrequency / 1e6).ToString("0.000") + " @" + a.AssignedRate : "",
                FormatMHz(a.Frequency),
                a.Units.ToString(),
            });
            if (military)
            {
                e.Item.ForeColor = System.Drawing.Color.FromArgb(255, 170, 60); // military rows stand out
            }
        }

        private void autoScanCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _processor.AutoScan = autoScanCheckBox.Checked;
        }

        private void autoCenterCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _processor.AutoCenter = autoCenterCheckBox.Checked;
            if (!_initializing)
            {
                AutoCenter();
            }
        }

        // Centre between the lowest and highest channel, then nudged until no channel is within DcGuard of the raw IQ centre
        private void AutoCenter()
        {
            _centerNote = null;
            var specs = _processor.ChannelSpecs;
            if (!autoCenterCheckBox.Checked || specs.Length == 0 || !_control.IsPlaying)
            {
                return;
            }
            if (!_control.SourceIsTunable)
            {
                _centerNote = "Auto centre: the source cannot be tuned now";
                return;
            }

            var low = specs[0].Frequency;
            var high = specs[0].Frequency;
            for (var i = 1; i < specs.Length; i++)
            {
                low = Math.Min(low, specs[i].Frequency);
                high = Math.Max(high, specs[i].Frequency);
            }
            var middle = (low + high) / 2 / CenterStep * CenterStep;
            var ifOffset = (long)_control.IFOffset;
            var sampleRate = _processor.SampleRate;

            for (var step = 0; step <= MaxNudgeSteps; step++)
            {
                var candidate = middle + ((step & 1) == 0 ? 1 : -1) * ((step + 1) / 2) * CenterStep;
                var rawCenter = candidate + ifOffset;
                var clear = true;
                var fits = true;
                for (var i = 0; i < specs.Length; i++)
                {
                    var offset = Math.Abs(specs[i].Frequency - rawCenter);
                    var rate = AeroRate.FromBitRate(specs[i].BitRate);
                    if (offset < DcGuard)
                    {
                        clear = false;
                    }
                    if (sampleRate > 0 && offset > UsableBandwidth * sampleRate - rate.HalfBandwidth - rate.SearchRange)
                    {
                        fits = false;
                    }
                }
                if (clear && fits)
                {
                    try
                    {
                        _control.CenterFrequency = candidate;
                    }
                    catch (ApplicationException ex)
                    {
                        _centerNote = "Auto centre: " + ex.Message;
                    }
                    return;
                }
            }
            _centerNote = "Auto centre: channels span more than the band";
        }

        private void stationTextBox_TextChanged(object sender, EventArgs e)
        {
            _processor.StationId = stationTextBox.Text;
        }

        private void udpCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _processor.UdpEnabled = udpCheckBox.Checked;
        }

        private void hostTextBox_TextChanged(object sender, EventArgs e)
        {
            _processor.UdpHost = hostTextBox.Text;
        }

        private void portNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            _processor.UdpPort = (int)portNumericUpDown.Value;
        }

        private void udp2CheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _processor.Udp2Enabled = udp2CheckBox.Checked;
        }

        private void udp2HostTextBox_TextChanged(object sender, EventArgs e)
        {
            _processor.Udp2Host = udp2HostTextBox.Text;
        }

        private void udp2PortNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            _processor.Udp2Port = (int)udp2PortNumericUpDown.Value;
        }

        private void refreshTimer_Tick(object sender, EventArgs e)
        {
            ShowStatus();
            if (!_processor.Enabled)
            {
                return;
            }

            if (!inputRateTextBox.Focused)
            {
                inputRateTextBox.Text = _processor.SampleRate.ToString("#,0") + " S/s, centre " + _control.CenterFrequency.ToString("#,0") + " Hz, IF offset " + _control.IFOffset + " Hz, rebuilds " + _processor.Rebuilds;
            }
            if (!udpTextBox.Focused)
            {
                var udp = _processor.UdpEnabled ? _processor.UdpStatus + " (" + _processor.UdpSent + " sent, " + _processor.UdpDropped + " dropped)" : "Off";
                if (_processor.Udp2Enabled)
                {
                    udp += " | 2: " + (_processor.Udp2Host.Length == 0 ? "no host" : _processor.Udp2Status + " (" + _processor.Udp2Sent + " sent, " + _processor.Udp2Dropped + " dropped)");
                }
                udpTextBox.Text = udp;
            }

            if (_processor.ChannelsVersion != _shownVersion)
            {
                RebuildList();
            }
            ShowChannels();
            ShowMessages();
            ShowAircraft();
            ShowSystem();

            if (_hasMessage && !ageTextBox.Focused)
            {
                ageTextBox.Text = ((Environment.TickCount64 - _lastMessageCreated) * 0.001).ToString("0") + " s ago";
            }
        }

        private void ShowStatus()
        {
            if (!_processor.Enabled)
            {
                var failure = _processor.Failure;
                statusLabel.Text = failure != null ? "Failed: " + failure : "Disabled";
                return;
            }
            if (!_processor.HasInput)
            {
                statusLabel.Text = "No input yet";
                return;
            }
            if (_centerNote != null)
            {
                statusLabel.Text = _centerNote;
                return;
            }
            var channels = _processor.Channels;
            if (channels.Length == 0)
            {
                statusLabel.Text = _processor.AutoScan ? (_processor.ScanCount < 0 ? "Scanning band..." : "Scan found " + _processor.ScanCount + ", no channels yet") : "No channels: add one";
                return;
            }
            var synced = 0;
            for (var i = 0; i < channels.Length; i++)
            {
                if (channels[i].State == ChannelState.FrameSync)
                {
                    synced++;
                }
            }
            var text = synced + " of " + channels.Length + " in frame sync";
            if (_processor.AutoScan)
            {
                var found = _processor.ScanCount;
                text += found < 0 ? ", scanning" : ", scan found " + found;
                var images = _processor.ScanImages;
                if (found >= 0 && images > 0)
                {
                    text += ", " + images + " images skipped";
                }
                text += ", covers +/-" + (_processor.ScanCoverage / 1000).ToString("0") + " kHz";
                var refs = _processor.ReceiverCount;
                if (refs > 0)
                {
                    text += ", rx error " + _processor.ReceiverOffset.ToString("+0;-0") + " Hz (" + refs + " ref)";
                }
            }
            statusLabel.Text = text;
        }

        // Rows: the saved list (so an add / remove shows at once, even with SDR# stopped), then the running auto channels
        private void RebuildList()
        {
            var version = _processor.ChannelsVersion;
            var channels = _processor.Channels;
            var saved = _processor.ChannelSpecs;
            var autoCount = 0;
            if (_processor.Enabled && version >= 0)
            {
                for (var i = 0; i < channels.Length; i++)
                {
                    if (channels[i].Spec.IsAuto)
                    {
                        autoCount++;
                    }
                }
            }
            var specs = new ChannelSpec[saved.Length + autoCount];
            Array.Copy(saved, specs, saved.Length);
            var n = saved.Length;
            for (var i = 0; i < channels.Length && n < specs.Length; i++)
            {
                if (channels[i].Spec.IsAuto)
                {
                    specs[n++] = channels[i].Spec;
                }
            }
            _shownVersion = version;
            channelListView.BeginUpdate();
            channelListView.Items.Clear();
            for (var i = 0; i < specs.Length; i++)
            {
                var item = new ListViewItem(FormatMHz(specs[i].Frequency) + (specs[i].IsAuto ? " auto" : ""));
                item.Tag = specs[i];
                item.SubItems.Add(specs[i].BitRate.ToString());
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                channelListView.Items.Add(item);
                if (specs[i].Frequency == _selectedFrequency && specs[i].IsAuto == _selectedIsAuto)
                {
                    item.Selected = true;
                }
            }
            channelListView.EndUpdate();
        }

        private void ShowChannels()
        {
            var channels = _processor.Channels;
            for (var i = 0; i < channelListView.Items.Count; i++)
            {
                var item = channelListView.Items[i];
                var spec = (ChannelSpec)item.Tag;
                AeroChannel channel = null;
                for (var k = 0; k < channels.Length; k++)
                {
                    if (channels[k].Spec == spec)
                    {
                        channel = channels[k];
                    }
                }

                string status;
                string offset;
                string snr;
                string units;
                if (channel == null)
                {
                    status = "Starting";
                    offset = "-";
                    snr = "-";
                    units = "-";
                }
                else
                {
                    var state = channel.State;
                    var tracking = state == ChannelState.NoFrameSync || state == ChannelState.FrameSync;
                    status = StateText(state);
                    var spurs = channel.SpurCount;
                    if (spurs > 0)
                    {
                        status += spurs == 1 ? ", 1 spur notched" : ", " + spurs + " spurs notched";
                    }
                    var stage = channel.LoopStage;
                    if (stage > 0)
                    {
                        status += stage == 2 ? ", fine loops" : ", narrow loops";
                    }
                    var nudges = channel.Nudges;
                    if (nudges > 0)
                    {
                        status += ", resynced " + nudges;
                    }
                    if (state == ChannelState.Searching)
                    {
                        offset = "best " + channel.AcquisitionEstimate.ToString("0");
                        snr = "score " + channel.AcquisitionScore.ToString("0.0");
                    }
                    else
                    {
                        offset = tracking ? channel.CarrierOffset.ToString("0.0") : "-";
                        var ebn0 = channel.EbN0;
                        if (state == ChannelState.FrameSync && !float.IsNaN(ebn0))
                        {
                            // from the bits the decoder corrected; ">" = too few errors to count, true value is higher
                            var bound = channel.QualityIsLowerBound ? ">" : "";
                            snr = bound + ebn0.ToString("0.0") + " / " + bound + channel.Margin.ToString("+0.0;-0.0");
                        }
                        else
                        {
                            // no frame sync: estimate from the carrier's power over the band's noise floor
                            var es = channel.SpectralEsN0;
                            if (float.IsNaN(es))
                            {
                                snr = "no carrier";
                            }
                            else
                            {
                                var margin = LinkQuality.MarginFromEsN0(es, channel.Rate.IsDifferential);
                                snr = "~" + (es + 3.0103).ToString("0.0") + " / ~" + margin.ToString("+0.0;-0.0");
                            }
                        }
                    }
                    units = channel.SignalUnitsOk + " / " + channel.SignalUnitsBad;
                    if (spec.IsAuto)
                    {
                        // fine-tuned auto channels show where they actually are
                        var label = FormatMHz(channel.TunedFrequency) + " auto";
                        if (item.Text != label)
                        {
                            item.Text = label;
                        }
                    }
                }
                SetSubItem(item, 2, status);
                SetSubItem(item, 3, offset);
                SetSubItem(item, 4, snr);
                SetSubItem(item, 5, units);
            }
        }

        private static void SetSubItem(ListViewItem item, int index, string text)
        {
            if (item.SubItems[index].Text != text)
            {
                item.SubItems[index].Text = text;
            }
        }

        private static string StateText(ChannelState state)
        {
            switch (state)
            {
                case ChannelState.RateTooLow:
                    return "Input rate too low";

                case ChannelState.OutOfBand:
                    return "Outside the band";

                case ChannelState.Searching:
                    return "Searching carrier";

                case ChannelState.NoFrameSync:
                    return "No frame sync";

                case ChannelState.FrameSync:
                    return "Frame sync";

                default:
                    return "Idle";
            }
        }

        private void ShowMessages()
        {
            var latest = _processor.MessageSequence;
            if (latest < 0 || latest == _shownSequence || logTextBox.Focused)
            {
                return;
            }

            int pending;
            if (_shownSequence < 0)
            {
                pending = Math.Min(latest + 1, InmarsatAeroProcessor.MessageSlotCount);
            }
            else
            {
                pending = latest - _shownSequence;
                if (pending < 0)
                {
                    pending += InmarsatAeroProcessor.SequenceModulus;
                }
                if (pending > InmarsatAeroProcessor.MessageSlotCount)
                {
                    pending = InmarsatAeroProcessor.MessageSlotCount;
                }
            }

            var text = new StringBuilder();
            for (var k = pending - 1; k >= 0; k--)
            {
                var sequence = latest - k;
                if (sequence < 0)
                {
                    sequence += InmarsatAeroProcessor.SequenceModulus;
                }
                var slot = _processor.GetMessageSlot(sequence % InmarsatAeroProcessor.MessageSlotCount);
                if (slot.Sequence != sequence)
                {
                    continue;
                }
                AppendMessage(text, slot);
            }
            _shownSequence = latest;

            if (logTextBox.TextLength > MaxLogLength)
            {
                logTextBox.Text = logTextBox.Text.Substring(logTextBox.TextLength / 2);
            }
            logTextBox.AppendText(text.ToString());
        }

        // System tab: redrawn when the tables change (and not while the user is selecting text in it)
        private void ShowSystem()
        {
            var table = _processor.SystemInfo;
            if (table.Version == _shownSystem || systemTextBox.Focused)
            {
                return;
            }
            _shownSystem = table.Version;
            systemTextBox.Text = table.Report();
        }

        private void AppendMessage(StringBuilder text, AcarsMessage slot)
        {
            var created = slot.Created;
            var frequency = slot.Frequency;
            var aesid = slot.AESID;
            var gesid = slot.GESID;
            var nonAcars = slot.NonAcars;
            var registration = Encoding.ASCII.GetString(slot.Registration, 0, Math.Min(slot.RegistrationLength, AcarsMessage.RegistrationCapacity));
            var label = new string(new char[] { (char)slot.Label0, slot.Label1 == 0x7F ? 'd' : (char)slot.Label1 });
            var blockId = (char)slot.BlockId;
            var body = Encoding.ASCII.GetString(slot.Text, 0, Math.Min(slot.TextLength, AcarsMessage.TextCapacity));

            _lastMessageCreated = created;
            _hasMessage = true;

            var time = DateTime.Now.AddMilliseconds(created - Environment.TickCount64);
            text.Append(time.ToString("HH:mm:ss"));
            text.Append("  ");
            text.Append(FormatMHz(frequency));
            text.Append("  GES ");
            text.Append(gesid.ToString("X2"));
            text.Append(" > AES ");
            text.Append(aesid.ToString("X6"));
            if (nonAcars)
            {
                text.Append("  non-ACARS ");
                text.Append(body);
            }
            else
            {
                text.Append("  ");
                text.Append(registration);
                text.Append("  ");
                text.Append(label);
                text.Append(" ");
                text.Append(blockId);
                if (body.Length > 0)
                {
                    text.Append("\r\n    ");
                    text.Append(body.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n').Replace("\n", "\r\n    "));
                }
            }
            text.Append("\r\n");
        }

        private decimal ClampFrequency(long frequency)
        {
            return Math.Min(Math.Max(frequency, (long)frequencyNumericUpDown.Minimum), (long)frequencyNumericUpDown.Maximum);
        }

        private static string FormatMHz(long frequency)
        {
            return (frequency / 1e6).ToString("0.0000") + " MHz";
        }
    }
}
