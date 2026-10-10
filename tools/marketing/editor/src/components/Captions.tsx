// Word-timed captions from the voice's word timings: a few words at a time, the one being
// said lit up in crimson (or whole lines, with mode "lines").
import React, { useMemo } from 'react';
import { AbsoluteFill, useCurrentFrame, useVideoConfig } from 'remotion';
import { brand } from '../brand.ts';
import { FONT } from '../fonts.ts';
import { captionChunks, chunkAt, spokenWords } from '../lib/timeline.ts';
import type { Edit, Language } from '../lib/types.ts';
import { SAFE, outline, useUnit, useVertical } from '../style.tsx';

export const Captions: React.FC<{ edit: Edit; lang: Language }> = ({ edit, lang }) => {
  const frame = useCurrentFrame();
  const { fps, height } = useVideoConfig();
  const unit = useUnit();
  const vertical = useVertical();
  const mode = edit.captions?.mode ?? 'words';
  const chunks = useMemo(
    () => captionChunks(spokenWords(edit.voice?.[lang]), mode === 'lines' ? 12 : edit.captions?.maxWords ?? 4),
    [edit, lang, mode],
  );
  if (mode === 'off') return null;

  const t = frame / fps;
  const chunk = chunkAt(chunks, t);
  if (!chunk) return null;

  const position = edit.captions?.position ?? 'bottom';
  const place: React.CSSProperties =
    position === 'top' ? { justifyContent: 'flex-start', paddingTop: vertical ? height * (SAFE.top + 0.1) : 90 * unit }
    : position === 'middle' ? { justifyContent: 'center' }
    : { justifyContent: 'flex-end', paddingBottom: vertical ? height * SAFE.bottom : 70 * unit };
  const size = (mode === 'lines' ? (vertical ? 58 : 50) : vertical ? 78 : 64) * unit;

  return (
    <AbsoluteFill style={{ alignItems: 'center', ...place }}>
      <div style={{
        maxWidth: vertical ? '86%' : '72%', textAlign: 'center', fontFamily: FONT, fontWeight: 900, fontSize: size, lineHeight: 1.15,
        color: brand.colours.paper, textShadow: outline(5 * unit),
      }}>
        {chunk.words.map((w, i) => {
          const now = mode === 'words' && t >= w.start && t < (chunk.words[i + 1]?.start ?? chunk.end);
          const her = w.speaker === 'karen';
          return (
            <React.Fragment key={i}>
              {i > 0 && ' '}
              <span style={{
                display: 'inline-block',
                color: now ? brand.colours.crimson : her ? brand.colours.game.karen : brand.colours.paper,
                transform: now ? 'scale(1.04)' : 'none',
              }}>{w.text}</span>
            </React.Fragment>
          );
        })}
      </div>
    </AbsoluteFill>
  );
};
