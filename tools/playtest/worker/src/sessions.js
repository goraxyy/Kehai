// PUT /sessions/<round>/<tester>/<launch>.zip, with the upload key: a build sends a session zip.
// The zip and its summary (summaries.js) may arrive in either order; each fills its half of one
// session row. A retry of the same bytes is accepted again (the first answer may have been lost);
// different bytes under the same id are refused (C1.5).
//
// Consent travels as headers: X-Kehai-Consent (its version) and X-Kehai-Training (1 when the tester
// agreed to training Karen). A build that sends neither is v1, and its session never trains her.
import { HttpError, MAX_BYTES, RETENTION_DAYS, addDays, clock, deletedTester, iso, json, requireKey, sha256Hex } from "./util.js";

export function consentOf(request) {
  const version = (request.headers.get("X-Kehai-Consent") || "").trim();
  if (!version) return { version: "v1", training: 0 };
  if (!/^[A-Za-z0-9.-]{1,16}$/.test(version)) throw new HttpError(400, "bad consent version");
  return { version, training: request.headers.get("X-Kehai-Training") === "1" ? 1 : 0 };
}

// The session row's insert-or-update, for either half. Consent only ever narrows: if the zip and
// the summary disagree, the session is kept from training.
export function upsertSession(env, s) {
  return env.DB.prepare(
    `INSERT INTO sessions (id, tester, round, launch, build, schema, consent_version, consent_training, collected, expires,
        zip_key, zip_bytes, zip_sha256, zip_received, summary_received, summary_sha256, computer, fps, minutes, ended)
     VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20)
     ON CONFLICT(id) DO UPDATE SET
       build = CASE WHEN sessions.build = '' THEN excluded.build ELSE sessions.build END,
       schema = MAX(sessions.schema, excluded.schema),
       consent_training = MIN(sessions.consent_training, excluded.consent_training),
       zip_key = COALESCE(sessions.zip_key, excluded.zip_key),
       zip_bytes = COALESCE(sessions.zip_bytes, excluded.zip_bytes),
       zip_sha256 = COALESCE(sessions.zip_sha256, excluded.zip_sha256),
       zip_received = COALESCE(sessions.zip_received, excluded.zip_received),
       summary_received = COALESCE(sessions.summary_received, excluded.summary_received),
       summary_sha256 = COALESCE(sessions.summary_sha256, excluded.summary_sha256),
       computer = COALESCE(sessions.computer, excluded.computer),
       fps = COALESCE(sessions.fps, excluded.fps),
       minutes = COALESCE(sessions.minutes, excluded.minutes),
       ended = COALESCE(sessions.ended, excluded.ended)`
  ).bind(s.id, s.tester, s.round, s.launch, s.build ?? "", s.schema ?? 0, s.consent.version, s.consent.training,
    s.now, iso(addDays(new Date(s.now), RETENTION_DAYS)),
    s.zipKey ?? null, s.zipBytes ?? null, s.zipSha ?? null, s.zipKey ? s.now : null,
    s.summarySha ? s.now : null, s.summarySha ?? null, s.computer ?? null, s.fps ?? null, s.minutes ?? null, s.ended ?? null);
}

export const testerSeen = (env, tester, now) =>
  env.DB.prepare("INSERT OR IGNORE INTO testers (id, first_seen) VALUES (?, ?)").bind(tester, now);

// A session already removed (expired, or a deletion request) stays removed.
export async function tombstone(env, id) {
  if (!env.DB) return false;
  const row = await env.DB.prepare("SELECT deleted FROM sessions WHERE id = ?").bind(id).first();
  return !!(row && row.deleted);
}

export async function upload(request, env, [round, tester, launch]) {
  requireKey(request, env.UPLOAD_KEY, "X-Kehai-Key");
  const id = `${round}/${tester}/${launch}`;
  if (await deletedTester(env, tester) || await tombstone(env, id)) throw new HttpError(410, "this session was deleted; don't send it again");
  const consent = consentOf(request);
  const length = Number(request.headers.get("Content-Length") || "0");
  if (length > MAX_BYTES) throw new HttpError(413, "too big");
  const body = await request.arrayBuffer();
  if (body.byteLength === 0) throw new HttpError(400, "empty");
  if (body.byteLength > MAX_BYTES) throw new HttpError(413, "too big");
  const head = new Uint8Array(body, 0, Math.min(4, body.byteLength));
  if (head[0] !== 0x50 || head[1] !== 0x4b || head[2] !== 0x03 || head[3] !== 0x04) throw new HttpError(400, "not a zip");

  const key = `sessions/${id}.zip`;
  const digest = await crypto.subtle.digest("SHA-256", body);
  const stored = await env.SESSIONS.put(key, body, {
    onlyIf: new Headers({ "If-None-Match": "*" }),
    sha256: digest,
    httpMetadata: { contentType: "application/zip" },
    customMetadata: { received: iso(clock(env)), round, code: tester, launch, consent: consent.version, training: String(consent.training) },
  });
  let status = 201;
  if (!stored) {
    if (!(await sameBytes(env, key, body, digest))) throw new HttpError(409, "session ID already contains different bytes");
    status = 200;   // a retry whose first answer was lost
  }
  if (env.DB) {
    const now = iso(clock(env));
    await env.DB.batch([
      testerSeen(env, tester, now),
      upsertSession(env, {
        id, tester, round, launch, consent, now,
        build: (request.headers.get("X-Kehai-Build") || "").slice(0, 64),
        schema: Number.parseInt(request.headers.get("X-Kehai-Schema") || "0", 10) || 0,
        zipKey: key, zipBytes: body.byteLength, zipSha: await sha256Hex(body),
      }),
    ]);
  }
  return json(status, { key, bytes: body.byteLength });
}

// Is what's stored under `key` these very bytes?
async function sameBytes(env, key, body, digest) {
  const previous = await env.SESSIONS.head(key);
  if (!previous) throw new HttpError(503, "session changed during receipt; retry");
  if (previous.size !== body.byteLength) return false;
  const checksum = previous.checksums?.sha256;
  if (checksum) return equalBytes(new Uint8Array(digest), new Uint8Array(checksum));
  // Older uploads have no SHA-256. Compare in chunks to avoid buffering a second ZIP.
  const original = await env.SESSIONS.get(key, { onlyIf: { etagMatches: previous.etag } });
  if (!original?.body) throw new HttpError(503, "session changed during receipt; retry");
  const expected = new Uint8Array(body);
  let offset = 0;
  for await (const chunk of original.body) {
    if (!equalBytes(chunk, expected.subarray(offset, offset + chunk.length))) return false;
    offset += chunk.length;
  }
  return offset === expected.length;
}

function equalBytes(a, b) {
  return a.length === b.length && a.every((value, index) => value === b[index]);
}
