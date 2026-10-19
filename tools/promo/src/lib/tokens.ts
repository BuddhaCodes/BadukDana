// Design spec of the Hoshi promo (autonomous free creation, video-shotcraft pipeline).
// Everything comes from Hoshi's own Mochi theme (src/Hoshi.App/Themes/HoshiTheme.cs) and its website:
// cream paper, plum ink, a rose accent, pastel clouds, Fredoka titles, Manrope text, Shippori Mincho for 星.
// Motion personality: "friendly / calm" preset (~26f main moves, soft ease, small real overshoot on landings).
import { Easing } from 'remotion';

export const FPS = 30;

export const C = {
  cream: '#F8F1E7',
  bar: '#FFFAF3',
  card: '#FFFFFF',
  ink: '#2D2440',
  ink2: '#7B6F88',
  rose: '#C8577A',
  pink: '#F6CFDB',
  mint: '#D3ECE2',
  lav: '#E2DCF5',
  butter: '#F6E8BD',
  border: '#EBDDCC',
  night: '#1C1828',
};

export const F = {
  title: 'Fredoka, sans-serif',
  ui: 'Manrope, sans-serif',
  brand: 'Shippori, serif',
  mono: 'JetBrains, monospace',
};

/** Main move: soft and friendly. */
export const EASE = Easing.bezier(0.25, 0.46, 0.45, 0.94);
/** Camera moves: slow in, slow out. */
export const CAM = Easing.bezier(0.45, 0, 0.25, 1);
/** Landings that should bounce a little: y1 > 1 (aesthetic rule, the tuned outro value). */
export const LAND = Easing.bezier(0.34, 1.4, 0.44, 1);
/** Letterpress word entrance (paper-title-card). */
export const PRESS = Easing.bezier(0.2, 0.75, 0.3, 1);

export const CLAMP = { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' } as const;
