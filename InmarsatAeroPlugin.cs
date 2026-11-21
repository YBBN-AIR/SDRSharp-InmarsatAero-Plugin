using SDRSharp.Common;
using SDRSharp.Radio;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace SDRSharp.InmarsatAero
{
    public class InmarsatAeroPlugin : ISharpPlugin, ICanLazyLoadGui, ISupportStatus, IExtendedNameProvider
    {
        private ISharpControl _control;
        private InmarsatAeroProcessor _processor;
        private ProcessorPanel _configGui;

        public UserControl Gui
        {
            get
            {
                LoadGui();
                return _configGui;
            }
        }

        public bool IsActive => _processor.Enabled;

        public string DisplayName => "Inmarsat Aero ACARS v" + PluginVersion.Text;

        public string Category => SharpConstants.CategoryNameFFT;

        public string MenuItemName => "Inmarsat Aero";

        public void Initialize(ISharpControl control)
        {
            _control = control;

            _processor = new InmarsatAeroProcessor();
            _processor.ChannelSpecs = ParseChannels(Utils.GetStringSetting("plugin.aero.channels", ""));
            _processor.StationId = Utils.GetStringSetting("plugin.aero.stationId", "");
            _processor.UdpEnabled = Utils.GetBooleanSetting("plugin.aero.udpEnabled");
            _processor.AutoCenter = Utils.GetBooleanSetting("plugin.aero.autoCenter");
            _processor.AutoScan = Utils.GetBooleanSetting("plugin.aero.autoScan");
            _processor.DatabasePath = Utils.GetStringSetting("plugin.aero.aircraftDb", "");
            _processor.MilitaryOnly = Utils.GetBooleanSetting("plugin.aero.militaryOnly");
            _processor.UdpHost = Utils.GetStringSetting("plugin.aero.udpHost", "127.0.0.1");
            _processor.UdpPort = Utils.GetIntSetting("plugin.aero.udpPort", 5002);
            _processor.Udp2Enabled = Utils.GetBooleanSetting("plugin.aero.udp2Enabled");
            _processor.Udp2Host = Utils.GetStringSetting("plugin.aero.udp2Host", "");
            _processor.Udp2Port = Utils.GetIntSetting("plugin.aero.udp2Port", 5003);
            _processor.CenterFrequency = _control.CenterFrequency + _control.IFOffset;
            _processor.Enabled = Utils.GetBooleanSetting("plugin.aero.enabled");

            _control.PropertyChanged += control_PropertyChanged;
            _control.RegisterStreamHook(_processor, ProcessorType.RawIQ);
        }

        public void Close()
        {
            Utils.SaveSetting("plugin.aero.enabled", _processor.Enabled);
            Utils.SaveSetting("plugin.aero.channels", FormatChannels(_processor.ChannelSpecs));
            Utils.SaveSetting("plugin.aero.stationId", _processor.StationId);
            Utils.SaveSetting("plugin.aero.udpEnabled", _processor.UdpEnabled);
            Utils.SaveSetting("plugin.aero.autoCenter", _processor.AutoCenter);
            Utils.SaveSetting("plugin.aero.autoScan", _processor.AutoScan);
            Utils.SaveSetting("plugin.aero.aircraftDb", _processor.DatabasePath);
            Utils.SaveSetting("plugin.aero.militaryOnly", _processor.MilitaryOnly);
            Utils.SaveSetting("plugin.aero.udpHost", _processor.UdpHost);
            Utils.SaveSetting("plugin.aero.udpPort", _processor.UdpPort);
            Utils.SaveSetting("plugin.aero.udp2Enabled", _processor.Udp2Enabled);
            Utils.SaveSetting("plugin.aero.udp2Host", _processor.Udp2Host);
            Utils.SaveSetting("plugin.aero.udp2Port", _processor.Udp2Port);
            _processor.Enabled = false;
            if (_configGui != null)
            {
                _configGui.StopRefresh();
            }
            _processor.Dispose();
        }

        public void LoadGui()
        {
            if (_configGui == null)
            {
                _configGui = new ProcessorPanel(_processor, _control);
            }
        }

        private void control_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "CenterFrequency":
                case "Frequency":
                case "StartRadio":
                    _processor.CenterFrequency = _control.CenterFrequency + _control.IFOffset;
                    break;
            }
        }

        // "frequencyHz:bitRate,..."
        private static ChannelSpec[] ParseChannels(string text)
        {
            var items = text.Split(',');
            var specs = new ChannelSpec[items.Length];
            var count = 0;
            for (var i = 0; i < items.Length && count < InmarsatAeroProcessor.MaxChannels; i++)
            {
                var parts = items[i].Split(':');
                long frequency;
                int bitRate;
                if (parts.Length == 2 && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out frequency) && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out bitRate) && frequency > 0)
                {
                    specs[count] = new ChannelSpec(frequency, bitRate);
                    count++;
                }
            }
            var result = new ChannelSpec[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = specs[i];
            }
            return result;
        }

        private static string FormatChannels(ChannelSpec[] specs)
        {
            var text = new StringBuilder();
            for (var i = 0; i < specs.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }
                text.Append(specs[i].Frequency.ToString(CultureInfo.InvariantCulture));
                text.Append(':');
                text.Append(specs[i].BitRate.ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }
    }
}
