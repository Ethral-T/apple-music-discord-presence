namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// The full-width "bar" variant served at "/" when the URL has ?style=bar: no artwork, a strip across the whole
    /// page with "Title - Artist" on it, where the strip itself fills left-to-right as
    /// the song progresses. Same /state feed, ?scale=, ?fade= and /show behaviour as the
    /// card at "/"; ?pos=top|center|bottom sets where it sits (default: bottom).
    /// </summary>
    internal static class BarPage
    {
        public const string Html = """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<title>Now Playing Bar</title>
<style>
  :root {
    --scale: 1;
    --hide-shift: 10px;   /* which way the bar slides while hidden; flipped for ?pos=top */
  }
  html[data-pos="top"] { --hide-shift: -10px; }
  html, body {
    margin: 0;
    height: 100%;
    background: transparent;
    overflow: hidden;
    font-family: -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  }
  body {
    display: flex;
    align-items: flex-end;   /* bottom edge by default */
  }
  html[data-pos="top"] body { align-items: flex-start; }
  html[data-pos="center"] body { align-items: center; }
  /* ?progress=off - a plain strip with just the text; no duration fill. */
  html[data-progress="off"] #fill { display: none; }

  #bar {
    position: relative;
    width: 100%;
    height: calc(clamp(28px, 5.5vh, 110px) * var(--scale));
    background: rgba(18, 18, 22, 0.72);
    color: #fff;
    overflow: hidden;
    opacity: 0;
    transform: translateY(var(--hide-shift));
    transition: opacity .4s ease, transform .4s ease;
  }
  #bar.visible { opacity: 1; transform: translateY(0); }

  /* The bar itself is the duration: this fills left-to-right as the song plays. */
  #fill {
    position: absolute;
    left: 0; top: 0; bottom: 0;
    width: 0%;
    background: rgba(255, 255, 255, 0.28);
    transition: width 1s linear;
  }

  #text {
    position: relative;      /* above #fill */
    height: 100%;
    display: flex;
    align-items: center;
    box-sizing: border-box;
    padding: 0 calc(clamp(10px, 2vh, 40px) * var(--scale));
    font-size: calc(clamp(12px, 2.6vh, 52px) * var(--scale));
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    text-shadow: 0 1px 3px rgba(0,0,0,.55);
    animation: text-in .45s cubic-bezier(.22, 1, .36, 1) both;
  }
  /* Auto margins on the first and last item centre the text horizontally. (Rather than
     justify-content: center, which clips both ends when the text is wider than the
     bar; auto margins collapse to zero in that case and it just starts at the left.) */
  #title { font-weight: 700; margin-left: auto; }
  #artist { opacity: .85; margin-right: auto; }
  #artist:not(:empty)::before { content: "\2014"; margin: 0 .6em; opacity: .7; }

  @keyframes text-in {
    from { transform: translateX(-24px); opacity: 0; }
    to   { transform: none; opacity: 1; }
  }
  @media (prefers-reduced-motion: reduce) {
    #text { animation: none; }
  }
</style>
</head>
<body>
  <div id="bar">
    <div id="fill"></div>
    <div id="text"><span id="title"></span><span id="artist"></span></div>
  </div>

<script>
  const params = new URLSearchParams(location.search);

  // ?scale=1.5 resizes the whole bar (0.2-6).
  (function applyScale() {
    const raw = parseFloat(params.get('scale'));
    const scale = Number.isFinite(raw) ? Math.min(6, Math.max(0.2, raw)) : 1;
    document.documentElement.style.setProperty('--scale', String(scale));
  })();

  // ?pos=top | center | bottom places the bar vertically (default: bottom).
  const pos = (params.get('pos') || '').toLowerCase();
  if (['top', 'center', 'bottom'].includes(pos)) document.documentElement.dataset.pos = pos;

  // ?progress=off (also 0 / false / no / hide / none) removes the duration fill.
  const progressParam = (params.get('progress') || '').toLowerCase();
  if (['off', '0', 'false', 'no', 'hide', 'none'].includes(progressParam)) document.documentElement.dataset.progress = 'off';

  // ?fade=20 hides the bar once the song has played that many seconds; it returns when
  // the next song starts, or when /show (or the tray item) recalls it. Keyed off real
  // playback position, so it's right even if OBS reloads the source mid-song.
  const fadeMs = (() => {
    const raw = parseFloat(params.get('fade'));
    return Number.isFinite(raw) && raw > 0 ? Math.min(86400, raw) * 1000 : null;
  })();

  const bar = document.getElementById('bar');
  const fill = document.getElementById('fill');
  const textEl = document.getElementById('text');
  const titleEl = document.getElementById('title');
  const artistEl = document.getElementById('artist');
  let lastKey = null;
  let wasForced = false;

  // Restart the slide-in on the text (animation:none, reflow, revert to the stylesheet).
  function playSwapAnimation() {
    textEl.style.animation = 'none';
    void textEl.offsetWidth;
    textEl.style.animation = '';
  }

  function render(data) {
    if (!data || !data.isPlaying) {
      bar.classList.remove('visible');
      lastKey = null;
      return;
    }

    const key = [data.title, data.artist].join('|');
    if (key !== lastKey) {
      lastKey = key;
      titleEl.textContent = data.title || '';
      artistEl.textContent = data.artist || '';

      // Snap the fill to the new song's position rather than easing down from the old one.
      fill.style.transition = 'none';
      requestAnimationFrame(() => { fill.style.transition = ''; });

      playSwapAnimation();
    }

    if (data.durationMs > 0) {
      const pct = Math.max(0, Math.min(100, (data.positionMs / data.durationMs) * 100));
      fill.style.width = pct + '%';
    } else {
      fill.style.width = '0%';
    }

    const pastFadeThreshold = fadeMs !== null && data.positionMs >= fadeMs;
    const forced = !!data.forceShow;
    if (forced && !wasForced && pastFadeThreshold) playSwapAnimation();
    wasForced = forced;
    bar.classList.toggle('visible', !pastFadeThreshold || forced);
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
