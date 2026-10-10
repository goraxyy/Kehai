// The shape of an edit, as schemas/edit.schema.json defines it (the schema is the source of
// truth; these types follow it).

export type Language = 'en' | 'ru';
export type Text = string | Partial<Record<Language, string>>;
export type Colour = 'crimson' | 'ink' | 'inkSoft' | 'paper' | 'mist' | 'karen' | 'you' | 'guess' | string;

export interface ZoomKey { at: number; scale: number; x?: number; y?: number }
export interface SpeedKey { at: number; rate: number }

export interface Shot {
  type: 'shot';
  src: string;
  trim?: number;
  speed?: number | SpeedKey[];
  zoom?: ZoomKey[];
  volume?: number;
  fit?: 'cover' | 'contain';
  label?: Text;
}
export interface Split { type: 'split'; direction?: 'row' | 'column'; ratio?: number; a: Shot; b: Shot }
export interface ImageVisual { type: 'image'; src: string; zoom?: ZoomKey[]; fit?: 'cover' | 'contain' }
export interface ColourVisual { type: 'color'; color?: Colour }
export type Visual = Shot | Split | ImageVisual | ColourVisual;

export interface Pip {
  src: string;
  trim?: number;
  corner?: 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right';
  size?: number;
  transparent?: boolean;
  label?: Text;
  from?: number;
  to?: number;
}

export type TransitionType = 'cut' | 'fade' | 'slide' | 'wipe' | 'flip' | 'clock';
export type Direction = 'from-left' | 'from-right' | 'from-top' | 'from-bottom';
export interface Transition { type: TransitionType; duration?: number; direction?: Direction }

export interface Span { from?: number; to?: number }
export interface Hook extends Span { type: 'hook'; text: Text }
export interface Label extends Span { type: 'label'; text: Text; x: number; y: number; style?: 'crimson' | 'ink' | 'paper'; size?: 's' | 'm' | 'l' }
export interface LowerThird extends Span { type: 'lowerThird'; title: Text; subtitle?: Text }
export interface Picture extends Span {
  type: 'image' | 'gif' | 'lottie'; src: string; x: number; y: number; width: number; rotate?: number; enter?: 'none' | 'pop' | 'fade' | 'slide-up';
}
// A mark that follows someone: where its point is at each moment, seconds from when it appears.
export interface FollowKey { at: number; x: number; y: number }
export interface Arrow extends Span { type: 'arrow'; from_x: number; from_y: number; to_x: number; to_y: number; color?: Colour; curve?: number; follow?: FollowKey[] }
export interface Circle extends Span { type: 'circle'; x: number; y: number; radius: number; color?: Colour; follow?: FollowKey[] }
export interface Meme extends Span {
  type: 'meme'; template: 'pov' | 'top-bottom' | 'nobody' | 'caption-bar' | 'expectation-reality';
  text?: Text; top?: Text; bottom?: Text; speaker?: Text;
}
export type Overlay = Hook | Label | LowerThird | Picture | Arrow | Circle | Meme;

export interface Sfx { src: string; at: number; volume?: number }

export interface Scene {
  id?: string;
  duration: number;
  transition?: Transition;
  visual: Visual;
  pip?: Pip;
  overlays?: Overlay[];
  sfx?: Sfx[];
}

export interface Word { text: string; start: number; end: number }
export interface VoiceClip {
  src: string; at: number; duration: number; speaker?: 'narrator' | 'karen'; volume?: number; words?: Word[];
}
// The voice-over as written; tools/marketing/voice.py speaks it into `voice` (the renderer ignores it).
export interface ScriptLine { id: string; speaker: 'narrator' | 'karen'; at: number; text: Text }
export interface Music { src: string; offset?: number; volume?: number; duck?: number; fadeIn?: number; fadeOut?: number }

export interface Edit {
  version: 1;
  id: string;
  kind: 'short' | 'long';
  format: '9:16' | '16:9';
  fps: 24 | 25 | 30 | 60;
  languages: Language[];
  title?: Text;
  source?: { stem?: string; moments?: number[]; notes?: string };
  scenes: Scene[];
  script?: ScriptLine[];
  voice?: Partial<Record<Language, VoiceClip[]>>;
  music?: Music;
  captions?: { mode?: 'words' | 'lines' | 'off'; position?: 'top' | 'middle' | 'bottom'; maxWords?: number };
  endCard?: { duration: number; cta?: Text; showHandles?: boolean };
}

export interface Brand {
  studio: string;
  game: { name: string; japanese: string; tagline: Record<Language, string> };
  antagonist: { name: string; japanese: string; pronoun: string };
  handles: Record<string, string | null>;
  links: Record<string, string | null>;
  colours: { crimson: string; ink: string; inkSoft: string; paper: string; mist: string; game: { karen: string; you: string; guess: string } };
  fonts: { display: { family: string; weight: number }; body: { family: string; weight: number }; japanese: { family: string; weight: number } };
}

export type Props = { edit: Edit; lang: Language };
