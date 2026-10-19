// HoshiPromo — 16:9, 1920×1080 @ 30fps. One timeline (SHOTS) drives the scenes, the captions and the sound.
import React from 'react';
import { AbsoluteFill, Audio, Img, Sequence, interpolate, staticFile, useCurrentFrame } from 'remotion';
import { C, CAM, CLAMP, EASE, F, LAND, PRESS } from '../lib/tokens';
import { useFonts } from '../lib/fonts';
import { Caption, Clouds, Cursor, Kuro, Phone, Shiro, Shot, TitleCard, TITLE_CARD, Wordmark, camAt, type Key } from '../lib/parts';

const DESK = { w: 2560, h: 1440 };
const pad3 = (n: number) => String(n).padStart(3, '0');

// ---------------------------------------------------------------- timeline (the only source of truth)

const LEN = {
  open: 150,
  t1: TITLE_CARD,
  play: 190,
  t2: TITLE_CARD,
  review: 240,
  t3: TITLE_CARD,
  study: 130,
  themes: 220,
  t4: TITLE_CARD,
  phones: 280,
  outro: 190,
};
type Name = keyof typeof LEN;
export const SHOTS = (() => {
  const out = {} as Record<Name, { from: number; dur: number }>;
  let at = 0;
  for (const k of Object.keys(LEN) as Name[]) {
    out[k] = { from: at, dur: LEN[k] };
    at += LEN[k];
  }
  return out;
})();
export const TOTAL = Object.values(LEN).reduce((a, b) => a + b, 0);

// ---------------------------------------------------------------- 1. open: Kuro & Shiro land, the wordmark presses in

