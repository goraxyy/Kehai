// POST /events, with the upload key: the funnel (C2.5). The game sends these only after consent,
// and makes each event's id, so a retried batch counts once.
//
//   { events: [{ id, tester, session, round, build, kind, at }] }   at most 100
import { HttpError, ROUND, TESTER, clip, clock, iso, json, readJson, requireKey } from "./util.js";

export const KINDS = new Set(["launch", "consent", "walkthrough", "first_shift", "shift", "end", "sent"]);
const ID = /^[A-Za-z0-9-]{8,64}$/;

export async function events(request, env) {
  requireKey(request, env.UPLOAD_KEY, "X-Kehai-Key");
  if (!env.DB) throw new HttpError(503, "no database bound");
  const { data } = await readJson(request, 64 * 1024);
  const list = Array.isArray(data?.events) ? data.events : null;
  if (!list) throw new HttpError(400, "no events");
  if (list.length > 100) throw new HttpError(413, "too many events");
  const now = iso(clock(env));

  const deleted = new Set();
  for (const tester of new Set(list.map(e => e?.tester)))
    if (typeof tester === "string" && await env.DB.prepare("SELECT 1 FROM deletions WHERE tester = ?").bind(tester).first()) deleted.add(tester);

  const statements = [];
  let refused = 0;
  for (const e of list) {
    const ok = e && ID.test(e.id) && TESTER.test(e.tester) && ROUND.test(e.round) && KINDS.has(e.kind) &&
      typeof e.at === "string" && !Number.isNaN(Date.parse(e.at)) && !deleted.has(e.tester);
    if (!ok) { refused++; continue; }
    statements.push(env.DB.prepare(
      "INSERT OR IGNORE INTO events (id, tester, session, round, build, kind, at, received) VALUES (?, ?, ?, ?, ?, ?, ?, ?)")
      .bind(e.id, e.tester, clip(e.session, 120), e.round, clip(e.build, 64) ?? "", e.kind, new Date(e.at).toISOString(), now));
  }
  if (statements.length) await env.DB.batch(statements);
  return json(200, { accepted: statements.length, refused });
}
