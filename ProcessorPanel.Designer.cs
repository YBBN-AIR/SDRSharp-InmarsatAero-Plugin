namespace SDRSharp.InmarsatAero
{
    partial class ProcessorPanel
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            enableCheckBox = new System.Windows.Forms.CheckBox();
            statusLabel = new System.Windows.Forms.Label();
            frequencyLabel = new System.Windows.Forms.Label();
            frequencyNumericUpDown = new SDRSharp.PanView.DoubleBufferedNumericUpDown();
            vfoButton = new System.Windows.Forms.Button();
            rateLabel = new System.Windows.Forms.Label();
            rateComboBox = new SDRSharp.PanView.DoubleBufferedComboBox();
            addButton = new System.Windows.Forms.Button();
            removeButton = new System.Windows.Forms.Button();
            channelListView = new ChannelListView();
            frequencyColumn = new System.Windows.Forms.ColumnHeader();
            rateColumn = new System.Windows.Forms.ColumnHeader();
            stateColumn = new System.Windows.Forms.ColumnHeader();
            offsetColumn = new System.Windows.Forms.ColumnHeader();
            snrColumn = new System.Windows.Forms.ColumnHeader();
            unitsColumn = new System.Windows.Forms.ColumnHeader();
            stationLabel = new System.Windows.Forms.Label();
            stationTextBox = new SDRSharp.PanView.DoubleBufferedTextBox();
            udpCheckBox = new System.Windows.Forms.CheckBox();
            hostLabel = new System.Windows.Forms.Label();
            hostTextBox = new SDRSharp.PanView.DoubleBufferedTextBox();
            udp2CheckBox = new System.Windows.Forms.CheckBox();
            udp2HostTextBox = new SDRSharp.PanView.DoubleBufferedTextBox();
            udp2PortNumericUpDown = new System.Windows.Forms.NumericUpDown();
            portLabel = new System.Windows.Forms.Label();
            portNumericUpDown = new SDRSharp.PanView.DoubleBufferedNumericUpDown();
            inputRateLabel = new System.Windows.Forms.Label();
            inputRateTextBox = new SDRSharp.PanView.DoubleBufferedTextBox();
            udpLabel = new System.Windows.Forms.Label();
            udpTextBox = new SDRSharp.PanView.DoubleBufferedTextBox();
            ageLabel = new System.Windows.Forms.Label();
            ageTextBox = new SDRSharp.PanView.DoubleBufferedTextBox();
            logTextBox = new LogTextBox();
            logTabs = new System.Windows.Forms.TabControl();
            logPage = new System.Windows.Forms.TabPage();
            aircraftPage = new System.Windows.Forms.TabPage();
            systemPage = new System.Windows.Forms.TabPage();
            systemTextBox = new LogTextBox();
            assignedColumn = new System.Windows.Forms.ColumnHeader();
            aircraftTable = new System.Windows.Forms.TableLayoutPanel();
            databaseLabel = new System.Windows.Forms.Label();
            databaseTextBox = new System.Windows.Forms.TextBox();
            databaseButton = new System.Windows.Forms.Button();
            aircraftStatusLabel = new System.Windows.Forms.Label();
            militaryOnlyCheckBox = new System.Windows.Forms.CheckBox();
            aircraftListView = new ChannelListView();
            hexColumn = new System.Windows.Forms.ColumnHeader();
            registrationColumn = new System.Windows.Forms.ColumnHeader();
            typeColumn = new System.Windows.Forms.ColumnHeader();
            militaryColumn = new System.Windows.Forms.ColumnHeader();
            countryColumn = new System.Windows.Forms.ColumnHeader();
            seenColumn = new System.Windows.Forms.ColumnHeader();
            eventColumn = new System.Windows.Forms.ColumnHeader();
            aircraftFrequencyColumn = new System.Windows.Forms.ColumnHeader();
            aircraftUnitsColumn = new System.Windows.Forms.ColumnHeader();
            mainTableLayoutPanel = new SDRSharp.PanView.DoubleBufferedTableLayoutPanel();
            autoCenterCheckBox = new System.Windows.Forms.CheckBox();
            autoScanCheckBox = new System.Windows.Forms.CheckBox();
            refreshTimer = new System.Windows.Forms.Timer(components);
            ((System.ComponentModel.ISupportInitialize)frequencyNumericUpDown).BeginInit();
            ((System.ComponentModel.ISupportInitialize)portNumericUpDown).BeginInit();
            ((System.ComponentModel.ISupportInitialize)udp2PortNumericUpDown).BeginInit();
            mainTableLayoutPanel.SuspendLayout();
            SuspendLayout();
            //
            // enableCheckBox
            //
            enableCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            enableCheckBox.AutoSize = true;
            enableCheckBox.Name = "enableCheckBox";
            enableCheckBox.TabIndex = 0;
            enableCheckBox.Text = "Enabled";
            enableCheckBox.CheckedChanged += enableCheckBox_ValueChanged;
            //
            // statusLabel
            //
            statusLabel.Anchor = System.Windows.Forms.AnchorStyles.Right;
            statusLabel.AutoSize = true;
            statusLabel.Name = "statusLabel";
            statusLabel.TabIndex = 1;
            statusLabel.Text = "Disabled";
            //
            // frequencyLabel
            //
            frequencyLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            frequencyLabel.AutoSize = true;
            frequencyLabel.Name = "frequencyLabel";
            frequencyLabel.TabIndex = 2;
            frequencyLabel.Text = "Frequency (Hz)";
            frequencyLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // frequencyNumericUpDown
            //
            frequencyNumericUpDown.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            frequencyNumericUpDown.Font = new System.Drawing.Font("Segoe UI", 11F);
            frequencyNumericUpDown.Name = "frequencyNumericUpDown";
            frequencyNumericUpDown.TabIndex = 3;
            frequencyNumericUpDown.Increment = new decimal(new int[] { 500, 0, 0, 0 });
            frequencyNumericUpDown.Maximum = new decimal(new int[] { 1705032704, 1, 0, 0 });
            frequencyNumericUpDown.MinimumSize = new System.Drawing.Size(30, 0);
            frequencyNumericUpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            frequencyNumericUpDown.ThousandsSeparator = true;
            //
            // vfoButton
            //
            vfoButton.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            vfoButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            vfoButton.Name = "vfoButton";
            vfoButton.TabIndex = 4;
            vfoButton.Text = "VFO";
            vfoButton.Click += vfoButton_Click;
            //
            // rateLabel
            //
            rateLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            rateLabel.AutoSize = true;
            rateLabel.Name = "rateLabel";
            rateLabel.TabIndex = 5;
            rateLabel.Text = "Bit Rate (bps)";
            rateLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // rateComboBox
            //
            rateComboBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            rateComboBox.Font = new System.Drawing.Font("Segoe UI", 11F);
            rateComboBox.Name = "rateComboBox";
            rateComboBox.TabIndex = 6;
            rateComboBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
            rateComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            rateComboBox.Items.AddRange(new object[] { "600", "1200", "10500" });
            //
            // addButton
            //
            addButton.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            addButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            addButton.Name = "addButton";
            addButton.TabIndex = 7;
            addButton.Text = "Add / Update";
            addButton.Click += addButton_Click;
            //
            // removeButton
            //
            removeButton.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            removeButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            removeButton.Name = "removeButton";
            removeButton.TabIndex = 8;
            removeButton.Text = "Remove";
            removeButton.Click += removeButton_Click;
            //
            // channelListView
            //
            channelListView.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            channelListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { frequencyColumn, rateColumn, stateColumn, offsetColumn, snrColumn, unitsColumn });
            channelListView.FullRowSelect = true;
            channelListView.HideSelection = false;
            channelListView.MultiSelect = false;
            channelListView.Name = "channelListView";
            channelListView.TabIndex = 9;
            channelListView.UseCompatibleStateImageBehavior = false;
            channelListView.View = System.Windows.Forms.View.Details;
            channelListView.SelectedIndexChanged += channelListView_SelectedIndexChanged;
            //
            // frequencyColumn
            //
            frequencyColumn.Text = "Frequency";
            frequencyColumn.Width = 110;
            //
            // rateColumn
            //
            rateColumn.Text = "Rate (bps)";
            rateColumn.Width = 75;
            //
            // stateColumn
            //
            stateColumn.Text = "Status";
            stateColumn.Width = 200;
            //
            // offsetColumn
            //
            offsetColumn.Text = "Offset (Hz)";
            offsetColumn.Width = 80;
            //
            // snrColumn
            //
            snrColumn.Text = "Eb/N0 / margin (dB)";
            snrColumn.Width = 115;
            //
            // unitsColumn
            //
            unitsColumn.Text = "SUs OK / Bad";
            unitsColumn.Width = 90;
            //
            // stationLabel
            //
            stationLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            stationLabel.AutoSize = true;
            stationLabel.Name = "stationLabel";
            stationLabel.TabIndex = 10;
            stationLabel.Text = "Station ID";
            stationLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // stationTextBox
            //
            stationTextBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            stationTextBox.Font = new System.Drawing.Font("Segoe UI", 11F);
            stationTextBox.Name = "stationTextBox";
            stationTextBox.TabIndex = 11;
            stationTextBox.TextChanged += stationTextBox_TextChanged;
            //
            // udpCheckBox
            //
            udpCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            udpCheckBox.AutoSize = true;
            udpCheckBox.Name = "udpCheckBox";
            udpCheckBox.TabIndex = 12;
            udpCheckBox.Text = "Send UDP (JAERO JSON)";
            udpCheckBox.CheckedChanged += udpCheckBox_CheckedChanged;
            //
            // hostLabel
            //
            hostLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            hostLabel.AutoSize = true;
            hostLabel.Name = "hostLabel";
            hostLabel.TabIndex = 13;
            hostLabel.Text = "UDP Host";
            hostLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // hostTextBox
            //
            hostTextBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            hostTextBox.Font = new System.Drawing.Font("Segoe UI", 11F);
            hostTextBox.Name = "hostTextBox";
            hostTextBox.TabIndex = 14;
            hostTextBox.TextChanged += hostTextBox_TextChanged;
            //
            // portLabel
            //
            portLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            portLabel.AutoSize = true;
            portLabel.Name = "portLabel";
            portLabel.TabIndex = 15;
            portLabel.Text = "UDP Port";
            portLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // portNumericUpDown
            //
            portNumericUpDown.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            portNumericUpDown.Font = new System.Drawing.Font("Segoe UI", 11F);
            portNumericUpDown.Name = "portNumericUpDown";
            portNumericUpDown.TabIndex = 16;
            portNumericUpDown.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            portNumericUpDown.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            portNumericUpDown.MinimumSize = new System.Drawing.Size(30, 0);
            portNumericUpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            portNumericUpDown.Value = new decimal(new int[] { 5002, 0, 0, 0 });
            portNumericUpDown.ValueChanged += portNumericUpDown_ValueChanged;
            //
            // UDP 2: checkbox, host, port on one row
            //
            udp2CheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            udp2CheckBox.AutoSize = true;
            udp2CheckBox.Name = "udp2CheckBox";
            udp2CheckBox.TabIndex = 25;
            udp2CheckBox.Text = "Send UDP 2";
            udp2CheckBox.CheckedChanged += udp2CheckBox_CheckedChanged;
            udp2HostTextBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            udp2HostTextBox.Font = new System.Drawing.Font("Segoe UI", 11F);
            udp2HostTextBox.Name = "udp2HostTextBox";
            udp2HostTextBox.TabIndex = 26;
            udp2HostTextBox.TextChanged += udp2HostTextBox_TextChanged;
            udp2PortNumericUpDown.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            udp2PortNumericUpDown.Font = new System.Drawing.Font("Segoe UI", 11F);
            udp2PortNumericUpDown.Name = "udp2PortNumericUpDown";
            udp2PortNumericUpDown.TabIndex = 27;
            udp2PortNumericUpDown.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            udp2PortNumericUpDown.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            udp2PortNumericUpDown.MinimumSize = new System.Drawing.Size(30, 0);
            udp2PortNumericUpDown.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            udp2PortNumericUpDown.Value = new decimal(new int[] { 5003, 0, 0, 0 });
            udp2PortNumericUpDown.ValueChanged += udp2PortNumericUpDown_ValueChanged;
            //
            // inputRateLabel
            //
            inputRateLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            inputRateLabel.AutoSize = true;
            inputRateLabel.Name = "inputRateLabel";
            inputRateLabel.TabIndex = 17;
            inputRateLabel.Text = "Input Rate";
            inputRateLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // inputRateTextBox
            //
            inputRateTextBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            inputRateTextBox.Font = new System.Drawing.Font("Segoe UI", 11F);
            inputRateTextBox.Name = "inputRateTextBox";
            inputRateTextBox.TabIndex = 18;
            inputRateTextBox.ReadOnly = true;
            inputRateTextBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            //
            // udpLabel
            //
            udpLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            udpLabel.AutoSize = true;
            udpLabel.Name = "udpLabel";
            udpLabel.TabIndex = 19;
            udpLabel.Text = "UDP";
            udpLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // udpTextBox
            //
            udpTextBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            udpTextBox.Font = new System.Drawing.Font("Segoe UI", 11F);
            udpTextBox.Name = "udpTextBox";
            udpTextBox.TabIndex = 20;
            udpTextBox.ReadOnly = true;
            udpTextBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            //
            // ageLabel
            //
            ageLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            ageLabel.AutoSize = true;
            ageLabel.Name = "ageLabel";
            ageLabel.TabIndex = 21;
            ageLabel.Text = "Last Message";
            ageLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // ageTextBox
            //
            ageTextBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            ageTextBox.Font = new System.Drawing.Font("Segoe UI", 11F);
            ageTextBox.Name = "ageTextBox";
            ageTextBox.TabIndex = 22;
            ageTextBox.ReadOnly = true;
            ageTextBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            //
            // logTextBox
            //
            logTextBox.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            logTextBox.Font = new System.Drawing.Font("Segoe UI", 11F);
            logTextBox.Multiline = true;
            logTextBox.Name = "logTextBox";
            logTextBox.ReadOnly = true;
            logTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            logTextBox.TabIndex = 23;
            logTextBox.WordWrap = false;
            logTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            logTextBox.BackColor = System.Drawing.Color.FromArgb(24, 24, 24); // explicit: a read-only box otherwise takes the theme's grey
            logTextBox.ForeColor = System.Drawing.Color.FromArgb(235, 235, 235);
            //
            // systemTextBox: satellite / GES / channel-assignment tables (fixed width)
            //
            systemTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            systemTextBox.Font = new System.Drawing.Font("Consolas", 10F);
            systemTextBox.Multiline = true;
            systemTextBox.Name = "systemTextBox";
            systemTextBox.ReadOnly = true;
            systemTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            systemTextBox.WordWrap = false;
            systemTextBox.BackColor = System.Drawing.Color.FromArgb(24, 24, 24);
            systemTextBox.ForeColor = System.Drawing.Color.FromArgb(235, 235, 235);
            systemPage.Name = "systemPage";
            systemPage.Text = "System";
            systemPage.Controls.Add(systemTextBox);
            //
            // logTabs: Log / Aircraft
            //
            logTabs.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            logTabs.Name = "logTabs";
            logTabs.TabIndex = 24;
            logTabs.Controls.Add(logPage);
            logTabs.Controls.Add(aircraftPage);
            logTabs.Controls.Add(systemPage);
            logPage.Name = "logPage";
            logPage.Text = "Log";
            logPage.Controls.Add(logTextBox);
            aircraftPage.Name = "aircraftPage";
            aircraftPage.Text = "Aircraft";
            // explicit colours on every page: a tab page paints itself white, while SDR#'s theme makes inherited text white
            foreach (var page in new[] { logPage, aircraftPage, systemPage })
            {
                page.UseVisualStyleBackColor = false;
                page.BackColor = System.Drawing.Color.FromArgb(24, 24, 24);
                page.ForeColor = System.Drawing.Color.FromArgb(235, 235, 235);
            }
            databaseTextBox.BackColor = System.Drawing.Color.FromArgb(40, 40, 40);
            databaseTextBox.ForeColor = System.Drawing.Color.FromArgb(235, 235, 235);
            databaseButton.UseVisualStyleBackColor = true;
            databaseButton.ForeColor = System.Drawing.SystemColors.ControlText;
            aircraftListView.BackColor = System.Drawing.Color.FromArgb(24, 24, 24);
            aircraftListView.ForeColor = System.Drawing.Color.FromArgb(235, 235, 235);
            aircraftPage.Controls.Add(aircraftTable);
            //
            // aircraftTable
            //
            aircraftTable.Dock = System.Windows.Forms.DockStyle.Fill;
            aircraftTable.Name = "aircraftTable";
            aircraftTable.ColumnCount = 3;
            aircraftTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            aircraftTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            aircraftTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            aircraftTable.RowCount = 3;
            aircraftTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            aircraftTable.RowStyles.Add(new System.Windows.Forms.RowStyle());
            aircraftTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            aircraftTable.Controls.Add(databaseLabel, 0, 0);
            aircraftTable.Controls.Add(databaseTextBox, 1, 0);
            aircraftTable.Controls.Add(databaseButton, 2, 0);
            aircraftTable.Controls.Add(militaryOnlyCheckBox, 0, 1);
            aircraftTable.Controls.Add(aircraftStatusLabel, 1, 1);
            aircraftTable.Controls.Add(aircraftListView, 0, 2);
            aircraftTable.SetColumnSpan(aircraftStatusLabel, 2);
            militaryOnlyCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            militaryOnlyCheckBox.AutoSize = true;
            militaryOnlyCheckBox.Name = "militaryOnlyCheckBox";
            militaryOnlyCheckBox.Text = "Military only";
            militaryOnlyCheckBox.CheckedChanged += militaryOnlyCheckBox_CheckedChanged;
            aircraftTable.SetColumnSpan(aircraftListView, 3);
            databaseLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            databaseLabel.AutoSize = true;
            databaseLabel.Name = "databaseLabel";
            databaseLabel.Text = "Aircraft DB";
            databaseTextBox.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            databaseTextBox.Name = "databaseTextBox";
            databaseTextBox.ReadOnly = true;
            databaseButton.AutoSize = true;
            databaseButton.Name = "databaseButton";
            databaseButton.Text = "Browse...";
            databaseButton.Click += databaseButton_Click;
            aircraftStatusLabel.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            aircraftStatusLabel.AutoSize = true;
            aircraftStatusLabel.Name = "aircraftStatusLabel";
            aircraftStatusLabel.Text = "No database";
            //
            // aircraftListView (virtual: rows come from the tracker's copy)
            //
            aircraftListView.Dock = System.Windows.Forms.DockStyle.Fill;
            aircraftListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { hexColumn, registrationColumn, typeColumn, militaryColumn, countryColumn, seenColumn, eventColumn, assignedColumn, aircraftFrequencyColumn, aircraftUnitsColumn });
            aircraftListView.FullRowSelect = true;
            aircraftListView.MultiSelect = false;
            aircraftListView.Name = "aircraftListView";
            aircraftListView.UseCompatibleStateImageBehavior = false;
            aircraftListView.View = System.Windows.Forms.View.Details;
            aircraftListView.VirtualMode = true;
            aircraftListView.RetrieveVirtualItem += aircraftListView_RetrieveVirtualItem;
            hexColumn.Text = "Hex";
            hexColumn.Width = 65;
            registrationColumn.Text = "Reg";
            registrationColumn.Width = 80;
            typeColumn.Text = "Type";
            typeColumn.Width = 50;
            militaryColumn.Text = "Mil";
            militaryColumn.Width = 40;
            countryColumn.Text = "Ctry";
            countryColumn.Width = 40;
            seenColumn.Text = "Last";
            seenColumn.Width = 65;
            eventColumn.Text = "Event";
            eventColumn.Width = 190;
            assignedColumn.Text = "Assigned P";
            assignedColumn.Width = 120;
            aircraftFrequencyColumn.Text = "MHz";
            aircraftFrequencyColumn.Width = 85;
            aircraftUnitsColumn.Text = "SUs";
            aircraftUnitsColumn.Width = 45;
            //
            // autoCenterCheckBox
            //
            autoCenterCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            autoCenterCheckBox.AutoSize = true;
            autoCenterCheckBox.Name = "autoCenterCheckBox";
            autoCenterCheckBox.TabIndex = 100;
            autoCenterCheckBox.Text = "Auto centre";
            autoCenterCheckBox.CheckedChanged += autoCenterCheckBox_CheckedChanged;
            //
            // autoScanCheckBox
            //
            autoScanCheckBox.Anchor = System.Windows.Forms.AnchorStyles.Left;
            autoScanCheckBox.AutoSize = true;
            autoScanCheckBox.Name = "autoScanCheckBox";
            autoScanCheckBox.TabIndex = 101;
            autoScanCheckBox.Text = "Auto scan whole band (temporary channels)";
            autoScanCheckBox.CheckedChanged += autoScanCheckBox_CheckedChanged;
            //
            // mainTableLayoutPanel
            //
            mainTableLayoutPanel.ColumnCount = 3;
            mainTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            mainTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            mainTableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            mainTableLayoutPanel.Controls.Add(enableCheckBox, 0, 0);
            mainTableLayoutPanel.Controls.Add(statusLabel, 1, 0);
            mainTableLayoutPanel.Controls.Add(frequencyLabel, 0, 1);
            mainTableLayoutPanel.Controls.Add(frequencyNumericUpDown, 1, 1);
            mainTableLayoutPanel.Controls.Add(vfoButton, 2, 1);
            mainTableLayoutPanel.Controls.Add(rateLabel, 0, 2);
            mainTableLayoutPanel.Controls.Add(rateComboBox, 1, 2);
            mainTableLayoutPanel.Controls.Add(autoCenterCheckBox, 0, 3);
            mainTableLayoutPanel.Controls.Add(addButton, 1, 3);
            mainTableLayoutPanel.Controls.Add(removeButton, 2, 3);
            mainTableLayoutPanel.Controls.Add(autoScanCheckBox, 0, 4);
            mainTableLayoutPanel.Controls.Add(channelListView, 0, 5);
            mainTableLayoutPanel.Controls.Add(stationLabel, 0, 6);
            mainTableLayoutPanel.Controls.Add(stationTextBox, 1, 6);
            mainTableLayoutPanel.Controls.Add(udpCheckBox, 0, 7);
            mainTableLayoutPanel.Controls.Add(hostLabel, 0, 8);
            mainTableLayoutPanel.Controls.Add(hostTextBox, 1, 8);
            mainTableLayoutPanel.Controls.Add(portLabel, 0, 9);
            mainTableLayoutPanel.Controls.Add(portNumericUpDown, 1, 9);
            mainTableLayoutPanel.Controls.Add(udp2CheckBox, 0, 10);
            mainTableLayoutPanel.Controls.Add(udp2HostTextBox, 1, 10);
            mainTableLayoutPanel.Controls.Add(udp2PortNumericUpDown, 2, 10);
            mainTableLayoutPanel.Controls.Add(inputRateLabel, 0, 11);
            mainTableLayoutPanel.Controls.Add(inputRateTextBox, 1, 11);
            mainTableLayoutPanel.Controls.Add(udpLabel, 0, 12);
            mainTableLayoutPanel.Controls.Add(udpTextBox, 1, 12);
            mainTableLayoutPanel.Controls.Add(ageLabel, 0, 13);
            mainTableLayoutPanel.Controls.Add(ageTextBox, 1, 13);
            mainTableLayoutPanel.Controls.Add(logTabs, 0, 14);
            mainTableLayoutPanel.SetColumnSpan(statusLabel, 2);
            mainTableLayoutPanel.SetColumnSpan(rateComboBox, 2);
            mainTableLayoutPanel.SetColumnSpan(autoScanCheckBox, 3);
            mainTableLayoutPanel.SetColumnSpan(channelListView, 3);
            mainTableLayoutPanel.SetColumnSpan(stationTextBox, 2);
            mainTableLayoutPanel.SetColumnSpan(udpCheckBox, 3);
            mainTableLayoutPanel.SetColumnSpan(hostTextBox, 2);
            mainTableLayoutPanel.SetColumnSpan(portNumericUpDown, 2);
            mainTableLayoutPanel.SetColumnSpan(inputRateTextBox, 2);
            mainTableLayoutPanel.SetColumnSpan(udpTextBox, 2);
            mainTableLayoutPanel.SetColumnSpan(ageTextBox, 2);
            mainTableLayoutPanel.SetColumnSpan(logTabs, 3);
            mainTableLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            mainTableLayoutPanel.Location = new System.Drawing.Point(0, 0);
            mainTableLayoutPanel.Name = "mainTableLayoutPanel";
            mainTableLayoutPanel.RowCount = 15;
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 40F));
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            mainTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 60F));
            mainTableLayoutPanel.Size = new System.Drawing.Size(600, 700);
            mainTableLayoutPanel.TabIndex = 0;
            //
            // refreshTimer
            //
            refreshTimer.Interval = 500;
            refreshTimer.Tick += refreshTimer_Tick;
            //
            // ProcessorPanel
            //
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            Controls.Add(mainTableLayoutPanel);
            Name = "ProcessorPanel";
            Size = new System.Drawing.Size(600, 700);
            ((System.ComponentModel.ISupportInitialize)frequencyNumericUpDown).EndInit();
            ((System.ComponentModel.ISupportInitialize)portNumericUpDown).EndInit();
            ((System.ComponentModel.ISupportInitialize)udp2PortNumericUpDown).EndInit();
            mainTableLayoutPanel.ResumeLayout(false);
            mainTableLayoutPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.CheckBox enableCheckBox;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Label frequencyLabel;
        private SDRSharp.PanView.DoubleBufferedNumericUpDown frequencyNumericUpDown;
        private System.Windows.Forms.Button vfoButton;
        private System.Windows.Forms.Label rateLabel;
        private SDRSharp.PanView.DoubleBufferedComboBox rateComboBox;
        private System.Windows.Forms.Button addButton;
        private System.Windows.Forms.Button removeButton;
        private ChannelListView channelListView;
        private System.Windows.Forms.ColumnHeader frequencyColumn;
        private System.Windows.Forms.ColumnHeader rateColumn;
        private System.Windows.Forms.ColumnHeader stateColumn;
        private System.Windows.Forms.ColumnHeader offsetColumn;
        private System.Windows.Forms.ColumnHeader snrColumn;
        private System.Windows.Forms.ColumnHeader unitsColumn;
        private System.Windows.Forms.Label stationLabel;
        private SDRSharp.PanView.DoubleBufferedTextBox stationTextBox;
        private System.Windows.Forms.CheckBox udpCheckBox;
        private System.Windows.Forms.Label hostLabel;
        private SDRSharp.PanView.DoubleBufferedTextBox hostTextBox;
        private System.Windows.Forms.CheckBox udp2CheckBox;
        private SDRSharp.PanView.DoubleBufferedTextBox udp2HostTextBox;
        private System.Windows.Forms.NumericUpDown udp2PortNumericUpDown;
        private System.Windows.Forms.Label portLabel;
        private SDRSharp.PanView.DoubleBufferedNumericUpDown portNumericUpDown;
        private System.Windows.Forms.Label inputRateLabel;
        private SDRSharp.PanView.DoubleBufferedTextBox inputRateTextBox;
        private System.Windows.Forms.Label udpLabel;
        private SDRSharp.PanView.DoubleBufferedTextBox udpTextBox;
        private System.Windows.Forms.Label ageLabel;
        private SDRSharp.PanView.DoubleBufferedTextBox ageTextBox;
        private LogTextBox logTextBox;
        private System.Windows.Forms.TabControl logTabs;
        private System.Windows.Forms.TabPage logPage;
        private System.Windows.Forms.TabPage aircraftPage;
        private System.Windows.Forms.TabPage systemPage;
        private LogTextBox systemTextBox;
        private System.Windows.Forms.ColumnHeader assignedColumn;
        private System.Windows.Forms.TableLayoutPanel aircraftTable;
        private System.Windows.Forms.Label databaseLabel;
        private System.Windows.Forms.TextBox databaseTextBox;
        private System.Windows.Forms.Button databaseButton;
        private System.Windows.Forms.Label aircraftStatusLabel;
        private System.Windows.Forms.CheckBox militaryOnlyCheckBox;
        private ChannelListView aircraftListView;
        private System.Windows.Forms.ColumnHeader hexColumn;
        private System.Windows.Forms.ColumnHeader registrationColumn;
        private System.Windows.Forms.ColumnHeader typeColumn;
        private System.Windows.Forms.ColumnHeader militaryColumn;
        private System.Windows.Forms.ColumnHeader countryColumn;
        private System.Windows.Forms.ColumnHeader seenColumn;
        private System.Windows.Forms.ColumnHeader eventColumn;
        private System.Windows.Forms.ColumnHeader aircraftFrequencyColumn;
        private System.Windows.Forms.ColumnHeader aircraftUnitsColumn;
        private SDRSharp.PanView.DoubleBufferedTableLayoutPanel mainTableLayoutPanel;
        private System.Windows.Forms.CheckBox autoCenterCheckBox;
        private System.Windows.Forms.CheckBox autoScanCheckBox;
        private System.Windows.Forms.Timer refreshTimer;
    }
}
