namespace AppleMusicDiscordPresence
{
    /// <summary>The single-file page served at "/" for use as an OBS Browser Source.</summary>
    internal static class OverlayPage
    {
        public const string Html = """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>Now Playing Overlay</title>
<style>
  /*
   * OBS Browser Sources render at ONE fixed pixel resolution and OBS stretches that
   * bitmap to fit however big you've dragged it on the canvas - that scaling happens
   * in OBS's compositor, after this page has already rendered, so nothing here can
   * see it or prevent it. That's true of every browser source; it's not fixable from
   * inside the page.
   *
   * What does work: leave the source's own Width/Height (its Properties dialog, not
   * the canvas drag handles) fixed at something that never needs to change - ideally
   * matching your OBS canvas resolution - and resize the WIDGET instead via the
   * ?scale= query parameter on the URL (e.g. ".../?scale=1.5"), which the script
   * below turns into --scale. Because the source's actual rendered resolution never
   * changes, OBS never has anything to stretch, so this always stays crisp.
   */
  :root {
    --scale: 1;
    /* Shared so both the layout and the slide-in keyframes agree on the geometry. */
    --art: calc(clamp(32px, 10.7vh, 190px) * var(--scale));
    --gap: calc(clamp(8px, 2.3vh, 40px) * var(--scale));
  }
  html, body {
    margin: 0;
    height: 100%;
    background: transparent;
    overflow: hidden;
    font-family: -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  }
  body {
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 3.3vh;
    box-sizing: border-box;
  }
  #card {
    display: flex;
    align-items: center;
    gap: var(--gap);
    /* Fixed footprint - it does not grow or shrink with the track's metadata.
       Long titles/artists ellipsize instead; see #text below. */
    width: calc(clamp(240px, 40vh, 720px) * var(--scale));
    max-width: 90vw;
    padding:
      calc(clamp(8px, 2vh, 34px) * var(--scale))
      calc(clamp(12px, 3.3vh, 56px) * var(--scale))
      calc(clamp(8px, 2vh, 34px) * var(--scale))
      calc(clamp(8px, 2vh, 34px) * var(--scale));
    border-radius: calc(clamp(6px, 2.3vh, 40px) * var(--scale));
    background: rgba(18, 18, 22, 0.72);
    backdrop-filter: blur(6px);
    color: #fff;
    /* Clips the art as it slides in from off the left edge, and the text while
       it's still tucked behind the art. */
    overflow: hidden;
    opacity: 0;
    transform: translateY(10px);
    transition: opacity .4s ease, transform .4s ease;
  }
  #card.visible { opacity: 1; transform: translateY(0); }
  /* ?pos=top|center|bottom - vertical placement (default: center). Always centered horizontally. */
  html[data-pos="top"] body { align-items: flex-start; }
  html[data-pos="center"] body { align-items: center; }
  html[data-pos="bottom"] body { align-items: flex-end; }
  #art {
    width: var(--art);
    height: var(--art);
    border-radius: calc(clamp(4px, 1.3vh, 24px) * var(--scale));
    object-fit: cover;
    flex-shrink: 0;
    background: #333 center/cover no-repeat;
    /* Above the text so the text is hidden behind it until it slides clear. */
    position: relative;
    z-index: 2;
    animation: art-in .5s cubic-bezier(.22, 1, .36, 1) both;
  }
  /* flex: 1 makes this claim whatever width #art and the gaps don't use, so title/
     artist/album truncate against the card's fixed width instead of the card
     growing to fit them; min-width: 0 is what lets a flex child shrink/truncate
     at all (its default min-width is auto, i.e. "never smaller than my content"). */
  #text {
    flex: 1 1 auto;
    min-width: 0;
    position: relative;
    z-index: 1;
    /* Starts one beat after the art, sliding out from behind it. */
    animation: text-out .5s cubic-bezier(.22, 1, .36, 1) .24s both;
  }
  #title {
    font-size: calc(clamp(10px, 3.2vh, 56px) * var(--scale));
    font-weight: 700;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    text-shadow: 0 1px 3px rgba(0,0,0,.5);
  }
  #artist {
    margin-top: calc(0.3vh * var(--scale));
    font-size: calc(clamp(9px, 2.3vh, 42px) * var(--scale));
    opacity: .88;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  #album {
    margin-top: calc(0.2vh * var(--scale));
    font-size: calc(clamp(8px, 2vh, 36px) * var(--scale));
    opacity: .6;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  #progress {
    margin-top: calc(clamp(4px, 1.3vh, 22px) * var(--scale));
    height: calc(clamp(2px, 0.7vh, 12px) * var(--scale));
    width: 100%;
    background: rgba(255,255,255,.25);
    border-radius: 999px;
    overflow: hidden;
  }
  #bar {
    height: 100%;
    width: 0%;
    background: #fff;
    border-radius: 999px;
    transition: width 1s linear;
  }

  @keyframes art-in {
    from { transform: translateX(calc(-1 * (var(--art) + var(--gap) + 12px))); opacity: 0; }
    to   { transform: none; opacity: 1; }
  }
  @keyframes text-out {
    from { transform: translateX(calc(-1 * (var(--art) + var(--gap)))); opacity: 0; }
    to   { transform: none; opacity: 1; }
  }
  @media (prefers-reduced-motion: reduce) {
    #art, #text { animation: none; }
  }
</style>
</head>
<body>
  <div id="card">
    <div id="art"></div>
    <div id="text">
      <div id="title"></div>
      <div id="artist"></div>
      <div id="album"></div>
      <div id="progress"><div id="bar"></div></div>
    </div>
  </div>

<script>
  // ?scale=1.5 resizes the widget itself, crisply - see the comment in <style> above
  // for why this is the right way to resize instead of dragging it on the OBS canvas.
  (function applyScale() {
    const raw = parseFloat(new URLSearchParams(location.search).get('scale'));
    const scale = Number.isFinite(raw) ? Math.min(6, Math.max(0.2, raw)) : 1;
    document.documentElement.style.setProperty('--scale', String(scale));
  })();

  // ?fade=20 hides the widget once the current song has been playing for that many
  // seconds, and brings it back the instant a new song starts. Keyed off the song's
  // actual playback position (which /state already reports live) rather than a timer
  // started in the browser, so it's correct immediately even if OBS reloads this
  // source mid-song. Omit ?fade= (or 0) to disable and always show it.
  const fadeMs = (() => {
    const raw = parseFloat(new URLSearchParams(location.search).get('fade'));
    return Number.isFinite(raw) && raw > 0 ? Math.min(86400, raw) * 1000 : null;
  })();

  const pos = (new URLSearchParams(location.search).get('pos') || '').toLowerCase();
  if (['top', 'center', 'bottom'].includes(pos)) document.documentElement.dataset.pos = pos;

  const card = document.getElementById('card');
  const art = document.getElementById('art');
  const textEl = document.getElementById('text');
  const titleEl = document.getElementById('title');
  const artistEl = document.getElementById('artist');
  const albumEl = document.getElementById('album');
  const progress = document.getElementById('progress');
  const bar = document.getElementById('bar');
  let lastKey = null;
  let wasForced = false;

  // Restart the CSS slide-in animations from the top (setting animation to 'none',
  // forcing a reflow, then reverting to the stylesheet value is the standard way).
  function playSwapAnimation() {
    for (const el of [art, textEl]) {
      el.style.animation = 'none';
      void el.offsetWidth;
      el.style.animation = '';
    }
  }

  function render(data) {
    if (!data || !data.isPlaying) {
      card.classList.remove('visible');
      lastKey = null;
      return;
    }

    const key = data.title + '' + data.artist + '' + data.album;
    if (key !== lastKey) {
      lastKey = key;
      titleEl.textContent = data.title || '';
      artistEl.textContent = data.artist || '';
      albumEl.textContent = data.album || '';
      albumEl.style.visibility = data.album ? '' : 'hidden';
      // No display:none fallback for missing art either - keep the art box's own
      // footprint and just fall back to its plain background color (set in CSS)
      // rather than removing it and reflowing the text over.
      art.style.backgroundImage = data.artUrl ? `url("${data.artUrl}")` : '';

      // Snap the progress bar to the new song instead of easing down from the old
      // song's position over a second, then restore the smooth transition.
      bar.style.transition = 'none';
      requestAnimationFrame(() => { bar.style.transition = ''; });

      playSwapAnimation();
    }

    if (data.durationMs > 0) {
      const pct = Math.max(0, Math.min(100, (data.positionMs / data.durationMs) * 100));
      bar.style.width = pct + '%';
      progress.style.visibility = '';
    } else {
      progress.style.visibility = 'hidden';
    }

    const pastFadeThreshold = fadeMs !== null && data.positionMs >= fadeMs;
    // /show (or the tray's "Show song on overlay now") sets forceShow for a few
    // seconds so the widget can be recalled after ?fade= has hidden it. Replay the
    // slide-in on the way back up so it doesn't just silently fade in.
    const forced = !!data.forceShow;
    if (forced && !wasForced && pastFadeThreshold) playSwapAnimation();
    wasForced = forced;
    card.classList.toggle('visible', !pastFadeThreshold || forced);
  }

  async function poll() {
    try {
      const res = await fetch('/state', { cache: 'no-store' });
      render(await res.json());
    } catch (e) {
      // server not up yet or momentarily unreachable - keep showing the last frame
    }
  }

  poll();
  setInterval(poll, 1000);
</script>
</body>
</html>
""";
    }
}
