# Launcher frontend performance and smoothness audit (src/Launcher/web)

All paths are relative to `src/Launcher/web/` unless they start with `src/` (the Rust backend, `src/Launcher/src/`). Sources are code locations (file:line), read on 2026-11-01. Impact estimates are reasoned from the code, not measured in a running WebView2. No profiling was done.

## 1. Loading and first paint: what blocks it

### Takeaway
The whole app (22 JS modules, 315,919 bytes) is one static module graph. `app.js` imports all 9 pages eagerly. Nothing renders until every module is fetched and evaluated. After that, the first frame shows the Play page and the default theme, then swaps to the saved page and theme once two IPC calls return. That swap is a visible flash on every start.

### Cited Findings
- Measured sizes (`find -printf %s`): `css/glass.css` 61,179; `js/pages/inventory.js` 57,368; `js/lineups-data.js` 49,083; `app.png` 45,574 (512x512 RGBA, shown at 40x40); `js/pages/live.js` 35,181; `js/pages/cases.js` 30,241; `js/pages/stats.js` 18,997; `js/pages/trainer.js` 18,457; `js/ui.js` 15,950; `js/pages/setup.js` 15,310; `js/vendor/preact-htm.js` 13,194; `js/pages/friends.js` 12,525; `js/pages/play.js` 10,886; `js/app.js` 9,972; `css/pages/play.css` 7,951; `js/icons.js` 6,770; `css/pages/sheets.css` 5,536; `js/palette.js` 4,487; `js/pages/workshop.js` 4,298; `js/theme.js` 3,560; `js/update.js` 3,393; `js/api.js` 3,199; `js/fade.js` 3,048; `css/pages/friends.css` 2,072; `index.html` 544. Fonts: Inter 400/500/600/700/800 are about 24 KB each (121 KB in total). OpenDyslexic 400 is 115,280 and 700 is 120,356. Total JS is 315,919 B. Total CSS is 76,738 B. — [index sizes](src/Launcher/web)
- `index.html` loads 4 render-blocking stylesheets (`glass.css`, `play.css`, `friends.css`, `sheets.css`), then one module script, `js/app.js`. There is no preload or modulepreload. — [index.html:7-10,16](src/Launcher/web/index.html)
- `app.js` statically imports all 9 pages, so every page module is on the critical path. That includes `inventory.js`, `live.js` and `cases.js`, and through `trainer.js` also `lineups-data.js` (49 KB of data). — [js/app.js:8-16](src/Launcher/web/js/app.js); [js/pages/trainer.js:7](src/Launcher/web/js/pages/trainer.js)
- `fade.js` (the fade % calculator) is imported eagerly by `inventory.js`. Its 1001-seed RNG table runs only when it is called, and the result is memoized. — [js/pages/inventory.js:4](src/Launcher/web/js/pages/inventory.js); [js/fade.js:39-58](src/Launcher/web/js/fade.js)
- The module graph is waterfall-shaped. `app.js`, then `ui.js`/pages, then `icons.js`/`api.js`/`play.js`/`inventory.js` (shared imports such as `cases.js` importing `inventory.js`, `live.js` importing `play.js`). The browser finds each level only after it parses the level above. — [js/pages/cases.js:5](src/Launcher/web/js/pages/cases.js); [js/pages/live.js:5](src/Launcher/web/js/pages/live.js); [js/pages/friends.js:5](src/Launcher/web/js/pages/friends.js)
- Wrong first page. `page` starts as `null`, so the first render picks `pages.find(...)`, which is undefined, then `?? PlayPage`. PlayPage mounts and fires its `play.state` and `play.health` calls. When `app.state` returns, the effect sets `state.page`. Any other saved page then unmounts Play and mounts the real page: double work and a visible flash. — [js/app.js:114,117,166,169](src/Launcher/web/js/app.js); [js/pages/play.js:13,62](src/Launcher/web/js/pages/play.js)
- Theme flash. The theme is applied only after the `setup.state` IPC call resolves, so the first frames use the hard-coded `:root` colours. A user with a non-default preset or background picture sees a colour/radius/font swap. — [js/app.js:115,154](src/Launcher/web/js/app.js); [js/theme.js:29-55](src/Launcher/web/js/theme.js); [css/glass.css:14-58](src/Launcher/web/css/glass.css)
- At startup about 6 IPC calls start at once: `app.state`, `setup.state`, `app.log` (module side effect in ui.js), and the prefetch of `play.state`, `cases.inventory` and `friends.state`. Then come the current page's own calls. `cases.inventory` is heavy on the backend: it reads the loadout, may sync StatTrak and save, and builds a view of every item. Each call runs in `spawn_blocking`. — [js/app.js:113,115,120](src/Launcher/web/js/app.js); [js/ui.js:284](src/Launcher/web/js/ui.js); [src/api/cases.rs:531-552](src/Launcher/src/api/cases.rs); [src/main.rs:42-49](src/Launcher/src/main.rs)
- `@font-face` rules have no `font-display`, so Chromium's default `auto` (block) applies. Fonts are local files, so the delay is small. Fonts load on first use, so OpenDyslexic (235 KB) loads only when it is chosen. — [css/glass.css:7-13](src/Launcher/web/css/glass.css)
- `watchCustomCss()` adds a `<link>` to `custom.css` with `?v=Date.now()` at startup. It reloads the file on every window `focus` event, and never removes that listener. Each alt-tab back refetches the file and recalculates the styles of the whole document. — [js/theme.js:57-68](src/Launcher/web/js/theme.js)
- `app.png` is a 512x512 PNG (45.6 KB) shown at 40x40 with a box-shadow. — [js/app.js:79](src/Launcher/web/js/app.js); [css/glass.css:194](src/Launcher/web/css/glass.css)

