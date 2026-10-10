// The arithmetic of an edit, apart from React so it can be tested on its own: where each
// scene starts, how long the video is, what a shot plays at a given moment, how loud the
// music is, which caption words are on screen. Seconds in, frames out where Remotion needs
// frames.

import type { Brand, Colour, Edit, FollowKey, Language, Music, Shot, SpeedKey, Text, Transition, VoiceClip, Word, ZoomKey } from './types.ts';

export const SIZES = { '9:16': { width: 1080, height: 1920 }, '16:9': { width: 1920, height: 1080 } } as const;

export const toFrames = (seconds: number, fps: number): number => Math.round(seconds * fps);

// ---- the timeline ----------------------------------------------------------------------------

// A transition's length in frames: none for a cut or the first scene, and never as long as the
// shorter of the two scenes it joins (Remotion needs both to outlast it).
export function transitionFrames(edit: Edit, index: number): number {
  if (index <= 0) return 0;
  const t: Transition | undefined = edit.scenes[index].transition;
  if (!t || t.type === 'cut') return 0;
  const want = toFrames(t.duration ?? 0.4, edit.fps);
  const room = Math.min(sceneFrames(edit, index - 1), sceneFrames(edit, index)) - 1;
  return Math.max(0, Math.min(want, room));
}

export const sceneFrames = (edit: Edit, index: number): number => Math.max(1, toFrames(edit.scenes[index].duration, edit.fps));

// The frame each scene starts on: each overlaps the one before by its transition.
export function sceneStarts(edit: Edit): number[] {
  const starts: number[] = [];
  let at = 0;
  edit.scenes.forEach((_, i) => {
    at -= transitionFrames(edit, i);
    starts.push(at);
    at += sceneFrames(edit, i);
  });
  return starts;
}

export function scenesEnd(edit: Edit): number {
  const starts = sceneStarts(edit);
  const last = edit.scenes.length - 1;
  return starts[last] + sceneFrames(edit, last);
}

export const endCardFrames = (edit: Edit): number => (edit.endCard ? toFrames(edit.endCard.duration, edit.fps) : 0);

export const totalFrames = (edit: Edit): number => scenesEnd(edit) + endCardFrames(edit);

// ---- words -------------------------------------------------------------------------------------

export function textFor(text: Text | undefined, lang: Language): string {
  if (text === undefined) return '';
  if (typeof text === 'string') return text;
  return text[lang] ?? text.en ?? Object.values(text)[0] ?? '';
}

export function colour(c: Colour | undefined, brand: Brand, fallback: string): string {
  if (!c) return fallback;
  if (c.startsWith('#')) return c;
  const palette: Record<string, string> = { ...brand.colours, ...brand.colours.game } as unknown as Record<string, string>;
  return palette[c] ?? fallback;
}

// ---- shots ---------------------------------------------------------------------------------------

export interface Segment {
  from: number;        // seconds into the scene
  to: number;
  rate: number;
  source: number;      // seconds into the file where this piece starts
}

