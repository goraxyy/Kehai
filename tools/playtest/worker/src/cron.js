// Q5, once a day in the cloud (wrangler.toml's cron trigger): sessions collected more than six
// months ago are removed (zip, rows, summary; a tombstone stays), and so are funnel events that
// old. Downloading or importing a session never moves its date. No computer of the owner's needs
// to be on.
import { RETENTION_DAYS, addDays, iso } from "./util.js";
import { removeSession } from "./remove.js";

export async function retention(env, now) {
  if (!env.DB) return { sessions: 0, events: 0 };
  const { results } = await env.DB.prepare("SELECT id FROM sessions WHERE deleted IS NULL AND expires <= ?").bind(iso(now)).all();
  for (const r of results) await removeSession(env, r.id, "expired", now);
  const cutoff = iso(addDays(now, -RETENTION_DAYS));
  const gone = await env.DB.prepare("DELETE FROM events WHERE received <= ?").bind(cutoff).run();
  await env.DB.prepare("DELETE FROM rate WHERE hour < ?").bind(iso(addDays(now, -1)).slice(0, 13)).run();
  return { sessions: results.length, events: gone.meta?.changes ?? 0 };
}
