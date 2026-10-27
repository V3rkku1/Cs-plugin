// Stats: are you improving? Everything the Trainer and your matches record (training.jsonl in the plugin
// folder), this week against last week, a chart per skill and a table per day.
// No chart library: each chart is one small inline SVG line.
import { html, useState } from "../vendor/preact-htm.js";
import { tryCall, useApi } from "../api.js";
import { Page, Section, Select, Segmented, Button, Icon, confirm } from "../ui.js";

const sum = (days, key) => days.reduce((a, d) => a + (d[key] ?? 0), 0);
/** The average of a per-day value, weighted by that day's count. */
const weighted = (days, key, count) => {
  const n = sum(days, count);
  return n ? days.reduce((a, d) => a + (d[key] ?? 0) * d[count], 0) / n : null;
};

// lower: smaller is better. count: the day field that says how much the value is based on.
const METRICS = [
  { key: "reflex", title: "Reaction Drill", unit: "ms", color: "green", lower: true, count: "reflexN", from: "Trainer › Reaction Drill",
    period: d => weighted(d, "reflex", "reflexN") },
  { key: "test", title: "Reaction Test", unit: "ms", color: "teal", lower: true, count: "testN", from: "Trainer › Reaction Test",
    period: d => weighted(d, "test", "testN") },
  { key: "hsPct", title: "Headshots", unit: "%", color: "orange", count: "kills", from: "kills in any mode (not with an aimbot)",
    period: d => sum(d, "kills") ? sum(d, "hs") * 100 / sum(d, "kills") : null },
  { key: "kd", title: "K/D", unit: "", color: "blue", count: "kills", digits: 2, from: "kills and deaths in any mode",
    period: d => sum(d, "kills") + sum(d, "deaths") ? sum(d, "kills") / Math.max(1, sum(d, "deaths")) : null },
  { key: "accPct", title: "Accuracy", unit: "%", color: "yellow", count: "fired", from: "shots that hit an enemy, any mode",
    period: d => sum(d, "fired") ? sum(d, "hits") * 100 / sum(d, "fired") : null },
  { key: "adr", title: "Damage / Round", unit: "", color: "pink", count: "rounds", from: "damage to enemies per round, any mode",
    period: d => sum(d, "rounds") ? sum(d, "damage") / sum(d, "rounds") : null },
  { key: "stopPct", title: "Stopped Shots", unit: "%", color: "red", count: "csShots", from: "Trainer › Counter-Strafe Check",
    period: d => weighted(d, "stopPct", "csShots") },
  { key: "sync", title: "Strafe Sync", unit: "%", color: "purple", count: "jumps", from: "Trainer › Jump Stats",
    period: d => weighted(d, "sync", "jumps") },
  { key: "jumpBest", title: "Longest Jump", unit: "u", color: "indigo", count: "jumps", from: "Trainer › Jump Stats",
    period: d => d.some(x => x.jumpBest != null) ? Math.max(...d.map(x => x.jumpBest ?? 0)) : null },
  { key: "drillPct", title: "Drill Rounds Won", unit: "%", color: "green", count: "drillRounds", from: "Trainer › Site Drills",
    period: d => weighted(d, "drillPct", "drillRounds") },
];

const fmt = (m, v) => v == null ? "–" : `${v.toFixed(m.digits ?? 0)}${m.unit && m.unit !== "%" ? " " : ""}${m.unit}`;
const dayOffset = (day, today) => Math.round((Date.parse(today) - Date.parse(day)) / 864e5);
const localToday = () => new Date(Date.now() - new Date().getTimezoneOffset() * 6e4).toISOString().slice(0, 10);

/** This week's value and the change from last week, with an arrow and words (never colour alone). */
function Tile({ m, days }) {
  const today = localToday();
  const week = days.filter(d => dayOffset(d.day, today) < 7);
  const before = days.filter(d => { const o = dayOffset(d.day, today); return o >= 7 && o < 14; });
  const now = m.period(week);
  const then = m.period(before);
  let trend = html`<span class="faint">no data last week</span>`;
  if (now != null && then != null) {
    const change = then === 0 ? (now === 0 ? 0 : 100 * Math.sign(now)) : (now - then) / Math.abs(then) * 100;
    const better = m.lower ? change < 0 : change > 0;
    trend = Math.abs(change) < 1 ? html`<span class="faint">same as last week</span>`
      : html`<span style=${`color: var(--${better ? "green" : "red"})`}>
          <${Icon} name=${better ? "check" : "warning"} size=${12} stroke=${2.5} /> ${Math.abs(change).toFixed(0)}% ${better ? "better" : "worse"}
        </span>`;
  }
  return html`<div class="card glass pad" style="min-width:0">
    <div class="subtitle">${m.title}</div>
    <div style="font-size:28px;font-weight:700;margin:4px 0">${fmt(m, now)}</div>
    <div style="font-size:12px">${trend}</div>
  </div>`;
}

