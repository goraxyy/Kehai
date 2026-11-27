// Playtest v2's upload service (C1.1–C1.3, C1.5, C1.6) against the stand-ins: a real SQLite
// database with the migrations applied, and an in-memory bucket. Run: node --test (no installs).
import assert from "node:assert/strict";
import { test } from "node:test";
import worker from "../src/index.js";
import { d1, r2 } from "./standins.mjs";

const UPLOAD = "upload-key-0123456789abcdef", ADMIN = "admin-key-0123456789abcdef", DATASET = "dataset-key-0123456789abcdef";
const TESTER = "A1".repeat(16);
const LAUNCH = `20261127_101500-${"ab".repeat(16)}`;
const ID = `round1/${TESTER}/${LAUNCH}`;

function env(start = "2026-11-27T10:00:00Z") {
  const e = { SESSIONS: r2(), DB: d1(), UPLOAD_KEY: UPLOAD, ADMIN_KEY: ADMIN, DATASET_KEY: DATASET, at: new Date(start) };
  e.NOW = () => e.at;
  return e;
}
const zip = (n = 64, fill = 0) => { const b = new Uint8Array(n).fill(fill); b.set([0x50, 0x4b, 0x03, 0x04]); return b; };
const call = (e, method, path, { headers = {}, body } = {}) =>
  worker.fetch(new Request("https://pt.example.dev" + path, { method, headers, body }), e);
const up = { "X-Kehai-Key": UPLOAD };
const admin = { Authorization: "Bearer " + ADMIN };
const data = { Authorization: "Bearer " + DATASET };
const consented = { ...up, "X-Kehai-Consent": "v2", "X-Kehai-Training": "1", "X-Kehai-Build": "0.5.2+85f67f4", "X-Kehai-Schema": "2" };

const putZip = (e, id = ID, headers = consented, body = zip()) => call(e, "PUT", `/sessions/${id}.zip`, { headers, body });
function summaryOf(id = ID, extra = {}) {
  const [round, tester, launch] = id.split("/");
  return {
    round, tester, launch, build: "0.5.2+85f67f4", schema: 2, consent: { version: "v2", training: true },
    computer: "MacBookAir M2, 8 GB", fps: 58.2, minutes: 21.5, ended: "finished",
    shifts: [
      { n: 1, seconds: 420, clocked_out: true, caught: 0, spotted: 3, closest_m: 4.2, trick_sounds: 2, lowest_energy: 0.4 },
      { n: 2, seconds: 380, clocked_out: false, caught: 1, spotted: 5, closest_m: 0.8, trick_sounds: 4, lowest_energy: 0.1 },
    ],
    answers: { taps: ["fair", "scary", "clear", "again"], a1: "Find the mop", a2: "When the lights went", a3: "", nickname: "Rin" },
    moments: [{ shift: 2, replay_s: 133.5, note: "stuck on the shelf" }],
    ...extra,
  };
}
const postSummary = (e, body = summaryOf(), headers = up) =>
  call(e, "POST", "/summaries", { headers: { ...headers, "Content-Type": "application/json" }, body: JSON.stringify(body) });
const session = e => e.DB.rows("SELECT * FROM sessions WHERE id = ?", ID)[0];

test("the zip and its summary make one session, in either order, and retries change nothing", async () => {
  for (const zipFirst of [true, false]) {
    const e = env();
    const first = zipFirst ? await putZip(e) : await postSummary(e);
    const second = zipFirst ? await postSummary(e) : await putZip(e);
    assert.deepEqual([first.status, second.status], [201, 201]);
    const s = session(e);
    assert.equal(s.zip_key, `sessions/${ID}.zip`);
    assert.ok(s.zip_received && s.summary_received, "both halves");
    assert.equal(s.computer, "MacBookAir M2, 8 GB");
    assert.equal(s.consent_training, 1);
    assert.equal(s.build, "0.5.2+85f67f4");

    assert.equal((await putZip(e)).status, 200, "the same zip again");
    assert.equal((await postSummary(e)).status, 200, "the same summary again");
    assert.equal(e.DB.rows("SELECT * FROM shifts").length, 2);
    assert.equal(e.DB.rows("SELECT * FROM moments").length, 1);
    assert.equal(e.DB.rows("SELECT * FROM answers").length, 1);
    assert.equal(e.DB.rows("SELECT * FROM sessions").length, 1);
    assert.equal(e.DB.rows("SELECT nickname FROM testers")[0].nickname, "Rin");

    assert.equal((await postSummary(e, summaryOf(ID, { minutes: 99 }))).status, 409, "a different summary is refused");
    assert.equal((await putZip(e, ID, consented, zip(64, 7))).status, 409, "different bytes are refused");
    assert.equal(session(e).minutes, 21.5);
  }
});

