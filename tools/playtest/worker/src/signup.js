// POST /signup, open: an email from the website, as a form or as JSON. A hidden field
// (`website`) that people never see catches most bots: filled in, the answer is a quiet "thanks"
// and nothing is kept. Five sign-ups an hour from one address; the address is kept only as a
// salted hash, in the rate table, for that hour.
import { HttpError, OPEN, clock, iso, json, sha256Hex } from "./util.js";

const EMAIL = /^[^\s@]{1,64}@[^\s@]{1,190}\.[^\s@]{2,63}$/;
const PER_HOUR = 5;

export async function signup(request, env) {
  if (!env.DB) throw new HttpError(503, "no database bound");
  const type = request.headers.get("Content-Type") || "";
  let fields;
  if (type.includes("application/json")) fields = await request.json().catch(() => null);
  else fields = Object.fromEntries((await request.formData().catch(() => new FormData())).entries());
  if (!fields) throw new HttpError(400, "nothing sent");
  if (typeof fields.website === "string" && fields.website.trim()) return json(200, { ok: true }, OPEN);   // a bot

  const email = typeof fields.email === "string" ? fields.email.trim().toLowerCase() : "";
  if (email.length > 254 || !EMAIL.test(email)) throw new HttpError(400, "that doesn't look like an email address");

  const now = clock(env);
  const hour = iso(now).slice(0, 13);
  const address = request.headers.get("CF-Connecting-IP") || "unknown";
  const key = "signup:" + (await sha256Hex(new TextEncoder().encode((env.ADMIN_KEY || "") + address))).slice(0, 32);
  const seen = await env.DB.prepare("SELECT hour, count FROM rate WHERE key = ?").bind(key).first();
  const count = seen && seen.hour === hour ? seen.count : 0;
  if (count >= PER_HOUR) throw new HttpError(429, "too many sign-ups from here; try again later");
  await env.DB.batch([
    env.DB.prepare("INSERT INTO rate (key, hour, count) VALUES (?1, ?2, 1) ON CONFLICT(key) DO UPDATE SET hour = ?2, count = ?3")
      .bind(key, hour, count + 1),
    env.DB.prepare("INSERT OR IGNORE INTO signups (email, at) VALUES (?, ?)").bind(email, iso(now)),
  ]);
  return json(200, { ok: true }, OPEN);
}
