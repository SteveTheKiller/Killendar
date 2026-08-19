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

  // ---- Accent ----

  function applyAccent(name) {
    var t = theme(), sw = $('accentSwitch');
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
      { src: 'screenshots/06.png', desc: 'Agenda view, Blood theme - the language menu with eleven languages and the date format picker' },
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

  function wireOutline() {
    var links = all('.outline a');
    if (!links.length) return;
    var secs = links.map(function (a) { return document.querySelector(a.getAttribute('href')); }).filter(Boolean);
    var sc = document.querySelector('.content-scroll');
    function onScroll() {
      var y = (sc ? sc.scrollTop : window.scrollY) + 100, cur = secs[0];
      secs.forEach(function (s) { if (s && s.offsetTop <= y) cur = s; });
      links.forEach(function (a) { a.classList.toggle('on', !!cur && a.getAttribute('href') === '#' + cur.id); });
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

  // ---- Language (Steve, 2026-07-31: the page speaks the app's languages) ----
  // English is not in this table: it IS the page, snapshotted off the DOM at boot, so the
  // markup stays the single source of truth for en and switching back restores it exactly.
  // The hero-info card carries no data-i18n on purpose - release.ps1 rewrites it by matching
  // the literal English label spans. Values may contain markup; they are our own strings.

  var LANGS = ['en', 'es', 'fr', 'de', 'cs', 'tr', 'ja', 'pl', 'bn', 'zh-CN', 'zh-TW'];

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
    pl: '<svg viewBox="0 0 24 24"><rect width="24" height="12" fill="#fff"/><rect y="12" width="24" height="12" fill="#dc143c"/></svg>'
  };

  var I18N = {
    en: {},   // filled from the DOM at boot
    es: {
      tag: "Un calendario que puedes cifrar, sin cuenta, sin sincronización y sin nada que llame a casa.",
      b1h: "Cifrado, si lo quieres",
      b1a: "Pon una contraseña y el archivo queda <b>cifrado en reposo</b> con SQLCipher.",
      b1b: "AES-256, HMAC-SHA512 por página y la derivación de clave del propio SQLCipher.",
      b1c: "Opcional: sin contraseña queda como SQLite normal, legible por cualquier cosa.",
      b1d: "Una contraseña por calendario: el del trabajo cerrado y el de la familia abierto.",
      b2h: "Es solo un archivo",
      b2a: "Un archivo <b>.kcal</b> es todo tu calendario, en tu propio perfil.",
      b2b: "Una base de datos SQLite normal: cópiala y ya tienes copia de seguridad.",
      b2c: "Legible con cualquier herramienta SQLite, con o sin esta aplicación.",
      b2d: "Importa y exporta <b>.ics</b> para todo lo demás.",
      b3h: "Cuatro vistas, un control",
      b3a: "Mes, semana, día y agenda, compartiendo un solo anterior / siguiente / hoy.",
      b3b: "Las citas solapadas se colocan lado a lado, nunca ocultas.",
      b3c: "<b>Categorías de color</b> que tú defines, pintando cada vista.",
      b3d: "Trece temas, once idiomas, atajos de una sola tecla.",
      dl: "Descargar", dlwin: "Descargar para Windows", gh: "Código en GitHub",
      navTech: "Técnico", navAbout: "Acerca de",
      note: "Instálalo o úsalo portátil. Gratis, código abierto, GPLv3.<br>Sin cuenta, sin suscripción, sin servicio de sincronización.<br>No se envía telemetría y no hay anuncios... nunca.",
      feats: "Qué hace",
      f1h: "Mes, semana, día, agenda",
      f1p: "Las citas solapadas se colocan lado a lado en carriles, así una hora con dos reuniones muestra ambas. Las citas de día completo tienen su propia franja sobre la cuadrícula horaria.",
      f2h: "Categorías de color",
      f2p: "Define las tuyas, ponles nombre y elige sus colores. Una cita puede llevar varias, y la primera la colorea en todas las vistas. Renombrar actualiza todas las citas a la vez.",
      f3h: "Tantos calendarios como quieras",
      f3p: "Uno para el trabajo, otro para las guardias, otro para la familia. Cada uno es su propio archivo con su propia contraseña, o ninguna. Archivos <code>.kcal</code>",
      f4h: "Cifrado opcional",
      f4p: "SQLCipher en reposo: AES-256, HMAC-SHA512 por página, su propia derivación de clave. Opcional: sin contraseña el archivo sigue siendo SQLite normal.",
      f5h: "iCalendar de entrada y salida",
      f5p: "Importación y exportación escritas contra RFC 5545 sin dependencias externas. Las categorías viajan con la cita en ambas direcciones. Importa / exporta <code>.ics</code>",
      f6h: "Trece temas, once idiomas",
      f6p: "Cada tema se cambia en marcha. Dark, Light, Black y 98SE admiten seis acentos cada uno, 33 aspectos en total. Atajos de una tecla que se apartan mientras escribes en un campo.",
      f7h: "Portátil o instalado",
      f7p: "Ejecútalo desde un USB, o deja que se instale por usuario o para todos en la máquina. Hay una ruta silenciosa para winget, Chocolatey y RMM.",
      f8h: "Firmado y comprobable",
      f8p: "La tarjeta Acerca de muestra el editor, la huella del certificado y el SHA-256 del exe en ejecución, validado con WinVerifyTrust en lugar de solo leerlo del archivo.",
      foot: "<a href=\"https://github.com/SteveTheKiller/Killendar\" target=\"_blank\" rel=\"noopener\">Código en GitHub</a> &middot; GPLv3 &middot; Parte de <a href=\"https://killertools.net\" target=\"_blank\" rel=\"noopener\">killertools.net</a>",
      egg: "Sin cuenta. Sin sincronización. Sin servidor. Tus citas nunca salen de esta máquina."
    },
    fr: {
      tag: "Un calendrier que vous pouvez chiffrer, sans compte, sans synchronisation et sans rien qui téléphone à la maison.",
      b1h: "Chiffré, si vous voulez",
      b1a: "Mettez un mot de passe et le fichier est <b>chiffré au repos</b> avec SQLCipher.",
      b1b: "AES-256, HMAC-SHA512 par page, la dérivation de clé de SQLCipher lui-même.",
      b1c: "Optionnel : sans mot de passe, il reste du SQLite ordinaire, lisible par tout.",
      b1d: "Un mot de passe par calendrier - le travail verrouillé, celui de la famille ouvert.",
      b2h: "Ce n'est qu'un fichier",
      b2a: "Un fichier <b>.kcal</b> est tout votre calendrier, dans votre propre profil.",
      b2b: "Une base SQLite ordinaire - sauvegardez-la en la copiant.",
      b2c: "Lisible avec n'importe quel outil SQLite, avec ou sans cette application.",
      b2d: "Importez et exportez du <b>.ics</b> pour tout le reste.",
      b3h: "Quatre vues, un contrôle",
      b3a: "Mois, semaine, jour et agenda, partageant un seul précédent / suivant / aujourd'hui.",
      b3b: "Les rendez-vous qui se chevauchent s'affichent côte à côte, jamais cachés.",
      b3c: "Des <b>catégories de couleur</b> que vous définissez, peignant chaque vue.",
      b3d: "Treize thèmes, onze langues, raccourcis à une seule touche.",
      dl: "Télécharger", dlwin: "Télécharger pour Windows", gh: "Source sur GitHub",
      navTech: "Technique", navAbout: "À propos",
      note: "Installez-le ou utilisez-le portable. Gratuit, open source, GPLv3.<br>Sans compte, sans abonnement, sans service de synchronisation.<br>Aucune télémétrie envoyée et aucune pub... jamais.",
      feats: "Ce qu'il fait",
      f1h: "Mois, semaine, jour, agenda",
      f1p: "Les rendez-vous qui se chevauchent s'affichent côte à côte en couloirs : une heure double affiche les deux. Les entrées sur toute la journée ont leur propre bande au-dessus de la grille horaire.",
      f2h: "Catégories de couleur",
      f2p: "Définissez les vôtres, nommez-les, choisissez leurs couleurs. Un rendez-vous peut en porter plusieurs, la première le colore dans chaque vue. Renommer met à jour tous les rendez-vous d'un coup.",
      f3h: "Autant de calendriers que vous voulez",
      f3p: "Un pour le travail, un pour l'astreinte, un pour la famille. Chacun est son propre fichier avec son propre mot de passe, ou aucun. Fichiers <code>.kcal</code>",
      f4h: "Chiffrement optionnel",
      f4p: "SQLCipher au repos : AES-256, HMAC-SHA512 par page, sa propre dérivation de clé. Optionnel : sans mot de passe, le fichier reste du SQLite ordinaire.",
      f5h: "iCalendar en entrée et en sortie",
      f5p: "Import et export écrits contre la RFC 5545 sans dépendances externes. Les catégories voyagent avec le rendez-vous dans les deux sens. Import / export <code>.ics</code>",
      f6h: "Treize thèmes, onze langues",
      f6p: "Chaque thème se change en cours d'exécution. Dark, Light, Black et 98SE prennent chacun six accents, soit 33 apparences en tout. Des raccourcis à une touche qui s'effacent pendant que vous tapez dans un champ.",
      f7h: "Portable ou installé",
      f7p: "Lancez-le depuis une clé USB, ou laissez-le s'installer par utilisateur ou pour toute la machine. Un chemin silencieux existe pour winget, Chocolatey et RMM.",
      f8h: "Signé, et vérifiable",
      f8p: "La carte À propos montre l'éditeur, l'empreinte du certificat et le SHA-256 de l'exe en cours, validé avec WinVerifyTrust plutôt que simplement lu dans le fichier.",
      foot: "<a href=\"https://github.com/SteveTheKiller/Killendar\" target=\"_blank\" rel=\"noopener\">Source sur GitHub</a> &middot; GPLv3 &middot; Membre de <a href=\"https://killertools.net\" target=\"_blank\" rel=\"noopener\">killertools.net</a>",
      egg: "Pas de compte. Pas de synchro. Pas de serveur. Vos rendez-vous ne quittent jamais cette machine."
    },
    de: {
      tag: "Ein Kalender zum Verschlüsseln - ohne Konto, ohne Sync und ohne dass etwas nach Hause telefoniert.",
      b1h: "Verschlüsselt, wenn Sie wollen",
      b1a: "Passwort setzen, und die Datei ist mit SQLCipher <b>im Ruhezustand verschlüsselt</b>.",
      b1b: "AES-256, HMAC-SHA512 pro Seite, SQLCiphers eigene Schlüsselableitung.",
      b1c: "Opt-in: ohne Passwort bleibt es normales SQLite, für alles lesbar.",
      b1d: "Ein Passwort pro Kalender - die Arbeit verschlossen, der Familienkalender offen.",
      b2h: "Es ist nur eine Datei",
      b2a: "Eine <b>.kcal</b>-Datei ist Ihr ganzer Kalender, im eigenen Profil.",
      b2b: "Eine gewöhnliche SQLite-Datenbank - Sicherung per Kopieren.",
      b2c: "Lesbar mit jedem SQLite-Werkzeug, mit oder ohne diese App.",
      b2d: "Import und Export per <b>.ics</b> für alles andere.",
      b3h: "Vier Ansichten, ein Steuer",
      b3a: "Monat, Woche, Tag und Agenda mit einem gemeinsamen Zurück / Weiter / Heute.",
      b3b: "Überlappende Termine stehen nebeneinander, nie versteckt.",
      b3c: "<b>Farbkategorien</b>, die Sie selbst anlegen und die jede Ansicht färben.",
      b3d: "Dreizehn Designs, elf Sprachen, Einzeltasten-Kürzel.",
      dl: "Download", dlwin: "Download für Windows", gh: "Quellcode auf GitHub",
      navTech: "Technik", navAbout: "Über",
      note: "Installieren oder portabel nutzen. Kostenlos, Open Source, GPLv3.<br>Kein Konto, kein Abo, kein Sync-Dienst.<br>Keine Telemetrie und keine Werbung... niemals.",
      feats: "Was es kann",
      f1h: "Monat, Woche, Tag, Agenda",
      f1p: "Überlappende Termine liegen in Spuren nebeneinander, eine doppelt belegte Stunde zeigt beide. Ganztägige Einträge bekommen ihren eigenen Streifen über dem Stundenraster.",
      f2h: "Farbkategorien",
      f2p: "Eigene anlegen, benennen, Farben wählen. Ein Termin kann mehrere tragen, die erste färbt ihn in jeder Ansicht. Umbenennen aktualisiert alle Termine auf einmal.",
      f3h: "So viele Kalender Sie wollen",
      f3p: "Einer für die Arbeit, einer für den Bereitschaftsdienst, einer für die Familie. Jeder ist seine eigene Datei mit eigenem Passwort, oder ohne. <code>.kcal</code>-Dateien",
      f4h: "Optionale Verschlüsselung",
      f4p: "SQLCipher im Ruhezustand: AES-256, HMAC-SHA512 pro Seite, eigene Schlüsselableitung. Opt-in - ohne Passwort bleibt die Datei normales SQLite.",
      f5h: "iCalendar rein und raus",
      f5p: "Import und Export gegen RFC 5545 geschrieben, ohne externe Abhängigkeiten. Kategorien reisen in beide Richtungen mit dem Termin. <code>.ics</code> Import / Export",
      f6h: "Dreizehn Designs, elf Sprachen",
      f6p: "Jedes Design zur Laufzeit wechselbar. Dark, Light, Black und 98SE tragen je sechs Akzentfarben, also 33 Looks insgesamt. Einzeltasten-Kürzel, die pausieren, während Sie in ein Feld tippen.",
      f7h: "Portabel oder installiert",
      f7p: "Vom USB-Stick starten oder pro Benutzer bzw. für alle auf der Maschine installieren. Ein stiller Pfad existiert für winget, Chocolatey und RMM.",
      f8h: "Signiert und nachprüfbar",
      f8p: "Die Info-Karte zeigt Herausgeber, Zertifikat-Fingerabdruck und den SHA-256 der laufenden Exe, validiert mit WinVerifyTrust statt nur aus der Datei gelesen.",
      foot: "<a href=\"https://github.com/SteveTheKiller/Killendar\" target=\"_blank\" rel=\"noopener\">Quellcode auf GitHub</a> &middot; GPLv3 &middot; Teil von <a href=\"https://killertools.net\" target=\"_blank\" rel=\"noopener\">killertools.net</a>",
      egg: "Kein Konto. Kein Sync. Kein Server. Ihre Termine verlassen diese Maschine nie."
    },
    cs: {
      tag: "Kalendář, který můžete zašifrovat - bez účtu, bez synchronizace a bez telefonování domů.",
      b1h: "Šifrovaný, pokud chcete",
      b1a: "Nastavte heslo a soubor je <b>šifrovaný v klidu</b> pomocí SQLCipher.",
      b1b: "AES-256, HMAC-SHA512 na stránku, vlastní odvození klíče SQLCipheru.",
      b1c: "Volitelné: bez hesla zůstává obyčejné SQLite, čitelné čímkoli.",
      b1d: "Jedno heslo na kalendář - práci zamčené, rodinný otevřený.",
      b2h: "Je to jen soubor",
      b2a: "Jeden soubor <b>.kcal</b> je celý váš kalendář, ve vašem profilu.",
      b2b: "Obyčejná databáze SQLite - zálohujete ji zkopírováním.",
      b2c: "Čitelné jakýmkoli nástrojem SQLite, s touto aplikací i bez ní.",
      b2d: "Import a export <b>.ics</b> pro všechno ostatní.",
      b3h: "Čtyři zobrazení, jedno ovládání",
      b3a: "Měsíc, týden, den a agenda se společným předchozí / další / dnes.",
      b3b: "Překrývající se schůzky leží vedle sebe, nikdy skryté.",
      b3c: "<b>Barevné kategorie</b>, které si definujete a které barví každé zobrazení.",
      b3d: "Třináct motivů, jedenáct jazyků, jednoklávesové zkratky.",
      dl: "Stáhnout", dlwin: "Stáhnout pro Windows", gh: "Zdroj na GitHubu",
      navTech: "Technika", navAbout: "O aplikaci",
      note: "Nainstalujte, nebo používejte přenosně. Zdarma, open source, GPLv3.<br>Bez účtu, bez předplatného, bez synchronizační služby.<br>Žádná telemetrie, žádné reklamy... nikdy.",
      feats: "Co umí",
      f1h: "Měsíc, týden, den, agenda",
      f1p: "Překrývající se schůzky leží vedle sebe v pruzích, dvojitě obsazená hodina ukáže obě. Celodenní záznamy mají vlastní pruh nad hodinovou mřížkou.",
      f2h: "Barevné kategorie",
      f2p: "Definujte vlastní, pojmenujte je a vyberte barvy. Schůzka jich může nést více, první ji barví v každém zobrazení. Přejmenování aktualizuje všechny schůzky najednou.",
      f3h: "Kolik kalendářů chcete",
      f3p: "Jeden na práci, jeden na pohotovost, jeden pro rodinu. Každý je vlastní soubor s vlastním heslem, nebo bez něj. Soubory <code>.kcal</code>",
      f4h: "Volitelné šifrování",
      f4p: "SQLCipher v klidu: AES-256, HMAC-SHA512 na stránku, vlastní odvození klíče. Volitelné - bez hesla zůstává soubor obyčejné SQLite.",
      f5h: "iCalendar dovnitř i ven",
      f5p: "Import a export napsané podle RFC 5545 bez externích závislostí. Kategorie cestují se schůzkou oběma směry. Import / export <code>.ics</code>",
      f6h: "Třináct motivů, jedenáct jazyků",
      f6p: "Každý motiv lze přepnout za běhu. Dark, Light, Black a 98SE mají po šesti akcentových barvách, celkem 33 vzhledů. Jednoklávesové zkratky, které se odmlčí, když píšete do pole.",
      f7h: "Přenosný nebo nainstalovaný",
      f7p: "Spusťte z USB, nebo jej nechte nainstalovat pro uživatele či pro celý stroj. Tichá cesta existuje pro winget, Chocolatey a RMM.",
      f8h: "Podepsaný a ověřitelný",
      f8p: "Karta O aplikaci ukazuje vydavatele, otisk certifikátu a SHA-256 běžícího exe, ověřené přes WinVerifyTrust, ne jen přečtené ze souboru.",
      foot: "<a href=\"https://github.com/SteveTheKiller/Killendar\" target=\"_blank\" rel=\"noopener\">Zdroj na GitHubu</a> &middot; GPLv3 &middot; Součást <a href=\"https://killertools.net\" target=\"_blank\" rel=\"noopener\">killertools.net</a>",
      egg: "Žádný účet. Žádná synchronizace. Žádný server. Vaše schůzky nikdy neopustí tento stroj."
    },
    tr: {
      tag: "Şifreleyebileceğiniz bir takvim - hesap yok, senkronizasyon yok, eve telefon eden hiçbir şey yok.",
      b1h: "İsterseniz şifreli",
      b1a: "Bir parola belirleyin, dosya SQLCipher ile <b>bekleme halinde şifrelenir</b>.",
      b1b: "AES-256, sayfa başına HMAC-SHA512, SQLCipher'ın kendi anahtar türetmesi.",
      b1c: "İsteğe bağlı: parola yoksa düz SQLite kalır, her şeyle okunur.",
      b1d: "Takvim başına bir parola - iş kilitli, aile takvimi açık.",
      b2h: "Sadece bir dosya",
      b2a: "Bir <b>.kcal</b> dosyası, kendi profilinizde tüm takviminizdir.",
      b2b: "Sıradan bir SQLite veritabanı - kopyalayarak yedekleyin.",
      b2c: "Bu uygulama olsun olmasın, her SQLite aracıyla okunur.",
      b2d: "Geri kalan her şey için <b>.ics</b> içe ve dışa aktarın.",
      b3h: "Dört görünüm, tek kontrol",
      b3a: "Ay, hafta, gün ve ajanda; tek bir önceki / sonraki / bugün paylaşır.",
      b3b: "Çakışan randevular yan yana dizilir, asla gizlenmez.",
      b3c: "Kendi tanımladığınız <b>renk kategorileri</b> her görünümü boyar.",
      b3d: "On üç tema, on bir dil, tek tuşlu kısayollar.",
      dl: "İndir", dlwin: "Windows için indir", gh: "GitHub'da kaynak",
      navTech: "Teknik", navAbout: "Hakkında",
      note: "Kurun ya da taşınabilir çalıştırın. Ücretsiz, açık kaynak, GPLv3.<br>Hesap yok, abonelik yok, senkronizasyon servisi yok.<br>Telemetri gönderilmez ve reklam yok... asla.",
      feats: "Ne yapar",
      f1h: "Ay, hafta, gün, ajanda",
      f1p: "Çakışan randevular şeritler halinde yan yana durur; çift dolu bir saat ikisini de gösterir. Tüm gün kayıtları saat ızgarasının üstünde kendi şeridini alır.",
      f2h: "Renk kategorileri",
      f2p: "Kendinizinkini tanımlayın, adlandırın, renklerini seçin. Bir randevu birkaçını taşıyabilir; ilki onu her görünümde boyar. Yeniden adlandırmak tüm randevuları tek seferde günceller.",
      f3h: "İstediğiniz kadar takvim",
      f3p: "Biri iş, biri nöbet, biri aile için. Her biri kendi parolalı ya da parolasız kendi dosyasıdır. <code>.kcal</code> dosyaları",
      f4h: "İsteğe bağlı şifreleme",
      f4p: "Beklemede SQLCipher: AES-256, sayfa başına HMAC-SHA512, kendi anahtar türetmesi. İsteğe bağlı - parola yoksa dosya düz SQLite kalır.",
      f5h: "iCalendar girer ve çıkar",
      f5p: "İçe ve dışa aktarma, harici bağımlılık olmadan RFC 5545'e göre yazıldı. Kategoriler randevuyla iki yönde de yolculuk eder. <code>.ics</code> içe / dışa aktarma",
      f6h: "On üç tema, on bir dil",
      f6p: "Her tema çalışırken değiştirilebilir; Dark, Light, Black ve 98SE altışar vurgu rengi taşır, toplam 33 görünüm. Bir alana yazarken kenara çekilen tek tuşlu kısayollar.",
      f7h: "Taşınabilir veya kurulu",
      f7p: "USB bellekten çalıştırın ya da kullanıcı başına veya makinedeki herkes için kurulsun. winget, Chocolatey ve RMM için sessiz bir yol var.",
      f8h: "İmzalı ve doğrulanabilir",
      f8p: "Hakkında kartı yayımcıyı, sertifika parmak izini ve çalışan exe'nin SHA-256'sını gösterir; dosyadan okunmak yerine WinVerifyTrust ile doğrulanır.",
      foot: "<a href=\"https://github.com/SteveTheKiller/Killendar\" target=\"_blank\" rel=\"noopener\">GitHub'da kaynak</a> &middot; GPLv3 &middot; <a href=\"https://killertools.net\" target=\"_blank\" rel=\"noopener\">killertools.net</a> ailesinden",
      egg: "Hesap yok. Senkronizasyon yok. Sunucu yok. Randevularınız bu makineden asla ayrılmaz."
    },
    ja: {
      tag: "暗号化できるカレンダー。アカウントなし、同期なし、外部に何も送信しません。",
      b1h: "望めば暗号化",
      b1a: "パスワードを設定すると、ファイルは SQLCipher で<b>保存時に暗号化</b>されます。",
      b1b: "AES-256、ページごとの HMAC-SHA512、SQLCipher 自身の鍵導出。",
      b1c: "任意選択：パスワードなしなら普通の SQLite のまま、何でも読めます。",
      b1d: "カレンダーごとにパスワード一つ - 仕事はロック、家族の分は開いたままに。",
      b2h: "ただのファイルです",
      b2a: "<b>.kcal</b> ファイル一つがあなたのカレンダー全部、自分のプロファイルの中に。",
      b2b: "普通の SQLite データベース - コピーするだけでバックアップ。",
      b2c: "このアプリがあってもなくても、どの SQLite ツールでも読めます。",
      b2d: "その他はすべて <b>.ics</b> で読み書き。",
      b3h: "四つの表示、一つの操作",
      b3a: "月・週・日・予定一覧が、前へ / 次へ / 今日を共有します。",
      b3b: "重なる予定は横に並び、隠れません。",
      b3c: "自分で定義する<b>カラーカテゴリ</b>がすべての表示を彩ります。",
      b3d: "テーマ十三種、十一か国語、ワンキーのショートカット。",
      dl: "ダウンロード", dlwin: "Windows 版をダウンロード", gh: "GitHub でソースを見る",
      navTech: "技術情報", navAbout: "について",
      note: "インストールしてもポータブルでも。無料、オープンソース、GPLv3。<br>アカウントなし、サブスクなし、同期サービスなし。<br>テレメトリは送信せず、広告も... 永遠になし。",
      feats: "できること",
      f1h: "月・週・日・予定一覧",
      f1p: "重なる予定はレーンで横に並び、ダブルブッキングの時間も両方見えます。終日の予定は時間グリッドの上に専用の帯を持ちます。",
      f2h: "カラーカテゴリ",
      f2p: "自分で作って名前と色を決める。予定は複数持て、最初の一つがすべての表示で色を付けます。名前変更は全予定を一括更新。",
      f3h: "カレンダーは好きなだけ",
      f3p: "仕事用、当番用、家族用。それぞれが独自のパスワード付き（またはなし）の単独ファイルです。<code>.kcal</code> ファイル",
      f4h: "任意の暗号化",
      f4p: "保存時は SQLCipher：AES-256、ページごとの HMAC-SHA512、独自の鍵導出。任意選択なのでパスワードなしなら普通の SQLite のまま。",
      f5h: "iCalendar の出入り",
      f5p: "読み書きは外部依存なしで RFC 5545 に沿って実装。カテゴリは双方向で予定と一緒に移動します。<code>.ics</code> の読み込み / 書き出し",
      f6h: "テーマ十三種、十一か国語",
      f6p: "どのテーマも実行中に切り替え可能。Dark、Light、Black、98SE はそれぞれ六色のアクセントを持ち、合計 33 通り。入力中は遠慮するワンキーショートカット。",
      f7h: "ポータブルもインストールも",
      f7p: "USB メモリから実行しても、ユーザー単位・全員向けにインストールしても。winget、Chocolatey、RMM 向けのサイレントパスもあります。",
      f8h: "署名済み、検証可能",
      f8p: "バージョン情報カードは発行元、証明書の拇印、実行中の exe の SHA-256 を表示し、ファイルから読むだけでなく WinVerifyTrust で検証します。",
      foot: "<a href=\"https://github.com/SteveTheKiller/Killendar\" target=\"_blank\" rel=\"noopener\">GitHub のソース</a> &middot; GPLv3 &middot; <a href=\"https://killertools.net\" target=\"_blank\" rel=\"noopener\">killertools.net</a> ファミリー",
      egg: "アカウントなし。同期なし。サーバーなし。予定がこのマシンを離れることはありません。"
    },
    pl: {
      tag: "Kalendarz, który możesz zaszyfrować - bez konta, bez synchronizacji i bez niczego, co dzwoni do domu.",
      b1h: "Szyfrowany, jeśli chcesz",
      b1a: "Ustaw hasło, a plik jest <b>szyfrowany w spoczynku</b> przez SQLCipher.",
      b1b: "AES-256, HMAC-SHA512 na stronę i własna derywacja klucza SQLCipher.",
      b1c: "Opcjonalnie: bez hasła plik pozostaje zwykłym SQLite, czytelnym dla wszystkiego.",
      b1d: "Jedno hasło na kalendarz - służbowy zamknięty, rodzinny otwarty.",
      b2h: "To tylko plik",
      b2a: "Jeden plik <b>.kcal</b> to cały twój kalendarz, w twoim własnym profilu.",
      b2b: "Zwykła baza SQLite - kopia zapasowa przez zwykłe skopiowanie.",
      b2c: "Czytelny dla każdego narzędzia SQLite, z tą aplikacją lub bez niej.",
      b2d: "Import i eksport <b>.ics</b> do całej reszty.",
      b3h: "Cztery widoki, jedno sterowanie",
      b3a: "Miesiąc, tydzień, dzień i agenda ze wspólnym poprzedni / następny / dziś.",
      b3b: "Nakładające się spotkania stają obok siebie, nigdy nie są ukryte.",
      b3c: "<b>Kolorowe kategorie</b>, które sam definiujesz, malują każdy widok.",
      b3d: "Trzynaście motywów, jedenaście języków, skróty jednym klawiszem.",
      dl: "Pobierz", dlwin: "Pobierz dla Windows", gh: "Źródło na GitHubie",
      navTech: "Techniczne", navAbout: "O aplikacji",
      note: "Zainstaluj lub uruchom przenośnie. Za darmo, open source, GPLv3.<br>Bez konta, bez subskrypcji, bez usługi synchronizacji.<br>Żadna telemetria nie jest wysyłana i żadnych reklam... nigdy.",
      feats: "Co potrafi",
      f1h: "Miesiąc, tydzień, dzień, agenda",
      f1p: "Nakładające się spotkania układają się obok siebie w pasach, więc podwójnie zajęta godzina pokazuje oba. Wpisy całodniowe mają własny pasek nad siatką godzin.",
      f2h: "Kolorowe kategorie",
      f2p: "Zdefiniuj własne, nazwij je i wybierz kolory. Spotkanie może nosić kilka, a pierwsza koloruje je w każdym widoku. Zmiana nazwy aktualizuje wszystkie spotkania naraz.",
      f3h: "Tyle kalendarzy, ile chcesz",
      f3p: "Jeden do pracy, jeden na dyżury, jeden dla rodziny. Każdy jest osobnym plikiem z własnym hasłem lub bez. Pliki <code>.kcal</code>",
      f4h: "Opcjonalne szyfrowanie",
      f4p: "SQLCipher w spoczynku: AES-256, HMAC-SHA512 na stronę, własna derywacja klucza. Opcjonalne - bez hasła plik pozostaje zwykłym SQLite.",
      f5h: "iCalendar w obie strony",
      f5p: "Import i eksport napisane według RFC 5545 bez zewnętrznych zależności. Kategorie podróżują ze spotkaniem w obu kierunkach. Import / eksport <code>.ics</code>",
      f6h: "Trzynaście motywów, jedenaście języków",
      f6p: "Każdy motyw przełączysz w trakcie działania. Dark, Light, Black i 98SE mają po sześć kolorów akcentu, łącznie 33 wyglądy. Skróty jednym klawiszem, które ustępują, gdy piszesz w polu.",
      f7h: "Przenośny lub zainstalowany",
      f7p: "Uruchom z pendrive'a albo pozwól mu zainstalować się dla użytkownika lub dla wszystkich na maszynie. Cicha ścieżka istnieje dla winget, Chocolatey i RMM.",
      f8h: "Podpisany i sprawdzalny",
      f8p: "Karta O aplikacji pokazuje wydawcę, odcisk certyfikatu i SHA-256 działającego exe, zweryfikowane przez WinVerifyTrust, a nie tylko odczytane z pliku.",
      foot: "<a href=\"https://github.com/SteveTheKiller/Killendar\" target=\"_blank\" rel=\"noopener\">Źródło na GitHubie</a> &middot; GPLv3 &middot; Część <a href=\"https://killertools.net\" target=\"_blank\" rel=\"noopener\">killertools.net</a>",
      egg: "Bez konta. Bez synchronizacji. Bez serwera. Twoje spotkania nigdy nie opuszczają tej maszyny."
    },
    bn: {
      tag: "এমন একটি ক্যালেন্ডার যা আপনি এনক্রিপ্ট করতে পারেন - অ্যাকাউন্ট নেই, সিঙ্ক নেই, বাইরে কিছু পাঠায় না।",
      b1h: "চাইলে এনক্রিপ্টেড",
      b1a: "পাসওয়ার্ড দিলে ফাইলটি SQLCipher দিয়ে <b>স্টোরেজে এনক্রিপ্টেড</b> থাকে।",
      b1b: "AES-256, প্রতি পৃষ্ঠায় HMAC-SHA512, SQLCipher-এর নিজস্ব কী ডেরিভেশন।",
      b1c: "ঐচ্ছিক: পাসওয়ার্ড না থাকলে সাধারণ SQLite, যেকোনো কিছু দিয়ে পড়া যায়।",
      b1d: "প্রতি ক্যালেন্ডারে একটি পাসওয়ার্ড - কাজেরটি তালাবদ্ধ, পরিবারেরটি খোলা।",
      b2h: "এটি শুধু একটি ফাইল",
      b2a: "একটি <b>.kcal</b> ফাইলই আপনার পুরো ক্যালেন্ডার, আপনার নিজের প্রোফাইলে।",
      b2b: "সাধারণ SQLite ডাটাবেস - কপি করলেই ব্যাকআপ।",
      b2c: "এই অ্যাপ থাকুক বা না থাকুক, যেকোনো SQLite টুল দিয়ে পড়া যায়।",
      b2d: "বাকি সবকিছুর জন্য <b>.ics</b> ইমপোর্ট ও এক্সপোর্ট।",
      b3h: "চারটি ভিউ, একটি নিয়ন্ত্রণ",
      b3a: "মাস, সপ্তাহ, দিন ও সূচি - একই আগের / পরের / আজ ভাগ করে নেয়।",
      b3b: "ওভারল্যাপ অ্যাপয়েন্টমেন্ট পাশাপাশি বসে, কখনও লুকায় না।",
      b3c: "আপনার সংজ্ঞায়িত <b>রঙের বিভাগ</b> প্রতিটি ভিউ রাঙায়।",
      b3d: "তেরোটি থিম, এগারোটি ভাষা, এক-কী শর্টকাট।",
      dl: "ডাউনলোড", dlwin: "Windows-এর জন্য ডাউনলোড", gh: "GitHub-এ সোর্স",
      navTech: "টেকনিক্যাল", navAbout: "সম্পর্কে",
      note: "ইনস্টল করুন বা পোর্টেবল চালান। ফ্রি, ওপেন সোর্স, GPLv3।<br>অ্যাকাউন্ট নেই, সাবস্ক্রিপশন নেই, সিঙ্ক সার্ভিস নেই।<br>কোনো টেলিমেট্রি পাঠানো হয় না, বিজ্ঞাপনও নেই... কখনও না।",
      feats: "এটি কী করে",
      f1h: "মাস, সপ্তাহ, দিন, সূচি",
      f1p: "ওভারল্যাপ অ্যাপয়েন্টমেন্ট লেনে পাশাপাশি বসে, তাই ডাবল-বুক করা ঘন্টায় দুটোই দেখা যায়। সারাদিনের অ্যাপয়েন্টমেন্ট ঘন্টার গ্রিডের উপরে নিজস্ব স্ট্রিপ পায়।",
      f2h: "রঙের বিভাগ",
      f2p: "নিজেরটি বানান, নাম দিন, রঙ বাছুন। একটি অ্যাপয়েন্টমেন্ট একাধিক নিতে পারে, প্রথমটি সব ভিউয়ে তার রঙ ঠিক করে। নাম বদলালে সব অ্যাপয়েন্টমেন্ট একসাথে আপডেট হয়।",
      f3h: "যত খুশি ক্যালেন্ডার",
      f3p: "একটি কাজের, একটি অন-কলের, একটি পরিবারের। প্রতিটি নিজস্ব পাসওয়ার্ডসহ (বা ছাড়া) নিজস্ব ফাইল। <code>.kcal</code> ফাইল",
      f4h: "ঐচ্ছিক এনক্রিপশন",
      f4p: "স্টোরেজে SQLCipher: AES-256, প্রতি পৃষ্ঠায় HMAC-SHA512, নিজস্ব কী ডেরিভেশন। ঐচ্ছিক - পাসওয়ার্ড না থাকলে ফাইল সাধারণ SQLite থাকে।",
      f5h: "iCalendar আসা-যাওয়া",
      f5p: "ইমপোর্ট ও এক্সপোর্ট RFC 5545 অনুযায়ী লেখা, কোনো বাইরের ডিপেন্ডেন্সি নেই। বিভাগগুলো দুদিকেই অ্যাপয়েন্টমেন্টের সঙ্গে যায়। <code>.ics</code> ইমপোর্ট / এক্সপোর্ট",
      f6h: "তেরোটি থিম, এগারোটি ভাষা",
      f6p: "প্রতিটি থিম চলন্ত অবস্থায় বদলানো যায়, চারটিতে (Dark, Light, Black ও 98SE) ছয়টি অ্যাকসেন্ট রঙ। এক-কী শর্টকাট, যা ফিল্ডে টাইপ করার সময় সরে দাঁড়ায়।",
      f7h: "পোর্টেবল বা ইনস্টলড",
      f7p: "USB থেকে চালান, বা প্রতি ব্যবহারকারী কিংবা সবার জন্য ইনস্টল হতে দিন। winget, Chocolatey ও RMM-এর জন্য সাইলেন্ট পথ আছে।",
      f8h: "স্বাক্ষরিত এবং যাচাইযোগ্য",
      f8p: "সম্পর্কে কার্ডে প্রকাশক, সার্টিফিকেটের থাম্বপ্রিন্ট ও চলন্ত exe-এর SHA-256 দেখায়, শুধু ফাইল থেকে পড়ে নয়, WinVerifyTrust দিয়ে যাচাই করে।",
      foot: "<a href=\"https://github.com/SteveTheKiller/Killendar\" target=\"_blank\" rel=\"noopener\">GitHub-এ সোর্স</a> &middot; GPLv3 &middot; <a href=\"https://killertools.net\" target=\"_blank\" rel=\"noopener\">killertools.net</a>-এর অংশ",
      egg: "অ্যাকাউন্ট নেই। সিঙ্ক নেই। সার্ভার নেই। আপনার অ্যাপয়েন্টমেন্ট এই মেশিন ছেড়ে যায় না।"
    },
    'zh-CN': {
      tag: "一个可以加密的日历 - 无账户、无同步、不向外发送任何东西。",
      b1h: "需要时即可加密",
      b1a: "设个密码，文件就由 SQLCipher <b>静态加密</b>。",
      b1b: "AES-256、每页 HMAC-SHA512、SQLCipher 自带的密钥派生。",
      b1c: "可选：不设密码就是普通 SQLite，什么都能读。",
      b1d: "每个日历一个密码 - 工作锁上，家庭的敞开。",
      b2h: "它只是一个文件",
      b2a: "一个 <b>.kcal</b> 文件就是你的全部日历，存在你自己的配置文件夹里。",
      b2b: "普通的 SQLite 数据库 - 复制一份就是备份。",
      b2c: "任何 SQLite 工具都能读，有没有这个应用都行。",
      b2d: "其他一切用 <b>.ics</b> 导入导出。",
      b3h: "四种视图，一套控制",
      b3a: "月、周、日、日程，共用一组上一个 / 下一个 / 今天。",
      b3b: "重叠的约会并排显示，从不遮挡。",
      b3c: "你自己定义的<b>颜色类别</b>，给每个视图上色。",
      b3d: "十三套主题、十一种语言、单键快捷键。",
      dl: "下载", dlwin: "下载 Windows 版", gh: "GitHub 源码",
      navTech: "技术", navAbout: "关于",
      note: "安装或便携运行。免费、开源、GPLv3。<br>无账户、无订阅、无同步服务。<br>不发送遥测，没有广告... 永远。",
      feats: "它能做什么",
      f1h: "月、周、日、日程",
      f1p: "重叠的约会分轨并排，双重预约的一小时两个都看得到。全天条目在小时网格上方有自己的一栏。",
      f2h: "颜色类别",
      f2p: "自己定义、命名、选色。一个约会可以带多个类别，第一个决定它在所有视图里的颜色。重命名一次更新所有约会。",
      f3h: "日历想建几个建几个",
      f3p: "工作一个，值班一个，家庭一个。每个都是独立文件，各自设密码或不设。<code>.kcal</code> 文件",
      f4h: "可选加密",
      f4p: "静态 SQLCipher：AES-256、每页 HMAC-SHA512、自带密钥派生。可选 - 不设密码文件仍是普通 SQLite。",
      f5h: "iCalendar 进出自如",
      f5p: "导入导出按 RFC 5545 从零实现，无外部依赖。类别随约会双向同行。<code>.ics</code> 导入 / 导出",
      f6h: "十三套主题，十一种语言",
      f6p: "每套主题运行中即可切换。Dark、Light、Black 和 98SE 各有六种强调色，共 33 种外观。单键快捷键在你输入时自动避让。",
      f7h: "便携或安装",
      f7p: "从U盘直接运行，或按用户、按整机安装。为 winget、Chocolatey 和 RMM 提供静默安装路径。",
      f8h: "已签名，可验证",
      f8p: "关于卡片显示发布者、证书指纹和运行中 exe 的 SHA-256，用 WinVerifyTrust 验证而不是只从文件里读。",
      foot: "<a href=\"https://github.com/SteveTheKiller/Killendar\" target=\"_blank\" rel=\"noopener\">GitHub 源码</a> &middot; GPLv3 &middot; <a href=\"https://killertools.net\" target=\"_blank\" rel=\"noopener\">killertools.net</a> 成员",
      egg: "无账户。无同步。无服务器。你的约会永远不离开这台机器。"
    },
    'zh-TW': {
      tag: "一個可以加密的行事曆 - 無帳戶、無同步、不向外傳送任何東西。",
      b1h: "需要時即可加密",
      b1a: "設個密碼，檔案就由 SQLCipher <b>靜態加密</b>。",
      b1b: "AES-256、每頁 HMAC-SHA512、SQLCipher 自帶的金鑰派生。",
      b1c: "可選：不設密碼就是普通 SQLite，什麼都能讀。",
      b1d: "每個行事曆一個密碼 - 工作鎖上，家庭的敞開。",
      b2h: "它只是一個檔案",
      b2a: "一個 <b>.kcal</b> 檔案就是你的全部行事曆，存在你自己的設定檔資料夾裡。",
      b2b: "普通的 SQLite 資料庫 - 複製一份就是備份。",
      b2c: "任何 SQLite 工具都能讀，有沒有這個應用都行。",
      b2d: "其他一切用 <b>.ics</b> 匯入匯出。",
      b3h: "四種檢視，一套控制",
      b3a: "月、週、日、行程，共用一組上一個 / 下一個 / 今天。",
      b3b: "重疊的約會並排顯示，從不遮擋。",
      b3c: "你自己定義的<b>顏色類別</b>，給每個檢視上色。",
      b3d: "十三套佈景主題、十一種語言、單鍵快速鍵。",
      dl: "下載", dlwin: "下載 Windows 版", gh: "GitHub 原始碼",
      navTech: "技術", navAbout: "關於",
      note: "安裝或隨身執行。免費、開源、GPLv3。<br>無帳戶、無訂閱、無同步服務。<br>不傳送遙測，沒有廣告... 永遠。",
      feats: "它能做什麼",
      f1h: "月、週、日、行程",
      f1p: "重疊的約會分軌並排，雙重預約的一小時兩個都看得到。全天項目在小時格線上方有自己的一欄。",
      f2h: "顏色類別",
      f2p: "自己定義、命名、選色。一個約會可以帶多個類別，第一個決定它在所有檢視裡的顏色。重新命名一次更新所有約會。",
      f3h: "行事曆想建幾個建幾個",
      f3p: "工作一個，值班一個，家庭一個。每個都是獨立檔案，各自設密碼或不設。<code>.kcal</code> 檔案",
      f4h: "可選加密",
      f4p: "靜態 SQLCipher：AES-256、每頁 HMAC-SHA512、自帶金鑰派生。可選 - 不設密碼檔案仍是普通 SQLite。",
      f5h: "iCalendar 進出自如",
      f5p: "匯入匯出按 RFC 5545 從零實作，無外部相依。類別隨約會雙向同行。<code>.ics</code> 匯入 / 匯出",
      f6h: "十三套主題，十一種語言",
      f6p: "每套主題執行中即可切換。Dark、Light、Black 和 98SE 各有六種強調色，共 33 種外觀。單鍵快速鍵在你輸入時自動讓開。",
      f7h: "隨身或安裝",
      f7p: "從隨身碟直接執行，或按使用者、按整機安裝。為 winget、Chocolatey 和 RMM 提供靜默安裝路徑。",
      f8h: "已簽署，可驗證",
      f8p: "關於卡片顯示發行者、憑證指紋和執行中 exe 的 SHA-256，用 WinVerifyTrust 驗證而不是只從檔案裡讀。",
      foot: "<a href=\"https://github.com/SteveTheKiller/Killendar\" target=\"_blank\" rel=\"noopener\">GitHub 原始碼</a> &middot; GPLv3 &middot; <a href=\"https://killertools.net\" target=\"_blank\" rel=\"noopener\">killertools.net</a> 成員",
      egg: "無帳戶。無同步。無伺服器。你的約會永遠不離開這台機器。"
    }
  };

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
