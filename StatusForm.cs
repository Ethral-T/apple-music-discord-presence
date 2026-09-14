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
            Width = 640;
            Height = 460;
            MinimumSize = new Size(480, 320);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;

            // Plain Dock stacking, deliberately not a TableLayoutPanel: nesting an
            // AutoSize TableLayoutPanel inside another AutoSize row is a known way to get
            // rows that collapse to near-zero height. Docked panels with fixed heights
            // are boring but reliable. Controls of the same Dock side stack in the order
            // they're added - the first Top-docked control claims the outer edge, so
            // adding credentialPanel before _status puts it above it.

            var credentialPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 6) };
            var credentialLabel = new Label
            {
                Text = "Discord Client ID:",
                AutoSize = true,
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var saveButton = new Button { Text = "Save", Dock = DockStyle.Right, AutoSize = true };
            saveButton.Click += OnSaveClientId;
            _clientIdBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 0, 6, 0),
                Text = PresenceBridge.DiscordClientIdForDisplay,
                PlaceholderText = "e.g. 1234567890123456789 - from discord.com/developers/applications",
            };
            // Add order within this panel: Left and Right first, Fill last, so the
            // text box's Fill correctly resolves to whatever space they didn't claim.
            credentialPanel.Controls.Add(credentialLabel);
            credentialPanel.Controls.Add(saveButton);
            credentialPanel.Controls.Add(_clientIdBox);

            _status = new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(10, 8, 10, 8),
                Text = PresenceBridge.CurrentStatus,
                Font = new Font(Font, FontStyle.Bold),
            };

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
            Controls.Add(_status);
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