// A shot as constant-speed pieces: one for a constant speed, a run of short steps along a ramp
// (the rate eases linearly from key to key, and each step plays at its average rate).
export function speedSegments(shot: Shot, duration: number, step = 0.2): Segment[] {
  const trim = shot.trim ?? 0;
  const speed = shot.speed ?? 1;
  if (typeof speed === 'number') return [{ from: 0, to: duration, rate: speed, source: trim }];

  const keys: SpeedKey[] = [...speed].sort((a, b) => a.at - b.at);
  const rateAt = (t: number): number => {
    if (t <= keys[0].at) return keys[0].rate;
    for (let i = 0; i + 1 < keys.length; i++) {
      const a = keys[i], b = keys[i + 1];
      if (t <= b.at) return a.rate + (b.rate - a.rate) * ((t - a.at) / Math.max(1e-6, b.at - a.at));
    }
    return keys[keys.length - 1].rate;
  };

  // Steady between keys of the same rate (and before the first, after the last); short steps
  // along a ramp, each at its average rate. Neighbours at the same rate merge.
  const points = [0, ...keys.map((k) => k.at).filter((t) => t > 0 && t < duration), duration];
  const segments: Segment[] = [];
  let source = trim;
  const push = (from: number, to: number, rate: number) => {
    const last = segments[segments.length - 1];
    if (last && Math.abs(last.rate - rate) < 1e-6) last.to = to;
    else segments.push({ from, to, rate, source });
    source += (to - from) * rate;
  };
  for (let i = 0; i + 1 < points.length; i++) {
    const a = points[i], b = points[i + 1];
    if (b - a < 1e-6) continue;
    if (Math.abs(rateAt(a) - rateAt(b)) < 1e-6) {
      push(a, b, rateAt(a));
      continue;
    }
    const n = Math.max(1, Math.ceil((b - a) / step));
    for (let k = 0; k < n; k++) {
      const s = a + ((b - a) * k) / n, e = a + ((b - a) * (k + 1)) / n;
      push(s, e, (rateAt(s) + rateAt(e)) / 2);
    }
  }
  return segments;
}

// How many seconds of the file a shot plays through in a scene of this length.
export function sourceSeconds(shot: Shot, duration: number): number {
  const segs = speedSegments(shot, duration);
  const last = segs[segs.length - 1];
  return last.source + (last.to - last.from) * last.rate - (shot.trim ?? 0);
}

// Where a following mark points at t (seconds since it appeared): straight lines between keys,
// still before the first and after the last; the fixed point when there are no keys.
export function followAt(keys: FollowKey[] | undefined, t: number, fixed: { x: number; y: number }): { x: number; y: number } {
  if (!keys || keys.length === 0) return fixed;
  if (t <= keys[0].at) return { x: keys[0].x, y: keys[0].y };
  for (let i = 0; i + 1 < keys.length; i++) {
    const a = keys[i], b = keys[i + 1];
    if (t <= b.at) {
      const u = (t - a.at) / Math.max(1e-6, b.at - a.at);
      return { x: a.x + (b.x - a.x) * u, y: a.y + (b.y - a.y) * u };
    }
  }
  const last = keys[keys.length - 1];
  return { x: last.x, y: last.y };
}

export interface Framing { scale: number; x: number; y: number }

const smooth = (u: number): number => u * u * (3 - 2 * u);

// The zoom at t: still at the first and last key, eased between keys.
export function zoomAt(keys: ZoomKey[] | undefined, t: number): Framing {
  if (!keys || keys.length === 0) return { scale: 1, x: 0.5, y: 0.5 };
  const sorted = [...keys].sort((a, b) => a.at - b.at);
  const f = (k: ZoomKey): Framing => ({ scale: k.scale, x: k.x ?? 0.5, y: k.y ?? 0.5 });
  if (t <= sorted[0].at) return f(sorted[0]);
  for (let i = 0; i + 1 < sorted.length; i++) {
    const a = f(sorted[i]), b = f(sorted[i + 1]);
    if (t <= sorted[i + 1].at) {
      const u = smooth((t - sorted[i].at) / Math.max(1e-6, sorted[i + 1].at - sorted[i].at));
      return { scale: a.scale + (b.scale - a.scale) * u, x: a.x + (b.x - a.x) * u, y: a.y + (b.y - a.y) * u };
    }
  }
  return f(sorted[sorted.length - 1]);
}

// ---- sound ---------------------------------------------------------------------------------------

export const DUCK_SECONDS = 0.25;   // how fast the music dips under a voice and comes back
// Everything is mixed 3 dB down so the sum never clips before mastering (render.mjs brings the
// finished video up to -14 LUFS).
export const HEADROOM = 0.7;
export const GAME_VOLUME = 0.7;     // the game's own sound in a shot, unless the edit says
export const GAME_DUCK = 0.35;      // …and how much of it is left while someone speaks

