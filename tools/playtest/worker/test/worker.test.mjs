// The upload service against an in-memory stand-in for R2. Run: node --test (no installs).
import assert from "node:assert/strict";
import { test } from "node:test";
import worker, { MAX_BYTES, same } from "../src/index.js";

const UPLOAD = "upload-key-0123456789abcdef", ADMIN = "admin-key-0123456789abcdef";

function bucket() {
  const store = new Map();
  return {
    store,
    async put(key, body, opts) {
      if (opts.onlyIf?.get("If-None-Match") === "*" && store.has(key)) return null;
      const row = { body: new Uint8Array(body), opts, uploaded: new Date() };
      store.set(key, row);
      return row;
    },
    async head(key) {
      const row = store.get(key);
      return row && { size: row.body.byteLength, checksums: { sha256: row.opts.sha256 }, etag: "original" };
    },
    async get(key) {
      const o = store.get(key);
      return o ? { body: new Response(o.body).body, size: o.body.byteLength } : null;
    },
    async delete(key) { store.delete(key); },
    async list({ prefix }) {
      const objects = [...store.entries()].filter(([k]) => k.startsWith(prefix))
        .map(([key, o]) => ({ key, size: o.body.byteLength, uploaded: o.uploaded }));
      return { objects, truncated: false };
    },
  };
}

const env = () => ({ SESSIONS: bucket(), UPLOAD_KEY: UPLOAD, ADMIN_KEY: ADMIN });
const zip = (n = 32) => { const b = new Uint8Array(n); b.set([0x50, 0x4b, 0x03, 0x04]); return b; };
const call = (e, method, path, { headers = {}, body } = {}) =>
  worker.fetch(new Request("https://pt.example.dev" + path, { method, headers, body }), e);

test("a build sends a session, the Mac lists, downloads and deletes it", async () => {
  const e = env();
  const put = await call(e, "PUT", "/sessions/round1/T07/20261004_101500.zip", { headers: { "X-Kehai-Key": UPLOAD }, body: zip(100) });
  assert.equal(put.status, 201);
  assert.deepEqual(await put.json(), { key: "sessions/round1/T07/20261004_101500.zip", bytes: 100 });

  const listed = await (await call(e, "GET", "/admin/sessions", { headers: { Authorization: "Bearer " + ADMIN } })).json();
  assert.deepEqual(listed.sessions.map(s => s.key), ["round1/T07/20261004_101500.zip"]);

  const got = await call(e, "GET", "/admin/sessions/round1/T07/20261004_101500.zip", { headers: { Authorization: "Bearer " + ADMIN } });
  assert.equal(got.status, 200);
  assert.equal((await got.arrayBuffer()).byteLength, 100);

  const del = await call(e, "DELETE", "/admin/sessions/round1/T07/20261004_101500.zip", { headers: { Authorization: "Bearer " + ADMIN } });
  assert.equal(del.status, 204);
  assert.equal(e.SESSIONS.store.size, 0);
});

test("the upload key can't read, and nothing works without the right key", async () => {
  const e = env();
  assert.equal((await call(e, "PUT", "/sessions/round1/T07/20261004_101500.zip", { headers: { "X-Kehai-Key": "wrong" }, body: zip() })).status, 401);
  assert.equal((await call(e, "PUT", "/sessions/round1/T07/20261004_101500.zip", { body: zip() })).status, 401);
  assert.equal((await call(e, "GET", "/admin/sessions", { headers: { Authorization: "Bearer " + UPLOAD } })).status, 401);
  assert.equal((await call(e, "GET", "/admin/sessions")).status, 401);
  assert.equal(e.SESSIONS.store.size, 0);
});

test("generated tester IDs keep two sessions from the same second distinct", async () => {
  const e = env();
  const tester = "A1".repeat(16);
  const first = `round1/${tester}/20261004_101500-${"ab".repeat(16)}.zip`;
  const second = `round1/${tester}/20261004_101500-${"cd".repeat(16)}.zip`;
  const upload = { headers: { "X-Kehai-Key": UPLOAD }, body: zip() };
  assert.equal((await call(e, "PUT", "/sessions/" + first, upload)).status, 201);
  assert.equal((await call(e, "PUT", "/sessions/" + second, upload)).status, 201);
  const admin = { headers: { Authorization: "Bearer " + ADMIN } };
  assert.equal((await call(e, "GET", "/admin/sessions/" + first, admin)).status, 200);
  const list = await (await call(e, "GET", "/admin/sessions", admin)).json();
  assert.deepEqual(list.sessions.map(s => s.key).sort(), [first, second].sort());
});

test("only zips, only session addresses, only so big", async () => {
  const e = env();
  const h = { "X-Kehai-Key": UPLOAD };
  assert.equal((await call(e, "PUT", "/sessions/round1/T07/20261004_101500.zip", { headers: h, body: new TextEncoder().encode("hello") })).status, 400);
  assert.equal((await call(e, "PUT", "/sessions/round1/T07/20261004_101500.zip", { headers: h, body: new Uint8Array(0) })).status, 400);
  assert.equal((await call(e, "PUT", "/sessions/round1/t07/20261004_101500.zip", { headers: h, body: zip() })).status, 404, "codes are upper case");
  assert.equal((await call(e, "PUT", "/sessions/round1/T07/../../x.zip", { headers: h, body: zip() })).status, 404);
  assert.equal((await call(e, "PUT", "/sessions/round1/T07/20261004_101500.zip", { headers: { ...h, "Content-Length": String(MAX_BYTES + 1) }, body: zip() })).status, 413);
  assert.equal((await call(e, "GET", "/sessions/round1/T07/20261004_101500.zip")).status, 405);
  assert.equal((await call(e, "GET", "/admin/sessions/round1/secret.txt", { headers: { Authorization: "Bearer " + ADMIN } })).status, 400);
  assert.equal((await call(e, "GET", "/admin/sessions/../secret", { headers: { Authorization: "Bearer " + ADMIN } })).status, 404, "URLs are normalised first");
  assert.equal(e.SESSIONS.store.size, 0);
});

test("keys compare whole, and a missing or short secret matches nothing", () => {
  assert.equal(same(UPLOAD, UPLOAD), true);
  assert.equal(same(UPLOAD + "x", UPLOAD), false);
  assert.equal(same(UPLOAD.slice(0, -1), UPLOAD), false);
  assert.equal(same("", UPLOAD), false);
  assert.equal(same("anything", undefined), false);
  assert.equal(same("short", "short"), false);
});

test("it says it's up", async () => {
  const r = await call(env(), "GET", "/");
  assert.equal(r.status, 200);
});

test("simultaneous uploads keep the first receipt, including on later retries", async () => {
  const e = env();
  const path = "/sessions/round1/T07/20261004_101500.zip";
  const send = body => call(e, "PUT", path, { headers: { "X-Kehai-Key": UPLOAD }, body });
  const first = zip(), second = zip();
  second[7] = 123;
  const results = await Promise.all([send(first), send(second)]);
  assert.deepEqual(results.map(r => r.status).sort(), [201, 409]);
  const winner = results[0].status === 201 ? first : second;
  const loser = winner === first ? second : first;
  const row = e.SESSIONS.store.get(path.slice(1));
  assert.equal((await send(winner)).status, 200);
  assert.equal((await send(loser)).status, 409);
  delete row.opts.sha256; // Sessions received by the older Worker.
  assert.equal((await send(winner)).status, 200);
  assert.equal((await send(loser)).status, 409);
  assert.equal(e.SESSIONS.store.get(path.slice(1)), row);
  assert.deepEqual(row.body, winner);
});
