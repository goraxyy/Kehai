// The playtest upload service (PLAYTEST.md, step 2): a Cloudflare Worker in front of a private
// R2 bucket. Playtest builds send sessions here; the owner's Mac lists, downloads and deletes them.
//
//   PUT    /sessions/<round>/<code>/<launch>.zip   X-Kehai-Key: UPLOAD_KEY     a build sends a session
//   GET    /admin/sessions                         Authorization: Bearer ADMIN_KEY   what's waiting
//   GET    /admin/sessions/<round>/<code>/<launch>.zip                      download one
//   DELETE /admin/sessions/<round>/<code>/<launch>.zip                      remove one (after the Mac has it)
//   GET    /                                       is it up
//
// The upload key is inside every playtest build, so it can only add sessions; reading and
// deleting take the admin key, which only the Mac has. Bindings (wrangler.toml): SESSIONS, the
// R2 bucket; secrets UPLOAD_KEY and ADMIN_KEY (wrangler secret put).

export const MAX_BYTES = 50 * 1024 * 1024;
const SESSION = /^\/sessions\/([A-Za-z0-9-]{1,40})\/([A-Z0-9-]{2,16})\/(\d{8}_\d{6})\.zip$/;
const ADMIN = /^\/admin\/sessions(?:\/(.+))?$/;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    try {
      if (url.pathname === "/" && request.method === "GET") return text(200, "Kehai playtest uploads\n");

      const session = url.pathname.match(SESSION);
      if (session) {
        if (request.method !== "PUT") return text(405, "PUT a session here\n");
        return await upload(request, env, session);
      }

      const admin = url.pathname.match(ADMIN);
      if (admin) {
        if (!same(bearer(request), env.ADMIN_KEY)) return text(401, "no\n");
        const key = admin[1] ? "sessions/" + decodeURIComponent(admin[1]) : null;
        if (key && !SESSION.test("/" + key)) return text(400, "not a session\n");
        if (!key && request.method === "GET") return await list(env, url);
        if (key && request.method === "GET") return await download(env, key);
        if (key && request.method === "DELETE") {
          await env.SESSIONS.delete(key);
          return new Response(null, { status: 204 });
        }
        return text(405, "no\n");
      }
      return text(404, "nothing here\n");
    } catch (e) {
      return text(500, "error: " + (e && e.message ? e.message : e) + "\n");
    }
  },
};

async function upload(request, env, match) {
  if (!same(request.headers.get("X-Kehai-Key"), env.UPLOAD_KEY)) return text(401, "no\n");
  const length = Number(request.headers.get("Content-Length") || "0");
  if (length > MAX_BYTES) return text(413, "too big\n");
  const body = await request.arrayBuffer();
  if (body.byteLength === 0 || body.byteLength > MAX_BYTES) return text(body.byteLength ? 413 : 400, "empty or too big\n");
  const head = new Uint8Array(body, 0, Math.min(4, body.byteLength));
  if (head[0] !== 0x50 || head[1] !== 0x4b || head[2] !== 0x03 || head[3] !== 0x04) return text(400, "not a zip\n");
  const [, round, code, launch] = match;
  const key = `sessions/${round}/${code}/${launch}.zip`;
  await env.SESSIONS.put(key, body, {
    httpMetadata: { contentType: "application/zip" },
    customMetadata: { received: new Date().toISOString(), round, code, launch },
  });
  return json(201, { key, bytes: body.byteLength });
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

async function download(env, key) {
  const o = await env.SESSIONS.get(key);
  if (!o) return text(404, "gone\n");
  return new Response(o.body, { status: 200, headers: { "Content-Type": "application/zip", "Content-Length": String(o.size) } });
}

function bearer(request) {
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

function text(status, body) {
  return new Response(body, { status, headers: { "Content-Type": "text/plain; charset=utf-8" } });
}

function json(status, data) {
  return new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });
}
