/* killendar.net - theme swatches, accent flyout, screenshot strip, lightbox, version egg.
   No framework and no build step: the folder is dragged straight into Cloudflare Pages.
   Shared by index.html, technical.html and about.html.

   The theme list and the accent hexes are the app's own, from Themes/*.xaml. Accents exist
   only on the three neutral families (dark, light, black); every other theme carries its own
   built-in accent, so the flyout hides on those - the same rule the app's theme flyout
   follows. The six grunge palettes are ported from pdf-landing's kp.css. */
(function () {
  'use strict';

  var THEMES = ['dark', 'light', 'black', 'blood', 'greed', 'cyanotic', 'ectoplasm', 'decay', 'malaise', 'sepulchre', 'delirium', 'mourning'];
  var THEMED = ['blood', 'greed', 'cyanotic', 'ectoplasm', 'decay', 'malaise', 'sepulchre', 'delirium', 'mourning'];  // fixed-color wordmark art

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

  function buildThemeFlyout() {
    var group = document.querySelector('.topbar .tgrp');
    if (!group || !group.parentNode) return;
    var toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'theme-toggle';
    toggle.title = 'Theme';
    toggle.setAttribute('aria-label', 'Choose theme');
    toggle.setAttribute('aria-haspopup', 'true');
    toggle.setAttribute('aria-expanded', 'false');
    var preview = document.createElement('span');
    preview.setAttribute('aria-hidden', 'true');
    toggle.appendChild(preview);
    group.parentNode.insertBefore(toggle, group);
    function closeFlyout(focusToggle) {
      group.classList.remove('open');
      toggle.setAttribute('aria-expanded', 'false');
      if (focusToggle) toggle.focus();
    }
    function syncPreview(name) {
      var active = group.querySelector('.swatch[data-theme="' + name + '"]') || group.querySelector('.swatch');
      if (active) preview.className = active.className;
      preview.removeAttribute('aria-pressed');
    }
    toggle.addEventListener('click', function (e) {
      e.stopPropagation();
      var opening = !group.classList.contains('open');
      group.classList.toggle('open', opening);
      toggle.setAttribute('aria-expanded', opening ? 'true' : 'false');
    });
    group.addEventListener('click', function (e) {
      var swatch = e.target.closest('.swatch[data-theme]');
      if (!swatch) return;
      syncPreview(swatch.getAttribute('data-theme'));
      closeFlyout(false);
    });
    document.addEventListener('click', function (e) {
      if (!group.contains(e.target) && !toggle.contains(e.target)) closeFlyout(false);
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && group.classList.contains('open')) closeFlyout(true);
    });
    syncPreview(theme());
  }
  buildThemeFlyout();

  // ---- Accent ----

  function applyAccent(name) {
    var t = theme(), sw = $('accentSwitch');
    ['dark', 'light', 'black'].forEach(function (neutralTheme) {
      var preview = ACCENTS[neutralTheme][name] || ACCENTS[neutralTheme][DEFAULT_ACCENT];
      document.querySelectorAll('.sw-' + neutralTheme).forEach(function (dot) {
        dot.style.setProperty('--sw-accent', preview);
      });
    });
    if (!hasAccents(t)) {
      root.style.removeProperty('--accent');
      if (sw) sw.hidden = true;    // .accent-switch[hidden] keeps the layout slot, so nothing shifts
      updateLogos();
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
  // Files come from Killer Branding/make-logo-svgs.py and mirror the app's live wordmark color.
  function updateLogos() {
    var t = theme();
    var src;
    if (THEMED.indexOf(t) >= 0) {
      // Fixed-color themes carry their own wordmark art, colored with the theme's in-app
      // PrimaryBrush (make-logo-svgs.py --themes).
      src = 'brand/killendar-logo-' + t + '.svg';
    } else {
      // Three variants, not two. Black is its own family with its own six hexes (its red is
      // #FF2929, not dark's #DD504B), so using the dark art there would put a wordmark on the
      // page whose accent disagreed with every other accent on it.
      var variant = (t === 'light') ? 'light' : (t === 'black') ? 'black' : 'dark';
      var color = hasAccents(t) ? accentName().toLowerCase() : DEFAULT_ACCENT.toLowerCase();
      src = 'brand/killendar-logo-' + variant + '-' + color + '.svg';
    }
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

  // ---- Screenshot strip: populate the sidebar thumbnails, then fade the top/bottom edge
  // while there is more to scroll (KillerPDF's pdf-landing pattern) ----

  function wireScreenshots() {
    var strip = $('sbThumbs');
    if (!strip) return;
    var placeholder = strip.querySelector('.sb-empty');
    if (placeholder) placeholder.remove();
    // One line per screenshot, describing what it actually shows - the tooltip on hover.
    var SHOTS = [
      { src: 'screenshots/01.png', desc: 'Multi-week view, Sepulchre theme - six weeks of color-coded bars beside a packed day list' },
      { src: 'screenshots/02.png', desc: 'Month view, Delirium theme - a full August of appointments, all-day runs across the top of each day' },
      { src: 'screenshots/03.png', desc: 'Week view, Decay theme - a five-day work week with overlapping appointments packed into lanes' },
      { src: 'screenshots/04.png', desc: 'Keyboard shortcuts, Black theme - the F1 overlay showing every binding on a drawn keyboard' },
      { src: 'screenshots/05.png', desc: 'Day view, 98SE theme - one wall-to-wall day in the hour grid, beside the day list' },
      { src: 'screenshots/06.png', desc: 'Agenda view, Blood theme - the language menu with twelve languages and the date format picker' },
      { src: 'screenshots/07.png', desc: 'Multi-week view, Ectoplasm theme - switching view mode from the toolbar' }
    ];
    SHOTS.forEach(function (shot, idx) {
      var b = document.createElement('button');
      b.className = 'sb-thumb';
      b.title = shot.desc;
      b.setAttribute('aria-label', shot.desc);
      var img = document.createElement('img');
      img.src = shot.src;
      img.alt = shot.desc;
      img.title = shot.desc;
      img.loading = 'lazy';
      // Fires after wireStrip() is wired below - nudge its scroll/resize listener to
      // recompute once each image has its real height, since the strip's scrollHeight
      // isn't accurate until then.
      img.addEventListener('load', function () { window.dispatchEvent(new Event('resize')); });
      img.addEventListener('error', function () { window.dispatchEvent(new Event('resize')); });
      b.appendChild(img);
      strip.appendChild(b);
    });
  }

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
    var caption = $('lightboxCaption');
    var lastTrigger = null;
    if (!box || !img || !strip) return;
    strip.addEventListener('click', function (e) {
      var t = e.target;
      if (t.tagName !== 'IMG') return;
      var description = t.alt || 'Killendar screenshot';
      lastTrigger = t.closest('button');
      img.src = t.getAttribute('data-full') || t.src;
      img.alt = description;
      img.title = description;
      if (caption) caption.textContent = description;
      box.setAttribute('aria-hidden', 'false');
      box.classList.add('show');
      box.focus();
    });
    function closeBox() {
      if (!box.classList.contains('show')) return;
      box.classList.remove('show');
      box.setAttribute('aria-hidden', 'true');
      if (lastTrigger) lastTrigger.focus();
    }
    box.addEventListener('click', closeBox);
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && box.classList.contains('show')) {
        closeBox();
        e.preventDefault();
      }
    });
  }

  // ---- Scroll-spy outline nav (technical, about) - KillerPDF's pdf-landing pattern verbatim.
  // Highlights the sidebar link for whichever section is currently in view. ----
  // A page that groups its outline into collapsible sections publishes window.knOutlineReveal
  // before this file loads; the call is guarded, so a page without one is unaffected.

  function wireOutline() {
    var links = all('.outline a');
    if (!links.length) return;
    var secs = links.map(function (a) { return document.querySelector(a.getAttribute('href')); }).filter(Boolean);
    var sc = document.querySelector('.content-scroll');
    function onScroll() {
      var y = (sc ? sc.scrollTop : window.scrollY) + 100, cur = secs[0], active = null;
      secs.forEach(function (s) { if (s && s.offsetTop <= y) cur = s; });
      links.forEach(function (a) {
        var on = !!cur && a.getAttribute('href') === '#' + cur.id;
        a.classList.toggle('on', on);
        if (on) active = a;
      });
      if (active && window.knOutlineReveal) window.knOutlineReveal(active);
    }
    (sc || window).addEventListener('scroll', onScroll, { passive: true });
    onScroll();
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
        toast.textContent = eggText();
        toast.classList.add('show');
        clearTimeout(egg._t);
        egg._t = setTimeout(function () { toast.classList.remove('show'); }, 2800);
      }
    });
  }

  // ---- Language (the page speaks the app's languages) ----
  // The dictionary itself lives in kd-i18n.js, which every page loads BEFORE this file.
  // English is not really in that table: it IS the page, snapshotted off the DOM at boot, so the
  // markup stays the single source of truth for en and switching back restores it exactly.
  // kd-i18n.js still carries a full en block as the reference for translators, and it covers
  // keys belonging to the other pages.
  // The hero-info card carries no data-i18n on purpose - release.ps1 rewrites it by matching
  // the literal English label spans. Values may contain markup; they are our own strings.

  var LANGS = ['en', 'es', 'fr', 'de', 'cs', 'tr', 'ja', 'pl', 'bn', 'zh-CN', 'zh-TW', 'hu', 'it', 'ru', 'kk'];

  // Flag SVGs, KillerPDF's kp.js set verbatim; the toggle wears the chosen language's flag.
  var FLAGS = {
    en: '<svg viewBox="0 0 24 24"><rect width="24" height="24" fill="#fff"/><g fill="#b22234"><rect width="24" height="1.85"/><rect y="3.7" width="24" height="1.85"/><rect y="7.4" width="24" height="1.85"/><rect y="11.1" width="24" height="1.85"/><rect y="14.8" width="24" height="1.85"/><rect y="18.5" width="24" height="1.85"/><rect y="22.2" width="24" height="1.8"/></g><rect width="11" height="12.95" fill="#3c3b6e"/></svg>',
    es: '<svg viewBox="0 0 24 24"><rect width="24" height="24" fill="#c60b1e"/><rect y="6" width="24" height="12" fill="#ffc400"/></svg>',
    de: '<svg viewBox="0 0 24 24"><rect width="24" height="8" fill="#000"/><rect y="8" width="24" height="8" fill="#dd0000"/><rect y="16" width="24" height="8" fill="#ffce00"/></svg>',
    fr: '<svg viewBox="0 0 24 24"><rect width="8" height="24" fill="#0055a4"/><rect x="8" width="8" height="24" fill="#fff"/><rect x="16" width="8" height="24" fill="#ef4135"/></svg>',
    ja: '<svg viewBox="0 0 24 24"><rect width="24" height="24" fill="#fff"/><circle cx="12" cy="12" r="7" fill="#bc002d"/></svg>',
    tr: '<svg viewBox="0 0 24 24"><rect width="24" height="24" fill="#e30a17"/><circle cx="9.5" cy="12" r="5" fill="#fff"/><circle cx="11" cy="12" r="4" fill="#e30a17"/><polygon points="15.5,9.4 16.12,11.15 17.97,11.2 16.5,12.32 17.03,14.1 15.5,13.05 13.97,14.1 14.5,12.32 13.03,11.2 14.88,11.15" fill="#fff"/></svg>',
    'zh-TW': '<svg viewBox="0 0 24 24"><rect width="24" height="24" fill="#fe0000"/><rect width="12" height="12" fill="#000095"/><polygon points="6,3 7.2,6.6 11,6.6 7.9,8.8 9.1,12.4 6,10.2 2.9,12.4 4.1,8.8 1,6.6 4.8,6.6" fill="#fff"/></svg>',
    'zh-CN': '<svg viewBox="0 0 24 24"><rect width="24" height="24" fill="#de2910"/><polygon points="4,3 4.9,5.6 7.6,5.6 5.4,7.3 6.2,9.9 4,8.3 1.8,9.9 2.6,7.3 0.4,5.6 3.1,5.6" fill="#ffde00"/></svg>',
    bn: '<svg viewBox="0 0 24 24"><rect width="24" height="24" fill="#006a4e"/><circle cx="10.5" cy="12" r="6" fill="#f42a41"/></svg>',
    cs: '<svg viewBox="0 0 24 24"><rect width="24" height="12" fill="#fff"/><rect y="12" width="24" height="12" fill="#d7141a"/><polygon points="0,0 12,12 0,24" fill="#11457e"/></svg>',
    pl: '<svg viewBox="0 0 24 24"><rect width="24" height="12" fill="#fff"/><rect y="12" width="24" height="12" fill="#dc143c"/></svg>',
    hu: '<svg viewBox="0 0 24 24"><rect width="24" height="8" fill="#ce2939"/><rect y="8" width="24" height="8" fill="#fff"/><rect y="16" width="24" height="8" fill="#477050"/></svg>',
    it: '<svg viewBox="0 0 24 24"><rect width="8" height="24" fill="#009246"/><rect x="8" width="8" height="24" fill="#fff"/><rect x="16" width="8" height="24" fill="#ce2b37"/></svg>',
    ru: '<svg viewBox="0 0 24 24"><rect width="24" height="8" fill="#fff"/><rect y="8" width="24" height="8" fill="#0039a6"/><rect y="16" width="24" height="8" fill="#d52b1e"/></svg>',
    // Sun over a soaring steppe eagle: four crossed bars make the rays, the disc covers their
    // middle, and the bird is one polygon - the same trick the tr star uses.
    kk: '<svg viewBox="0 0 24 24"><rect width="24" height="24" fill="#00AFCA"/><g fill="#FEC50C"><rect x="11.65" y="5.2" width="0.7" height="9.6"/><rect x="11.65" y="5.2" width="0.7" height="9.6" transform="rotate(45 12 10)"/><rect x="11.65" y="5.2" width="0.7" height="9.6" transform="rotate(90 12 10)"/><rect x="11.65" y="5.2" width="0.7" height="9.6" transform="rotate(135 12 10)"/><circle cx="12" cy="10" r="3.4"/><polygon points="12,16.2 14,17.2 18,17 21.5,17.8 17.5,18.4 14.5,19.2 12.6,18.6 12,19.8 11.4,18.6 9.5,19.2 6.5,18.4 2.5,17.8 6,17 10,17.2"/></g></svg>'
  };

  // kd-i18n.js sets a plain global. Falling back to an empty table means a page that somehow
  // loaded without it still renders in English rather than throwing on the first lookup.
  var I18N = (typeof window !== 'undefined' && window.I18N) ? window.I18N : {};
  if (!I18N.en) I18N.en = {};

  var i18nBooted = false;

  function applyLang(l) {
    if (LANGS.indexOf(l) < 0) l = 'en';
    // First call: snapshot the page's own English off the DOM, so switching back restores it
    // byte for byte and en never drifts from the markup.
    if (!i18nBooted) {
      all('[data-i18n]').forEach(function (el) {
        I18N.en[el.getAttribute('data-i18n')] = el.innerHTML;
      });
      i18nBooted = true;
    }
    var dict = I18N[l] || {};
    all('[data-i18n]').forEach(function (el) {
      var k = el.getAttribute('data-i18n');
      el.innerHTML = dict[k] || I18N.en[k] || el.innerHTML;
    });
    root.setAttribute('lang', l === 'zh-TW' ? 'zh-Hant' : (l === 'zh-CN' ? 'zh-Hans' : l));
    store('kcal-lang', l);
    var tog = $('langToggle');
    if (tog) tog.innerHTML = FLAGS[l] || FLAGS.en;
    all('.lang-item[data-lang]').forEach(function (b) {
      b.setAttribute('aria-pressed', b.getAttribute('data-lang') === l ? 'true' : 'false');
    });
  }

  function eggText() {
    var l = read('kcal-lang') || 'en';
    return (I18N[l] && I18N[l].egg) ||
      'No account. No sync. No server. Your appointments never leave this machine.';
  }

  function defaultLang() {
    var saved = read('kcal-lang');
    if (saved && LANGS.indexOf(saved) >= 0) return saved;
    var nav = (navigator.language || 'en');
    if (/^zh\b/i.test(nav)) return (/tw|hk|hant/i.test(nav)) ? 'zh-TW' : 'zh-CN';
    var two = nav.slice(0, 2).toLowerCase();
    return LANGS.indexOf(two) >= 0 ? two : 'en';
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

  // Language menu: KillerPDF's manners verbatim - picking a language also closes the menu.
  var ltog = $('langToggle'), lmenu = $('langMenu');
  function closeLangMenu() {
    if (lmenu) { lmenu.hidden = true; if (ltog) ltog.setAttribute('aria-expanded', 'false'); }
  }
  if (ltog && lmenu) {
    ltog.addEventListener('click', function (e) {
      e.stopPropagation();
      var willOpen = lmenu.hidden;
      lmenu.hidden = !willOpen;
      ltog.setAttribute('aria-expanded', willOpen ? 'true' : 'false');
    });
    all('.lang-item[data-lang]').forEach(function (b) {
      b.addEventListener('click', function () { applyLang(b.getAttribute('data-lang')); closeLangMenu(); });
    });
    document.addEventListener('click', function (e) {
      if (!lmenu.hidden && !e.target.closest('.lang-switch')) closeLangMenu();
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') closeLangMenu();
    });
  }

  wireScreenshots();
  wireStrip();
  wireLightbox();
  wireOutline();
  wireEgg();
  applyTheme(theme());
  applyLang(defaultLang());
})();
