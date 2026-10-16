// Setup › Appearance: turns the saved theme values into CSS variables, plus the background picture
// and the user's custom.css (both served by the launcher from its data folder).

const token = new URLSearchParams(location.search).get("t") ?? "";

/** Ready-made looks. Picking one sets these values; each can still be changed after. */
export const PRESETS = [
  { id: "midnight", name: "Midnight", accent: "#0a84ff", background: "#0f1117", panel: "#1a1d26" },
  { id: "black", name: "OLED Black", accent: "#0a84ff", background: "#000000", panel: "#121214" },
  { id: "slate", name: "Slate", accent: "#64d2ff", background: "#1b1f27", panel: "#262b36" },
  { id: "cs2", name: "CS2 Orange", accent: "#ff9f0a", background: "#121012", panel: "#1f1b1a" },
  { id: "ops", name: "Green Ops", accent: "#30d158", background: "#0c120e", panel: "#17201a" },
  { id: "violet", name: "Violet", accent: "#bf5af2", background: "#110e19", panel: "#1d1829" },
  { id: "crimson", name: "Crimson", accent: "#ff453a", background: "#130d0e", panel: "#211617" },
  { id: "ocean", name: "Ocean", accent: "#00c7be", background: "#0a1216", panel: "#132027" },
];

export const DEFAULTS = { accent: "#0a84ff", background: "#0f1117", panel: "#1a1d26", opacity: "100", radius: "100", font: "inter", dim: "45" };

const FONTS = {
  inter: `Inter, "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif`,
  segoe: `"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif`,
  system: `system-ui, sans-serif`,
  mono: `"Cascadia Mono", Consolas, ui-monospace, monospace`,
};

const RADII = { "--r-xl": 30, "--r-lg": 22, "--r-md": 14, "--r-sm": 10 };

/** Applies the theme to the page. Called whenever Setup's state changes. */
export function applyTheme(theme = {}, solid = false) {
  const t = { ...DEFAULTS, ...theme };
  const root = document.documentElement.style;
  // "Reduce Transparency" always wins over the opacity slider.
  const opacity = solid ? 100 : Math.min(100, Math.max(40, Number(t.opacity) || 100));
  root.setProperty("--bg", t.background);
  root.setProperty("--panel", t.panel);
  root.setProperty("--blue", t.accent);
  root.setProperty("--glass-tint", `color-mix(in srgb, ${t.panel} ${opacity}%, transparent)`);
  root.setProperty("--glass-tint-2", `color-mix(in srgb, color-mix(in srgb, ${t.panel} 90%, black) ${opacity}%, transparent)`);
  // See-through panels blur what is behind them; fully solid ones don't need to.
  root.setProperty("--glass-blur", opacity < 100 ? "22px" : "0px");
  root.setProperty("--glass-sat", opacity < 100 ? "140%" : "100%");
  const k = (Number(t.radius) || 0) / 100;
  for (const [name, px] of Object.entries(RADII)) root.setProperty(name, `${Math.round(px * k)}px`);
  root.setProperty("--font", FONTS[t.font] ?? FONTS.inter);

  if (theme.image) {
    root.setProperty("--bg-image", `url("/theme/background?t=${token}&v=${theme.image}")`);
    root.setProperty("--bg-dim", String((Number(t.dim) || 0) / 100));
  } else {
    root.removeProperty("--bg-image");
    root.removeProperty("--bg-dim");
  }
}

/** Loads custom.css after the built-in styles, and reloads it when the window gets focus (after editing it). */
export function watchCustomCss() {
  let link = document.getElementById("custom-css");
  if (!link) {
    link = document.createElement("link");
    link.id = "custom-css";
    link.rel = "stylesheet";
    document.head.appendChild(link);
  }
  const reload = () => { link.href = `/theme/custom.css?t=${token}&v=${Date.now()}`; };
  reload();
  window.addEventListener("focus", reload);
}
