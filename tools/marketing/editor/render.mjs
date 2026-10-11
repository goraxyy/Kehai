#!/usr/bin/env node
// Renders an edit: one MP4 per language (docs/MARKETING.md).
//
//   node render.mjs <edit.json> [--lang en|ru|all] [--root <working folder>] [--out <folder>]
//                   [--concurrency 1|2] [--scale 0.5] [--frames 0-150] [--dry-run]
//
// Files in the edit are relative to the working folder (--root, else KEHAI_MARKETING, else
// ~/TokenLimit/marketing); videos go to <root>/drafts/<id>.<lang>.mp4 with a .json beside each.
// The sound is normalised to -14 LUFS with peaks under -1.5 dBTP (what YouTube, TikTok and
// Instagram play at), in two passes with Remotion's own ffmpeg.
// Before rendering it checks the edit against schemas/edit.schema.json, that every file is
// there, and that every shot is long enough for its trim and speed. It takes the heavy-job
// lock (<root>/state/heavy.lock, shared with render_shot.sh), so one heavy job runs at a time.
// --dry-run does the checks and says what it would render.
//
// Exit codes: 0 done · 1 the edit is wrong · 2 the render failed · 75 another heavy job holds
// the lock (try again later).

import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import Ajv2020 from 'ajv/dist/2020.js';
import { bundle } from '@remotion/bundler';
import { getVideoMetadata, renderMedia, selectComposition } from '@remotion/renderer';
import { filesOf, sceneFrames, sourceSeconds, totalFrames } from './src/lib/timeline.ts';

const here = path.dirname(fileURLToPath(import.meta.url));
const MAX_CONCURRENCY = 2;   // 8 GB of memory: one or two Chrome tabs at a time
const LOUDNESS = { I: -14, TP: -1.5, LRA: 11 };
const FFMPEG_DIR = path.join(here, 'node_modules', '@remotion', `compositor-${process.platform}-${process.arch}`);

// Remotion's ffmpeg finds its libraries in its own folder, so it runs from there.
function ffmpeg(args) {
  const r = spawnSync(path.join(FFMPEG_DIR, 'ffmpeg'), ['-y', '-hide_banner', ...args], { cwd: FFMPEG_DIR, encoding: 'utf8', maxBuffer: 1 << 26 });
  if (r.status !== 0) throw new Error(`ffmpeg ${args.join(' ')} failed:\n${(r.stderr || '').slice(-2000)}`);
  return r.stderr || '';
}

// Two-pass loudness normalisation: measure, then apply linearly (no pumping).
function normalise(input, output) {
  const target = `I=${LOUDNESS.I}:TP=${LOUDNESS.TP}:LRA=${LOUDNESS.LRA}`;
  const probe = path.join(os.tmpdir(), `kehai-loudness-${process.pid}.wav`);
  let measured;
  try {
    const log = ffmpeg(['-loglevel', 'info', '-i', input, '-vn', '-af', `loudnorm=${target}:print_format=json`, '-f', 'wav', probe]);
    measured = JSON.parse(log.slice(log.lastIndexOf('{'), log.lastIndexOf('}') + 1));
  } finally {
    fs.rmSync(probe, { force: true });
  }
  const m = measured;
  ffmpeg(['-loglevel', 'error', '-i', input, '-map', '0:v', '-map', '0:a?', '-c:v', 'copy',
    '-af', `loudnorm=${target}:measured_I=${m.input_i}:measured_TP=${m.input_tp}:measured_LRA=${m.input_lra}:measured_thresh=${m.input_thresh}:offset=${m.target_offset}:linear=true`,
    '-ar', '48000', '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', output]);
  return { before: Number(m.input_i), peakBefore: Number(m.input_tp) };
}

function fail(code, message) {
  console.error(`render: ${message}`);
  process.exit(code);
}

// ---- arguments ---------------------------------------------------------------------------------

const args = process.argv.slice(2);
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};
const editPath = args.find((a, i) => !a.startsWith('--') && (i === 0 || !args[i - 1].startsWith('--') || args[i - 1] === '--dry-run'));
if (!editPath) fail(1, 'usage: node render.mjs <edit.json> [--lang en|ru|all] [--root <folder>] [--out <folder>] [--concurrency 1] [--scale 1] [--frames a-b] [--dry-run]');

