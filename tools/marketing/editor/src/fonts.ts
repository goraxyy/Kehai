// The brand's soft, rounded fonts, fetched at render time (never committed). Nunito for English
// and Russian, from Google Fonts. For Japanese only the glyphs the videos use (気配, カレン) of
// M PLUS Rounded 1c: the whole Japanese font is ~120 files, those few glyphs are one small one.
import { continueRender, delayRender } from 'remotion';
import { loadFont as loadNunito } from '@remotion/google-fonts/Nunito';
import { brand } from './brand.ts';

const nunito = loadNunito('normal', { weights: ['700', '800', '900'], subsets: ['latin', 'latin-ext', 'cyrillic'] });

const JAPANESE_FAMILY = 'Kehai Japanese';
const glyphs = [...new Set(brand.game.japanese + brand.antagonist.japanese)].join('');
const waiting = delayRender('Loading the Japanese glyphs');
fetch(`https://fonts.googleapis.com/css2?family=${encodeURIComponent(brand.fonts.japanese.family)}:wght@${brand.fonts.japanese.weight}&text=${encodeURIComponent(glyphs)}`)
  .then((r) => r.text())
  .then((css) => {
    const url = /url\((https:[^)]+)\)/.exec(css)?.[1];
    if (!url) throw new Error('no font in the response');
    return new FontFace(JAPANESE_FAMILY, `url(${url})`, { weight: String(brand.fonts.japanese.weight) }).load();
  })
  .then((face) => {
    (document.fonts as unknown as { add(face: FontFace): void }).add(face);
    continueRender(waiting);
  })
  .catch((e) => {
    console.warn(`The Japanese glyphs didn't load (${e}); a system font stands in.`);
    continueRender(waiting);
  });

export const FONT = `"${nunito.fontFamily}", "${JAPANESE_FAMILY}", sans-serif`;
export const JAPANESE = `"${JAPANESE_FAMILY}", "${nunito.fontFamily}", sans-serif`;