### Inferences (fixes and estimated impact)
- **Lazy-load pages (high impact on start).** Keep `PlayPage` (and Setup if it is the saved page) static. Load the others with `import()` in `pages[]` and render a `Page` shell while they load. Warm them with `requestIdleCallback(() => import(...))` after the first paint. This takes about 250 KB of the 316 KB JS (inventory, live, cases, stats, trainer with lineups-data, friends, workshop) off the critical path. Of all fixes here, it gives the biggest gain in time to first paint.
- **Render the saved page first (medium).** Keep `last_page` and the theme in `localStorage` after each change, and read them synchronously before `render()`. Then `useState(() => localStorage.page)` and `applyTheme(cachedTheme)` run before the first frame. This removes the Play-to-saved-page flash and the theme flash, plus one wasted page mount.
- **Add `<link rel="modulepreload">`** for `vendor/preact-htm.js`, `api.js`, `ui.js`, `icons.js` and `pages/play.js`. Add `<link rel="preload" as="font" crossorigin>` for Inter 400 and 600. This flattens the waterfall (low to medium; local disk, so latency per level is small).
- **Delay the prefetch.** Run the `cases.inventory` prefetch on idle or after first paint, so the heavy backend call does not compete with `app.state`/`setup.state` (low to medium).
- **custom.css.** Reload it only when the file's mtime changed (ask the backend), or only on an explicit "Reload" action (low; removes a full style recalc on every focus).
- Resize `app.png` to 80x80 for 2x DPI, about 3 KB (very low).
- Split `glass.css` (61 KB) per page, or accept it: parsing CSS is fast. A low priority.

### Gaps
- No real timing was taken from WebView2 (no DevTools trace). The ranking is reasoned from the code.
- Tauri's custom-protocol fetch latency per module on Windows was not measured.

## 2. Rendering: re-renders, keys, large lists, effects, polling

### Takeaway
The largest rendering costs are these. Every backend `changed` event reloads every mounted `useApi` hook with no deduplication. App's state changes re-render the whole current page, because the page is not memoized. The Inventory page re-filters and re-sorts the whole inventory synchronously on every keystroke and every render. Polling (2 s, 3 s, 8 s) is moderate, but some of it runs while the page is hidden behind sub-tabs.