const Open: React.FC = () => {
  const frame = useCurrentFrame();
  const land = (cue: number) => interpolate(frame, [cue, cue + 16], [0, 1], { ...CLAMP, easing: LAND });
  const squash = (cue: number) => {
    const u = interpolate(frame, [cue + 14, cue + 18, cue + 26], [0, 1, 0], CLAMP);
    return { sx: 1 + 0.08 * u, sy: 1 - 0.1 * u };
  };
  const k = land(10);
  const s = land(20);
  const ks = squash(10);
  const ss = squash(20);
  const rule = interpolate(frame, [70, 84], [0, 1], { ...CLAMP, easing: CAM });
  const tag = interpolate(frame, [80, 94], [0, 1], { ...CLAMP, easing: EASE });
  const out = interpolate(frame, [LEN.open - 10, LEN.open], [1, 0], CLAMP);
  return (
    <AbsoluteFill style={{ opacity: out }}>
      <Clouds />
      <AbsoluteFill style={{ alignItems: 'center', justifyContent: 'center' }}>
        <div style={{ display: 'flex', gap: 10, alignItems: 'flex-end', height: 170, marginBottom: 6 }}>
          <div style={{ transform: `translateY(${(1 - k) * -520}px) scale(${ks.sx},${ks.sy})`, transformOrigin: '50% 100%', opacity: k > 0 ? 1 : 0 }}>
            <Kuro size={168} />
          </div>
          <div style={{ transform: `translateY(${(1 - s) * -520}px) scale(${ss.sx},${ss.sy})`, transformOrigin: '50% 100%', opacity: s > 0 ? 1 : 0 }}>
            <Shiro size={136} />
          </div>
        </div>
        <Wordmark at={40} size={170} />
        <div style={{ height: 8, width: 240, borderRadius: 4, background: C.rose, transform: `scaleX(${rule})`, margin: '26px auto 0' }} />
        <div style={{ fontFamily: F.title, fontWeight: 500, fontSize: 52, color: C.ink2, marginTop: 26, opacity: tag, transform: `translateY(${(1 - tag) * 16}px)` }}>
          Play and study Go, beautifully.
        </div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 3. the game unfolds on the desktop

const PLAY_MOVES = 24;
const PLAY_STEP = 6; // frames per move: a calm, followable pace (rule R3)
const PLAY_START = 24;

const DeskPlay: React.FC = () => {
  const frame = useCurrentFrame();
  const raw = (frame - PLAY_START) / PLAY_STEP;
  const i = Math.max(0, Math.min(PLAY_MOVES, Math.floor(raw)));
  const fadeNew = i < PLAY_MOVES ? interpolate(raw - i, [0, 0.5], [0, 1], CLAMP) : 0;
  const keys: Key[] = [
    { f: 0, cx: 1280, cy: 720, z: 0.8 },
    { f: LEN.play, cx: 1250, cy: 720, z: 0.9 },
  ];
  const enter = interpolate(frame, [0, 20], [0, 1], { ...CLAMP, easing: EASE });
  return (
    <AbsoluteFill>
      <Clouds />
      <AbsoluteFill style={{ opacity: enter, transform: `translateY(${(1 - enter) * 40}px)` }}>
        <Shot src={`shots/desk-seq/${pad3(i)}.png`} {...DESK} keys={keys} />
        {fadeNew > 0 ? <Shot src={`shots/desk-seq/${pad3(i + 1)}.png`} {...DESK} keys={keys} opacity={fadeNew} /> : null}
      </AbsoluteFill>
      <Caption text="A real kaya board, and stones that settle with a click." from={30} to={LEN.play - 4} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 5. review: cursor-flyover over the real review screen

const REVIEW_KEYS: Key[] = [
  { f: 0, cx: 1280, cy: 720, z: 0.82 },
  { f: 30, cx: 1280, cy: 720, z: 0.86 },
  { f: 54, cx: 1580, cy: 560, z: 1.55 }, // KataGo's suggestion on the board
  { f: 100, cx: 1600, cy: 560, z: 1.58 },
  { f: 124, cx: 2180, cy: 380, z: 1.7 }, // biggest mistakes
  { f: 168, cx: 2190, cy: 380, z: 1.72 },
  { f: 192, cx: 420, cy: 520, z: 1.6 }, // graph and the graded move list
  { f: LEN.review, cx: 430, cy: 520, z: 1.62 },
];
const STOPS = [
  { at: 54, x: 1672, y: 602 },
  { at: 124, x: 2470, y: 318 },
  { at: 192, x: 80, y: 760 },
];

const Review: React.FC = () => {
  const frame = useCurrentFrame();
  const k = camAt(REVIEW_KEYS, frame);
  const s = (k.z * 1920) / DESK.w;
  // the cursor flies with the camera (one system) towards the next stop
  let px = 1280;
  let py = 900;
  let click = 0;
  for (let j = 0; j < STOPS.length; j++) {
    const from = j === 0 ? { x: 1280, y: 900 } : STOPS[j - 1];
    const startF = j === 0 ? 30 : STOPS[j - 1].at + 24;
    const u = interpolate(frame, [startF, STOPS[j].at], [0, 1], { ...CLAMP, easing: CAM });
    if (frame >= startF) {
      px = from.x + (STOPS[j].x - from.x) * u;
      py = from.y + (STOPS[j].y - from.y) * u;
    }

    const c = interpolate(frame, [STOPS[j].at, STOPS[j].at + 12], [0, 1], CLAMP);
    if (c > 0 && c < 1) {
      click = c;
    }
  }

  const sx = 960 + (px - k.cx) * s;
  const sy = 540 + (py - k.cy) * s;
  const cursorIn = interpolate(frame, [26, 36], [0, 1], CLAMP);
  return (
    <AbsoluteFill>
      <Clouds />
      <Shot src="shots/desk/review-mochi.png" {...DESK} keys={REVIEW_KEYS} />
      <div style={{ position: 'absolute', inset: 0, opacity: cursorIn }}>
        <Cursor x={sx} y={sy} click={click} />
      </div>
      <Caption text="See the move KataGo would play." from={56} to={112} />
      <Caption text="Your biggest mistakes, one click away." from={126} to={182} />
      <Caption text="Every move graded, every swing graphed." from={194} to={LEN.review - 2} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 7. study: drawings on the board

const Study: React.FC = () => {
  const keys: Key[] = [
    { f: 0, cx: 1280, cy: 720, z: 0.9 },
    { f: 60, cx: 860, cy: 640, z: 1.42 },
    { f: LEN.study, cx: 850, cy: 640, z: 1.46 },
  ];
  return (
    <AbsoluteFill>
      <Clouds />
      <Shot src="shots/desk/study-mochi.png" {...DESK} keys={keys} />
      <Caption text="Arrows, areas and labels, kept with the game." from={20} to={LEN.study - 2} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 8. themes: one sweep (theme-switch A), then the wall of ten

const THEMES = ['mochi', 'matcha', 'yuzu', 'kuromochi', 'night', 'kaya', 'sumi', 'zen', 'minimal', 'contrast'];
const SLANT = 1080 * Math.tan((15 * Math.PI) / 180);

const Themes: React.FC = () => {
  const frame = useCurrentFrame();
  // A: the dark version is revealed behind a 15° edge, fast then slow (out poly3 ~38f), then settles 0.985→1
  const p = interpolate(frame, [14, 52], [-20, 1920 + SLANT + 40], { ...CLAMP, easing: (x) => 1 - Math.pow(1 - x, 3) });
  const settle = interpolate(frame, [52, 53, 64], [1, 1.015, 1], { ...CLAMP, easing: EASE });
  const lineOp = interpolate(frame, [14, 18, 48, 54], [0, 1, 1, 0], CLAMP);
  // then the camera pulls back: the dark screen becomes one tile of the wall
  const pull = interpolate(frame, [84, 130], [0, 1], { ...CLAMP, easing: CAM });
  const cols = 5;
  const gap = 26;
  const tileW = (1920 - 120 - gap * (cols - 1)) / cols;
  const tileH = (tileW * 9) / 16;
  const gridTop = (1080 - (2 * tileH + gap)) / 2;
  const idx = THEMES.indexOf('kuromochi');
  const tx = 60 + (idx % cols) * (tileW + gap);
  const ty = gridTop + Math.floor(idx / cols) * (tileH + gap);
  // camera mapping the kuromochi tile to the full frame at pull = 0 (16:9 tile → exactly fills 1920×1080);
  // applied to each tile's layout size (not a transform) so the screenshots stay crisp while the camera pulls back
  const scale0 = 1920 / tileW;
  const S = Math.exp(Math.log(scale0) * (1 - pull));
  return (
    <AbsoluteFill>
      <Clouds />
      {frame < 84 ? (
        <AbsoluteFill style={{ transform: `scale(${settle})` }}>
          <Img src={staticFile('shots/desk/theme-mochi.png')} style={{ width: 1920, height: 1080 }} />
          <div style={{ position: 'absolute', inset: 0, clipPath: `polygon(0 0, ${p}px 0, ${p - SLANT}px 1080px, 0 1080px)` }}>
            <Img src={staticFile('shots/desk/theme-kuromochi.png')} style={{ width: 1920, height: 1080 }} />
          </div>
          {lineOp > 0 ? (
            <div
              style={{
                position: 'absolute', left: p - SLANT / 2 - 2, top: -80, width: 4, height: 1240, background: '#fff',
                boxShadow: `0 0 18px 4px ${C.pink}`, transform: 'rotate(15deg)', opacity: lineOp,
              }}
            />
          ) : null}
        </AbsoluteFill>
      ) : (
        <AbsoluteFill>
          {THEMES.map((t, i) => {
            const x = 60 + (i % cols) * (tileW + gap);
            const y = gridTop + Math.floor(i / cols) * (tileH + gap);
            const appear = t === 'kuromochi' ? 1 : interpolate(frame, [96 + i * 2, 112 + i * 2], [0, 1], { ...CLAMP, easing: EASE });
            return (
              <div
                key={t}
                style={{
                  position: 'absolute', left: (x - tx) * S + tx * pull, top: (y - ty) * S + ty * pull, width: tileW * S, height: tileH * S, borderRadius: 14 * S,
                  overflow: 'hidden', opacity: appear, boxShadow: '0 18px 40px rgba(45,36,64,0.2)',
                }}
              >
                <Img src={staticFile(`shots/desk/theme-${t}.png`)} style={{ width: tileW * S, height: tileH * S, display: 'block' }} />
              </div>
            );
          })}
        </AbsoluteFill>
      )}
      <Caption text="Light or dark, in a sweep." from={20} to={80} />
      <Caption text="Ten themes, from Mochi to Pole star." from={128} to={LEN.themes - 2} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 10. phones

const PHONE_MOVES = 30;
const PHONE_STEP = 5;
const PHONE_START = 36;

const Phones: React.FC = () => {
  const frame = useCurrentFrame();
  const rise = (cue: number) => interpolate(frame, [cue, cue + 26], [0, 1], { ...CLAMP, easing: LAND });
  const c = rise(0);
  const l = rise(8);
  const r = rise(14);
  const raw = (frame - PHONE_START) / PHONE_STEP;
  const i = Math.max(0, Math.min(PHONE_MOVES, Math.floor(raw)));
  const showConfirm = frame >= 206;
  const confirmIn = interpolate(frame, [206, 216], [0, 1], CLAMP);
  const H = 900;
  const W = ((H - 28) * 824) / 1784 + 28;
  return (
    <AbsoluteFill>
      <Clouds />
      <Phone
        src="shots/phone/analysis-night.png"
        height={H * 0.86}
        style={{ left: 960 - W * 0.86 / 2 - 470, top: 540 - (H * 0.86) / 2 + 40 + (1 - l) * 900, transform: 'rotate(-4deg)' }}
      />
      <Phone
        src="shots/phone/study-kuromochi.png"
        height={H * 0.86}
        style={{ left: 960 - W * 0.86 / 2 + 470, top: 540 - (H * 0.86) / 2 + 40 + (1 - r) * 900, transform: 'rotate(4deg)' }}
      />
      <Phone src={`shots/phone-seq-mochi/${pad3(i)}.png`} height={H} style={{ left: 960 - W / 2, top: 540 - H / 2 + 10 + (1 - c) * 900 }}>
        {showConfirm ? (
          <Img src={staticFile('shots/phone/confirm-mochi.png')} style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', opacity: confirmIn }} />
        ) : null}
      </Phone>
      <Caption text="The same Hoshi, on Android." from={40} to={150} top={20} />
      <Caption text="Tap to aim, tap again to play." from={160} to={LEN.phones - 2} top={20} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- 11. outro: group photo around the wordmark

type Piece = { key: string; src: string; w: number; h: number; sx: number; sy: number; sw: number; sh: number; cx: number; cy: number; scale: number; rot: number; dx: number; dy: number; cue: number; phone?: boolean };

const PIECES: Piece[] = [
  // crops of the real screenshots (sx/sy/sw/sh in source pixels)
  { key: 'board', src: 'shots/desk/theme-mochi.png', w: 2560, h: 1440, sx: 640, sy: 104, sw: 1240, sh: 1240, cx: 330, cy: 330, scale: 0.36, rot: -5, dx: -520, dy: -160, cue: 4 },
  { key: 'mistakes', src: 'shots/desk/review-mochi.png', w: 2560, h: 1440, sx: 2200, sy: 290, sw: 350, sh: 190, cx: 1600, cy: 250, scale: 1.05, rot: 4, dx: 520, dy: -200, cue: 7 },
  { key: 'phone', src: 'shots/phone/home-mochi.png', w: 824, h: 1784, sx: 0, sy: 0, sw: 824, sh: 1784, cx: 1640, cy: 720, scale: 0.3, rot: 5, dx: 480, dy: 260, cue: 10, phone: true },
  { key: 'palette', src: 'shots/desk/study-mochi.png', w: 2560, h: 1440, sx: 18, sy: 490, sw: 72, sh: 470, cx: 120, cy: 760, scale: 0.75, rot: -3, dx: -420, dy: 220, cue: 13 },
  { key: 'night', src: 'shots/desk/theme-night.png', w: 2560, h: 1440, sx: 0, sy: 0, sw: 2560, sh: 1440, cx: 560, cy: 860, scale: 0.17, rot: 3, dx: -300, dy: 340, cue: 16 },
  { key: 'graph', src: 'shots/desk/review-mochi.png', w: 2560, h: 1440, sx: 0, sy: 80, sw: 330, sh: 200, cx: 1340, cy: 900, scale: 1.0, rot: -2.5, dx: 300, dy: 330, cue: 19 },
  { key: 'kuro', src: 'shots/desk/theme-kuromochi.png', w: 2560, h: 1440, sx: 0, sy: 0, sw: 2560, sh: 1440, cx: 1250, cy: 170, scale: 0.13, rot: -3, dx: 160, dy: -320, cue: 22 },
];

const Outro: React.FC = () => {
  const frame = useCurrentFrame();
  const crane = interpolate(frame, [0, 40], [0, 1], { ...CLAMP, easing: CAM });
  const push = interpolate(frame, [40, LEN.outro], [0, 1], CLAMP);
  const camScale = 1.06 - 0.06 * crane + 0.03 * push;
  const recede = interpolate(frame, [42, 50], [0, 1], CLAMP);
  const stage = interpolate(frame, [42, 50, 58], [0, 0.55, 0.3], CLAMP);
  const rule = interpolate(frame, [62, 74], [0, 1], { ...CLAMP, easing: CAM });
  const tag = interpolate(frame, [72, 86], [0, 1], { ...CLAMP, easing: EASE });
  const url = interpolate(frame, [82, 96], [0, 1], { ...CLAMP, easing: EASE });
  return (
    <AbsoluteFill>
      <Clouds />
      <AbsoluteFill style={{ transform: `perspective(1400px) rotateX(${4 * (1 - crane)}deg) scale(${camScale})`, transformOrigin: '50% 45%' }}>
        {PIECES.map((p) => {
          if (frame < p.cue) {
            return null;
          }

          const t = interpolate(frame, [p.cue, p.cue + 12], [0, 1], { ...CLAMP, easing: LAND });
          const op = interpolate(frame, [p.cue, p.cue + 3], [0, 1], CLAMP) * (1 - 0.14 * recede);
          const dw = p.sw * p.scale;
          const dh = p.sh * p.scale;
          const k = p.scale;
          return (
            <div
              key={p.key}
              style={{
                position: 'absolute', left: p.cx - dw / 2, top: p.cy - dh / 2, width: dw, height: dh, opacity: op,
                transform: `translate(${p.dx * (1 - t)}px, ${p.dy * (1 - t)}px) rotate(${p.rot * (2 - t)}deg) scale(${1.12 - 0.12 * t})`,
                borderRadius: p.phone ? 40 : 18, overflow: 'hidden', boxShadow: '0 18px 40px rgba(45,36,64,0.2), 0 3px 8px rgba(45,36,64,0.1)',
                border: p.phone ? '10px solid #241D33' : undefined, boxSizing: 'content-box',
              }}
            >
              <Img src={staticFile(p.src)} style={{ position: 'absolute', left: -p.sx * k, top: -p.sy * k, width: p.w * k, height: p.h * k, maxWidth: 'none' }} />
            </div>
          );
        })}
      </AbsoluteFill>
      <AbsoluteFill style={{ background: 'radial-gradient(760px 420px at 960px 500px, rgba(255,250,243,0.97), rgba(255,250,243,0.5) 60%, rgba(255,250,243,0) 80%)', opacity: stage }} />
      <AbsoluteFill style={{ alignItems: 'center', justifyContent: 'center' }}>
        <div style={{ textAlign: 'center', marginTop: -20 }}>
          <Wordmark at={42} size={176} />
          <div style={{ height: 8, width: 260, borderRadius: 4, background: C.rose, transform: `scaleX(${rule})`, margin: '24px auto 0' }} />
          <div style={{ fontFamily: F.title, fontWeight: 600, fontSize: 50, color: C.ink, marginTop: 26, opacity: tag }}>
            Free · Windows · macOS · Linux · Android
          </div>
          <div style={{ fontFamily: F.ui, fontWeight: 700, fontSize: 40, color: C.rose, marginTop: 14, opacity: url }}>
            buddhacodes.github.io/BadukDana
          </div>
        </div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- sound (declarative, relative to SHOTS)

type Sfx = { from: number; src: string; volume: number; dur?: number };
const STONES = ['stone_1.wav', 'stone_2.wav', 'stone_3.wav'];
// music ducks under the stone clicks and the outro hit, so the sound effects stay audible
const DUCKS: [number, number][] = [
  [SHOTS.play.from, SHOTS.play.from + SHOTS.play.dur],
  [SHOTS.phones.from, SHOTS.phones.from + SHOTS.phones.dur],
  [SHOTS.outro.from + 30, SHOTS.outro.from + 90],
];
const bgmVolume = (f: number) => {
  const env = interpolate(f, [0, 30, TOTAL - 60, TOTAL], [0, 0.42, 0.42, 0], CLAMP);
  const duck = Math.max(...DUCKS.map(([a, b]) => interpolate(f, [a - 10, a + 4, b - 4, b + 10], [0, 1, 1, 0], CLAMP)));
  return env * (1 - 0.45 * duck);
};
export const SFX: Sfx[] = [
  // open: Kuro and Shiro land with a firm stone click each, then the wordmark settles
  { from: SHOTS.open.from + 24, src: 'stone_firm_1.wav', volume: 0.55 },
  { from: SHOTS.open.from + 34, src: 'stone_firm_2.wav', volume: 0.45 },
  { from: SHOTS.open.from + 40, src: 'transition-soft.mp3', volume: 0.25 },
  // every title card enters with the same quick swoosh (one sound per kind of element)
  ...(['t1', 't2', 't3', 't4'] as Name[]).map((n) => ({ from: SHOTS[n].from, src: 'swoosh-quick.mp3', volume: 0.32 })),
  // the game: one stone sound per move, alternating samples, stepping down so it never sounds like a machine gun
  ...Array.from({ length: PLAY_MOVES }, (_, m) => ({
    from: SHOTS.play.from + PLAY_START + m * PLAY_STEP + 2,
    src: STONES[m % 3],
    volume: 0.75 - 0.014 * m,
  })),
  // review: a soft whoosh for each camera flight
  { from: SHOTS.review.from + 30, src: 'whoosh-fast.mp3', volume: 0.18 },
  { from: SHOTS.review.from + 100, src: 'whoosh-fast.mp3', volume: 0.16 },
  { from: SHOTS.review.from + 168, src: 'whoosh-fast.mp3', volume: 0.16 },
  { from: SHOTS.study.from, src: 'transition-soft.mp3', volume: 0.22 },
  // themes: one long whoosh for the sweep, a soft one for the pull-back
  { from: SHOTS.themes.from + 12, src: 'whoosh-big.mp3', volume: 0.3 },
  { from: SHOTS.themes.from + 84, src: 'transition-soft.mp3', volume: 0.22 },
  // phones rise
  { from: SHOTS.phones.from, src: 'transition-soft.mp3', volume: 0.25 },
  ...Array.from({ length: PHONE_MOVES }, (_, m) => ({
    from: SHOTS.phones.from + PHONE_START + (m + 1) * PHONE_STEP,
    src: STONES[(m + 1) % 3],
    volume: 0.5 - 0.008 * m,
  })),
  // outro sentence: riser → impact on the wordmark → sparkle on the rule
  { from: SHOTS.outro.from - 30, src: 'riser-cine.mp3', volume: 0.3, dur: 74 },
  { from: SHOTS.outro.from + 42, src: 'impact-deep-whoosh.mp3', volume: 0.6 },
  { from: SHOTS.outro.from + 62, src: 'sparkle.mp3', volume: 0.3 },
];

// ---------------------------------------------------------------- the film

export const HoshiPromo: React.FC<{ bgm: boolean }> = ({ bgm }) => {
  useFonts();
  const S = (n: Name, el: React.ReactNode) => (
    <Sequence key={n} from={SHOTS[n].from} durationInFrames={SHOTS[n].dur}>
      {el}
    </Sequence>
  );
  return (
    <AbsoluteFill style={{ background: C.cream }}>
      {S('open', <Open />)}
      {S('t1', <TitleCard words={['Every', 'game,', 'a', 'book', 'you', 'read', 'again.']} accent={3} />)}
      {S('play', <DeskPlay />)}
      {S('t2', <TitleCard words={['KataGo', 'reviews', 'every', 'move.']} accent={2} />)}
      {S('review', <Review />)}
      {S('t3', <TitleCard words={['Draw,', 'note,', 'ask', '“what if?”']} accent={3} />)}
      {S('study', <Study />)}
      {S('themes', <Themes />)}
      {S('t4', <TitleCard words={['And', 'it', 'fits', 'in', 'your', 'pocket.']} accent={5} />)}
      {S('phones', <Phones />)}
      {S('outro', <Outro />)}
      {bgm ? <Audio src={staticFile('audio/lofi.wav')} volume={(f) => bgmVolume(f)} /> : null}
      {SFX.map((s, i) => (
        <Sequence key={`sfx-${i}`} from={s.from} durationInFrames={s.dur ?? 150}>
          <Audio src={staticFile(`audio/${s.src}`)} volume={s.volume} />
        </Sequence>
      ))}
    </AbsoluteFill>
  );
};
