// The editor's arithmetic, and the samples against the schema. `npm test`.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import Ajv2020 from 'ajv/dist/2020.js';
import {
  captionChunks, filesOf, followAt, gameVolume, musicVolume, sceneStarts, sourceSeconds, speedSegments, spokenWords, textFor,
  totalFrames, transitionFrames, zoomAt,
} from '../src/lib/timeline.ts';
import type { Edit, Shot } from '../src/lib/types.ts';

const here = path.dirname(fileURLToPath(import.meta.url));
const read = (p: string) => JSON.parse(fs.readFileSync(path.join(here, p), 'utf8'));

function edit(scenes: Edit['scenes'], extra: Partial<Edit> = {}): Edit {
  return { version: 1, id: 'test-edit', kind: 'short', format: '9:16', fps: 30, languages: ['en'], scenes, ...extra };
}
const colour = { type: 'color' as const, color: 'ink' };

test('scenes overlap by their transitions, and the end card follows', () => {
  const e = edit([
    { duration: 2, visual: colour },
    { duration: 3, visual: colour, transition: { type: 'fade', duration: 0.5 } },
    { duration: 1, visual: colour, transition: { type: 'cut' } },
  ], { endCard: { duration: 2 } });
  assert.deepEqual(sceneStarts(e), [0, 45, 135]);
  assert.equal(totalFrames(e), 60 + 90 + 30 - 15 + 60);
  assert.equal(transitionFrames(e, 0), 0, 'the first scene has nothing to join');
});

test('a transition is never as long as the scenes it joins', () => {
  const e = edit([{ duration: 0.3, visual: colour }, { duration: 2, visual: colour, transition: { type: 'slide', duration: 1 } }]);
  assert.equal(transitionFrames(e, 1), 8);
});

test('a constant speed is one piece; a ramp eases through short steps', () => {
  const steady: Shot = { type: 'shot', src: 'a.mp4', trim: 2, speed: 0.5 };
  assert.deepEqual(speedSegments(steady, 4), [{ from: 0, to: 4, rate: 0.5, source: 2 }]);
  assert.equal(sourceSeconds(steady, 4), 2);

  const ramp: Shot = { type: 'shot', src: 'a.mp4', speed: [{ at: 0, rate: 1 }, { at: 1, rate: 1 }, { at: 2, rate: 0.5 }] };
  const segs = speedSegments(ramp, 3);
  assert.equal(segs[0].rate, 1);
  assert.equal(segs[0].to, 1, 'steady up to the ramp');
  assert.equal(segs[segs.length - 1].rate, 0.5, 'steady after it');
  for (let i = 1; i < segs.length; i++) {
    assert.ok(Math.abs(segs[i].from - segs[i - 1].to) < 1e-9, 'no gaps');
    assert.ok(segs[i].rate <= segs[i - 1].rate + 1e-9, 'slowing down');
    assert.ok(Math.abs(segs[i].source - (segs[i - 1].source + (segs[i - 1].to - segs[i - 1].from) * segs[i - 1].rate)) < 1e-9, 'the file plays on');
  }
  // 1 s at 1×, a 1 s ramp from 1× to 0.5× (0.75 s of footage), 1 s at 0.5×.
  assert.ok(Math.abs(sourceSeconds(ramp, 3) - 2.25) < 1e-9);
});

test('zooms hold at their ends and ease between keys', () => {
  const keys = [{ at: 1, scale: 1 }, { at: 3, scale: 2, x: 0.2, y: 0.8 }];
  assert.deepEqual(zoomAt(keys, 0), { scale: 1, x: 0.5, y: 0.5 });
  assert.deepEqual(zoomAt(keys, 5), { scale: 2, x: 0.2, y: 0.8 });
  assert.equal(zoomAt(keys, 2).scale, 1.5);
  assert.ok(zoomAt(keys, 1.2).scale - 1 < 0.2 * 0.5, 'eases in');
  assert.deepEqual(zoomAt(undefined, 1), { scale: 1, x: 0.5, y: 0.5 });
});

test('a following mark moves in straight lines between its keys and holds at the ends', () => {
  const keys = [{ at: 0, x: 0.2, y: 0.5 }, { at: 1, x: 0.4, y: 0.6 }];
  assert.deepEqual(followAt(keys, -1, { x: 0, y: 0 }), { x: 0.2, y: 0.5 });
  const mid = followAt(keys, 0.5, { x: 0, y: 0 });
  assert.ok(Math.abs(mid.x - 0.3) < 1e-9 && Math.abs(mid.y - 0.55) < 1e-9);
  assert.deepEqual(followAt(keys, 9, { x: 0, y: 0 }), { x: 0.4, y: 0.6 });
  assert.deepEqual(followAt(undefined, 1, { x: 0.7, y: 0.1 }), { x: 0.7, y: 0.1 }, 'a fixed mark stays put');
});