/** One series over the days that have it: 2px line, 8px markers, a tooltip per point. */
function Chart({ m, days }) {
  const points = days.filter(d => d[m.key] != null && (d[m.count] ?? 0) > 0);
  if (points.length === 0) return html`<div class="subtitle" style="padding:28px 0;text-align:center">No data yet · ${m.from}</div>`;
  const W = 420, H = 140, L = 44, R = 10, T = 10, B = 22;
  const values = points.map(d => d[m.key]);
  let lo = Math.min(...values), hi = Math.max(...values);
  if (lo === hi) { lo -= 1; hi += 1; }
  const x = i => points.length === 1 ? (L + W - R) / 2 : L + i * (W - L - R) / (points.length - 1);
  const y = v => T + (hi - v) * (H - T - B) / (hi - lo);
  const line = points.map((d, i) => `${i ? "L" : "M"}${x(i).toFixed(1)},${y(d[m.key]).toFixed(1)}`).join("");
  const c = `var(--${m.color})`;
  return html`<svg viewBox=${`0 0 ${W} ${H}`} style="width:100%;height:auto;display:block" role="img"
      aria-label=${`${m.title} per day, ${fmt(m, values[0])} on ${points[0].day} to ${fmt(m, values.at(-1))} on ${points.at(-1).day}`}>
    <g fill="currentColor" opacity=".55" font-size="11">
      <line x1=${L} x2=${W - R} y1=${y(hi)} y2=${y(hi)} stroke="currentColor" opacity=".25" />
      <line x1=${L} x2=${W - R} y1=${y(lo)} y2=${y(lo)} stroke="currentColor" opacity=".25" />
      <text x=${L - 6} y=${y(hi) + 4} text-anchor="end">${fmt(m, hi)}</text>
      <text x=${L - 6} y=${y(lo) + 4} text-anchor="end">${fmt(m, lo)}</text>
      <text x=${L} y=${H - 4}>${points[0].day.slice(5)}</text>
      ${points.length > 1 && html`<text x=${W - R} y=${H - 4} text-anchor="end">${points.at(-1).day.slice(5)}</text>`}
    </g>
    <path d=${line} fill="none" stroke=${c} stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />
    ${points.map((d, i) => html`<g>
      <circle cx=${x(i)} cy=${y(d[m.key])} r="4" fill=${c} stroke="var(--bg, #000)" stroke-width="2" />
      <circle cx=${x(i)} cy=${y(d[m.key])} r="12" fill="transparent">
        <title>${d.day}: ${fmt(m, d[m.key])} (${d[m.count]} ${m.count === "kills" ? "kills" : "samples"})</title>
      </circle>
    </g>`)}
  </svg>`;
}

// Best single day for each skill. min: the day needs that many samples to count (no 1-kill 100% HS days).
const BESTS = [
  { title: "Fastest Reaction", m: METRICS[0], pick: d => d.reflexBest, count: "reflexN", min: 1, lower: true },
  { title: "Best Headshot Day", m: METRICS[2], pick: d => d.hsPct, count: "kills", min: 10 },
  { title: "Best Accuracy Day", m: METRICS[4], pick: d => d.accPct, count: "fired", min: 50 },
  { title: "Best Damage Day", m: METRICS[5], pick: d => d.adr, count: "rounds", min: 5 },
  { title: "Longest Jump", m: METRICS[8], pick: d => d.jumpBest, count: "jumps", min: 1 },
];

