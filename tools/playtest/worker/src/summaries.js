// POST /summaries, with the upload key: a session's structured rows (C2.6), beside its zip.
//
//   { round, tester, launch, build, schema, consent: { version, training },
//     computer, fps, minutes, ended,
//     shifts:  [{ n, seconds, clocked_out, caught, spotted, closest_m, trick_sounds, lowest_energy }],
//     answers: { taps: [..4], a1, a2, a3, nickname },
//     moments: [{ n, shift, replay_s, note }] }
//
// Retry-safe (C1.5): the same summary again is accepted (200); a different one for the same
// session is refused (409). It may arrive before or after the zip.
import { HttpError, SESSION_PATH, clip, clock, deletedTester, flag, iso, json, number, readJson, requireKey, sha256Hex } from "./util.js";
import { testerSeen, tombstone, upsertSession } from "./sessions.js";

const MAX_SHIFTS = 50, MAX_MOMENTS = 500;

export async function summary(request, env) {
  requireKey(request, env.UPLOAD_KEY, "X-Kehai-Key");
  if (!env.DB) throw new HttpError(503, "no database bound");
  const { data, raw } = await readJson(request);
  const s = validate(data);
  if (await deletedTester(env, s.tester) || await tombstone(env, s.id)) throw new HttpError(410, "this session was deleted; don't send it again");

  const sha = await sha256Hex(raw);
  const existing = await env.DB.prepare("SELECT summary_sha256 FROM sessions WHERE id = ?").bind(s.id).first();
  if (existing?.summary_sha256) {
    if (existing.summary_sha256 === sha) return json(200, { session: s.id, rows: s.rowCount });
    throw new HttpError(409, "this session already has a different summary");
  }

  const now = iso(clock(env));
  const statements = [
    testerSeen(env, s.tester, now),
    upsertSession(env, { ...s, now, summarySha: sha }),
  ];
  if (s.answers.nickname) statements.push(env.DB.prepare("UPDATE testers SET nickname = ? WHERE id = ?").bind(s.answers.nickname, s.tester));
  for (const r of s.shifts)
    statements.push(env.DB.prepare(
      `INSERT OR REPLACE INTO shifts (session, n, seconds, clocked_out, caught, spotted, closest_m, trick_sounds, lowest_energy)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`).bind(s.id, r.n, r.seconds, r.clocked_out, r.caught, r.spotted, r.closest_m, r.trick_sounds, r.lowest_energy));
  for (const m of s.moments)
    statements.push(env.DB.prepare("INSERT OR REPLACE INTO moments (session, n, shift, replay_s, note) VALUES (?, ?, ?, ?, ?)")
      .bind(s.id, m.n, m.shift, m.replay_s, m.note));
  const a = s.answers;
  if (a.taps || a.a1 || a.a2 || a.a3 || a.nickname)
    statements.push(env.DB.prepare("INSERT OR REPLACE INTO answers (session, taps, a1, a2, a3, nickname) VALUES (?, ?, ?, ?, ?, ?)")
      .bind(s.id, a.taps, a.a1, a.a2, a.a3, a.nickname));
  await env.DB.batch(statements);   // all or nothing: a half-written summary can't be left behind
  return json(201, { session: s.id, rows: s.rowCount });
}

export function validate(d) {
  if (!d || typeof d !== "object") throw new HttpError(400, "not a summary");
  const id = `${d.round}/${d.tester}/${d.launch}`;
  const path = id.match(SESSION_PATH);
  if (!path) throw new HttpError(400, "bad session id");
  const c = d.consent;
  if (!c || typeof c.version !== "string" || !/^[A-Za-z0-9.-]{1,16}$/.test(c.version)) throw new HttpError(400, "the consent is missing");
  const shifts = Array.isArray(d.shifts) ? d.shifts : [];
  const moments = Array.isArray(d.moments) ? d.moments : [];
  if (shifts.length > MAX_SHIFTS || moments.length > MAX_MOMENTS) throw new HttpError(413, "too many rows");
  const taps = Array.isArray(d.answers?.taps) ? d.answers.taps.slice(0, 4).map(t => clip(String(t), 40)) : null;
  const answers = {
    taps: taps && taps.some(Boolean) ? JSON.stringify(taps) : null,
    a1: clip(d.answers?.a1, 4000), a2: clip(d.answers?.a2, 4000), a3: clip(d.answers?.a3, 4000),
    nickname: clip(d.answers?.nickname, 40),
  };
  const shiftRows = shifts.map((r, i) => ({
    n: Number.isInteger(r?.n) ? r.n : i + 1,
    seconds: number(r?.seconds), clocked_out: flag(r?.clocked_out), caught: number(r?.caught),
    spotted: number(r?.spotted), closest_m: number(r?.closest_m), trick_sounds: number(r?.trick_sounds),
    lowest_energy: number(r?.lowest_energy),
  }));
  const momentRows = moments.map((m, i) => ({ n: i + 1, shift: number(m?.shift), replay_s: number(m?.replay_s), note: clip(m?.note, 2000) }));
  return {
    id, round: path[1], tester: path[2], launch: path[3],
    build: clip(d.build, 64) ?? "", schema: Number.isInteger(d.schema) ? d.schema : 0,
    consent: { version: c.version, training: c.training === true ? 1 : 0 },
    computer: clip(d.computer, 200), fps: number(d.fps), minutes: number(d.minutes), ended: clip(d.ended, 40),
    shifts: shiftRows, moments: momentRows, answers,
    rowCount: { shifts: shiftRows.length, moments: momentRows.length, answers: answers.taps || answers.a1 || answers.a2 || answers.a3 || answers.nickname ? 1 : 0 },
  };
}