### Cited Findings
- `useApi` subscribes every hook instance to the global `"changed"` event (plus any extra events), and each event triggers a new IPC call. There is no deduplication of calls in flight across instances that share the same key. Examples: `cases.inventory` is used by InventoryPage, TradeUpSheet, the sticker sheet and the cases.js trade-up. `app.state` is used by App and SetupPage. `setup.state` is used by App and SetupPage. — [js/api.js:52-71](src/Launcher/web/js/api.js); [js/pages/inventory.js:152,346,685](src/Launcher/web/js/pages/inventory.js); [js/pages/cases.js:384](src/Launcher/web/js/pages/cases.js); [js/pages/setup.js:10,151](src/Launcher/web/js/pages/setup.js); [js/app.js:113,115](src/Launcher/web/js/app.js)
- The backend sends `changed` from about 75 sites, for example every case open, currency change and setup action. Its comment says "every page reloads its data". — [src/api/mod.rs:167-170](src/Launcher/src/api/mod.rs); e.g. [src/api/cases.rs:558,650,692,706,713](src/Launcher/src/api/cases.rs)
- `play.tick` is sent only when the tick JSON differs from the last one, which is good. But `usePlay`, `play.health`, `friends.state`, `trainer.lineups` and `trainer.stats` all reload on every tick. — [src/api/play.rs:645-657](src/Launcher/src/api/play.rs); [js/pages/play.js:13,62](src/Launcher/web/js/pages/play.js); [js/pages/friends.js:65](src/Launcher/web/js/pages/friends.js); [js/pages/trainer.js:132](src/Launcher/web/js/pages/trainer.js); [js/pages/stats.js:226](src/Launcher/web/js/pages/stats.js)
- Results are always replaced with `setData(result)`, even when nothing changed, so each reload re-renders the consumer. The cache gives stale-while-revalidate on page switch: it shows the cached result at once, then always refetches. — [js/api.js:56-62](src/Launcher/web/js/api.js)
- The page is not memoized. `App` renders `<${Current} key=${page} />` with no props, but Preact re-renders child components whenever the parent renders, unless they use `memo` or `shouldComponentUpdate`. Every `app.state` or `setup.state` result, for example after each `changed`, therefore re-renders the whole current page tree. The Inventory page's filter and sort is included. — [js/app.js:112-173](src/Launcher/web/js/app.js)
- Inventory on each render, keystroke included, since `q` is not debounced:
  - `state.items.filter(...)` over every item.
  - a full copy plus `sort` whose comparator calls `favs.has` twice and, for "rarity", `GRADE_RANK.indexOf` twice.
  - `grades = GRADE_RANK.filter(g => state.items.some(...))`, which is O(grades×N).
  - a `cases` Set over all items, and `state.items.find` for the selected item.
  - None of this is memoized. Only `duplicates` uses `useMemo`, and the `haystack` and `openedAt` strings are cached in a WeakMap.
  — [js/pages/inventory.js:13-16,684-737](src/Launcher/web/js/pages/inventory.js)
