// The builds testers download. `playtest.py publish` puts each zip in R2 with wrangler
// (builds/<round>/<version>/<mac|windows>.zip; too big for a Worker's request) and then sets
// the current build with POST /admin/latest. Open routes:
//
//   GET /latest                 the current build (Q6: the game's "a new build is out" line)
//   GET /download/<mac|windows> that platform's zip, counted per day and platform only
import { HttpError, OPEN, ROUND, clock, iso, json } from "./util.js";

export const LATEST = "builds/latest.json";
const VERSION = /^[0-9A-Za-z.+-]{1,40}$/;
export const buildKey = (round, version, platform) => `builds/${round}/${version}/${platform}.zip`;

async function current(env) {
  const o = await env.SESSIONS.get(LATEST);
  if (!o) return null;
  return JSON.parse(await new Response(o.body).text());
}

export async function latest(env) {
  const build = await current(env);
  if (!build) return json(404, { error: "no build yet" }, OPEN);
  return json(200, build, { ...OPEN, "Cache-Control": "max-age=300" });
}

export async function download(env, platform) {
  const build = await current(env);
  if (!build) throw new HttpError(404, "no build yet");
  const o = await env.SESSIONS.get(buildKey(build.round, build.version, platform));
  if (!o) throw new HttpError(404, `no ${platform} build in ${build.version}`);
  if (env.DB) {
    const day = iso(clock(env)).slice(0, 10);
    await env.DB.prepare(
      "INSERT INTO downloads (day, platform, count) VALUES (?, ?, 1) ON CONFLICT(day, platform) DO UPDATE SET count = count + 1")
      .bind(day, platform).run();
  }
  return new Response(o.body, {
    status: 200,
    headers: {
      ...OPEN,
      "Content-Type": "application/zip",
      "Content-Length": String(o.size),
      "Content-Disposition": `attachment; filename="Kehai-${build.version}-${platform === "mac" ? "Mac" : "Windows"}.zip"`,
    },
  });
}

// POST /admin/latest { round, version }: only once both platforms' zips are in R2.
export async function setLatest(env, data) {
  const round = data?.round, version = data?.version;
  if (!ROUND.test(round ?? "") || !VERSION.test(version ?? "")) throw new HttpError(400, "round and version, please");
  const missing = [];
  for (const platform of ["mac", "windows"])
    if (!(await env.SESSIONS.head(buildKey(round, version, platform)))) missing.push(platform);
  if (missing.length) throw new HttpError(409, `not in R2 yet: ${missing.join(", ")}`);
  const build = { round, version, published: iso(clock(env)) };
  await env.SESSIONS.put(LATEST, JSON.stringify(build), { httpMetadata: { contentType: "application/json" } });
  return json(200, build);
}
