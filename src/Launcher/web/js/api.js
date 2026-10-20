// Talks to the launcher (Rust, Tauri): calls, live events and toasts.
import { useState, useEffect, useCallback, useRef } from "./vendor/preact-htm.js";

const { invoke, convertFileSrc } = window.__TAURI__.core;
const listeners = new Map();
/** Last result of each useApi call ("name|args"), so a page shows at once when you switch back to it. */
const cache = new Map();

/** The launcher's own files (pictures, theme background, custom.css): lst://localhost/ or http://lst.localhost/. */
export const media = convertFileSrc("", "lst");

/** Calls a launcher method. Errors become a toast and reject with the message. */
export async function call(name, args) {
  try {
    return await invoke("call", { name, args: args ?? null });
  } catch (error) {
    toast(String(error), "bad");
    throw new Error(String(error));
  }
}

/** Like call(), but swallows the error (the toast already told the user). */
export const tryCall = (name, args) => call(name, args).catch(() => undefined);

/** Subscribes to a launcher event ("changed", "log", "toast", page-specific ones). Returns unsubscribe. */
export function on(type, fn) {
  if (!listeners.has(type)) listeners.set(type, new Set());
  listeners.get(type).add(fn);
  return () => listeners.get(type).delete(fn);
}

function emit(type, data) {
  listeners.get(type)?.forEach(fn => fn(data));
}

/** Image URL through the launcher's picture cache. */
export const img = (url, size = 256) => url ? `${media}img?s=${size}&u=${encodeURIComponent(url)}` : "";

/** Switches the window to another page: navigate("live"). */
export const navigate = id => window.dispatchEvent(new CustomEvent("navigate", { detail: id }));

/** Shows a toast: kind is info, good, warning or bad. */
export function toast(text, kind = "info") {
  emit("toast", { text, kind });
}

window.__TAURI__.event.listen("lst", e => emit(e.payload.type, e.payload.data));

/**
 * Loads a launcher call's result and reloads it whenever the launcher says something changed
 * (or on the extra events given). Returns [data, reload].
 */
export function useApi(name, args, events = []) {
  const key = JSON.stringify(args ?? null);
  const cacheKey = `${name}|${key}`;
  const [data, setData] = useState(() => cache.get(cacheKey));
  const seq = useRef(0);
  const reload = useCallback(() => {
    const mine = ++seq.current;
    return call(name, args).then(result => {
      cache.set(cacheKey, result);
      if (mine === seq.current) setData(result);
      return result;
    }).catch(() => undefined);
  }, [name, key]);
  useEffect(() => {
    reload();
    const offs = ["changed", ...events].map(type => on(type, () => reload()));
    return () => offs.forEach(off => off());
  }, [reload]);
  return [data, reload];
}

/** Loads calls into the useApi cache in the background (the pages you are likely to open next). */
export function prefetch(names) {
  for (const name of names)
    call(name).then(result => cache.set(`${name}|null`, result), () => undefined);
}

/** Subscribes to an event for the component's lifetime. */
export function useEvent(type, fn) {
  const ref = useRef(fn);
  ref.current = fn;
  useEffect(() => on(type, data => ref.current(data)), [type]);
}