- The inventory grid is paged at `PAGE = 120` cards, with "Show more" adding 120. There is no virtualization. Each card holds an `<img loading=lazy>` with a CSS `drop-shadow` filter. After a few "Show more" clicks, hundreds of filtered images stay mounted. The Loadout view caps at 240. — [js/pages/inventory.js:18-21,784-797,648](src/Launcher/web/js/pages/inventory.js); [css/glass.css:809-815](src/Launcher/web/css/glass.css)
- `.cs-grid` has no `content-visibility`/`contain`. The only `content-visibility: auto` in the CSS is in `sheets.css`. — [css/glass.css:809](src/Launcher/web/css/glass.css); [css/pages/sheets.css:31](src/Launcher/web/css/pages/sheets.css)
- `LogView` keeps up to 600 lines and re-renders the whole list on every `log` event, with no keys and a regex test per line. Its effect has no deps, so it runs after every render and writes `scrollTop = scrollHeight`, which forces layout. — [js/ui.js:282-306](src/Launcher/web/js/ui.js)
- The Live page `Logs` polls `live.logs` every 2000 ms while the Server/Plugin tab is open. Each poll runs `JSON.stringify` on both the old and the new log files to compare them. Each render runs `isError` (a long regex) twice per line, once in the filter and once in the class, plus `toLowerCase` per line for search. — [js/pages/live.js:76-106](src/Launcher/web/js/pages/live.js)
- The Live page `Performance` polls `play.perf` every 3000 ms for as long as it is mounted, and calls `setP` on every result, so it always re-renders. — [js/pages/live.js:146-152](src/Launcher/web/js/pages/live.js)
- Friends polls `friends.list` every 8000 ms through `reload`. Because `call()` turns any error into a toast, a failing list call shows a toast every 8 s. — [js/pages/friends.js:22-23](src/Launcher/web/js/pages/friends.js); [js/api.js:12-19](src/Launcher/web/js/api.js)
- Missing keys on dynamic lists: `ContentCard` in the case contents, the Live log lines, `LogView` lines, the stats tables and the Live player badges. Most of these lists are append-only or static, so the cost is low. The log lists shift from the front (splice at 600), which makes Preact patch every row. — [js/pages/cases.js:288](src/Launcher/web/js/pages/cases.js); [js/pages/live.js:105,115,133](src/Launcher/web/js/pages/live.js); [js/ui.js:305](src/Launcher/web/js/ui.js); [js/pages/stats.js:141,267](src/Launcher/web/js/pages/stats.js)
- Every `Segmented` instance creates its own `ResizeObserver` and measures with `offsetLeft/offsetWidth` in a layout effect. Its deps include `items.map(i => i.label).join()`, which is recomputed every render. The Inventory page has several Segmented controls, and the Live page has many. — [js/ui.js:76-97](src/Launcher/web/js/ui.js)
- The global sheen handler is a `pointermove` listener on `window` for the app's lifetime. On every pointer move it calls `closest(".sheen")`. Over a `.sheen` it calls `getBoundingClientRect()` (forced layout if dirty) and two `style.setProperty` calls with no rAF throttle. `.sheen` appears only on the Play quick tiles. — [js/app.js:158-162](src/Launcher/web/js/app.js); [js/ui.js:9-13](src/Launcher/web/js/ui.js); [js/pages/play.js:139](src/Launcher/web/js/pages/play.js)
- Each page switch also calls `app.page`. On the backend it sets the window title and calls `app.save(&st)`, a settings write, on every switch. — [js/app.js:122-125](src/Launcher/web/js/app.js); [src/api/shell.rs:72-87](src/Launcher/src/api/shell.rs)

### Inferences (fixes and estimated impact)
- **Deduplicate and share `useApi` (high).** Keep one in-flight promise per `cacheKey` and one shared subscription per key, with all subscribers fed from it. Skip `setData` when the new result is deep-equal to the old one (compare the JSON string, as `live.js:82` already does). Then one `changed` event costs one IPC call per distinct key, not one per hook, and causes no re-render when the data did not change.
- **Memoize the page (high).** Wrap the page components in `memo` from `preact/compat` (if it is vendored), or move `useApi("app.state")` and `useApi("setup.state")` out of `App` into `Sidebar`, `UpdatePopup` and a small `Appearance` component. Then App state changes do not re-render the page.
- **Inventory (high for big inventories).**
  - Wrap the filter and sort in `useMemo` keyed on `[state, tab, onlyST, onlyFav, fromCase, minP, maxP, grade, wear, sort, favs, words]`.
  - Put `grades` and `cases` in a `useMemo` on `[state]`.
  - Precompute a grade rank Map.
  - Debounce `q` by about 120 ms, as `AddItemSheet` already does (`inventory.js:523`).
  - Add `content-visibility: auto; contain-intrinsic-size: 150px 190px` to `.cs-item`, or virtualize the grid.
  - Expected result: keystrokes stay below one frame with thousands of items. Today the cost is O(N log N) plus string work per keystroke.
- **LogView and Live logs (medium while those views are open).** Give the `useEffect` deps (`[logLines.length]`). Scroll only when the view was already at the bottom. Precompute `isError` once per line, for example in a cached WeakMap or at insert time. Render with `key` set to the line index plus a running offset. Consider capping the rendered lines to the last 200.
- **Polling (low to medium).** Pause `setInterval` polls when `document.hidden` is true, or when the Tauri window is not focused (`visibilitychange`). Compare before `setP`. For friends, use `call(...).catch(() => {})` without a toast, or back off on errors.
- **Sheen (low).** Attach `pointermove` only to the Play page's quick tiles, or rAF-throttle the handler and cache the rect on `pointerenter`.
- **`app.page` (low, backend).** Debounce the settings save, or write `last_page` only when the app exits.

