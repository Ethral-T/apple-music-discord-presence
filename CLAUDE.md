# Notes for Claude

Working notes for any Claude session that opens this repo. The owner uses the app on their own Windows PC, mostly for streaming. The repo is public.

## The pieces

- `PresenceBridge.cs` is the core: it reads Apple Music's media session, debounces, and drives the Discord presence. It's a static class with no UI code in it.
- `DiscordIpcClient.cs` is the `discord-ipc-N` named-pipe client (handshake, `SET_ACTIVITY`, ping/pong).
- `OverlayServer.cs` serves the OBS overlay on `http://127.0.0.1:39285/`. Routes: `/` (the card, or the bar with `?style=bar`), `/state` (JSON, includes `forceShow`) and `/show[?seconds=N]`.
- `OverlayPage.cs` (card) and `BarPage.cs` (bar) are each one self-contained HTML page held in a C# raw string literal.
- `TrayAppContext.cs` owns the tray icon, its menu and the app's lifetime. `StatusForm.cs` is the **Show status** window.
- `AppSettings.cs` is the one `settings.json` store (autostart and the Discord Client ID). `AutoStart.cs` writes the Run-key entry. `AppLog.cs` is a tiny log the status window subscribes to.
- `docs/USER_GUIDE.md` is the user guide. **Update it with every feature change.**
- `CHANGELOG.md`: add an entry for each change, newest first, with a date version (`YYYY.MM.DD`). The README's version badge should match the newest entry.
- `docs/images/` holds the README art. The pictures are original illustrations: **no Apple or Discord logos**.

## House rules (from the owner)

- **Never run `git commit`, `git push` or `git merge` unless the owner says so in that specific message.** Finishing a change isn't permission to commit it.
- **Don't verify visual changes yourself.** No screenshots, zooming or opening the browser pane to check UI. The owner looks at it directly, and it's much faster. Only do it if they say they can't check something. Numeric or HTTP checks (call `/state`, confirm served content) are fine.
- A browser source caches its page. If a layout change "didn't work", the source may just need a refresh.
- **The Discord Client ID never goes in source, in an environment variable, or in the repo.** It's saved by the **Show status** window into `%APPDATA%\AppleMusicDiscordPresence\settings.json`. The placeholder is `YOUR_DISCORD_CLIENT_ID_HERE`.
- **No Apple or Discord artwork in the repo.** `AppleMusic.png` and `AppIcon.ico` are gitignored on purpose.
- The license is PolyForm Noncommercial 1.0.0. Don't change it.
- The overlay server only answers `127.0.0.1`. The only call the app makes to the internet is the iTunes Search lookup for cover art.
- **No background polling of the media session.** Re-attaching is the **Reconnect to Apple Music** button. (Polling doesn't help when Windows' own media broker is stuck, and it isn't free.)
- In the status window, every control has a **fixed `Location` and `Size`**. Don't use Dock, Fill, Anchor or AutoSize to place controls inside a row: that put the text box underneath its label before.
- UI text is plain, friendly English with no jargon.
- Update the README, user guide and changelog together when a feature changes.

## How the code fits together

- Everything in `PresenceBridge` runs on background threads and uses `ConfigureAwait(false)`, so it never blocks the WinForms UI thread. The exception is awaiting WinRT `IAsyncOperation` values, which have no `ConfigureAwait`. The UI only hears about it through `AppLog` and `StatusUpdated`, and marshals back with `BeginInvoke((MethodInvoker)…)`.
- Flow: a media-session event calls `QueueUpdate`, then `EvaluateAsync` snapshots the state and debounces (`SettleDelay`, `RapidChangeWindow`, `MinSendGap`), then `PublishAsync` sends it. `ForceResyncAsync` re-pushes immediately after a reconnect.
- Locks: `_sessionLock` guards the attached session, `_debounceLock` guards the debounce state, and `_publishGate` serializes sends. Never nest them. Presence logic never runs while holding `_sessionLock`.
- `SetDiscordClientId` swaps in a fresh `DiscordIpcClient` and reconnects with no restart, and cancels any older connect loop.
- `CaptureAsync` is the single place that reads Apple Music. `UnscrambleSingleMetadata` splits `Artist — Album` when the album is blank.
- `/state` reads live (no debounce), so the overlay is accurate even while Discord's updates are held back.
- The two overlay pages read the same tags (`scale`, `fade`, `pos`, `progress`) and the same `forceShow`. **Keep them in step** when you add or change one.
- The overlay pages are raw string literals: don't put `"""` inside them.
- `AppSettings` always does a read-modify-write over the whole file. Never write the file any other way, or one setting will wipe the other.

## Building and testing

- `dotnet build -c Release`, then run `bin\Release\net8.0-windows10.0.19041.0\AppleMusicDiscordPresence.exe`. **Stop the running exe first**, or the build can't overwrite it.
- With the app running, `GET /state` shows what the overlay sees, and `GET /show?seconds=3` followed by `/state` shows `forceShow` switching on and off.
- To check what Windows reports for Apple Music, use a tiny throwaway console project targeting `net8.0-windows10.0.19041.0` that lists `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` sessions. Keep it outside the repo: a nested project folder would be compiled into the app by the default globbing.
- If nothing is ever detected as playing and the Win+Z media flyout is empty too, Windows' media broker is stuck. That's outside the app. Restarting Windows fixes it.
