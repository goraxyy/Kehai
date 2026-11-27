// With the read-only dataset key (Authorization: Bearer DATASET_KEY), for the Windows training PC
// (C1.6, C3.7). It can read what may train Karen and nothing else: no writes, deletes or publishing.
//
//   GET /dataset/manifest?round=&build=   the sessions that may train her: consent to training,
//                                         a zip, not deleted, not past Q5's limit; with hashes
//   GET /dataset/sessions/<id>.zip        one of those zips
//   GET /dataset/deletions?since=         sessions removed since then (expired or by request), so
//                                         a local copy can be removed before the next use
import { HttpError, SESSION_PATH, clock, iso, json, requireKey } from "./util.js";
import { zip } from "./admin.js";

const ELIGIBLE = "consent_training = 1 AND deleted IS NULL AND zip_key IS NOT NULL AND expires > ?";

export async function dataset(request, env, path) {
  requireKey(request, env.DATASET_KEY);
  if (request.method !== "GET") throw new HttpError(405, "read only");
  if (!env.DB) throw new HttpError(503, "no database bound");
  const url = new URL(request.url);
  const now = iso(clock(env));
  if (path === "manifest") {
    const where = [ELIGIBLE], args = [now];
    for (const field of ["round", "build"]) {
      const v = url.searchParams.get(field);
      if (v) { where.push(`${field} = ?`); args.push(v); }
    }
    const { results } = await env.DB.prepare(
      `SELECT id, round, build, tester, schema, consent_version, zip_sha256, zip_bytes, collected, expires,
              summary_received IS NOT NULL AS complete
       FROM sessions WHERE ${where.join(" AND ")} ORDER BY id`).bind(...args).all();
    return json(200, { generated: now, sessions: results.map(r => ({ ...r, complete: !!r.complete })) });
  }
  if (path.startsWith("sessions/") && path.endsWith(".zip")) {
    const id = path.slice("sessions/".length, -".zip".length);
    if (!SESSION_PATH.test(id)) throw new HttpError(400, "not a session");
    const ok = await env.DB.prepare(`SELECT 1 FROM sessions WHERE id = ? AND ${ELIGIBLE}`).bind(id, now).first();
    if (!ok) throw new HttpError(404, "not in the dataset");
    return zip(env, `sessions/${id}.zip`);
  }
  if (path === "deletions") {
    const since = url.searchParams.get("since") || "";
    const { results } = await env.DB.prepare(
      "SELECT id, deleted AS reason, deleted_at FROM sessions WHERE deleted IS NOT NULL AND deleted_at > ? ORDER BY deleted_at")
      .bind(since).all();
    return json(200, { generated: now, deletions: results });
  }
  throw new HttpError(404, "nothing here");
}