test("consent: v1 builds and a narrowed consent never train her; only consented sessions are in the dataset", async () => {
  const e = env();
  const old = `round1/T07/20261127_090000`;
  assert.equal((await putZip(e, old, up)).status, 201, "a v1 build still uploads");
  assert.equal(e.DB.rows("SELECT consent_version, consent_training FROM sessions WHERE id = ?", old)[0].consent_training, 0);

  const narrowed = `round1/${TESTER}/20261127_110000-${"cd".repeat(16)}`;
  await putZip(e, narrowed);
  await postSummary(e, summaryOf(narrowed, { consent: { version: "v2", training: false } }));
  assert.equal(e.DB.rows("SELECT consent_training FROM sessions WHERE id = ?", narrowed)[0].consent_training, 0, "the stricter one wins");

  await putZip(e);
  await postSummary(e);
  const manifest = await (await call(e, "GET", "/dataset/manifest", { headers: data })).json();
  assert.deepEqual(manifest.sessions.map(s => s.id), [ID]);
  assert.equal(manifest.sessions[0].complete, true);
  assert.match(manifest.sessions[0].zip_sha256, /^[0-9a-f]{64}$/);
  assert.equal((await call(e, "GET", `/dataset/sessions/${narrowed}.zip`, { headers: data })).status, 404);
  assert.equal((await call(e, "GET", `/dataset/sessions/${ID}.zip`, { headers: data })).status, 200);

  assert.equal((await postSummary(e, { ...summaryOf(), consent: undefined })).status, 400, "a summary must say its consent");
});

test("the dataset key reads and nothing more; without a key nothing private shows", async () => {
  const e = env();
  await putZip(e);
  for (const [method, path] of [["GET", "/admin/sessions"], ["DELETE", `/admin/tester/${TESTER}`], ["POST", "/admin/latest"]])
    assert.equal((await call(e, method, path, { headers: data })).status, 401, `${method} ${path}`);
  assert.equal((await call(e, "POST", "/dataset/manifest", { headers: data })).status, 405);
  assert.equal((await putZip(e, `round1/${TESTER}/20261127_120000-${"ef".repeat(16)}`, { "X-Kehai-Key": DATASET })).status, 401);
  for (const path of ["/dataset/manifest", `/dataset/sessions/${ID}.zip`, "/admin/sessions", `/admin/sessions/${ID}.zip`])
    assert.equal((await call(e, "GET", path)).status, 401, path);
  assert.equal((await call(e, "GET", "/dataset/manifest", { headers: up })).status, 401, "the upload key can't read");
});

test("the funnel: a retried batch counts once, and only known kinds from known testers count", async () => {
  const e = env();
  const ev = (id, kind) => ({ id, tester: TESTER, session: ID, round: "round1", build: "0.5.2", kind, at: "2026-11-27T10:01:00Z" });
  const batch = { events: [ev("e0000001", "launch"), ev("e0000002", "consent"), ev("e0000003", "first_shift"), ev("e0000004", "hacked")] };
  const send = () => call(e, "POST", "/events", { headers: { ...up, "Content-Type": "application/json" }, body: JSON.stringify(batch) });
  assert.deepEqual(await (await send()).json(), { accepted: 3, refused: 1 });
  await send();
  assert.deepEqual(e.DB.rows("SELECT kind FROM events ORDER BY id").map(r => r.kind), ["launch", "consent", "first_shift"]);
  assert.equal((await call(e, "POST", "/events", { headers: up, body: "{}" })).status, 400);
});

