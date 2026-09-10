using System;
using System.Drawing;
using System.Windows.Forms;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// The window behind the tray icon's "Show status" item. Closing it (the X button)
    /// just hides it - the app keeps running in the tray until you actually choose Exit.
    /// </summary>
    internal sealed class StatusForm : Form
    {
        private const int MaxLogLines = 500;

        private readonly Label _status;
        private readonly TextBox _log;
        private bool _allowRealClose;

        public StatusForm()
        {
            Text = "Apple Music -> Discord Rich Presence";
            Width = 620;
            Height = 420;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;

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

            FormClosing += OnFormClosing;
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
