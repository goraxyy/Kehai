// Shared by every route: responses, keys, limits, the clock and the session path.

export const MAX_BYTES = 50 * 1024 * 1024;     // a session zip
export const MAX_JSON = 256 * 1024;            // a summary or a batch of events
export const RETENTION_DAYS = 183;             // Q5: six months from collection

// <round>/<tester>/<launch>: a v1 code (T07) or the automatic 32-hex ID; the launch's time, and in
// v2 a per-launch suffix so two sessions in the same second stay apart.
export const SESSION_PATH = /^([A-Za-z0-9-]{1,40})\/([A-Z0-9-]{2,16}|[A-F0-9]{32})\/(\d{8}_\d{6}(?:-[a-f0-9]{32})?)$/;
export const TESTER = /^(?:[A-Z0-9-]{2,16}|[A-F0-9]{32})$/;
export const ROUND = /^[A-Za-z0-9-]{1,40}$/;

export class HttpError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}

export const clock = env => (typeof env.NOW === "function" ? env.NOW() : new Date());
export const iso = date => date.toISOString();
export const addDays = (date, days) => new Date(date.getTime() + days * 86400_000);

export function text(status, body, headers = {}) {
  return new Response(body, { status, headers: { "Content-Type": "text/plain; charset=utf-8", ...headers } });
}

export function json(status, data, headers = {}) {
  return new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json", ...headers } });
}

export function bearer(request) {
  const h = request.headers.get("Authorization") || "";
  return h.startsWith("Bearer ") ? h.slice(7) : "";
}

// Constant-time comparison, so the keys can't be guessed from timing. A missing secret matches nothing.
export function same(given, secret) {
  if (typeof given !== "string" || typeof secret !== "string" || secret.length < 16) return false;
  const a = new TextEncoder().encode(given), b = new TextEncoder().encode(secret);
  let diff = a.length ^ b.length;
  for (let i = 0; i < b.length; i++) diff |= (a[i % (a.length || 1)] ?? 0) ^ b[i];
  return diff === 0;
}

export function requireKey(request, secret, header) {
  const given = header ? request.headers.get(header) : bearer(request);
  if (!same(given, secret)) throw new HttpError(401, "no");
}

export async function sha256Hex(bytes) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}

// The body as JSON, refused past `max` bytes, before or after reading it.
export async function readJson(request, max = MAX_JSON) {
  if (Number(request.headers.get("Content-Length") || "0") > max) throw new HttpError(413, "too big");
  const raw = new Uint8Array(await request.arrayBuffer());
  if (raw.byteLength > max) throw new HttpError(413, "too big");
  try {
    return { data: JSON.parse(new TextDecoder().decode(raw)), raw };
  } catch {
    throw new HttpError(400, "not JSON");
  }
}

// Short, trimmed text, or null; never longer than `max` characters.
export function clip(value, max) {
  if (typeof value !== "string") return null;
  const t = value.trim();
  return t ? t.slice(0, max) : null;
}

export const number = v => (typeof v === "number" && Number.isFinite(v) ? v : null);
export const flag = v => (v === true || v === 1 ? 1 : v === false || v === 0 ? 0 : null);

// Has this tester asked to be deleted? Their later sends are refused (410), so the game stops retrying.
export async function deletedTester(env, tester) {
  if (!env.DB) return false;
  return !!(await env.DB.prepare("SELECT 1 FROM deletions WHERE tester = ?").bind(tester).first());
}

// The website (Pages) calls the open routes from another origin.
export const OPEN = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Methods": "GET, POST, OPTIONS", "Access-Control-Allow-Headers": "Content-Type" };
