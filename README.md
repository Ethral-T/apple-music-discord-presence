# Apple Music → Discord Rich Presence

A small Windows tray app that watches the native "now playing" media session
(SMTC — the same thing behind the volume-flyout media widget) for **Apple Music**
and:

- pushes it to **Discord** as a `Listening to…` Rich Presence (with real cover
  art and a progress bar), over Discord's local IPC pipe, and
- serves a **local now-playing overlay** you can drop straight into OBS as a
  Browser Source.

No Apple Music API, no Discord bot, no account login — it only reads what Windows
already knows about the current track.

## Requirements

- Windows 10 1903+ / Windows 11 (the SMTC APIs are Windows-only)
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (only needed to build)
- Apple Music for Windows, and the Discord desktop client (for the presence half)

## Quick start

```powershell
git clone https://github.com/Ethral-T/apple-music-discord-presence.git
cd apple-music-discord-presence
dotnet build -c Release
dotnet run -c Release
```

It starts minimised to the **system tray** (no window). Right-click the tray
icon for status, the overlay URL, an autostart toggle, and Exit.

The **OBS overlay works immediately** with no further setup. The **Discord**
half needs a one-time Discord application (below); until you set that up, the
tray just says "Overlay only — no Discord Client ID" and the Discord side stays
dormant.

## Discord setup (one time)

1. Go to <https://discord.com/developers/applications> → **New Application**.
   Name it whatever you want Discord to show after "Listening to" (e.g.
   `Apple Music`).
2. Copy the **Application ID** (a.k.a. Client ID) from the General Information
   page.
3. In the app, right-click the tray icon → **Show status**, paste it into the
   **Discord Client ID** box at the top, and click **Save**. It connects
   immediately — no restart needed.
4. Optional but recommended — under **Rich Presence → Art Assets**, upload two
   images with these exact asset key names:
   - `apple_music_logo` — shown as the large image when cover-art lookup fails
   - `apple_music_logo_small` — the small badge in the corner of the art

No app verification or OAuth is needed, and it reconnects on its own if
Discord restarts. Your Client ID is saved to a local settings file
(`%APPDATA%\AppleMusicDiscordPresence\settings.json`) — never as a
machine/user environment variable, and never committed to source. It's not
actually a secret (every Rich Presence embeds it, and Discord's docs treat
Client IDs as public), but keeping it local and out of your environment
variables means it can't leak into every other process's environment.

If you'd rather not type it through the UI: stop the app, create/edit that
settings file yourself with `{"discordClientId": "<your ID>"}` (merge it in if
the file already has other keys, e.g. `"autoStart"`), then start the app.

### What it looks like in Discord

```
Listening to <your app name>
[cover art]  Song Title
             Artist Name
             0:42 ───●──────── 3:15
```

`type = 2` is what makes Discord say "Listening to…" instead of "Playing…". The
progress bar is derived from `timestamps.start` / `end`.

## OBS overlay

The tray menu's **Open OBS overlay in browser** / **Copy OBS overlay URL** items
point at `http://127.0.0.1:39285/` — a self-contained now-playing widget (art,
title, artist, album, live progress bar, slide-in animation on track change).

1. In OBS: **Sources → + → Browser**.
2. URL: `http://127.0.0.1:39285/`. Set Width/Height to your OBS canvas
   resolution (Settings → Video) and leave them there.
3. Uncheck "Shutdown source when not visible" so it keeps polling across scene
   switches.

**Resize it with the URL, not the OBS canvas handles.** A Browser Source renders
at one fixed resolution and OBS stretches that bitmap to whatever size you drag
it to — that blur happens in OBS's compositor, after the page has rendered, so
nothing the page does can prevent it. Instead:

| Query param | Effect |
|---|---|
| `?scale=1.5` | Scale the whole widget (`0.2`–`6`). Stays crisp because the source resolution never changes. |
| `?fade=20` | Hide the widget once the current song has been playing `20` seconds; it reappears the instant a new song starts. Keyed off real playback position, so it's correct even if OBS reloads the source mid-song. |

Combine them: `http://127.0.0.1:39285/?scale=1.4&fade=25`

**Bar variant.** Add `?style=bar` for a full-width strip instead of the card: no
artwork, just "Title — Artist" across the page, with the bar itself filling
left-to-right as the song plays. `?scale=` and `?fade=` (and the recall below)
work the same, e.g. `http://127.0.0.1:39285/?style=bar&scale=1.2&fade=25`. The
tray menu has **Copy OBS bar overlay URL** for it.

**Position.** `?pos=top`, `?pos=center` or `?pos=bottom` sets where the widget
sits vertically, on either layout (the card is always centered horizontally; the
bar spans the full width). Defaults: the card is centered, the bar is at the
bottom. Example: `http://127.0.0.1:39285/?style=bar&pos=top`.

**Duration bar.** `?progress=off` removes it: on the card the progress bar
disappears (and the card gets a little shorter), on `?style=bar` the fill goes and
you're left with a plain strip carrying the text. Example:
`http://127.0.0.1:39285/?style=bar&progress=off`.

