# Apple Music Presence: user guide

Last updated Oct 12, 2026

> The guide for using the app day to day. [Back to the project page](../README.md)

Apple Music Presence shows what Apple Music is playing on your **Discord** profile and in an **OBS** overlay. It runs in the Windows system tray.

---

## Quick start

1. Build and run it (see the [README](../README.md#-get-it-running)). It starts in the tray, with no window.
2. Right-click the tray icon and choose **Show status**.
3. Paste your Discord **Application ID** into the **Discord Client ID** box and press **Save**.
4. Play something in Apple Music. After a few seconds it appears on your Discord profile.

The OBS overlay needs no setup at all. It works as soon as the app is running.

---

## The tray menu

| Item | What it does |
| :-- | :-- |
| **Show status** | Opens the status window. |
| **Reconnect to Discord** | Tries the Discord connection again. |
| **Show song on overlay now (10s)** | Brings the overlay back for 10 seconds, even if `?fade=` has hidden it. |
| **Open OBS overlay in browser** | Opens the card overlay so you can preview it. |
| **Copy OBS overlay URL** | Copies `http://127.0.0.1:39285/`. |
| **Copy OBS bar overlay URL** | Copies `http://127.0.0.1:39285/?style=bar`. |
| **Start with Windows** | Starts the app when you sign in. Your choice is remembered. |
| **Exit** | Clears your Discord presence, then quits. |

---

## The status window

From top to bottom:

- **Discord Client ID** box and **Save**. It connects as soon as you save. Clear the box and save to turn Discord off (the overlay keeps working).
- **Status line**, in plain words, such as *Connected - nothing playing* or *Now playing: Title - Artist*.
- **Reconnect to Apple Music** button, for when the app has lost track of Apple Music (for example after Apple Music restarts).
- **Log** of what the app is doing. Closing the window just hides it. The app keeps running in the tray.

---

## Discord

1. Create an application at <https://discord.com/developers/applications> and copy its **Application ID**.
2. Save it in the status window.
3. Optional: under **Rich Presence → Art Assets** upload `apple_music_logo` and `apple_music_logo_small`. They're the fallback cover and the corner badge.

Updates wait about 3 seconds after a song settles, and if one was just sent they wait up to 15 seconds, so skipping around doesn't spam your presence. Pausing clears it.

Your presence only shows to other people if Discord's **Settings → Activity Privacy → Share your detected activities** is on.

---

## The OBS overlay

Add a **Browser Source** in OBS with the URL `http://127.0.0.1:39285/`. Set its Width and Height to your canvas size and leave them there. Untick *Shutdown source when not visible*.

### Tags

Add tags after a `?`, and join them with `&`.

| Tag | What it does |
| :-- | :-- |
| `style=bar` | The full-width bar instead of the card. |
| `scale=1.5` | Scales everything together, from `0.2` to `6`. |
| `fade=20` | Hides it once the song has played 20 seconds. It returns when the next song starts. |
| `pos=top`, `pos=center`, `pos=bottom` | Vertical placement. The card defaults to center, the bar to the bottom. |
| `progress=off` | Removes the duration bar. |

Example: `http://127.0.0.1:39285/?style=bar&pos=top&scale=1.2&fade=25`

### Resizing

Resize with `scale=`, not by dragging the source in OBS. OBS stretches a Browser Source's fixed-size picture, which makes it blurry, and a page can't prevent that.

### Bringing it back after a fade

Use **Show song on overlay now (10s)** in the tray, or send a GET request to `http://127.0.0.1:39285/show`. Add `?seconds=15` for a different length (1 to 300).

**Streamer.bot:** make an action with a **Core → Network → Fetch URL** sub-action pointing at the `/show` address, then a `!song` command that triggers it. Give the command a cooldown so chat can't spam it.

**Stream Deck:** any button that sends a web request (GET) to the `/show` address will do.

---

## Troubleshooting

| Problem | Fix |
| :-- | :-- |
| *Overlay only - no Discord Client ID set* | Save your Application ID in the status window. |
| *Connected*, but nothing on your profile | Turn on **Activity Privacy → Share your detected activities** in Discord. |
| The cover is the fallback icon | The iTunes search found no match. Upload an `apple_music_logo` art asset. |
| Nothing detected as playing | Press **Reconnect to Apple Music**. If the Win+Z media flyout also shows nothing, restart Windows. |
| The overlay is blurry in OBS | Keep the source at your canvas size and use `?scale=`. |
| The overlay vanished | That's `?fade=`. Recall it with **Show song on overlay now**. |
| The overlay won't start | Port `39285` is taken. Change it in `TrayAppContext.cs`. |
