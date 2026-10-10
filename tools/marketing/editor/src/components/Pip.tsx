// Picture in picture: Karen's mind rendered alone (with alpha) floating over the store, or a
// second shot in a framed box, in a corner, with a small tag.
import React from 'react';
import { OffthreadVideo, spring, staticFile, useCurrentFrame, useVideoConfig } from 'remotion';
import { brand } from '../brand.ts';
import { FONT } from '../fonts.ts';
import { textFor } from '../lib/timeline.ts';
import type { Language, Pip } from '../lib/types.ts';
import { Emphasis, SAFE, useUnit, useVertical } from '../style.tsx';

export const PipView: React.FC<{ pip: Pip; lang: Language }> = ({ pip, lang }) => {
  const frame = useCurrentFrame();
  const { fps, width, height } = useVideoConfig();
  const unit = useUnit();
  const vertical = useVertical();
  const corner = pip.corner ?? 'top-right';
  const w = (pip.size ?? 0.38) * width;
  const margin = 28 * unit;
  const top = corner.startsWith('top');
  const left = corner.endsWith('left');
  const inTop = vertical ? height * SAFE.top : margin;
  const inBottom = vertical ? height * SAFE.bottom : margin;
  const grow = spring({ frame, fps, config: { damping: 16, stiffness: 140 } });
  const label = textFor(pip.label, lang);
  // A shot gets a crimson frame; her mind (drawn with alpha) a dark glass panel, so it reads
  // over a busy picture.
  const glass = pip.transparent ?? false;

  return (
    <div style={{
      position: 'absolute', width: w,
      [top ? 'top' : 'bottom']: top ? inTop : inBottom,
      [left ? 'left' : 'right']: margin,
      transform: `scale(${0.6 + 0.4 * grow})`, opacity: grow, transformOrigin: `${left ? 'left' : 'right'} ${top ? 'top' : 'bottom'}`,
      display: 'flex', flexDirection: 'column', alignItems: left ? 'flex-start' : 'flex-end', gap: 8 * unit,
    }}>
      {label && top === false && <Tag text={label} unit={unit} />}
      <div style={{
        width: '100%', borderRadius: 22 * unit, overflow: 'hidden',
        border: `${(glass ? 3 : 5) * unit}px solid ${brand.colours.crimson}`,
        boxShadow: '0 12px 40px rgba(0,0,0,0.5)',
        backgroundColor: glass ? 'rgba(21, 21, 24, 0.66)' : brand.colours.ink,
      }}>
        <OffthreadVideo src={staticFile(pip.src)} trimBefore={Math.round((pip.trim ?? 0) * fps)} transparent={pip.transparent ?? false} muted
          style={{ width: '100%', display: 'block' }} />
      </div>
      {label && top && <Tag text={label} unit={unit} />}
    </div>
  );
};

const Tag: React.FC<{ text: string; unit: number }> = ({ text, unit }) => (
  <div style={{
    padding: `${6 * unit}px ${16 * unit}px`, borderRadius: 999, backgroundColor: brand.colours.crimson, color: brand.colours.paper,
    fontFamily: FONT, fontWeight: 900, fontSize: 38 * unit, whiteSpace: 'nowrap',
  }}>
    <Emphasis text={text} accent={brand.colours.ink} />
  </div>
);