**Recalling the widget after it has faded** (e.g. someone asks what's playing):
right-click the tray icon → **Show song on overlay now (10s)**, or hit
`http://127.0.0.1:39285/show` (optionally `?seconds=15`, 1–300). That endpoint is
a plain GET, so it can be wired to a Stream Deck button, a Streamer.bot `!song`
command ("Fetch URL" action), or a browser bookmark. The widget slides back in
within about a second and fades out again on its own once the time is up.

### Overlay JSON

The page polls `GET /state` once a second; you can hit the same endpoint to
build your own widget:

```json
{
  "isPlaying": true,
  "title": "...", "artist": "...", "album": "...",
  "artUrl": "https://...", "positionMs": 12345, "durationMs": 210000
}
```

(`{ "isPlaying": false }` when nothing is playing.) Unlike the Discord presence,
this is **not** debounced or rate-limited — it reflects playback essentially
live, including the progress bar.

## Update timing (Discord side)

To stay under Discord's IPC rate limit and to not spam your presence while you
skip around, presence updates are debounced (constants in `PresenceBridge.cs`):

- A track change is pushed **~3 s** after playback settles on it (`SettleDelay`).
- If a track change was pushed **< 15 s** ago, the next one waits out the window
  (`RapidChangeWindow`). Skip through ten songs and Discord sees none of them
  until you land on one.
- Only title/artist/album changes count. Seeking within a track isn't re-pushed,
  so the progress bar is set once per track and won't jump if you scrub.
- Pausing / stopping clears the presence after the settle delay.

## How it works

| File | Role |
|---|---|
| `Program.cs` | WinForms entry point; hands off to the tray app. |
| `TrayAppContext.cs` | The tray icon, its menu, and the status window; owns the app's lifetime. |
| `StatusForm.cs` | The "Show status" window — Discord Client ID entry, current status, and a live log. |
| `PresenceBridge.cs` | Reads the SMTC session, debounces, and drives the Discord presence. |
| `DiscordIpcClient.cs` | Minimal `discord-ipc-N` named-pipe client: handshake + `SET_ACTIVITY`. |
| `AlbumArtLookup.cs` | Resolves cover art to a public HTTPS URL via the iTunes Search API. |
| `OverlayServer.cs` | The local HTTP server for the OBS overlay (`/` and `/state`). |
| `OverlayPage.cs` | The overlay's single-file HTML/CSS/JS. |
| `AutoStart.cs` | The "Start with Windows" registry entry. |
| `AppSettings.cs` | The shared `settings.json` store (autostart preference, Discord Client ID). |
| `AppLog.cs` | Tiny log pub/sub the status window subscribes to. |

Cover art is looked up separately because Discord fetches Rich Presence image
URLs **server-side** — it can't reach a `127.0.0.1` URL or a local file, so the
art has to be a public URL (Apple's own CDN, via iTunes Search).

## Known limitations

- **No "Listen Along" button.** That's special-cased by Discord for its
  first-party Spotify integration; a generic Rich Presence can't add it.
- **Cover art depends on an iTunes Search match.** Obscure releases or metadata
  that doesn't match Apple's catalog can miss; it then falls back to the
  `apple_music_logo` asset. Needs outbound access to `itunes.apple.com`.
- **Apple Music metadata quirks.** Apple Music for Windows sometimes reports a
  single's album name folded into the artist field; the app unscrambles the
  common `Artist — Album` form, but genuinely wrong metadata from Apple Music
  passes through as-is.
- **AUMID matching is a substring check** (`AppleInc.AppleMusicWin`). If a future
  Apple Music update changes its package identity, update `AppleMusicAumidFragment`
  in `PresenceBridge.cs`.
- **One Discord client at a time.** With multiple installs (Stable + PTB +
  Canary) it attaches to whichever owns the first pipe it can open.
- **Overlay port `39285`** is fixed; if it's taken, the tray log says so and the
  Discord side keeps working. Change it in `TrayAppContext.cs` if needed.
- **If nothing is ever detected as playing**, even with Apple Music open and
  playing, Windows' own media-session broker (the same thing behind the Win+Z
  media flyout) may itself be stuck - check whether that flyout shows your
  current track either. If it doesn't, that's a Windows-level issue outside
  anything this app touches; restarting Windows resets it. Otherwise, use the
  **Reconnect to Apple Music** button in **Show status** to re-attach without
  restarting the whole app.

## Trademarks & affiliation

Not affiliated with, endorsed by, or sponsored by Apple Inc. or Discord Inc.
"Apple Music" and the Apple Music logo are trademarks of Apple Inc.; "Discord"
is a trademark of Discord Inc. This repository ships **no** icons or artwork —
provide your own `AppIcon.ico` (the `.csproj` picks it up automatically if
present) and your own Discord Rich Presence art assets. You are responsible for
the rights to any images you add.

## License

[PolyForm Noncommercial License 1.0.0](LICENSE) — free to use, modify, and share
for any **noncommercial** purpose. You may **not** sell it or use it
commercially.
