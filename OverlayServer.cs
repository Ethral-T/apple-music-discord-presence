using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// Tiny local HTTP server for an OBS Browser Source: "/" serves a self-contained
    /// now-playing widget (art/title/artist/album/progress bar), which polls "/state"
    /// once a second for live data straight from Apple Music - independent of whatever
    /// Discord's rate limit is doing to the Rich Presence.
    /// </summary>
    internal sealed class OverlayServer
    {
        public int Port { get; }
        public string Url => $"http://127.0.0.1:{Port}/";

        private readonly HttpListener _listener = new();

        // Until when (UTC ticks) the overlay should show itself regardless of ?fade=.
        // Set by /show or the tray menu; reported to the page as "forceShow" in /state.
        private long _showUntilTicks;

        /// <summary>
        /// Pops the widget back up for a while even if its ?fade= timer has hidden it -
        /// e.g. when someone in chat asks what's playing.
        /// </summary>
        public void ShowNow(TimeSpan duration)
            => Interlocked.Exchange(ref _showUntilTicks, (DateTime.UtcNow + duration).Ticks);

        private bool ForceShowActive => DateTime.UtcNow.Ticks < Interlocked.Read(ref _showUntilTicks);

        public OverlayServer(int port)
        {
            Port = port;
            _listener.Prefixes.Add(Url);
        }

        public void Start()
        {
            _listener.Start();
            _ = Task.Run(ListenLoop);
        }

        public void Stop()
        {
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
        }

        private async Task ListenLoop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch (HttpListenerException) { break; }   // listener stopped
                catch (ObjectDisposedException) { break; }
                catch (Exception)
                {
                    await Task.Delay(100); // don't spin on a transient failure
                    continue;
                }

                // Handle off the accept loop so a slow /state lookup can't stall it.
                _ = HandleAsync(ctx);
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod != "GET")
                {
                    ctx.Response.StatusCode = 405;
                    ctx.Response.Close();
                    return;
                }

                switch (ctx.Request.Url?.AbsolutePath)
                {
                    case "/":
                    case "/index.html":
                        // ?style=bar swaps the card for the full-width bar variant; every other
                        // tag (?scale=, ?fade=, ?pos=) is read by whichever page is served.
                        bool bar = string.Equals(ctx.Request.QueryString["style"], "bar", StringComparison.OrdinalIgnoreCase);
                        await WriteAsync(ctx, "text/html; charset=utf-8", bar ? BarPage.Html : OverlayPage.Html);
                        break;

                    case "/state":
                        await WriteStateAsync(ctx);
                        break;

                    case "/show":
                        // GET /show or /show?seconds=15 - handy to wire to a Stream Deck
                        // button, a Streamer.bot "!song" command, or a browser bookmark.
                        int seconds = int.TryParse(ctx.Request.QueryString["seconds"], out var s) ? s : 10;
                        seconds = Math.Clamp(seconds, 1, 300);
                        ShowNow(TimeSpan.FromSeconds(seconds));
                        await WriteAsync(ctx, "application/json; charset=utf-8", $"{{\"ok\":true,\"seconds\":{seconds}}}");
                        break;

                    default:
                        ctx.Response.StatusCode = 404;
                        ctx.Response.Close();
                        break;
                }
            }
            catch (Exception)
            {
                try { ctx.Response.Abort(); } catch { /* ignore */ }
            }
        }

        private async Task WriteStateAsync(HttpListenerContext ctx)
        {
            var np = await PresenceBridge.GetNowPlayingAsync();

            object payload = np == null
                ? new { isPlaying = false }
                : new
                {
                    isPlaying = true,
                    title = np.Title,
                    artist = np.Artist,
                    album = np.Album,
                    artUrl = np.ArtUrl,
                    positionMs = (long)np.Position.TotalMilliseconds,
                    durationMs = (long)np.Duration.TotalMilliseconds,
                    forceShow = ForceShowActive,
                };

            await WriteAsync(ctx, "application/json; charset=utf-8", JsonSerializer.Serialize(payload));
        }

        private static async Task WriteAsync(HttpListenerContext ctx, string contentType, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }
}
