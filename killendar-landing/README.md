# killendar-landing

The killendar.net site. No build step: the folder is dragged into Cloudflare Pages by hand,
the same as `pdf-landing/`.

`kd.css` is a copy of `KillerShell/shell-landing/ks.css`, changed in only four ways, so the
spacing, grain, scrollbars and pane treatment are the family's rather than a reimplementation:

1. the six palette lines, mapped onto the app's own `Themes/*.xaml` values
2. the `hc` theme key renamed to `black`, which is what Killendar calls it
3. the font-face and every font stack swapped for the typewriter TTF
4. KillerShell-only blocks dropped: language switcher, accent bar, diagram lightbox

## What is here

| File | What it is |
| --- | --- |
| `index.html` | Hero, three bullet blurbs, feature cards, screenshot sidebar. Theme and accent are applied before first paint by the inline script in `<head>`, or the browser's white flashes through. |
| `technical.html` | The `.kcal` format, time handling, encryption and what it does NOT protect, iCalendar, the keyboard map and shortcut tables, running it, signing, and a glossary. The keyboard map is inline SVG themed from CSS variables, generated from `Shell/Shortcuts.cs` - regenerate it if a binding changes. |
| `about.html` | Why it exists, what it will never do, GPLv3, Killer Tools. |
| `kd.css` | Shared styles. See above. |
| `kd.js` | Theme swatches, accent flyout, screenshot strip fades, lightbox, version egg. Shared by all three pages. No framework. |
| `_headers` | Cloudflare security headers, including the CSP. |
| `robots.txt`, `sitemap.xml` | All three pages are listed. |
| `grain.png` | The family texture, byte-identical to shell-landing's. |
| `og-image.png` | 1200x630, generated from the real icon and the real wordmark font. |
| `brand/` | `kd-icon.png`, `kd-icon.ico`, the typewriter TTF every heading renders in, and the wordmark SVGs. |

The wordmark is an `<img>`, not styled text, matching the rest of the family. `brand/` holds 18
`killendar-logo-<variant>-<accent>.svg` (3 variants x 6 accents) which `updateLogos()` in `kd.js`
swaps on any theme or accent change, plus a `-brand-` pair per sibling app for the "Also try..."
grid on about.html. All of them come from `Killer Branding/make-logo-svgs.py` and share a
996-unit viewBox height with every other app's, which is what keeps "Killer" the same size across
all five sites - regenerate rather than hand-editing, and never change the viewBox height.

Layout: a `.topbar` with the home lockup, download button, nav, six theme swatches and the
accent flyout; a `.shell` grid holding the rounded `.content` card beside a 268px screenshot
`.sidebar`; the app's statusbar as the footer, corner grip dots included. The accent flyout
hides itself on blood, greed and cyanotic, which carry their own built-in accent - the same
rule the app's theme flyout follows.

## Still to do

- **Placeholders.** `index.html` has two `TODO`s in the hero info panel that need a release
  build: the exe **size**, and the **sha256** from `bin\Release\net48\publish\SHA256SUMS.txt`
  (lowercase, split across two lines with `<br>`, the way killerpdf.net does it). Version and
  release date are already filled from the csproj.
- **Screenshots.** The sidebar shows "coming soon". Capture with `Killendar.exe --demo` so the
  calendar has real content and the About card renders signed, drop them in `screenshots/`, and
  replace the `.sb-empty` div with one `<button class="sb-thumb"><img ...></button>` per shot.
- **A bigger icon.** `brand/kd-icon.png` is 128x128 but the hero renders it at up to 230px, so
  it is being upscaled. Export a 512px PNG from `brand/logo.xcf` when convenient. The `.ico`
  does carry a 256px frame, so the favicon is fine.
- ~~**Analytics.**~~ Done 2026-07-30. Umami, `data-website-id` `158a903b-...`, on all three
  pages, with `koya.thekiller.net` added to both `script-src` and `connect-src` in `_headers`.
  The two go together: the tag without the CSP entry is silently blocked and looks like a
  server problem. This measures the SITE, not the app - Killendar itself still phones nobody,
  which is the claim the copy actually makes.
- **howto.html and i18n.** shell-landing has both; neither is built here. The app is localized,
  the site is not.

## Bug found in a sibling site

`KillerShell/shell-landing/robots.txt` points at **`https://killerscan.net/sitemap.xml`** rather
than killershell.net - a copy-paste from scan-landing. Worth fixing there.
