# Changelog

How Apple Music Presence grew, newest first. Versions are dates (`YYYY.MM.DD`).

## 2026.10.12

- **Docs:** the README now follows the same layout as the owner's other repos, with a light/dark banner, badges, a hero picture, a feature grid, a flow diagram and a troubleshooting table.
- **Docs:** added this changelog, a user guide, and original logo, banner and hero art in `docs/images/` (no Apple or Discord logos).

## 2026.09.21

- **Overlay:** `?style=bar` shows a full-width strip with **Title — Artist** centered on it, where the bar itself fills as the song plays.
- **Overlay:** `?pos=top`, `?pos=center` and `?pos=bottom` place either layout vertically.
- **Overlay:** `?progress=off` removes the duration bar on either layout.
- **Overlay:** `/show` (and **Show song on overlay now** in the tray) brings the widget back after `?fade=` has hidden it.
- **Tray:** **Copy OBS bar overlay URL**.

## 2026.09.13

- **Status window:** fixed the Discord Client ID box being covered by its label. Every control now has a fixed position, and the Save button is back.
- **Status window:** the status line has its own full-width row, and **Reconnect to Apple Music** has a row of its own.
- **Apple Music:** removed the 10-second automatic re-attach loop. Re-attaching is now the **Reconnect to Apple Music** button.
- **Discord Client ID:** moved out of the environment into a setting saved on this PC (`settings.json`), entered in the status window.

## 2026.09.11

- **Fixed:** the app could get stuck reporting "nothing playing" after a long run, because the Windows event that announces session changes can stop firing.

## 2026.09.10

- First release: Discord Rich Presence for Apple Music, with cover art from the iTunes catalog, debounced updates and automatic reconnects.
- The OBS overlay, with `?scale=` and `?fade=`, the slide-in animation and a live progress bar.
- The tray app, with a status window, **Start with Windows**, and a clean **Exit**.
- Apple Music's single-release artist and album metadata is unscrambled.
