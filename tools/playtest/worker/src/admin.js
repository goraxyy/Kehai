// With the admin key (Authorization: Bearer ADMIN_KEY), which only the owner's machine has:
//
//   GET    /admin/sessions                      the session zips (R2), with each one's size
//   GET    /admin/sessions/<round>/<tester>/<launch>.zip   download one
//   GET    /admin/rows/<table>?session=&after=&limit=      read a D1 table, a page at a time
//   POST   /admin/summaries { session, text }   an optional analysis run's summary
//   POST   /admin/latest { round, version }     the build testers download (builds.js)
//   DELETE /admin/tester/<id>                   a deletion request: every file and row of theirs
//
// Files are never deleted after a pull (v1 did): they stay until Q5's limit (cron.js).
import { HttpError, SESSION_PATH, TESTER, clip, clock, iso, json, readJson, requireKey } from "./util.js";
import { setLatest } from "./builds.js";
import { removeSession } from "./remove.js";

export const TABLES = ["testers", "sessions", "shifts", "answers", "moments", "events", "summaries", "signups", "downloads", "deletions"];

export async function admin(request, env, path) {
  requireKey(request, env.ADMIN_KEY);
  const m = request.method;
  if (path === "sessions" && m === "GET") return list(env, new URL(request.url));
  if (path.startsWith("sessions/")) {
    const id = path.slice("sessions/".length).replace(/\.zip$/, "");
    if (!SESSION_PATH.test(id) || !path.endsWith(".zip")) throw new HttpError(400, "not a session");
    if (m === "GET") return zip(env, `sessions/${id}.zip`);
    if (m === "DELETE") throw new HttpError(405, "sessions stay until the retention limit; for a deletion request, DELETE /admin/tester/<id>");
  }
  if (path.startsWith("rows/") && m === "GET") return rows(env, path.slice(5), new URL(request.url));
  if (path === "summaries" && m === "POST") return writeSummary(env, (await readJson(request)).data);
  if (path === "latest" && m === "POST") return setLatest(env, (await readJson(request)).data);
  if (path.startsWith("tester/") && m === "DELETE") return forget(env, path.slice(7));
  throw new HttpError(404, "nothing here");
}

async function list(env, url) {
  const sessions = [];
  let cursor = url.searchParams.get("cursor") || undefined;
  do {
    const page = await env.SESSIONS.list({ prefix: "sessions/", cursor, include: ["customMetadata"] });
    for (const o of page.objects) sessions.push({ key: o.key.slice("sessions/".length), bytes: o.size, uploaded: o.uploaded });
    cursor = page.truncated ? page.cursor : undefined;
  } while (cursor && sessions.length < 5000);
  return json(200, { sessions });
}

export async function zip(env, key) {
  const o = await env.SESSIONS.get(key);
  if (!o) throw new HttpError(404, "gone");
  return new Response(o.body, { status: 200, headers: { "Content-Type": "application/zip", "Content-Length": String(o.size) } });
}

async function rows(env, table, url) {
  if (!env.DB) throw new HttpError(503, "no database bound");
  if (!TABLES.includes(table)) throw new HttpError(404, "no such table");
  const limit = Math.min(1000, Math.max(1, Number.parseInt(url.searchParams.get("limit") || "500", 10) || 500));
  const after = Number.parseInt(url.searchParams.get("after") || "0", 10) || 0;
  const session = url.searchParams.get("session");
  const hasSession = ["shifts", "answers", "moments", "events", "summaries"].includes(table);
  const where = [`rowid > ?`], args = [after];
  if (session && hasSession) { where.push("session = ?"); args.push(session); }
  if (session && table === "sessions") { where.push("id = ?"); args.push(session); }
  const { results } = await env.DB.prepare(
    `SELECT rowid AS _row, * FROM ${table} WHERE ${where.join(" AND ")} ORDER BY rowid LIMIT ?`).bind(...args, limit).all();
  const next = results.length === limit ? results[results.length - 1]._row : null;
  return json(200, { table, rows: results, next });
}

async function writeSummary(env, data) {
  if (!env.DB) throw new HttpError(503, "no database bound");
  const session = typeof data?.session === "string" && SESSION_PATH.test(data.session) ? data.session : null;
  const body = clip(data?.text, 20000);
  if (!session || !body) throw new HttpError(400, "session and text, please");
  const known = await env.DB.prepare("SELECT deleted FROM sessions WHERE id = ?").bind(session).first();
  if (!known) throw new HttpError(404, "no such session");
  if (known.deleted) throw new HttpError(410, "that session was deleted");
  await env.DB.prepare("INSERT OR REPLACE INTO summaries (session, text, written) VALUES (?, ?, ?)").bind(session, body, iso(clock(env))).run();
  return json(200, { session });
}

// A deletion request (Q7): every session file and row of this tester, now. Later sends from the
// same ID are refused, so a game that was offline can't put them back.
async function forget(env, tester) {
  if (!TESTER.test(tester)) throw new HttpError(400, "not a tester ID");
  const now = clock(env);
  const ids = new Set();
  let cursor;
  do {
    const page = await env.SESSIONS.list({ prefix: "sessions/", cursor });
    for (const o of page.objects) {
      const id = o.key.slice("sessions/".length).replace(/\.zip$/, "");
      if (id.split("/")[1] === tester) ids.add(id);
    }
    cursor = page.truncated ? page.cursor : undefined;
  } while (cursor);
  if (env.DB) {
    const { results } = await env.DB.prepare("SELECT id FROM sessions WHERE tester = ? AND deleted IS NULL").bind(tester).all();
    for (const r of results) ids.add(r.id);
  }
  for (const id of ids) await removeSession(env, id, "request", now);
  if (env.DB)
    await env.DB.batch([
      env.DB.prepare("DELETE FROM events WHERE tester = ?").bind(tester),
      env.DB.prepare("DELETE FROM testers WHERE id = ?").bind(tester),
      env.DB.prepare("INSERT OR IGNORE INTO deletions (tester, at) VALUES (?, ?)").bind(tester, iso(now)),
    ]);
  return json(200, { tester, sessions: ids.size });
}