test('the music ducks under the voice and fades at both ends', () => {
  const music = { src: 'm.wav', volume: 0.4, duck: 0.1, fadeIn: 1, fadeOut: 2 };
  const voice = [{ src: 'v.wav', at: 5, duration: 3 }];
  assert.equal(musicVolume(music, voice, 0, 20), 0);
  assert.equal(musicVolume(music, voice, 3, 20), 0.4);
  assert.ok(Math.abs(musicVolume(music, voice, 6, 20) - 0.1) < 1e-9, 'under the voice');
  assert.ok(musicVolume(music, voice, 4.9, 20) < 0.4 && musicVolume(music, voice, 4.9, 20) > 0.1, 'ramping down just before');
  assert.equal(musicVolume(music, voice, 20, 20), 0);
});

test('the game\'s own sound dips under the voice too', () => {
  const voice = [{ src: 'v.wav', at: 5, duration: 3 }];
  assert.equal(gameVolume(undefined, voice, 2), 0.7, 'a shot plays at 0.7 unless the edit says');
  assert.ok(Math.abs(gameVolume(1, voice, 6) - 0.35) < 1e-9, 'and keeps 35% of it under the voice');
  assert.equal(gameVolume(0, voice, 6), 0);
});

test('captions come a few words at a time, breaking at pauses, sentence ends and speakers', () => {
  const words = spokenWords([
    { src: 'a', at: 10, duration: 3, words: [
      { text: 'I', start: 0, end: 0.2 }, { text: 'hid', start: 0.25, end: 0.5 }, { text: 'behind', start: 0.55, end: 0.8 },
      { text: 'shelves.', start: 0.85, end: 1.2 }, { text: 'She', start: 2.0, end: 2.2 }, { text: 'knew', start: 2.25, end: 2.6 },
    ] },
    { src: 'b', at: 13, duration: 1, speaker: 'karen', words: [{ text: 'Hello', start: 0, end: 0.5 }] },
  ]);
  assert.equal(words[0].start, 10, 'on the video clock');
  const chunks = captionChunks(words, 3);
  assert.deepEqual(chunks.map((c) => c.words.map((w) => w.text).join(' ')), ['I hid behind', 'shelves.', 'She knew', 'Hello']);
  for (let i = 0; i + 1 < chunks.length; i++) assert.ok(chunks[i].end <= chunks[i + 1].start, 'never two at once');
});

test('text falls back to English, then to whatever there is', () => {
  assert.equal(textFor({ en: 'hi', ru: 'привет' }, 'ru'), 'привет');
  assert.equal(textFor({ en: 'hi' }, 'ru'), 'hi');
  assert.equal(textFor('same', 'ru'), 'same');
  assert.equal(textFor(undefined, 'en'), '');
});

// ---- the samples and the schema ----------------------------------------------------------------

const ajv = new Ajv2020({ allErrors: true, strict: false });
const validate = ajv.compile(read('../../schemas/edit.schema.json'));

test('the samples are valid edits, and name every file they play', () => {
  for (const name of ['short_catch.json', 'long_shift.json']) {
    const sample = read(`../samples/${name}`);
    assert.ok(validate(sample), `${name}: ${JSON.stringify(validate.errors?.slice(0, 3))}`);
    const files = filesOf(sample as Edit);
    assert.ok(files.length > 3, name);
    assert.ok(files.every((f) => !f.startsWith('/') && !f.includes('..')), name);
  }
  const long = read('../samples/long_shift.json') as Edit;
  const seconds = totalFrames(long) / long.fps;
  assert.ok(seconds >= 110 && seconds <= 130, `the long sample is about two minutes (${seconds.toFixed(1)} s)`);
});

test('the schema turns away what the renderer couldn\'t play', () => {
  const ok = edit([{ duration: 2, visual: colour }]);
  assert.ok(validate(ok));
  const bad = (change: (e: any) => void) => { const e = structuredClone(ok) as any; change(e); return validate(e); };
  assert.equal(bad((e) => { e.scenes[0].visual = { type: 'shot', src: '/Users/x/a.mp4' }; }), false, 'absolute paths');
  assert.equal(bad((e) => { e.scenes[0].visual = { type: 'shot', src: '../a.mp4' }; }), false, 'paths out of the folder');
  assert.equal(bad((e) => { e.scenes[0].overlays = [{ type: 'sticker', src: 'a.png' }]; }), false, 'unknown overlays');
  assert.equal(bad((e) => { e.scenes[0].overlays = [{ type: 'hook', text: 'x', colour: 'red' }]; }), false, 'unknown fields');
  assert.equal(bad((e) => { e.languages = ['de']; }), false, 'languages we have no voice for');
  assert.equal(bad((e) => { e.format = '4:3'; }), false, 'other shapes');
  assert.equal(bad((e) => { e.voice = { en: [{ src: 'v.wav', at: 0 }] }; }), false, 'a voice clip without its length');
});
