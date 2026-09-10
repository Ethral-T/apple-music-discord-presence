using System;
using System.Net;
using System.Text;
using System.Text.Json;
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

        private static async Task HandleAsync(HttpListenerContext ctx)
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
                        await WriteAsync(ctx, "text/html; charset=utf-8", OverlayPage.Html);
                        break;

                    case "/state":
                        await WriteStateAsync(ctx);
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

        private static async Task WriteStateAsync(HttpListenerContext ctx)
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
