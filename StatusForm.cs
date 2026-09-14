using System;
using System.Drawing;
using System.Windows.Forms;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// The window behind the tray icon's "Show status" item. Closing it (the X button)
    /// just hides it - the app keeps running in the tray until you actually choose Exit.
    /// Also where you set your Discord Client ID - it's saved to AppSettings (a JSON
    /// file under %APPDATA%), never as a machine/user environment variable and never in
    /// source, so it's local to your account and doesn't leak into every other process
    /// on the machine's environment.
    /// </summary>
    internal sealed class StatusForm : Form
    {
        private const int MaxLogLines = 500;

        private readonly TextBox _clientIdBox;
        private readonly Label _status;
        private readonly TextBox _log;
        private bool _allowRealClose;

        public StatusForm()
        {
            Text = "Apple Music -> Discord Rich Presence";
            Width = 620;
            Height = 440;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;

            // Row 0: Discord Client ID entry.
            var credentialLabel = new Label
            {
                Text = "Discord Client ID:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 8, 8, 0),
            };
            _clientIdBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = PresenceBridge.DiscordClientIdForDisplay,
                PlaceholderText = "e.g. 1234567890123456789 - from discord.com/developers/applications",
            };
            var saveButton = new Button { Text = "Save", AutoSize = true };
            saveButton.Click += OnSaveClientId;

            var credentialRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                Padding = new Padding(8, 6, 8, 6),
            };
            credentialRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            credentialRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            credentialRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            credentialRow.Controls.Add(credentialLabel, 0, 0);
            credentialRow.Controls.Add(_clientIdBox, 1, 0);
            credentialRow.Controls.Add(saveButton, 2, 0);

            // Row 1: current status.
            _status = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 8),
                Text = PresenceBridge.CurrentStatus,
                Font = new Font(Font, FontStyle.Bold),
            };

            // Row 2: scrolling log, fills the rest.
            _log = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                BackColor = SystemColors.Window,
            };

            // A TableLayoutPanel (rather than stacking multiple Dock.Top controls) so the
            // row order is explicit instead of depending on Controls-collection z-order.
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(credentialRow, 0, 0);
            root.Controls.Add(_status, 0, 1);
            root.Controls.Add(_log, 0, 2);
            Controls.Add(root);

            AcceptButton = saveButton;
            FormClosing += OnFormClosing;
        }

        private void OnSaveClientId(object? sender, EventArgs e)
        {
            PresenceBridge.SetDiscordClientId(_clientIdBox.Text);
            _clientIdBox.Text = PresenceBridge.DiscordClientIdForDisplay; // reflects the trimmed/cleared value
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (_allowRealClose) return;
            e.Cancel = true;
            Hide();
        }

        /// <summary>Lets the next Close() actually close the window instead of hiding it.</summary>
        public void AllowRealClose() => _allowRealClose = true;

        public void SetStatus(string text) => _status.Text = text;

        public void AppendLog(string line)
        {
            _log.AppendText(line + Environment.NewLine);

            if (_log.Lines.Length > MaxLogLines)
            {
                var trimmed = _log.Lines[^MaxLogLines..];
                _log.Lines = trimmed;
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            }
        }

        public void ShowAndActivate()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }
    }
}