function Bests({ days }) {
  const items = BESTS.map(b => {
    const ok = days.filter(d => (d[b.count] ?? 0) >= b.min && b.pick(d) != null);
    if (!ok.length) return null;
    const best = ok.reduce((a, d) => (b.lower ? b.pick(d) < b.pick(a) : b.pick(d) > b.pick(a)) ? d : a);
    return { ...b, value: b.pick(best), day: best.day };
  }).filter(Boolean);
  if (!items.length) return null;
  return html`<${Section} title="Personal Bests">
    <div class="cols" style=${`--n:${items.length};padding:14px;gap:14px`}>
      ${items.map(i => html`<div>
        <div class="subtitle"><${Icon} name="star" size=${12} /> ${i.title}</div>
        <div style="font-size:22px;font-weight:700">${fmt(i.m, i.value)}</div>
        <div class="subtitle faint">${i.day}</div>
      </div>`)}
    </div>
  <//>`;
}

const RANGES = [[14, "2 Weeks"], [30, "Month"], [90, "3 Months"], [100000, "All"]];

export function StatsPage() {
  const [player, setPlayer] = useState("");
  const [view, setView] = useState(0);
  const [range, setRange] = useState(1);
  const [stats] = useApi("trainer.stats", player, ["play.tick"]);
  if (!stats) return html`<${Page} title="Stats" />`;
  const today = localToday();
  const days = stats.days.filter(d => dayOffset(d.day, today) < RANGES[range][0]);
  const shown = METRICS.filter(m => days.some(d => (d[m.count] ?? 0) > 0));
  const reset = async () => {
    if (await confirm({ title: "Reset all stats?", message: "Every player's history starts again from zero. The old file is kept as training.jsonl.bak in the plugin folder.", ok: "Reset", danger: true }))
      tryCall("trainer.statsReset");
  };

  return html`<${Page} title="Stats" subtitle="Your reaction time, headshots and movement over time: are you improving?"
      actions=${html`${stats.players.length > 1 && html`<${Select} options=${stats.players.map(p => ({ value: p, label: p }))}
        value=${stats.player} onChange=${setPlayer} width=${180} />`}
        ${stats.days.length > 0 && html`<${Button} size="sm" kind="plain" icon="trash" onClick=${reset}>Reset<//>`}`}>
    ${stats.days.length === 0 ? html`<${Section}>
        <div class="pad subtitle" style="padding:28px">Nothing recorded yet. Play a match, or use the Trainer: Reaction Drill,
          Reaction Test, Jump Stats, Counter-Strafe Check and Site Drills all count. Stats show up here by day.</div>
      <//>` : html`
      <div class="section-title">This week <span class="note">compared with last week</span></div>
      <div class="cols" style="--n:5;margin-bottom:22px">${METRICS.map(m => html`<${Tile} m=${m} days=${stats.days} />`)}</div>
      <div style="margin-bottom:22px"><${Bests} days=${stats.days} /></div>
      <div style="margin-bottom:12px;display:flex;gap:10px;flex-wrap:wrap">
        <${Segmented} options=${["Charts", "Table"]} value=${view} onChange=${setView} />
        <${Segmented} options=${RANGES.map(r => r[1])} value=${range} onChange=${setRange} />
      </div>
      ${days.length === 0 && html`<div class="subtitle" style="padding:12px 0">Nothing in this time range.</div>`}
      ${view === 0
        ? html`<div class="cols wrap" style="--n:2">${shown.map(m => html`<${Section} title=${m.title} note=${m.lower ? "lower is better" : "higher is better"}>
            <div style="padding:12px 14px"><${Chart} m=${m} days=${days} /></div>
          <//>`)}</div>`
        : html`<${Section} title="Per Day">
            <div style="overflow:auto"><table style="width:100%;border-collapse:collapse;font-size:13px;font-variant-numeric:tabular-nums">
              <thead><tr>${["Day", "Kills", ...METRICS.map(m => m.title)].map(h => html`<th style="text-align:right;padding:8px 10px;font-weight:600;white-space:nowrap">${h}</th>`)}</tr></thead>
              <tbody>${[...days].reverse().map(d => html`<tr style="border-top:1px solid var(--hairline)">
                <td style="text-align:right;padding:6px 10px">${d.day}</td>
                <td style="text-align:right;padding:6px 10px">${d.kills}</td>
                ${METRICS.map(m => html`<td style="text-align:right;padding:6px 10px">${(d[m.count] ?? 0) > 0 ? fmt(m, d[m.key]) : "–"}</td>`)}
              </tr>`)}</tbody>
            </table></div>
          <//>`}`}
  <//>`;
}