### Gaps
- How big real inventories get. The code comment says "thousands" (`inventory.js:18`), but the payload size of `cases.inventory` was not measured.
- Whether `preact/compat` (`memo`) is available in the vendored bundle was not checked: `preact-htm.js` is minified and was not inspected.

## 3. Animation and CSS cost

### Takeaway
`backdrop-filter` is applied very widely. Every `.glass` element gets it, even when the blur is `0px`. Every `.btn` always gets `blur(16px) saturate(160%)`, whatever the theme or the "Reduce glass" setting. A few animations change layout or paint properties every frame: the case reel's per-frame `filter: blur`, the winner box-shadow pulse, and the width/height of the Segmented thumb. Many item images carry `filter: drop-shadow`.

### Cited Findings
- `.glass` always sets `backdrop-filter: blur(var(--glass-blur)) saturate(var(--glass-sat))`. The default is `--glass-blur: 0px; --glass-sat: 100%`, and `applyTheme` sets 0px when opacity is 100. A `backdrop-filter` other than `none` still creates a backdrop-filter stacking context and compositing layer in Chromium, even at 0 px. `.glass` also has a two-layer `box-shadow`, `isolation: isolate`, and a `::before` rim drawn with a mask-composite. — [css/glass.css:41-43,121-147](src/Launcher/web/css/glass.css); [js/theme.js:41-42](src/Launcher/web/js/theme.js)
- `.btn` has a hard-coded `backdrop-filter: blur(16px) saturate(160%)`, a three-layer box-shadow and a transition on `box-shadow`. `:root.reduce-glass` only changes `--glass-blur`, so buttons keep blurring when "Reduce glass" is on. Only `.btn.plain` turns it off. — [css/glass.css:380-407,65-70](src/Launcher/web/css/glass.css)
- The `.scrim` behind sheets uses `backdrop-filter: blur(6px)` over the whole window while it fades in. — [css/glass.css:658](src/Launcher/web/css/glass.css)
- The case reel sets `strip.style.filter = blur(...)` on every frame while it moves fast. The strip holds many `ItemPic` images, so a large filtered layer is re-rasterized every frame. The strip itself has `will-change: transform` and moves with `translate3d`, which is good. — [js/pages/cases.js:118-132](src/Launcher/web/js/pages/cases.js); [css/glass.css:786](src/Launcher/web/css/glass.css)
- `.reel-card.winner` runs `winner-pulse`, an infinite animation of `box-shadow`, which is a repaint every frame. The light rays use `repeating-conic-gradient` with a radial `mask-image`, rotating infinitely (18 s) on a 560x560 element. The rotation is a transform, which is fine. — [css/glass.css:852-853,866-870](src/Launcher/web/css/glass.css)
- Layout-property transitions:
  - The `.segmented .thumb` transitions `width` and `height` with a 0.5 s spring, so it re-lays out every frame of every segmented change.
  - The `.switch .knob` transitions `width`.
  - `.progress > div` transitions `width`.
  — [css/glass.css:460,433,608](src/Launcher/web/css/glass.css); [js/ui.js:93](src/Launcher/web/js/ui.js); [js/ui.js:212](src/Launcher/web/js/ui.js)
- `filter: drop-shadow(...)` is set on these images:
  - every `.cs-item .pic img` (inventory, case contents)
  - `.finish .pic img` in sheets, which also has a transform transition
  - inspect and reveal pictures, sticker canvas images and case pictures
  - the `case-float` infinite animation on `.case-hero-pic img`, which also has a drop-shadow
  — [css/glass.css:815,826,795,879,956-957,969](src/Launcher/web/css/glass.css); [css/pages/sheets.css:51,70](src/Launcher/web/css/pages/sheets.css)
