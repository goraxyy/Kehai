# Kehai video editor

One [Remotion](https://www.remotion.dev) composition that plays an **edit**: a JSON file describing a
video (scenes, shots, text, voice, music), written to `../schemas/edit.schema.json`. Claude writes
and revises edits (Phase 6); this renders them. Remotion is free for individuals and companies of up
to three people.

```bash
npm install                     # once (also brings the ffmpeg render_shot.sh uses)
npm run studio                  # preview samples/short_catch.json in the browser
node render.mjs samples/short_catch.json            # → ~/TokenLimit/marketing/drafts/<id>.<lang>.mp4
node render.mjs <edit.json> --lang ru --scale 0.5 --frames 0-150 --dry-run
npm test && npm run typecheck
```

Files in an edit are paths relative to the marketing working folder (`~/TokenLimit/marketing`, or
`KEHAI_MARKETING`): `shots/…` from `render_shot.sh`, `audio/…` voice, `assets/…` from the library
(`../add_asset.py`). Nothing media goes in the repo. Edits are usually written by the Python steps
(`../README.md`): Claude drafts, the code turns the draft into an edit.

## An edit

- `format` `9:16` (1080×1920) or `16:9` (1920×1080); `fps`; `languages` (`en`, `ru`): one video each.
- `scenes` play in order; a scene's `transition` (fade, slide, wipe, flip, clock) overlaps it with
  the one before. Each scene has:
  - a `visual`:
    - a `shot` with `trim`, a `speed` (a number, or keys it ramps between), `zoom` keys (scale and
      the point zoomed into, eased: zooms and pans), `volume`, `label`;
    - or a `split` of two shots (`row` or `column`);
    - or an `image` (zoomable);
    - or a `color`.
  - a `pip`: picture in picture, for example Karen's mind rendered alone with alpha
    (`render_shot.sh -alpha … -out x.webm`, `transparent: true`).
  - `overlays` with `from`/`to` in scene seconds:
    - `hook`, `label`, `lowerThird`;
    - `image`, `gif`, `lottie`;
    - `arrow`, `circle` (they draw themselves; with `follow` keys they move with someone, placed
      from the shot's track by the Python steps);
    - `meme` (`pov`, `top-bottom`, `nobody`, `caption-bar`, `expectation-reality`).
  - `sfx` at scene seconds.
- `voice` per language: clips on the video's clock with their `duration` and word timings. They
  make the word-timed captions (`captions.mode` `words` or `lines`), and the `music` ducks under
  them. `script` is the voice-over as written; `../voice.py` speaks it into `voice` (the renderer
  ignores it).
- `endCard`: the name, 気配, the tagline, a call to action, the handles from `../brand.json`.
- Text is a string, or `{ "en": …, "ru": … }`. `*Starred words*` come out in crimson.

## The look

Everything comes from `../brand.json`:

- **Colours:** crimson `#DC143C`, soft black `#151518` and white.
- **Fonts:** Nunito (English and Russian) and M PLUS Rounded 1c (only the glyphs 気配 and カレン),
  loaded from Google Fonts at render time.
- **Vertical video:** text stays inside the safe area, clear of the platforms' buttons.

## Code

| | |
|---|---|
| `src/lib/timeline.ts` | the arithmetic: scene starts, transitions, speed-ramp pieces, zoom easing, music ducking, caption grouping (tested in `test/`) |
| `src/EditVideo.tsx` | the composition: scenes, voice, music, captions, end card |
| `src/components/` | visuals, picture in picture, overlays and memes, captions, end card |
| `render.mjs` | validation, file and length checks, the heavy-job lock, bundling, one MP4 per language |
