using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// Watches Apple Music's SMTC session and mirrors it to a Discord Rich Presence.
    /// Runs entirely on background threads (every await uses ConfigureAwait(false)) so
    /// it never ties up the WinForms UI thread that hosts the tray icon - the UI only
    /// hears about it through <see cref="AppLog"/> and <see cref="StatusUpdated"/>.
    /// </summary>
    internal static class PresenceBridge
    {
        // Your Discord application's Client ID - create one at
        // https://discord.com/developers/applications (see README.md), then set it from
        // the tray's "Show status" window (saved to AppSettings, never committed to
        // source or set as a machine/user environment variable). Without it, Rich
        // Presence is disabled but the OBS overlay still works.
        private const string PlaceholderClientId = "YOUR_DISCORD_CLIENT_ID_HERE";
        private static string _discordClientId = AppSettings.GetDiscordClientId() ?? PlaceholderClientId;
        private static bool DiscordConfigured => _discordClientId != PlaceholderClientId;

        /// <summary>The configured Client ID, or "" if none is set (for the settings UI).</summary>
        public static string DiscordClientIdForDisplay => DiscordConfigured ? _discordClientId : "";

        // Cancels whatever connect-retry loop is currently in flight when the Client ID
        // changes, so an old loop retrying a since-replaced ID doesn't linger.
        private static CancellationTokenSource? _discordConnectCts;
        private static int _discordWatchdogStarted;

        /// <summary>
        /// Called from the settings UI. Pass null/empty to clear it (falls back to
        /// overlay-only). Saves it, swaps in a fresh IPC client, and (re)connects -
        /// no restart needed.
        /// </summary>
        public static void SetDiscordClientId(string? id)
        {
            id = (id ?? string.Empty).Trim();

            _discordConnectCts?.Cancel();
            var old = Discord;

            if (id.Length == 0)
            {
                _discordClientId = PlaceholderClientId;
                AppSettings.SetDiscordClientId(null);
                Discord = new DiscordIpcClient(PlaceholderClientId);
                try { old.ClearActivity(); } catch { /* best effort */ }
                try { old.Dispose(); } catch { /* ignore */ }
                AppLog.Write("Discord Client ID cleared - Rich Presence disabled.");
                SetStatus("Overlay only - no Discord Client ID set");
                return;
            }

            _discordClientId = id;
            AppSettings.SetDiscordClientId(id);
            Discord = new DiscordIpcClient(id);
            try { old.Dispose(); } catch { /* ignore */ }

            AppLog.Write("Discord Client ID saved - connecting...");
            StartDiscordConnect();
        }

        // Apple Music for Windows' AppUserModelId. This is what's checked to find the
        // right media session among Spotify/Edge/etc. Verify it on your machine (README
        // shows how) and adjust if it doesn't match.
        private const string AppleMusicAumidFragment = "AppleInc.AppleMusicWin";

        // After the current track changes we wait for playback to "settle" this long
        // before pushing anything. It coalesces the little burst of events a single
        // track change produces.
        private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(3);

        // If a track change was pushed to Discord less than this ago, the next track
        // change is held until the window elapses. Deliberate, spaced-out changes still
        // publish after just the settle delay; rapid skipping publishes nothing until
        // you land on something for real. 15s is Discord's practical floor for IPC
        // SET_ACTIVITY before it starts throttling/dropping updates.
        private static readonly TimeSpan RapidChangeWindow = TimeSpan.FromSeconds(15);

        // Floor between any two IPC updates (e.g. clearing on pause), to stay well clear
        // of Discord's rate limit.
        private static readonly TimeSpan MinSendGap = TimeSpan.FromSeconds(10);

        private static DiscordIpcClient Discord = new(_discordClientId);

        // Sentinel key for "nothing is playing".
        private const string IdleKey = "\0idle";

        private static readonly object _sessionLock = new();
        private static GlobalSystemMediaTransportControlsSession? _session;

        // Debounce / publish state. Guarded by _debounceLock.
        private static readonly object _debounceLock = new();
        private static string? _pendingKey;      // state we're counting down to publish
        private static string? _publishedKey;    // state Discord is currently showing
        private static CancellationTokenSource? _debounceCts;
        private static DateTimeOffset _lastSendUtc = DateTimeOffset.MinValue;

        private static readonly SemaphoreSlim _publishGate = new(1, 1);
        private static int _shuttingDown;
        private static int _started;

        /// <summary>Short one-line status for the tray tooltip / status window header.</summary>
        public static string CurrentStatus { get; private set; } = "Starting...";
        public static event Action? StatusUpdated;

        /// <summary>Begins connecting and watching. Safe to call once; returns immediately.</summary>
        public static void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            _ = Task.Run(RunAsync);
        }

        private static async Task RunAsync()
        {
            AppLog.Write("Apple Music -> Discord Rich Presence bridge starting.");

            // The media session drives both Discord AND the OBS overlay, so attach to it
            // first and unconditionally.
            GlobalSystemMediaTransportControlsSessionManager manager;
            try
            {
                manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            }
            catch (Exception ex)
            {
                AppLog.Write($"Could not access the Windows media session manager: {ex.Message}");
                SetStatus("Failed to start (see log)");
                return;
            }

            manager.SessionsChanged += (_, _) => AttachToAppleMusicSession(manager);
            AttachToAppleMusicSession(manager);

            // SessionsChanged is the "should" way to hear about Apple Music opening,
            // closing, or restarting - but it's a known-flaky WinRT event (it can simply
            // stop firing after a while, especially across sleep/wake or when the app
            // restarts) and when it does, _session is left pointing at a session that no
            // longer reflects reality, so everything downstream quietly reports "nothing
            // playing" forever. This just re-checks GetSessions() on a timer regardless -
            // a no-op if nothing's actually changed, a self-heal if the event went missing.
            _ = Task.Run(SessionWatchdogAsync);

            if (!DiscordConfigured)
            {
                AppLog.Write("No Discord Client ID set yet - Rich Presence is disabled, but the OBS overlay still works. Set one from the tray's \"Show status\" window.");
                SetStatus("Overlay only - no Discord Client ID set");
                return;
            }

            StartDiscordConnect();
        }

        /// <summary>
        /// (Re)starts the connect-and-watch flow against whatever `Discord` currently
        /// is. Cancels any connect loop already in flight first, so calling this again
        /// after SetDiscordClientId changes the client cleanly supersedes the old one.
        /// </summary>
        private static void StartDiscordConnect()
        {
            _discordConnectCts?.Cancel();
            var cts = new CancellationTokenSource();
            _discordConnectCts = cts;
            _ = Task.Run(() => ConnectAndWatchDiscordAsync(cts.Token));
        }

        private static async Task ConnectAndWatchDiscordAsync(CancellationToken token)
        {
            var discord = Discord; // snapshot: keep talking to the client this loop was started for

            SetStatus("Connecting to Discord...");
            while (!discord.Connect())
            {
                AppLog.Write($"Discord connect failed: {discord.LastConnectError}");
                if (Volatile.Read(ref _shuttingDown) != 0 || token.IsCancellationRequested) return;
                try { await Task.Delay(5000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
            if (token.IsCancellationRequested) return;

            AppLog.Write("Connected to Discord.");
            SetStatus("Connected - watching for Apple Music");

            if (Interlocked.Exchange(ref _discordWatchdogStarted, 1) == 0)
                _ = Task.Run(ConnectionWatchdogAsync);

            await ForceResyncAsync().ConfigureAwait(false); // push whatever's already playing
            AppLog.Write("Watching for Apple Music playback.");
        }

        private static void SetStatus(string status)
        {
            CurrentStatus = status;
            StatusUpdated?.Invoke();
        }

        private static void AttachToAppleMusicSession(GlobalSystemMediaTransportControlsSessionManager manager)
        {
            lock (_sessionLock)
            {
                GlobalSystemMediaTransportControlsSession? target = null;
                try
                {
                    foreach (var s in manager.GetSessions())
                    {
                        if (s.SourceAppUserModelId.Contains(AppleMusicAumidFragment, StringComparison.OrdinalIgnoreCase))
                        {
                            target = s;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Write($"Failed to enumerate media sessions: {ex.Message}");
                    return;
                }

                if (ReferenceEquals(target, _session))
                    return;

                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnMediaChanged;
                    _session.PlaybackInfoChanged -= OnPlaybackChanged;
                }

                _session = target;

                if (_session == null)
                {
                    AppLog.Write("Apple Music session ended.");
                    QueueUpdate();
                    return;
                }

                _session.MediaPropertiesChanged += OnMediaChanged;
                _session.PlaybackInfoChanged += OnPlaybackChanged;
                AppLog.Write("Attached to Apple Music session.");
                QueueUpdate();
            }
        }

        private static void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e)
            => QueueUpdate();

        private static void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
            => QueueUpdate();

        // Kick evaluation onto the thread pool so event callbacks return immediately and
        // we never run presence logic while holding _sessionLock.
        private static void QueueUpdate() => _ = Task.Run(EvaluateAsync);

        /// <summary>
        /// Snapshots the current playback state and, if it differs from what Discord is
        /// showing, schedules a debounced publish. A different state arriving before the
        /// timer elapses cancels this one and restarts the countdown, so nothing is
        /// pushed until playback holds still.
        /// </summary>
        private static async Task EvaluateAsync()
        {
            try
            {
                var snapshot = await CaptureAsync().ConfigureAwait(false);
                string key = snapshot?.Key ?? IdleKey;

                CancellationToken token;
                lock (_debounceLock)
                {
                    if (key == _publishedKey)
                    {
                        // Whatever changed, we're back to what's already on screen.
                        _pendingKey = null;
                        _debounceCts?.Cancel();
                        return;
                    }
                    if (key == _pendingKey)
                        return; // already counting down for this exact state

                    _pendingKey = key;
                    _debounceCts?.Cancel();
                    _debounceCts = new CancellationTokenSource();
                    token = _debounceCts.Token;
                }

                try
                {
                    await Task.Delay(SettleDelay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return; // superseded by a newer state
                }

                await PublishAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Write($"Presence evaluation failed: {ex.Message}");
            }
        }

        private static async Task PublishAsync(string expectedKey, CancellationToken token)
        {
            try { await _publishGate.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                // Re-read: position and artwork may have moved on during the wait.
                var snapshot = await CaptureAsync().ConfigureAwait(false);
                string key = snapshot?.Key ?? IdleKey;

                if (!StillCurrent(expectedKey, key, token))
                    return;

                // Rate limiting: a track change is held back if we pushed one recently;
                // a clear (pause/stop) only honours the smaller floor.
                DateTimeOffset lastSend;
                lock (_debounceLock) lastSend = _lastSendUtc;
                var floor = snapshot == null ? MinSendGap : RapidChangeWindow;
                var sinceLast = DateTimeOffset.UtcNow - lastSend;
                if (sinceLast < floor)
                {
                    try { await Task.Delay(floor - sinceLast, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    if (!StillCurrent(expectedKey, key, token))
                        return;
                }

                bool ok;
                if (snapshot == null)
                {
                    ok = Discord.ClearActivity();
                    if (ok)
                    {
                        AppLog.Write("Cleared presence (nothing playing).");
                        SetStatus("Connected - nothing playing");
                    }
                }
                else
                {
                    string? artUrl = await AlbumArtLookup
                        .TryGetArtworkUrlAsync(snapshot.Artist, snapshot.Album, snapshot.Title)
                        .ConfigureAwait(false);
                    if (artUrl == null)
                        AppLog.Write("No album art match; using the fallback icon.");

                    ok = Discord.SetActivity(BuildActivity(snapshot, artUrl));
                    if (ok)
                    {
                        AppLog.Write($"Updated presence: {snapshot.Title} - {snapshot.Artist}");
                        SetStatus($"Now playing: {snapshot.Title} - {snapshot.Artist}");
                    }
                }

                if (!ok)
                {
                    AppLog.Write("Discord IPC send failed; will resync on reconnect.");
                    SetStatus("Discord connection lost");
                    return;
                }

                lock (_debounceLock)
                {
                    _publishedKey = key;
                    if (_pendingKey == key) _pendingKey = null;
                    _lastSendUtc = DateTimeOffset.UtcNow;
                }
            }
            finally
            {
                _publishGate.Release();
            }
        }

        private static bool StillCurrent(string expectedKey, string key, CancellationToken token)
        {
            if (token.IsCancellationRequested || key != expectedKey)
                return false;
            lock (_debounceLock)
                return key == _pendingKey;
        }

        private static object BuildActivity(Snapshot s, string? artUrl)
        {
            var assets = new Dictionary<string, object?>
            {
                // Falls back to a static asset key (upload one named apple_music_logo in
                // your Discord app's Rich Presence art settings) if there's no art.
                ["large_image"] = artUrl ?? "apple_music_logo",
                ["large_text"] = Clamp(string.IsNullOrWhiteSpace(s.Album) ? "Apple Music" : s.Album, 128),
                ["small_image"] = "apple_music_logo_small",
                ["small_text"] = "Apple Music",
            };

            var activity = new Dictionary<string, object?>
            {
                ["type"] = 2, // 2 = "Listening to ..."
                ["details"] = Clamp(s.Title, 128),
                ["state"] = Clamp(s.Artist, 128),
                ["assets"] = assets,
            };

            // "start" = when the track effectively began (now minus elapsed), "end" = when
            // it will finish. That's what gives Discord its little countdown/progress bar.
            // Only sent when we actually have a duration (streams/podcasts may not).
            if (s.Duration > TimeSpan.Zero)
            {
                var now = DateTimeOffset.UtcNow;
                activity["timestamps"] = new
                {
                    start = now.Subtract(s.Position).ToUnixTimeMilliseconds(),
                    end = now.Subtract(s.Position).Add(s.Duration).ToUnixTimeMilliseconds(),
                };
            }

            return activity;
        }

        private static string Clamp(string value, int max) => value.Length <= max ? value : value[..max];

        /// <summary>Live now-playing info for the OBS overlay endpoint.</summary>
        internal sealed record NowPlayingInfo(
            string Title, string Artist, string Album, string? ArtUrl, TimeSpan Position, TimeSpan Duration);

        /// <summary>
        /// Unlike the state pushed to Discord, this reflects Apple Music at the instant
        /// it's called, with no debounce - the overlay isn't subject to Discord's rate
        /// limit, so it's free to poll this every second for a live progress bar.
        /// </summary>
        public static async Task<NowPlayingInfo?> GetNowPlayingAsync()
        {
            var snapshot = await CaptureAsync().ConfigureAwait(false);
            if (snapshot == null) return null;

            string? artUrl = await AlbumArtLookup
                .TryGetArtworkUrlAsync(snapshot.Artist, snapshot.Album, snapshot.Title)
                .ConfigureAwait(false);

            return new NowPlayingInfo(snapshot.Title, snapshot.Artist, snapshot.Album, artUrl, snapshot.Position, snapshot.Duration);
        }

        private sealed record Snapshot(
            string Key,
            string Title,
            string Artist,
            string Album,
            TimeSpan Position,
            TimeSpan Duration);

        /// <summary>
        /// Reads the current Apple Music state. Returns null when nothing is playing or
        /// the session can't be read (it may vanish mid-call). The key covers
        /// title/artist/album only - seeking within a track is not a "change".
        /// </summary>
        private static async Task<Snapshot?> CaptureAsync()
        {
            GlobalSystemMediaTransportControlsSession? session;
            lock (_sessionLock) session = _session;
            if (session == null) return null;

            try
            {
                if (session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return null;

                var props = await session.TryGetMediaPropertiesAsync();
                var timeline = session.GetTimelineProperties();

                string title = string.IsNullOrWhiteSpace(props.Title) ? "Unknown Title" : props.Title;
                string artist = string.IsNullOrWhiteSpace(props.Artist) ? "Unknown Artist" : props.Artist;
                string album = string.IsNullOrEmpty(props.AlbumTitle) ? string.Empty : props.AlbumTitle;
                (artist, album) = UnscrambleSingleMetadata(artist, album);

                var duration = timeline.EndTime - timeline.StartTime;
                var position = timeline.Position - timeline.StartTime;
                if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
                if (position < TimeSpan.Zero) position = TimeSpan.Zero;

                string key = $"{title}{artist}{album}";
                return new Snapshot(key, title, artist, album, position, duration);
            }
            catch (Exception ex)
            {
                AppLog.Write($"Could not read media properties: {ex.Message}");
                return null;
            }
        }

        // Apple Music for Windows sometimes reports singles with the real album name
        // folded into the Artist field instead of the (blank) Album field, e.g.
        //   Artist = "Said The Sky, ILLENIUM & Heather Sommer — When The Light Breaks - Single"
        //   Album  = ""
        // instead of Artist = "Said The Sky, ILLENIUM & Heather Sommer", Album = "When
        // The Light Breaks - Single". The em dash is the tell - it's how Apple Music's
        // own UI separates artist from release subtitle, and it's not something that
        // otherwise shows up in an artist name - so only split on it, and only when the
        // album is already blank (never overwrite real album metadata).
        private const string ScrambledArtistAlbumSeparator = " — ";

        private static (string Artist, string Album) UnscrambleSingleMetadata(string artist, string album)
        {
            if (!string.IsNullOrEmpty(album)) return (artist, album);

            int idx = artist.IndexOf(ScrambledArtistAlbumSeparator, StringComparison.Ordinal);
            if (idx <= 0) return (artist, album);

            string realArtist = artist[..idx].Trim();
            string realAlbum = artist[(idx + ScrambledArtistAlbumSeparator.Length)..].Trim();
            if (realArtist.Length == 0 || realAlbum.Length == 0) return (artist, album);

            return (realArtist, realAlbum);
        }

        /// <summary>
        /// Safety net for AttachToAppleMusicSession: periodically re-checks for the
        /// current Apple Music session even without a SessionsChanged event, using a
        /// freshly-requested manager each time rather than trusting the one obtained at
        /// startup indefinitely. SessionsChanged is a known-flaky WinRT event - it can
        /// simply stop firing after a while (app restarts, sleep/wake), and a long-held
        /// manager or session reference can go stale the same way - either of which
        /// leaves the app stuck reporting "nothing playing" forever with no way to
        /// notice on its own. This makes that self-heal within one interval instead.
        /// </summary>
        private static async Task SessionWatchdogAsync()
        {
            while (Volatile.Read(ref _shuttingDown) == 0)
            {
                await Task.Delay(10_000).ConfigureAwait(false);
                try
                {
                    var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    AttachToAppleMusicSession(manager);
                }
                catch (Exception ex)
                {
                    AppLog.Write($"Session watchdog check failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Watches the IPC connection and reconnects if Discord restarts, then re-pushes
        /// the current state immediately (bypassing the settle delay).
        /// </summary>
        private static async Task ConnectionWatchdogAsync()
        {
            while (Volatile.Read(ref _shuttingDown) == 0)
            {
                await Task.Delay(5000).ConfigureAwait(false);
                if (!DiscordConfigured || Discord.IsConnected) continue;

                AppLog.Write("Discord connection lost; reconnecting...");
                SetStatus("Reconnecting to Discord...");
                if (!Discord.Connect())
                {
                    AppLog.Write($"Reconnect failed: {Discord.LastConnectError}");
                    continue;
                }

                AppLog.Write("Reconnected to Discord.");
                await ForceResyncAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Forces a fresh push of whatever's playing now, bypassing debounce state.</summary>
        private static async Task ForceResyncAsync()
        {
            CancellationToken token;
            var snapshot = await CaptureAsync().ConfigureAwait(false);
            string key = snapshot?.Key ?? IdleKey;
            lock (_debounceLock)
            {
                _publishedKey = null; // force a re-push of whatever's playing now
                _lastSendUtc = DateTimeOffset.MinValue;
                _debounceCts?.Cancel();
                _debounceCts = new CancellationTokenSource();
                _pendingKey = key;
                token = _debounceCts.Token;
            }

            try { await PublishAsync(key, token).ConfigureAwait(false); }
            catch (Exception ex) { AppLog.Write($"Resync failed: {ex.Message}"); }
        }

        /// <summary>Manual "Reconnect now" action for the tray menu.</summary>
        public static void ReconnectNow()
        {
            if (!DiscordConfigured)
            {
                AppLog.Write("No Discord Client ID set - nothing to reconnect. Set one from \"Show status\".");
                return;
            }
            AppLog.Write("Manual reconnect requested.");
            StartDiscordConnect();
        }

        public static void Shutdown()
        {
            if (Interlocked.Exchange(ref _shuttingDown, 1) != 0) return;
            try { Discord.ClearActivity(); } catch { /* ignore */ }
            try { Discord.Dispose(); } catch { /* ignore */ }
            AppLog.Write("Shut down, presence cleared.");
        }
    }
}
