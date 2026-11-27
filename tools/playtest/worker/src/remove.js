// Removing a session, for Q5's limit (cron.js) or a deletion request (admin.js): its zip, its
// rows and its summary go. One row stays as a tombstone (its id, why and when, nothing about the
// tester's play), so the Windows copies can be reconciled (GET /dataset/deletions) and a late
// retry from the game is refused instead of stored again.
import { iso } from "./util.js";

export async function removeSession(env, id, reason, now) {
  await env.SESSIONS.delete(`sessions/${id}.zip`);
  if (!env.DB) return;
  await env.DB.batch([
    env.DB.prepare("DELETE FROM shifts WHERE session = ?").bind(id),
    env.DB.prepare("DELETE FROM moments WHERE session = ?").bind(id),
    env.DB.prepare("DELETE FROM answers WHERE session = ?").bind(id),
    env.DB.prepare("DELETE FROM summaries WHERE session = ?").bind(id),
    env.DB.prepare("DELETE FROM events WHERE session = ?").bind(id),
    env.DB.prepare(
      `UPDATE sessions SET deleted = ?, deleted_at = ?, zip_key = NULL, zip_bytes = NULL, zip_sha256 = NULL,
         computer = NULL, fps = NULL, minutes = NULL, ended = NULL WHERE id = ?`).bind(reason, iso(now), id),
  ]);
}
