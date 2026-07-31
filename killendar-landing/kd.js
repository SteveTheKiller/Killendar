/* killendar.net - theme swatches, accent flyout, screenshot strip, lightbox, version egg.
   No framework and no build step: the folder is dragged straight into Cloudflare Pages.
   Shared by index.html, technical.html and about.html.

   The theme list and the accent hexes are the app's own, from Themes/*.xaml. Accents exist
   only on the three neutral families (dark, light, black); blood, greed and cyanotic carry
   their own built-in accent, so the flyout hides on those - the same rule the app's theme
   flyout follows. */
(function () {
  'use strict';

  var THEMES = ['dark', 'light', 'black', 'blood', 'greed', 'cyanotic'];

  // Per-family accents. Each neutral defines its own red, and Dark's #DD504B is the one the
  // wordmark and the og-image are built on, which is why Red is the default.
  var ACCENTS = {
    dark:  { Red: '#DD504B', Orange: '#E8962C', Green: '#1EA54C', Teal: '#1FB8A8', Blue: '#50AEE8', Purple: '#B982E3' },
    light: { Red: '#931A1A', Orange: '#C7710F', Green: '#1B5E20', Teal: '#0D827E', Blue: '#18608E', Purple: '#5A1690' },
    black: { Red: '#FF2929', Orange: '#FF910A', Green: '#00FF66', Teal: '#0AFFE7', Blue: '#298DFF', Purple: '#B829FF' }
  };
  var DEFAULT_ACCENT = 'Red';

  var root = document.documentElement;
  var $ = function (id) { return document.getElementById(id); };
  var all = function (sel) { return Array.prototype.slice.call(document.querySelectorAll(sel)); };

  function store(k, v) { try { localStorage.setItem(k, v); } catch (e) {} }
  function read(k) { try { return localStorage.getItem(k); } catch (e) { return null; } }

  function theme() { return root.getAttribute('data-theme') || 'dark'; }
  function hasAccents(t) { return Object.prototype.hasOwnProperty.call(ACCENTS, t); }
  function accentName() { return read('kcal-accent-name') || DEFAULT_ACCENT; }

  // ---- Accent ----

  function applyAccent(name) {
    var t = theme(), sw = $('accentSwitch');
    if (!hasAccents(t)) {
      root.style.removeProperty('--accent');
      if (sw) sw.hidden = true;    // .accent-switch[hidden] keeps the layout slot, so nothing shifts
      updateLogos();               // still needed here: blood/greed/cyanotic fall back to Red
      return;
    }
    if (sw) sw.hidden = false;
    var hex = ACCENTS[t][name] || ACCENTS[t][DEFAULT_ACCENT];
    root.style.setProperty('--accent', hex);
    store('kcal-accent', hex);
    store('kcal-accent-name', name);
    paintAccents();
    updateLogos();
  }

  // ---- Wordmark ----

  // The wordmark is an SVG with the accent baked in, so it is swapped rather than recolored.
  // Files come from Killer Branding/make-logo-svgs.py. The three non-neutral themes have no
  // accent of their own, so they fall back to Red, which is what the app and the icon use.
  function updateLogos() {
    var t = theme();
    // Three variants, not two. Black is its own family with its own six hexes (its red is
    // #FF2929, not dark's #DD504B), so using the dark art there would put a wordmark on the
    // page whose accent disagreed with every other accent on it.
    var variant = (t === 'light') ? 'light' : (t === 'black') ? 'black' : 'dark';
    var color = hasAccents(t) ? accentName().toLowerCase() : DEFAULT_ACCENT.toLowerCase();
    var src = 'brand/killendar-logo-' + variant + '-' + color + '.svg';
    all('img.wm-logo').forEach(function (img) { img.src = src; });
  }

  function paintAccents() {
    var t = theme();
    if (!hasAccents(t)) return;
    var active = accentName(), tog = $('accentToggle');
    if (tog) tog.style.background = ACCENTS[t][active] || ACCENTS[t][DEFAULT_ACCENT];
    all('.acc').forEach(function (b) {
      var hex = ACCENTS[t][b.getAttribute('data-accent')];
      if (!hex) return;
      // color as well as background: the pressed ring is drawn with currentColor.
      b.style.background = hex;
      b.style.color = hex;
      b.setAttribute('aria-pressed', b.getAttribute('data-accent') === active ? 'true' : 'false');
    });
  }

  // ---- Theme ----

  function applyTheme(id) {
    root.setAttribute('data-theme', id);
    store('kcal-theme', id);
    // Re-resolve the accent: the same name is a different hex in each family, and the three
    // non-neutral themes have none at all.
    applyAccent(accentName());
    all('.swatch[data-theme]').forEach(function (b) {
      b.setAttribute('aria-pressed', b.getAttribute('data-theme') === id ? 'true' : 'false');
    });
  }

  // ---- Screenshot strip: fade the top/bottom edge while there is more to scroll ----

  function wireStrip() {
    var strip = $('sbThumbs');
    if (!strip) return;
    var top = document.querySelector('.sb-fade-top'),
        bot = document.querySelector('.sb-fade-bottom');
    function update() {
      if (!top || !bot) return;
      var more = strip.scrollHeight - strip.clientHeight - strip.scrollTop;
      top.classList.toggle('on', strip.scrollTop > 4);
      bot.classList.toggle('on', more > 4);
    }
    strip.addEventListener('scroll', update);
    window.addEventListener('resize', update);
    update();
  }

  // ---- Lightbox ----

  function wireLightbox() {
    var box = $('lightbox'), img = $('lightboxImg'), strip = $('sbThumbs');
    if (!box || !img || !strip) return;
    strip.addEventListener('click', function (e) {
      var t = e.target;
      if (t.tagName !== 'IMG') return;
      img.src = t.getAttribute('data-full') || t.src;
      img.alt = t.alt || 'Killendar screenshot';
      box.classList.add('show');
    });
    box.addEventListener('click', function () { box.classList.remove('show'); });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') box.classList.remove('show');
    });
  }

  // ---- Easter egg: click the version number. The family one - it rains, then it quips. ----

  function wireEgg() {
    var egg = $('verEgg'), toast = $('eggToast');
    if (!egg) return;
    egg.addEventListener('click', function () {
      for (var i = 0; i < 18; i++) {
        var d = document.createElement('span');
        d.className = 'drip';
        d.style.left = (Math.random() * 100) + 'vw';
        d.style.height = (18 + Math.random() * 64) + 'px';
        d.style.opacity = (0.6 + Math.random() * 0.4).toFixed(2);
        var dur = 1.1 + Math.random() * 1.6;
        d.style.animation = 'dripfall ' + dur + 's linear forwards';
        d.style.animationDelay = (Math.random() * 0.5) + 's';
        document.body.appendChild(d);
        (function (el) { setTimeout(function () { el.remove(); }, (dur + 0.8) * 1000); })(d);
      }
      if (toast) {
        toast.textContent = 'No account. No sync. No server. Your appointments never leave this machine.';
        toast.classList.add('show');
        clearTimeout(egg._t);
        egg._t = setTimeout(function () { toast.classList.remove('show'); }, 2800);
      }
    });
  }

  // ---- Boot ----

  all('.swatch[data-theme]').forEach(function (b) {
    b.addEventListener('click', function () { applyTheme(b.getAttribute('data-theme')); });
  });
  all('.acc').forEach(function (b) {
    b.addEventListener('click', function () { applyAccent(b.getAttribute('data-accent')); });
  });

  var tog = $('accentToggle'), pop = $('accentPop');
  if (tog && pop) {
    tog.addEventListener('click', function (e) {
      e.stopPropagation();
      var opening = pop.hidden;
      pop.hidden = !opening;
      tog.setAttribute('aria-expanded', opening ? 'true' : 'false');
    });
    pop.addEventListener('click', function (e) { e.stopPropagation(); });
    document.addEventListener('click', function () {
      pop.hidden = true; tog.setAttribute('aria-expanded', 'false');
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') { pop.hidden = true; tog.setAttribute('aria-expanded', 'false'); }
    });
  }

  wireStrip();
  wireLightbox();
  wireEgg();
  applyTheme(theme());
})();
