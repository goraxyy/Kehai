// Stand-ins for the cloud, for node --test with nothing installed:
//   d1() is a real SQLite database (Node's built-in node:sqlite) behind D1's API, with the
//        migrations applied, so the SQL the Worker runs is the SQL that's checked;
//   r2() is an in-memory bucket with the parts of R2's API the Worker uses.
import { readdirSync, readFileSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { fileURLToPath } from "node:url";

const MIGRATIONS = fileURLToPath(new URL("../migrations/", import.meta.url));

export function d1() {
  const db = new DatabaseSync(":memory:");
  for (const file of readdirSync(MIGRATIONS).filter(f => f.endsWith(".sql")).sort())
    db.exec(readFileSync(MIGRATIONS + file, "utf8"));
  const statement = (sql, args = []) => ({
    sql, args,
    bind: (...values) => {
      if (values.some(v => v === undefined)) throw new Error("D1 refuses undefined: " + sql);
      return statement(sql, values);
    },
    async first(column) {
      const row = db.prepare(sql).get(...args);
      if (row === undefined) return null;
      return column ? row[column] : { ...row };
    },
    async all() { return { success: true, results: db.prepare(sql).all(...args).map(r => ({ ...r })) }; },
    async run() { return run(sql, args); },
  });
  const run = (sql, args) => {
    const s = db.prepare(sql);
    if (/^\s*(SELECT|WITH)/i.test(sql)) return { success: true, results: s.all(...args).map(r => ({ ...r })) };
    const r = s.run(...args);
    return { success: true, meta: { changes: Number(r.changes) } };
  };
  return {
    db,
    prepare: sql => statement(sql),
    // D1's batch runs as one transaction: all of it, or none.
    async batch(list) {
      db.exec("BEGIN");
      try {
        const out = list.map(s => run(s.sql, s.args));
        db.exec("COMMIT");
        return out;
      } catch (e) {
        db.exec("ROLLBACK");
        throw e;
      }
    },
    rows: (sql, ...args) => db.prepare(sql).all(...args).map(r => ({ ...r })),
  };
}

export function r2() {
  const store = new Map();
  const bytes = body => (typeof body === "string" ? new TextEncoder().encode(body) : new Uint8Array(body));
  return {
    store,
    async put(key, body, opts = {}) {
      const only = opts.onlyIf;
      if (only && typeof only.get === "function" && only.get("If-None-Match") === "*" && store.has(key)) return null;
      const row = { body: bytes(body), opts, uploaded: new Date() };
      store.set(key, row);
      return row;
    },
    async head(key) {
      const row = store.get(key);
      return row ? { size: row.body.byteLength, checksums: { sha256: row.opts.sha256 }, etag: "original" } : null;
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