- `.cs-item:hover` animates `transform` together with a `box-shadow` change (0.12 s). — [css/glass.css:811-813](src/Launcher/web/css/glass.css)
- Page switches animate only `transform` (`page-in`, `translateY(10px)`, 0.35 s). The comment says opacity was removed on purpose, so that the card blur would not flicker. — [css/glass.css:279,286-291](src/Launcher/web/css/glass.css)
- Infinite animations of compositor-friendly properties (`transform`/`opacity`): `spin`, `slide`, `pulse`, `join-pulse`, `check-spin`, `sign-pen`. These are cheap unless the animated element sits under a backdrop-filter. — [css/glass.css:602-610,1048,1063,943](src/Launcher/web/css/glass.css); [css/pages/play.css:47](src/Launcher/web/css/pages/play.css); [css/pages/sheets.css:113](src/Launcher/web/css/pages/sheets.css)
- `.reduce-motion` turns off all animations and transitions with `!important`. — [css/glass.css:72-75](src/Launcher/web/css/glass.css)
- `applyTheme` cost: about 12 `style.setProperty` calls on `documentElement`, each a root custom property, so each forces a style recalc of the whole document (batched into one recalc per frame). It runs only when `JSON.stringify(settings.theme)` or `reduceGlass` changes, so it is cheap in steady state. — [js/theme.js:30-54](src/Launcher/web/js/theme.js); [js/app.js:154](src/Launcher/web/js/app.js)
- `fade.js` is not an animation. It is a fade % calculator: 1001 RNG runs per finish and weapon, memoized. — [js/fade.js:39-62](src/Launcher/web/js/fade.js)

### Inferences (fixes and estimated impact)
- **Remove the backdrop-filter when it does nothing (high for scrolling and switch smoothness).**
  - Default `.glass` to `backdrop-filter: none`. Add a `.see-through` class on `:root` (set by `applyTheme` when opacity < 100) that turns the blur on.
  - Make `.btn` follow the same rule, or use `var(--glass-blur)`. A button over a solid card blurs nothing it can show anyway.
  - This removes dozens of backdrop layers per page. Each one samples and blurs what is behind it on every scroll frame and every animated frame beneath it.
- **Reel (medium, during case opens).** Remove the per-frame `filter: blur`. Instead, crossfade a pre-blurred duplicate strip with `opacity`, or skip motion blur.
- **Winner pulse (low to medium).** Animate `opacity` on a `::after` glow layer instead of `box-shadow`.
- **Segmented thumb, switch knob and progress (low to medium).** Animate `transform: translateX() scaleX()` instead of `width/height`. For progress, use `transform: scaleX(p)` with `transform-origin: left`.
- **drop-shadow on many images (medium on the Inventory grid).**
  - Replace it on `.cs-item .pic img` with a static radial-gradient shadow under the picture, or bake it in.
  - Or keep it and add `content-visibility: auto` so only visible cards paint.

### Gaps
- No GPU or paint profile was taken, so the cost of 0 px backdrop-filter layers in this WebView2 version is inferred from known Chromium behaviour, not measured here.

## 4. API calls (api.js call / tryCall / useApi)

### Takeaway
Calls are mostly parallel, because each `useApi` starts on its own. The cost comes from repetition, not from serial awaits: no deduplication, a refetch on every mount and every `changed`, and polling. The few serial awaits in the code are deliberate animation minimums.

### Cited Findings
- `call()` is a single `invoke("call", {name, args})`. Every error becomes a toast. — [js/api.js:12-19](src/Launcher/web/js/api.js)
- The `useApi` cache key is `name|JSON.stringify(args)`. On mount it shows the cached data, then always refetches. A sequence number drops stale answers. — [js/api.js:52-71](src/Launcher/web/js/api.js)
- `prefetch` warms only `play.state`, `cases.inventory` and `friends.state`, once at startup. It does not refresh them after `changed`, so a cached value can be stale until the page mounts and refetches. — [js/app.js:120](src/Launcher/web/js/app.js); [js/api.js:74-77](src/Launcher/web/js/api.js)
- Deliberate serial or minimum waits:
  - case open uses `Promise.all([call, 900 ms])`.
  - trade-up sign uses `Promise.all([call, 1600 ms])`.
  - Both run in parallel and are UX timing, not waste.
  — [js/pages/cases.js:229](src/Launcher/web/js/pages/cases.js); [js/pages/inventory.js:400](src/Launcher/web/js/pages/inventory.js)