const root = path.resolve(opt('--root', process.env.KEHAI_MARKETING || path.join(os.homedir(), 'TokenLimit', 'marketing')));
const outDir = path.resolve(opt('--out', path.join(root, 'drafts')));
const dryRun = args.includes('--dry-run');
const concurrency = Math.max(1, Math.min(MAX_CONCURRENCY, Number(opt('--concurrency', '1')) || 1));
const scale = Math.max(0.1, Math.min(1, Number(opt('--scale', '1')) || 1));
const framesArg = opt('--frames', null);
const frameRange = framesArg ? framesArg.split('-').map(Number) : null;

// ---- the edit ------------------------------------------------------------------------------------

let edit;
try {
  edit = JSON.parse(fs.readFileSync(editPath, 'utf8'));
} catch (e) {
  fail(1, `can't read ${editPath}: ${e.message}`);
}

const schema = JSON.parse(fs.readFileSync(path.join(here, '..', 'schemas', 'edit.schema.json'), 'utf8'));
const ajv = new Ajv2020({ allErrors: true, strict: false });
const valid = ajv.compile(schema);
if (!valid(edit)) {
  // oneOf failures repeat themselves for every branch: keep the specific ones.
  const lines = [...new Set(valid.errors.filter((e) => e.keyword !== 'oneOf').map((e) => `  ${e.instancePath || '/'} ${e.message}${e.params?.additionalProperty ? ` (${e.params.additionalProperty})` : ''}`))];
  fail(1, `${editPath} doesn't match edit.schema.json:\n${lines.slice(0, 20).join('\n')}`);
}

const want = opt('--lang', 'all');
const languages = want === 'all' ? edit.languages : [want];
for (const l of languages) if (!edit.languages.includes(l)) fail(1, `the edit has no "${l}" (it has ${edit.languages.join(', ')})`);

const problems = [];
const missing = new Set(languages.flatMap((l) => filesOf(edit, l)).filter((f) => !fs.existsSync(path.join(root, f))));
for (const f of missing) problems.push(`missing file: ${f}`);

// Every shot must last as long as its scene asks of it (trim, speed and all).
const shots = [];
edit.scenes.forEach((scene, i) => {
  const v = scene.visual;
  const list = v.type === 'shot' ? [v] : v.type === 'split' ? [v.a, v.b] : [];
  for (const s of list) shots.push({ scene: scene.id ?? i + 1, shot: s, needs: (s.trim ?? 0) + sourceSeconds(s, sceneFrames(edit, i) / edit.fps) });
  for (const o of scene.overlays ?? []) {
    if ((o.from ?? 0) >= scene.duration || (o.to ?? scene.duration) > scene.duration + 1e-6)
      problems.push(`scene ${scene.id ?? i + 1}: a ${o.type} runs outside the scene (${o.from ?? 0}–${o.to ?? scene.duration} s of ${scene.duration} s)`);
  }
});
for (const { scene, shot, needs } of shots) {
  if (missing.has(shot.src)) continue;
  try {
    const meta = await getVideoMetadata(path.join(root, shot.src));
    if (meta.durationInSeconds + 0.05 < needs)
      problems.push(`scene ${scene}: ${shot.src} is ${meta.durationInSeconds.toFixed(2)} s, but the scene plays ${needs.toFixed(2)} s of it`);
  } catch (e) {
    problems.push(`scene ${scene}: can't read ${shot.src}: ${e.message}`);
  }
}
if (problems.length) fail(1, `${editPath}:\n  ${problems.join('\n  ')}`);

const frames = totalFrames(edit);
const size = edit.format === '9:16' ? '1080×1920' : '1920×1080';
console.log(`render: ${edit.id}: ${edit.scenes.length} scenes, ${(frames / edit.fps).toFixed(1)} s (${frames} frames at ${edit.fps} fps), ${size}` +
  `${scale < 1 ? ` at ${Math.round(scale * 100)}%` : ''}, ${languages.join(' + ')} → ${outDir}`);
if (dryRun) {
  console.log('render: dry run: the edit is valid and every file is there; nothing rendered.');
  process.exit(0);
}

// ---- one heavy job at a time -----------------------------------------------------------------