// How much someone is speaking at t seconds into the video: 1 during a clip, 0 well clear of
// any, with a short ramp either side.
export function speaking(voice: VoiceClip[], t: number): number {
  let s = 0;
  for (const c of voice) {
    const inside = Math.min(t - (c.at - DUCK_SECONDS), c.at + c.duration + DUCK_SECONDS - t);
    if (inside > 0) s = Math.max(s, Math.min(1, inside / DUCK_SECONDS));
  }
  return s;
}

// The music's volume at t: faded in and out, and dipped while anyone speaks.
export function musicVolume(music: Music, voice: VoiceClip[], t: number, total: number): number {
  const full = music.volume ?? 0.35;
  const duck = music.duck ?? 0.12;
  let v = full + (duck - full) * speaking(voice, t);
  const fadeIn = music.fadeIn ?? 0.5, fadeOut = music.fadeOut ?? 1.5;
  if (fadeIn > 0) v *= Math.min(1, Math.max(0, t / fadeIn));
  if (fadeOut > 0) v *= Math.min(1, Math.max(0, (total - t) / fadeOut));
  return Math.max(0, v);
}

// A shot's own sound at t: its volume, lowered under the voice.
export function gameVolume(volume: number | undefined, voice: VoiceClip[], t: number): number {
  const v = volume ?? GAME_VOLUME;
  return v * (1 - (1 - GAME_DUCK) * speaking(voice, t));
}

// ---- captions ------------------------------------------------------------------------------------

export interface TimedWord extends Word { speaker: 'narrator' | 'karen' }
export interface Chunk { start: number; end: number; words: TimedWord[] }

// Every spoken word on the video's clock, in order.
export function spokenWords(voice: VoiceClip[] | undefined): TimedWord[] {
  const words: TimedWord[] = [];
  for (const c of voice ?? [])
    for (const w of c.words ?? [])
      words.push({ text: w.text, start: c.at + w.start, end: c.at + w.end, speaker: c.speaker ?? 'narrator' });
  return words.sort((a, b) => a.start - b.start);
}

// Words grouped for the screen: at most `maxWords` at a time, a new group after a pause, at the
// end of a sentence, or when the speaker changes. A group stays up until the next one starts
// (or a moment after its last word, if a pause follows).
export function captionChunks(words: TimedWord[], maxWords: number, pause = 0.45, hold = 0.35): Chunk[] {
  const chunks: Chunk[] = [];
  let current: TimedWord[] = [];
  const flush = () => {
    if (current.length === 0) return;
    chunks.push({ start: current[0].start, end: current[current.length - 1].end + hold, words: current });
    current = [];
  };
  for (const w of words) {
    const prev = current[current.length - 1];
    if (prev && (current.length >= maxWords || w.start - prev.end > pause || /[.!?…]$/.test(prev.text) || prev.speaker !== w.speaker)) flush();
    current.push(w);
  }
  flush();
  for (let i = 0; i + 1 < chunks.length; i++) chunks[i].end = Math.min(chunks[i].end, chunks[i + 1].start);
  return chunks;
}

export function chunkAt(chunks: Chunk[], t: number): Chunk | undefined {
  return chunks.find((c) => t >= c.start && t < c.end);
}

// ---- files ---------------------------------------------------------------------------------------

// Every file an edit plays (for checking they exist before a render).
export function filesOf(edit: Edit, lang?: Language): string[] {
  const files = new Set<string>();
  const shot = (s: Shot) => files.add(s.src);
  for (const scene of edit.scenes) {
    const v = scene.visual;
    if (v.type === 'shot') shot(v);
    else if (v.type === 'split') { shot(v.a); shot(v.b); }
    else if (v.type === 'image') files.add(v.src);
    if (scene.pip) files.add(scene.pip.src);
    for (const o of scene.overlays ?? []) if ('src' in o && typeof o.src === 'string') files.add(o.src);
    for (const s of scene.sfx ?? []) files.add(s.src);
  }
  for (const [l, clips] of Object.entries(edit.voice ?? {}))
    if (!lang || l === lang) for (const c of clips ?? []) files.add(c.src);
  if (edit.music) files.add(edit.music.src);
  return [...files];
}
