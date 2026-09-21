using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// Owns the app's lifetime while it has no main window: a tray icon plus a status
    /// window it can show on demand. Created once from Program.Main and handed to
    /// Application.Run.
    /// </summary>
    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly StatusForm _statusForm = new();
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly OverlayServer _overlay = new(port: 39285);
        private bool _exiting;

        public TrayAppContext()
        {
            // Force the status form's window handle to exist now (without showing it) so
            // BeginInvoke works for cross-thread updates even while it's hidden.
            _ = _statusForm.Handle;

            _trayIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
                Text = Truncate("Apple Music -> Discord: " + PresenceBridge.CurrentStatus, 63),
                Visible = true,
            };
            _trayIcon.DoubleClick += (_, _) => _statusForm.ShowAndActivate();

            _autoStartItem = new ToolStripMenuItem("Start with Windows")
            {
                CheckOnClick = true,
                Checked = AutoStart.IsEnabled,
            };
            _autoStartItem.CheckedChanged += (_, _) => AutoStart.IsEnabled = _autoStartItem.Checked;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show status", null, (_, _) => _statusForm.ShowAndActivate());
            menu.Items.Add("Reconnect to Discord", null, (_, _) => PresenceBridge.ReconnectNow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Show song on overlay now (10s)", null, (_, _) => _overlay.ShowNow(TimeSpan.FromSeconds(10)));
            menu.Items.Add("Open OBS overlay in browser", null, (_, _) => OpenOverlayInBrowser());
            menu.Items.Add("Copy OBS overlay URL", null, (_, _) => CopyOverlayUrl());
            menu.Items.Add("Copy OBS bar overlay URL", null, (_, _) => CopyOverlayUrl("?style=bar"));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_autoStartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitApp());
            _trayIcon.ContextMenuStrip = menu;

            AppLog.MessageLogged += OnLogMessage;
            PresenceBridge.StatusUpdated += OnStatusUpdated;

            AutoStart.EnsureAppliedOnStartup();
            PresenceBridge.Start();
            StartOverlayServer();

            // Safety net if Windows terminates the process directly (logoff/shutdown).
            AppDomain.CurrentDomain.ProcessExit += (_, _) => PresenceBridge.Shutdown();
        }

        private void StartOverlayServer()
        {
            try
            {
                _overlay.Start();
                AppLog.Write($"OBS overlay available at {_overlay.Url}");
            }
            catch (Exception ex)
            {
                // Most likely cause: port 39285 is already in use (another instance of
                // this app, or something else on the machine). The Discord side of the
                // app still works fine without this.
                AppLog.Write($"Could not start the OBS overlay server: {ex.Message}");
            }
        }

        private void OpenOverlayInBrowser()
        {
            try { Process.Start(new ProcessStartInfo(_overlay.Url) { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Write($"Could not open the overlay URL: {ex.Message}"); }
        }

        private void CopyOverlayUrl(string query = "")
        {
            var url = _overlay.Url + query;
            try { Clipboard.SetText(url); AppLog.Write($"Copied overlay URL: {url}"); }
            catch (Exception ex) { AppLog.Write($"Could not copy the overlay URL: {ex.Message}"); }
        }

        private void OnLogMessage(string line)
        {
            if (_statusForm.IsDisposed) return;
            _statusForm.BeginInvoke((MethodInvoker)(() => _statusForm.AppendLog(line)));
        }

        private void OnStatusUpdated()
        {
            if (_statusForm.IsDisposed) return;
            _statusForm.BeginInvoke((MethodInvoker)(() =>
            {
                _statusForm.SetStatus(PresenceBridge.CurrentStatus);
                _trayIcon.Text = Truncate("Apple Music -> Discord: " + PresenceBridge.CurrentStatus, 63);
            }));
        }

        private void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;

            PresenceBridge.Shutdown();
            AppLog.MessageLogged -= OnLogMessage;
            PresenceBridge.StatusUpdated -= OnStatusUpdated;
            _overlay.Stop();

            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            _statusForm.AllowRealClose();
            _statusForm.Close();

            ExitThreadCore();
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    }
}