test("a deletion request removes every file and row, keeps a tombstone, and refuses later sends", async () => {
  const e = env();
  await putZip(e);
  await postSummary(e);
  await call(e, "POST", "/events", { headers: up, body: JSON.stringify({ events: [{ id: "e0000001", tester: TESTER, round: "round1", kind: "launch", at: "2026-11-27T10:01:00Z" }] }) });
  const other = "round1/T08/20261127_090000";
  await putZip(e, other);

  const r = await call(e, "DELETE", `/admin/tester/${TESTER}`, { headers: admin });
  assert.deepEqual(await r.json(), { tester: TESTER, sessions: 1 });
  assert.deepEqual([...e.SESSIONS.store.keys()], [`sessions/${other}.zip`], "only the other tester's zip is left");
  for (const table of ["shifts", "moments", "answers", "events", "testers WHERE id = '" + TESTER + "'"])
    assert.equal(e.DB.rows(`SELECT * FROM ${table}`).length, 0, table);
  const s = session(e);
  assert.equal(s.deleted, "request");
  assert.equal(s.computer, null);

  assert.equal((await putZip(e)).status, 410, "the game's late retry");
  assert.equal((await postSummary(e)).status, 410);
  const later = await (await call(e, "POST", "/events", { headers: up, body: JSON.stringify({ events: [{ id: "e0000009", tester: TESTER, round: "round1", kind: "end", at: "2026-11-27T11:00:00Z" }] }) })).json();
  assert.deepEqual(later, { accepted: 0, refused: 1 });
  const gone = await (await call(e, "GET", "/dataset/deletions", { headers: data })).json();
  assert.deepEqual(gone.deletions.map(d => [d.id, d.reason]), [[ID, "request"]]);
});

test("Q5: six months from collection, whatever is downloaded in between", async () => {
  const e = env("2026-01-10T00:00:00Z");
  await putZip(e);
  await postSummary(e);
  e.at = new Date("2026-05-01T00:00:00Z");
  const young = `round1/${TESTER}/20260501_000000-${"cd".repeat(16)}`;
  await putZip(e, young);
  assert.equal((await call(e, "GET", `/dataset/sessions/${ID}.zip`, { headers: data })).status, 200, "a download");
  assert.equal((await call(e, "GET", `/admin/sessions/${ID}.zip`, { headers: admin })).status, 200);

  e.at = new Date("2026-07-11T12:00:00Z");   // 182.5 days after the first: not yet
  await worker.scheduled({ scheduledTime: e.at.getTime() }, e, { waitUntil: p => p });
  assert.equal(session(e).deleted, null);
  e.at = new Date("2026-07-12T00:00:01Z");   // 183 days: gone
  const done = [];
  await worker.scheduled({ scheduledTime: e.at.getTime() }, e, { waitUntil: p => done.push(p) });
  await Promise.all(done);
  assert.equal(session(e).deleted, "expired");
  assert.equal(e.SESSIONS.store.has(`sessions/${ID}.zip`), false);
  assert.equal(e.DB.rows("SELECT * FROM shifts").length, 0);
  assert.equal(e.SESSIONS.store.has(`sessions/${young}.zip`), true, "the younger session stays");
  const manifest = await (await call(e, "GET", "/dataset/manifest", { headers: data })).json();
  assert.deepEqual(manifest.sessions.map(s => s.id), [young]);
  assert.equal((await putZip(e)).status, 410, "an expired session isn't stored again");
});