const lock = path.join(root, 'state', 'heavy.lock');
function takeLock() {
  fs.mkdirSync(path.dirname(lock), { recursive: true });
  try {
    fs.mkdirSync(lock);
  } catch {
    const holder = Number(fs.readFileSync(path.join(lock, 'pid'), 'utf8').trim() || 0);
    let alive = false;
    try { process.kill(holder, 0); alive = true; } catch { /* gone */ }
    if (alive) return false;
    fs.rmSync(lock, { recursive: true, force: true });   // its owner is gone
    fs.mkdirSync(lock);
  }
  fs.writeFileSync(path.join(lock, 'pid'), String(process.pid));
  fs.writeFileSync(path.join(lock, 'job'), `render ${edit.id}`);
  return true;
}
const wait = Number(process.env.KEHAI_LOCK_WAIT || 0);
for (let waited = 0; !takeLock(); waited += 5) {
  if (waited >= wait) {
    let job = 'unknown';
    try { job = fs.readFileSync(path.join(lock, 'job'), 'utf8'); } catch { /* */ }
    fail(75, `another heavy job is running (${job}); try again later`);
  }
  await new Promise((r) => setTimeout(r, 5000));
}
let publicDir;
const release = () => {
  fs.rmSync(lock, { recursive: true, force: true });
  if (publicDir) fs.rmSync(publicDir, { recursive: true, force: true });
};
process.on('SIGINT', () => { release(); process.exit(130); });
process.on('SIGTERM', () => { release(); process.exit(143); });

// ---- the render --------------------------------------------------------------------------------

try {
  // Remotion copies its public folder into the bundle: give it only this edit's files (hard
  // links, so nothing is duplicated on disk until then).
  publicDir = fs.mkdtempSync(path.join(os.tmpdir(), 'kehai-public-'));
  for (const f of new Set(languages.flatMap((l) => filesOf(edit, l)))) {
    const to = path.join(publicDir, f);
    fs.mkdirSync(path.dirname(to), { recursive: true });
    try { fs.linkSync(path.join(root, f), to); } catch { fs.copyFileSync(path.join(root, f), to); }
  }

  const started = Date.now();
  const serveUrl = await bundle({ entryPoint: path.join(here, 'src', 'index.ts'), publicDir });
  fs.mkdirSync(outDir, { recursive: true });

  for (const lang of languages) {
    const inputProps = { edit, lang };
    const composition = await selectComposition({ serveUrl, id: 'Edit', inputProps });
    const file = path.join(outDir, `${edit.id}.${lang}.mp4`);
    const raw = file.replace(/\.mp4$/, '.unmastered.mp4');
    let shown = -1;
    const t0 = Date.now();
    await renderMedia({
      composition, serveUrl, inputProps, codec: 'h264', crf: 18, audioCodec: 'aac', outputLocation: raw,
      concurrency, scale, frameRange: frameRange ?? undefined,
      onProgress: ({ progress }) => {
        const pct = Math.floor(progress * 10) * 10;
        if (pct !== shown) { shown = pct; console.log(`render: ${edit.id}.${lang} ${pct}%`); }
      },
    });
    const loud = normalise(raw, file);
    fs.rmSync(raw, { force: true });
    const seconds = (Date.now() - t0) / 1000;
    fs.writeFileSync(file.replace(/\.mp4$/, '.json'), JSON.stringify({
      version: 1, id: edit.id, lang, file: path.basename(file), format: edit.format, fps: edit.fps,
      frames: frameRange ? frameRange[1] - frameRange[0] + 1 : composition.durationInFrames,
      width: Math.round(composition.width * scale), height: Math.round(composition.height * scale),
      edit: path.resolve(editPath), rendered: new Date().toISOString().slice(0, 19), renderSeconds: Math.round(seconds),
      loudness: { target: `${LOUDNESS.I} LUFS, ${LOUDNESS.TP} dBTP`, measuredBefore: loud.before, peakBefore: loud.peakBefore },
    }, null, 2));
    console.log(`render: done ${file} (${(fs.statSync(file).size / 1e6).toFixed(1)} MB in ${seconds.toFixed(0)} s; ` +
      `loudness ${loud.before.toFixed(1)} LUFS, peak ${loud.peakBefore.toFixed(1)} dBTP → ${LOUDNESS.I} LUFS, under ${LOUDNESS.TP} dBTP)`);
  }
  fs.rmSync(serveUrl, { recursive: true, force: true });
  console.log(`render: all done in ${((Date.now() - started) / 1000).toFixed(0)} s`);
} catch (e) {
  console.error(e);
  release();
  process.exit(2);
}
release();
