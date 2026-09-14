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
            // Pins the DPI baseline to 96 and tells the form to scale itself (once, at
            // creation) rather than leaving AutoScaleMode at its ambient default - without
            // this, every explicit pixel Location/Size below gets rescaled unpredictably
            // on a scaled display, which is what pushed the Save button off the visible
            // window entirely. This is the same pair the WinForms Designer emits for any
            // DPI-aware form.
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = "Apple Music -> Discord Rich Presence";
            Width = 640;
            Height = 460;
            MinimumSize = new Size(480, 320);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;

            // Every control below has a fixed Location/Size, set once, with no Dock/Fill/
            // Anchor math involved in placing it - each row's text box is locked to its
            // own fixed boundary and the button for that row sits at a fixed spot right
            // beside it. Nothing here is computed from the window's width, so nothing can
            // land at the wrong x-coordinate depending on when that width happens to be
            // read - which is what caused controls to render on top of each other.
            var credentialPanel = new Panel { Dock = DockStyle.Top, Height = 40 };
            var credentialLabel = new Label
            {
                Text = "Discord Client ID:",
                AutoSize = false,
                Location = new Point(8, 10),
                Size = new Size(130, 20),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            _clientIdBox = new TextBox
            {
                Location = new Point(146, 8),
                Size = new Size(400, 23),
                Text = PresenceBridge.DiscordClientIdForDisplay,
                PlaceholderText = "e.g. 1234567890123456789",
            };
            var saveButton = new Button
            {
                Text = "Save",
                Location = new Point(554, 6),
                Size = new Size(70, 26),
            };
            saveButton.Click += OnSaveClientId;
            credentialPanel.Controls.Add(credentialLabel);
            credentialPanel.Controls.Add(_clientIdBox);
            credentialPanel.Controls.Add(saveButton);

            // Status line on its own full-width row so the whole thing stays readable
            // regardless of length.
            var statusPanel = new Panel { Dock = DockStyle.Top, Height = 32 };
            _status = new Label
            {
                Location = new Point(8, 0),
                Size = new Size(600, 32),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = PresenceBridge.CurrentStatus,
                Font = new Font(Font, FontStyle.Bold),
            };
            statusPanel.Controls.Add(_status);

            // A manual "Reconnect to Apple Music" button, on its own row below the
            // status line. There's no automatic background polling for this any more -
            // re-requesting the OS media session on a timer isn't free, and doesn't help
            // if the broker itself is stuck (only a Windows restart fixes that) - so this
            // is the on-demand way to ask the app to re-check right now, e.g. after Apple
            // Music restarts.
            var reconnectPanel = new Panel { Dock = DockStyle.Top, Height = 34 };
            var reconnectButton = new Button
            {
                Text = "Reconnect to Apple Music",
                Location = new Point(8, 4),
                Size = new Size(170, 24),
            };
            reconnectButton.Click += (_, _) => PresenceBridge.ReconnectAppleMusic();
            reconnectPanel.Controls.Add(reconnectButton);

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

            Controls.Add(_log);
            Controls.Add(reconnectPanel);
            Controls.Add(statusPanel);
            Controls.Add(credentialPanel);

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
