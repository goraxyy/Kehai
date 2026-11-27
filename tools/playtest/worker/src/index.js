// The playtest upload service (playtest v2, Phase 1): a Cloudflare Worker in front of a private R2
// bucket (session zips, builds) and a D1 database (rows). Builds send sessions here; nothing on
// arrival starts analysis or training, and no owner computer needs to be on.
//
//   With the upload key (X-Kehai-Key, inside every playtest build: it can only add):
//     PUT  /sessions/<round>/<tester>/<launch>.zip   a session zip (sessions.js)
//     POST /summaries                                its rows (summaries.js)
//     POST /events                                   the funnel (events.js)
//   Open:
//     GET  /latest, GET /download/<mac|windows>      the current build (builds.js)
//     POST /signup                                   the website's email box (signup.js)
//     GET  /                                         is it up
//   With the admin key: /admin/... (admin.js). With the read-only dataset key: /dataset/... (dataset.js).
//   Once a day: Q5's six-month limit (cron.js).
//
// Bindings (wrangler.toml): SESSIONS (R2), DB (D1); secrets UPLOAD_KEY, ADMIN_KEY, DATASET_KEY.
import { HttpError, OPEN, SESSION_PATH, text } from "./util.js";
import { upload } from "./sessions.js";
import { summary } from "./summaries.js";
import { events } from "./events.js";
import { download, latest } from "./builds.js";
import { signup } from "./signup.js";
import { admin } from "./admin.js";
import { dataset } from "./dataset.js";
import { retention } from "./cron.js";

export { MAX_BYTES, same } from "./util.js";

export default {
  async fetch(request, env) {
    try {
      return await route(request, env);
    } catch (e) {
      if (e instanceof HttpError) return text(e.status, e.message + "\n", e.status === 429 || e.status === 400 ? OPEN : {});
      return text(500, "error: " + (e && e.message ? e.message : e) + "\n");
    }
  },

  async scheduled(event, env, ctx) {
    ctx.waitUntil(retention(env, new Date(event.scheduledTime ?? Date.now())));
  },
};

async function route(request, env) {
  const url = new URL(request.url);
  const path = url.pathname;
  const m = request.method;
  if (m === "OPTIONS") return new Response(null, { status: 204, headers: OPEN });
  if (path === "/" && m === "GET") return text(200, "Kehai playtest uploads\n");

  if (path.startsWith("/sessions/")) {
    const match = path.slice("/sessions/".length).replace(/\.zip$/, "").match(SESSION_PATH);
    if (!match || !path.endsWith(".zip")) return text(404, "nothing here\n");
    if (m !== "PUT") return text(405, "PUT a session here\n");
    return upload(request, env, match.slice(1));
  }
  if (path === "/summaries" && m === "POST") return summary(request, env);
  if (path === "/events" && m === "POST") return events(request, env);
  if (path === "/latest" && m === "GET") return latest(env);
  const dl = path.match(/^\/download\/(mac|windows)$/);
  if (dl && m === "GET") return download(env, dl[1]);
  if (path === "/signup" && m === "POST") return signup(request, env);
  if (path.startsWith("/admin/")) return admin(request, env, decodeURIComponent(path.slice("/admin/".length)));
  if (path.startsWith("/dataset/")) return dataset(request, env, decodeURIComponent(path.slice("/dataset/".length)));
  return text(404, "nothing here\n");
}