test("the current build: published only when both zips are in R2; downloads counted by day and platform only", async () => {
  const e = env();
  assert.equal((await call(e, "GET", "/latest")).status, 404);
  const publish = () => call(e, "POST", "/admin/latest", { headers: { ...admin, "Content-Type": "application/json" }, body: JSON.stringify({ round: "round1", version: "0.5.2" }) });
  await e.SESSIONS.put("builds/round1/0.5.2/mac.zip", zip(10));
  assert.equal((await publish()).status, 409, "no Windows zip yet");
  await e.SESSIONS.put("builds/round1/0.5.2/windows.zip", zip(20));
  assert.equal((await publish()).status, 200);
  const latest = await call(e, "GET", "/latest");
  assert.equal(latest.headers.get("Access-Control-Allow-Origin"), "*");
  assert.deepEqual(await latest.json(), { round: "round1", version: "0.5.2", published: "2026-11-27T10:00:00.000Z" });
  const mac = await call(e, "GET", "/download/mac");
  assert.equal(mac.status, 200);
  assert.match(mac.headers.get("Content-Disposition"), /Kehai-0\.5\.2-Mac\.zip/);
  await call(e, "GET", "/download/mac");
  await call(e, "GET", "/download/windows");
  assert.deepEqual(e.DB.rows("SELECT day, platform, count FROM downloads ORDER BY platform"),
    [{ day: "2026-11-27", platform: "mac", count: 2 }, { day: "2026-11-27", platform: "windows", count: 1 }]);
  assert.equal((await call(e, "GET", "/download/linux")).status, 404);
});

test("sign-ups: one per address, a hidden field for bots, five an hour from one place", async () => {
  const e = env();
  const form = (email, website = "") => { const f = new FormData(); f.set("email", email); f.set("website", website); return f; };
  const send = (body, ip = "203.0.113.9") => call(e, "POST", "/signup", { headers: { "CF-Connecting-IP": ip }, body });
  assert.equal((await send(form("Rin@Example.com"))).status, 200);
  assert.equal((await send(form("rin@example.com"))).status, 200);
  assert.equal((await send(form("bot@example.com", "http://spam"))).status, 200);
  assert.equal((await send(form("not an email"))).status, 400);
  assert.deepEqual(e.DB.rows("SELECT email FROM signups").map(r => r.email), ["rin@example.com"]);
  for (let i = 0; i < 3; i++) await send(form(`p${i}@example.com`));   // the bot and the bad address never counted
  assert.equal((await send(form("late@example.com"))).status, 429, "the sixth from one place in an hour");
  assert.equal((await send(form("late@example.com"), "198.51.100.1")).status, 200, "another place is fine");
  assert.equal(JSON.stringify(e.DB.rows("SELECT * FROM rate")).includes("203.0.113.9"), false, "addresses aren't kept");
});

test("admin rows: a page at a time, known tables only; an analysis summary for a known session", async () => {
  const e = env();
  await putZip(e);
  await postSummary(e);
  const page = await (await call(e, "GET", "/admin/rows/shifts?limit=1", { headers: admin })).json();
  assert.equal(page.rows.length, 1);
  const rest = await (await call(e, "GET", `/admin/rows/shifts?limit=1&after=${page.next}`, { headers: admin })).json();
  assert.equal(rest.rows[0].n, 2);
  assert.equal((await call(e, "GET", "/admin/rows/rate", { headers: admin })).status, 404);
  assert.equal((await call(e, "GET", "/admin/rows/sqlite_master", { headers: admin })).status, 404);
  const write = body => call(e, "POST", "/admin/summaries", { headers: admin, body: JSON.stringify(body) });
  assert.equal((await write({ session: ID, text: "Got stuck once; caught in shift 2." })).status, 200);
  assert.equal((await write({ session: "round1/T09/20261127_000000", text: "x" })).status, 404);
  assert.equal(e.DB.rows("SELECT text FROM summaries")[0].text, "Got stuck once; caught in shift 2.");
});

test("limits: an oversized summary, too many rows, a bad session id", async () => {
  const e = env();
  assert.equal((await postSummary(e, summaryOf(ID, { answers: { a1: "x".repeat(300 * 1024) } }))).status, 413);
  assert.equal((await postSummary(e, summaryOf(ID, { shifts: Array.from({ length: 51 }, () => ({})) }))).status, 413);
  assert.equal((await postSummary(e, summaryOf(ID, { tester: "../etc" }))).status, 400);
  assert.equal((await postSummary(e, summaryOf(), { "X-Kehai-Key": "wrong" })).status, 401);
  assert.equal(e.DB.rows("SELECT * FROM sessions").length, 0);
});
