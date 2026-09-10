using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// Resolves cover art to a public HTTPS URL via the iTunes Search API
    /// (https://itunes.apple.com/search). Discord's media proxy fetches Rich Presence
    /// image URLs server-side, so the art has to live somewhere it can actually reach -
    /// a localhost URL or the raw SMTC thumbnail bytes won't do.
    ///
    /// Results are cached per track. Returns null when there's no match or the lookup
    /// fails, in which case the caller falls back to a static asset key.
    /// </summary>
    public static class AlbumArtLookup
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly Dictionary<string, string?> Cache = new();
        private static readonly object CacheLock = new();

        static AlbumArtLookup()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("AppleMusicDiscordPresence/1.0");
        }

        /// <param name="size">Square edge length to request from Apple's CDN (it resizes on the fly).</param>
        public static async Task<string?> TryGetArtworkUrlAsync(string artist, string album, string title, int size = 512)
        {
            string cacheKey = $"{artist}{album}{title}";

            lock (CacheLock)
            {
                if (Cache.TryGetValue(cacheKey, out var cached))
                    return Resize(cached, size);
            }

            string? artwork = await QueryAsync(artist, album, title).ConfigureAwait(false);

            lock (CacheLock)
            {
                if (Cache.Count > 256) Cache.Clear(); // crude bound; track sets are small
                Cache[cacheKey] = artwork;
            }

            return Resize(artwork, size);
        }

        private static async Task<string?> QueryAsync(string artist, string album, string title)
        {
            // An album match gives the most stable art; fall back to the track itself.
            if (!string.IsNullOrWhiteSpace(album))
            {
                var byAlbum = await SearchAsync($"{artist} {album}", "album").ConfigureAwait(false);
                if (byAlbum != null) return byAlbum;
            }

            return await SearchAsync($"{artist} {title}", "song").ConfigureAwait(false);
        }

        private static async Task<string?> SearchAsync(string term, string entity)
        {
            try
            {
                var uri = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(term)}" +
                          $"&media=music&entity={entity}&limit=1";

                using var resp = await Http.GetAsync(uri).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("results", out var results)
                    || results.ValueKind != JsonValueKind.Array
                    || results.GetArrayLength() == 0)
                    return null;

                var first = results[0];
                if (first.TryGetProperty("artworkUrl100", out var art) && art.ValueKind == JsonValueKind.String)
                    return art.GetString();

                return null;
            }
            catch
            {
                return null; // offline, timeout, malformed response - caller uses the fallback
            }
        }

        // artworkUrl100 looks like ".../source/.../100x100bb.jpg"; Apple's CDN honours
        // other sizes in the same spot.
        private static string? Resize(string? artworkUrl100, int size)
            => artworkUrl100?.Replace("100x100bb", $"{size}x{size}bb");
    }
}
