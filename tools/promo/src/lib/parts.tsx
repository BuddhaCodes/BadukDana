// Shared building blocks of the promo: the Mochi clouds, the title card, captions, the crisp camera over a
// real screenshot, the phone frame, Kuro & Shiro and the cursor. All deterministic (no random, no clock).
import React from 'react';
import { AbsoluteFill, Img, interpolate, staticFile, useCurrentFrame, useVideoConfig } from 'remotion';
import { C, CAM, CLAMP, EASE, F, PRESS } from './tokens';

// ---------------------------------------------------------------- clouds

const CLOUDS = [
  { x: 0.1, y: 0.16, s: 0.52, sp: 0.011, ph: 0.0, c: C.pink },
  { x: 0.9, y: 0.2, s: 0.46, sp: 0.009, ph: 1.7, c: C.lav },
  { x: 0.84, y: 0.88, s: 0.58, sp: 0.01, ph: 3.1, c: C.mint },
  { x: 0.16, y: 0.86, s: 0.44, sp: 0.012, ph: 4.4, c: C.butter },
  { x: 0.5, y: 0.5, s: 0.36, sp: 0.008, ph: 2.2, c: '#FBE3EA' },
];

/** Hoshi's Mochi background (the same pastel clouds as the app), drifting very slowly. */
export const Clouds: React.FC<{ dark?: boolean; dim?: number }> = ({ dark = false, dim = 1 }) => {
  const frame = useCurrentFrame();
  const { width, height } = useVideoConfig();
  const t = frame / 30;
  const longer = Math.max(width, height);
  const tints = dark ? ['#3B2F5C', '#4A2B45', '#1F3B45', '#3A3354', '#452F4E'] : CLOUDS.map((c) => c.c);
  return (
    <AbsoluteFill style={{ background: dark ? C.night : C.cream, overflow: 'hidden' }}>
      {CLOUDS.map((cl, i) => {
        const a = t * cl.sp * 2 * Math.PI * 6 + cl.ph;
        const cx = (cl.x + 0.05 * Math.sin(a)) * width;
        const cy = (cl.y + 0.04 * Math.cos(a * 1.3)) * height;
        const r = cl.s * longer;
        return (
          <div
            key={i}
            style={{
              position: 'absolute', left: cx - r, top: cy - r, width: 2 * r, height: 2 * r, borderRadius: '50%',
              background: `radial-gradient(circle, ${tints[i]} 0%, ${tints[i]}00 70%)`, opacity: 0.85 * dim,
            }}
          />
        );
      })}
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- title card (paper-title-card, Mochi skin)

export const TITLE_CARD = 55;

/**
 * paper-title-card adapted to Hoshi: each word presses onto the cream paper (scale 1.28→1, blur 7→0,
 * delay 4 + i·4, 9f, bezier(0.2,0.75,0.3,1)); exactly one accent word in rose; a rose rule closes (16→34f);
 * the card fades out over its last 8f. Fredoka instead of a serif (Hoshi's title face).
 */
export const TitleCard: React.FC<{ words: string[]; accent: number; size?: number; sub?: string }> = ({ words, accent, size = 112, sub }) => {
  const frame = useCurrentFrame();
  const fadeOut = interpolate(frame, [TITLE_CARD - 8, TITLE_CARD], [1, 0], CLAMP);
  const rule = interpolate(frame, [16, 34], [0, 1], { ...CLAMP, easing: CAM });
  const subT = interpolate(frame, [12, 24], [0, 1], CLAMP);
  return (
    <AbsoluteFill style={{ opacity: fadeOut }}>
      <Clouds dim={0.7} />
      <AbsoluteFill style={{ justifyContent: 'center', alignItems: 'center' }}>
        <div style={{ textAlign: 'center', maxWidth: '86%' }}>
          <div
            style={{
              fontFamily: F.title, fontWeight: 600, fontSize: size, lineHeight: 1.12, color: C.ink, letterSpacing: '-0.01em',
              display: 'flex', flexWrap: 'wrap', justifyContent: 'center', columnGap: '0.24em',
            }}
          >
            {words.map((w, i) => {
              const t = interpolate(frame, [4 + i * 4, 13 + i * 4], [0, 1], { ...CLAMP, easing: PRESS });
              return (
                <span
                  key={i}
                  style={{
                    display: 'inline-block', opacity: t, transform: `scale(${1.28 - 0.28 * t})`, filter: `blur(${(1 - t) * 7}px)`,
                    color: i === accent ? C.rose : undefined,
                  }}
                >
                  {w}
                </span>
              );
            })}
          </div>
          <div style={{ height: 8, width: 220, margin: '36px auto 0', borderRadius: 4, background: C.rose, transform: `scaleX(${rule})` }} />
          {sub ? (
            <div style={{ fontFamily: F.ui, fontWeight: 600, fontSize: 40, color: C.ink2, marginTop: 30, opacity: subT }}>{sub}</div>
          ) : null}
        </div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- caption

/** A narration line on a soft cream pill; ≥56px effective height (aesthetic rule Q11). */
export const Caption: React.FC<{ text: string; from: number; to: number; bottom?: number; top?: number; size?: number }> = ({
  text, from, to, bottom = 64, top, size = 58,
}) => {
  const frame = useCurrentFrame();
  const t = interpolate(frame, [from, from + 14], [0, 1], { ...CLAMP, easing: EASE });
  const out = interpolate(frame, [to - 10, to], [1, 0], CLAMP);
  if (frame < from || frame > to) {
    return null;
  }

  return (
    <AbsoluteFill style={{ pointerEvents: 'none', alignItems: 'center', justifyContent: top === undefined ? 'flex-end' : 'flex-start' }}>
      <div
        style={{
          marginBottom: top === undefined ? bottom : 0, marginTop: top ?? 0, opacity: t * out, transform: `translateY(${(1 - t) * 22}px)`,
          fontFamily: F.title, fontWeight: 600, fontSize: size, color: C.ink, background: 'rgba(255,250,243,0.94)',
          padding: '16px 40px 20px', borderRadius: 999, boxShadow: '0 8px 0 rgba(120,90,60,0.10), 0 18px 40px rgba(45,36,64,0.14)',
          maxWidth: '88%', textAlign: 'center', lineHeight: 1.15,
        }}
      >
        {text}
      </div>
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- crisp camera over a screenshot

export type Key = { f: number; cx: number; cy: number; z: number };

const keyAt = (keys: Key[], frame: number) => {
  if (frame <= keys[0].f) {
    return keys[0];
  }

  for (let i = 1; i < keys.length; i++) {
    const a = keys[i - 1];
    const b = keys[i];
    if (frame <= b.f) {
      const u = CAM((frame - a.f) / Math.max(1, b.f - a.f));
      // zoom in log space so pushes feel even
      const z = Math.exp(Math.log(a.z) + (Math.log(b.z) - Math.log(a.z)) * u);
      return { f: frame, cx: a.cx + (b.cx - a.cx) * u, cy: a.cy + (b.cy - a.cy) * u, z };
    }
  }

  return keys[keys.length - 1];
};

/**
 * A real screenshot under a 2D camera. The zoom is applied to the image's layout size (not a transform), so
 * the browser rasterises it at the shown size from the high-resolution source: text stays crisp (rule Q2).
 * z = 1 shows the image's full width; below 1 it floats as a window with rounded corners and a soft shadow.
 */
export const Shot: React.FC<{
  src: string; w: number; h: number; keys: Key[]; radius?: number; frameOffset?: number; opacity?: number;
}> = ({ src, w, h, keys, radius = 22, frameOffset = 0, opacity = 1 }) => {
  const frame = useCurrentFrame() - frameOffset;
  const { width, height } = useVideoConfig();
  const k = keyAt(keys, frame);
  const s = (k.z * width) / w;
  const dw = w * s;
  const dh = h * s;
  const left = width / 2 - k.cx * s;
  const top = height / 2 - k.cy * s;
  const r = radius * Math.min(1.6, s * (w / width));
  return (
    <div
      style={{
        position: 'absolute', left, top, width: dw, height: dh, borderRadius: r, overflow: 'hidden', opacity,
        boxShadow: '0 30px 70px rgba(45,36,64,0.22), 0 6px 18px rgba(45,36,64,0.10)',
      }}
    >
      <Img src={staticFile(src)} style={{ width: dw, height: dh, display: 'block' }} />
    </div>
  );
};

/** Camera state at a frame (for overlays that must follow the camera, e.g. the cursor). */
export const camAt = keyAt;

// ---------------------------------------------------------------- phone

/** An Android phone: a soft plum bezel around a real screenshot (824×1784). */
export const Phone: React.FC<{ src: string; height: number; style?: React.CSSProperties; children?: React.ReactNode }> = ({
  src, height, style, children,
}) => {
  const screenH = height - 28;
  const screenW = (screenH * 824) / 1784;
  return (
    <div
      style={{
        position: 'absolute', width: screenW + 28, height, borderRadius: height * 0.065, background: '#241D33', padding: 14,
        boxSizing: 'border-box', boxShadow: '0 40px 80px rgba(45,36,64,0.28), 0 8px 20px rgba(45,36,64,0.18), inset 0 0 0 2px #3A3150',
        ...style,
      }}
    >
      <div style={{ position: 'relative', width: screenW, height: screenH, borderRadius: height * 0.052, overflow: 'hidden', background: '#000' }}>
        <Img src={staticFile(src)} style={{ width: screenW, height: screenH, display: 'block' }} />
        {children}
      </div>
    </div>
  );
};

// ---------------------------------------------------------------- Kuro & Shiro (Hoshi's own drawing, as on the site)

export const Kuro: React.FC<{ size: number }> = ({ size }) => (
  <svg width={size} height={size} viewBox="0 0 100 100">
    <ellipse cx="50" cy="56" rx="44" ry="40" fill={C.ink} />
    <circle cx="38" cy="56" r="5" fill="#fff" />
    <circle cx="62" cy="56" r="5" fill="#fff" />
    <path d="M42 70 Q50 76 58 70" stroke="#fff" strokeWidth="3" fill="none" strokeLinecap="round" />
    <circle cx="27" cy="66" r="5" fill={C.pink} />
    <circle cx="73" cy="66" r="5" fill={C.pink} />
  </svg>
);

export const Shiro: React.FC<{ size: number }> = ({ size }) => (
  <svg width={size} height={size} viewBox="0 0 100 100">
    <ellipse cx="50" cy="56" rx="44" ry="40" fill="#fff" stroke={C.ink} strokeWidth="3" />
    <path d="M33 56h10" stroke={C.ink} strokeWidth="3.4" strokeLinecap="round" />
    <circle cx="62" cy="56" r="4.6" fill={C.ink} />
    <path d="M42 70 Q50 75 58 70" stroke={C.ink} strokeWidth="3" fill="none" strokeLinecap="round" />
    <circle cx="27" cy="66" r="5" fill={C.pink} />
    <circle cx="73" cy="66" r="5" fill={C.pink} />
  </svg>
);

// ---------------------------------------------------------------- cursor (cursor-flyover)

/** The cursor-flyover pointer with its click ripple (26px ring 0.3→1.9, fading over ~10f). */
export const Cursor: React.FC<{ x: number; y: number; click: number }> = ({ x, y, click }) => (
  <>
    {click > 0 && click < 1 ? (
      <div
        style={{
          position: 'absolute', left: x, top: y, width: 34, height: 34, borderRadius: '50%', border: `3px solid ${C.rose}`,
          transform: `translate(-50%,-50%) scale(${0.3 + click * 1.6})`, opacity: (1 - click) * 0.9,
        }}
      />
    ) : null}
    <svg
      viewBox="0 0 24 24"
      style={{ position: 'absolute', left: x, top: y, width: 40, height: 40, filter: 'drop-shadow(0 4px 6px rgba(45,36,64,.35))' }}
    >
      <path d="M4 2 L4 19 L9 14.4 L12.2 21.5 L15.4 20 L12.2 13 L19 12.6 Z" fill={click > 0 && click < 0.4 ? C.pink : '#fff'} stroke={C.ink} strokeWidth={1.3} />
    </svg>
  </>
);

// ---------------------------------------------------------------- wordmark

/** "Hoshi 星": letters press in one after another (outro letterpress: delay i·1.8, 8f). */
export const Wordmark: React.FC<{ at: number; size: number }> = ({ at, size }) => {
  const frame = useCurrentFrame();
  const letters = 'Hoshi'.split('');
  const star = interpolate(frame, [at + 12, at + 24], [0, 1], { ...CLAMP, easing: PRESS });
  return (
    <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'center', gap: size * 0.28 }}>
      <div style={{ display: 'flex', fontFamily: F.title, fontWeight: 600, fontSize: size, color: C.ink, letterSpacing: '-0.01em' }}>
        {letters.map((ch, i) => {
          const d = Math.round(at + i * 1.8);
          const t = interpolate(frame, [d, d + 8], [0, 1], { ...CLAMP, easing: PRESS });
          return (
            <span key={i} style={{ display: 'inline-block', opacity: t, transform: `translateY(${(1 - t) * 26}px) scale(${1.35 - 0.35 * t})`, filter: `blur(${(1 - t) * 8}px)` }}>
              {ch}
            </span>
          );
        })}
      </div>
      <span
        style={{
          fontFamily: F.brand, fontWeight: 700, fontSize: size * 0.9, color: C.rose, display: 'inline-block', opacity: star,
          transform: `scale(${1.3 - 0.3 * star}) rotate(${(1 - star) * -8}deg)`, filter: `blur(${(1 - star) * 6}px)`,
        }}
      >
        星
      </span>
    </div>
  );
};
