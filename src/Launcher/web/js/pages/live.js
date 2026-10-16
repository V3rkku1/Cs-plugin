// Live Server page (being rebuilt).
import { html } from "../vendor/preact-htm.js";
import { Page } from "../ui.js";

export function LivePage() {
  return html`<${Page} title="Live Server" subtitle="Coming in the new design." />`;
}
