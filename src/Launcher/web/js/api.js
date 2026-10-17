// Talks to the launcher (C#) over the local server: calls, live events and toasts.
import { useState, useEffect, useCallback, useRef } from "./vendor/preact-htm.js";

/** The launcher's random token from the page address: every call, event and picture needs it. */
export const token = new URLSearchParams(location.search).get("t") ?? "";
const listeners = new Map();
/** Last result of each useApi call ("name|args"), so a page shows at once when you switch back to it. */
const cache = new Map();

/** Calls a launcher method. Errors become a toast and reject with the message. */
export async function call(name, args) {
  let response;
  try {
    response = await fetch(`/api/${name}`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Token": token },
      body: args === undefined ? undefined : JSON.stringify(args),
    });
  } catch {
    toast("The launcher isn't responding.", "bad");
    throw new Error("offline");
  }
  const reply = await response.json().catch(() => ({ ok: false, error: `HTTP ${response.status}` }));
  if (!reply.ok) {
    toast(reply.error, "bad");
    throw new Error(reply.error);
  }
  return reply.result;
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
export const img = (url, size = 256) => url ? `/img?s=${size}&t=${token}&u=${encodeURIComponent(url)}` : "";

/** Switches the window to another page: navigate("live"). */
export const navigate = id => window.dispatchEvent(new CustomEvent("navigate", { detail: id }));

/** Shows a toast: kind is info, good, warning or bad. */
export function toast(text, kind = "info") {
  emit("toast", { text, kind });
}

let source;
function connect() {
  source = new EventSource(`/events?t=${token}`);
  source.onmessage = e => {
    const { type, data } = JSON.parse(e.data);
    emit(type, data);
  };
  source.onopen = () => emit("changed");
  source.onerror = () => {
    source.close();
    setTimeout(connect, 1500);
  };
}
connect();

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