- `tradeup.check` is called on every change to the picked set. It has a stale guard but no debounce. — [js/pages/inventory.js:363-366](src/Launcher/web/js/pages/inventory.js)
- `AddItemSheet` debounces the catalog search by 180 ms. This is the pattern to copy for the Inventory search. — [js/pages/inventory.js:521-525](src/Launcher/web/js/pages/inventory.js)
- `inventory.unitItems` is called with `open ?? ""`, so it fires even when no unit is open. — [js/pages/inventory.js:50](src/Launcher/web/js/pages/inventory.js)
- Backend `call` runs every request on `spawn_blocking`, and many handlers take the global `app.state()` lock. Many parallel calls (startup, `changed` storms) then queue on that lock. — [src/main.rs:42-49](src/Launcher/src/main.rs); [src/api/cases.rs:532](src/Launcher/src/api/cases.rs)

### Inferences (fixes and estimated impact)
- Deduplicate in-flight calls per key and skip `setData` on equal data (see section 2) (high).
- Have the backend's `changed` carry a scope, such as `"inventory"`, `"setup"` or `"play"`. Then `useApi` reloads only the keys it affects. Today one case open reloads app.state, setup.state and every mounted hook (medium to high when many hooks are mounted).
- Skip `inventory.unitItems` when `open` is null, and debounce `tradeup.check` by about 100 ms (low).

### Gaps
- The size of the `cases.inventory` JSON for a real inventory, and the backend time per call, were not measured.

## 5. Pages: heaviest at mount and lazy-loading opportunities

### Takeaway
The heaviest at mount are Inventory, Live, Cases and Stats. Inventory is the largest module (57 KB) and does the most work per render. Live mounts the most controls, two polls and a log view. Trainer pulls in 49 KB of lineup data. All pages are loaded eagerly today.

### Cited Findings
- Inventory: a 57 KB module. On mount it renders about 9 header buttons, each with a 16 px backdrop blur. It renders 6 Segmented options plus 4 Selects, 2 Toggles, 3 TextFields, and up to 120 cards with images and drop-shadow filters. The filter and sort cost is in section 2. — [js/pages/inventory.js:739-797](src/Launcher/web/js/pages/inventory.js)
- Live: 35 KB. It has many Selects, Segmenteds and Sliders, and an action grid of `Button`s, each with a backdrop blur. It also has the `Performance` 3 s poll and the `Logs` 2 s poll when that tab is open. — [js/pages/live.js:146-152,76-86,136-141,215-424](src/Launcher/web/js/pages/live.js)
- Cases: 30 KB. It has a case-tile grid, contents grouped by grade (`ContentCard` without keys), the reel animation, and a WebAudio sound synth. — [js/pages/cases.js:100-140,269-290,466](src/Launcher/web/js/pages/cases.js)
- Stats: 19 KB. It builds SVG charts and tables from `trainer.stats` and reloads them on every `play.tick`. — [js/pages/stats.js:79-100,226-270](src/Launcher/web/js/pages/stats.js)
- Trainer: 18 KB plus `lineups-data.js` (49 KB), imported statically. — [js/pages/trainer.js:7](src/Launcher/web/js/pages/trainer.js)
- Every page switch remounts the page, because of `key=${page}`. That reruns its mount effects and refetches. — [js/app.js:169](src/Launcher/web/js/app.js)

### Inferences (fixes and estimated impact)
- Lazy-load candidates by gain: inventory (57 KB, and cases.js depends on it), lineups-data and trainer (67 KB together), live (35 KB), cases (30 KB), stats (19 KB), friends (12.5 KB), workshop (4 KB). After first paint, warm them on idle, so page switches stay instant (high for start, neutral for switches).
- Inside Trainer, load `lineups-data.js` with `import()` only when the lineups view opens (medium; 49 KB of parse).
- For instant switches between heavy pages, consider keeping recently visited pages mounted but hidden with `display:none` (memory cost). Or keep remounting, but make the first render cheap with the memoization from section 2.

### Gaps
- Mount timing per page was not measured. The ranking comes from module size, mounted components and effects.
