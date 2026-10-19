// HoshiPromoVertical — 9:16, 1080×1920 @ 30fps (Reels / TikTok / Shorts): the phone first, the desktop as a coda.
import React from 'react';
import { AbsoluteFill, Audio, Img, Sequence, interpolate, staticFile, useCurrentFrame } from 'remotion';
import { useFonts } from '../lib/fonts';
import { C, CAM, CLAMP, EASE, F, LAND } from '../lib/tokens';
import { Caption, Clouds, Kuro, Phone, Shiro, Shot, TitleCard, TITLE_CARD, Wordmark, type Key } from '../lib/parts';

const pad3 = (n: number) => String(n).padStart(3, '0');

const LEN = { open: 120, t1: TITLE_CARD, phone: 420, desk: 90, outro: 130 };
type Name = keyof typeof LEN;
export const VSHOTS = (() => {
  const out = {} as Record<Name, { from: number; dur: number }>;
  let at = 0;
  for (const k of Object.keys(LEN) as Name[]) {
    out[k] = { from: at, dur: LEN[k] };
    at += LEN[k];
  }
  return out;
})();
export const VTOTAL = Object.values(LEN).reduce((a, b) => a + b, 0);

// ---------------------------------------------------------------- open

const Open: React.FC<{ len: number; outro?: boolean }> = ({ len, outro = false }) => {
  const frame = useCurrentFrame();
  const land = (cue: number) => interpolate(frame, [cue, cue + 16], [0, 1], { ...CLAMP, easing: LAND });
  const squash = (cue: number) => {
    const u = interpolate(frame, [cue + 14, cue + 18, cue + 26], [0, 1, 0], CLAMP);
    return { sx: 1 + 0.08 * u, sy: 1 - 0.1 * u };
  };
  const k = land(8);
  const s = land(16);
  const ks = squash(8);
  const ss = squash(16);
  const rule = interpolate(frame, [62, 76], [0, 1], { ...CLAMP, easing: CAM });
  const tag = interpolate(frame, [72, 86], [0, 1], { ...CLAMP, easing: EASE });
  const url = interpolate(frame, [82, 96], [0, 1], { ...CLAMP, easing: EASE });
  const out = outro ? 1 : interpolate(frame, [len - 10, len], [1, 0], CLAMP);
  return (
    <AbsoluteFill style={{ opacity: out }}>
      <Clouds />
      <AbsoluteFill style={{ alignItems: 'center', justifyContent: 'center' }}>
        <div style={{ display: 'flex', gap: 12, alignItems: 'flex-end', height: 200, marginBottom: 10 }}>
          <div style={{ transform: outro ? `scale(${k}) rotate(${(1 - k) * -40}deg)` : `translateY(${(1 - k) * -700}px) scale(${ks.sx},${ks.sy})`, transformOrigin: '50% 100%', opacity: k > 0 ? 1 : 0 }}>
            <Kuro size={200} />
          </div>
          <div style={{ transform: outro ? `scale(${s}) rotate(${(1 - s) * 40}deg)` : `translateY(${(1 - s) * -700}px) scale(${ss.sx},${ss.sy})`, transformOrigin: '50% 100%', opacity: s > 0 ? 1 : 0 }}>
            <Shiro size={162} />
          </div>
        </div>
        <Wordmark at={34} size={180} />
        <div style={{ height: 9, width: 240, borderRadius: 5, background: C.rose, transform: `scaleX(${rule})`, margin: '30px auto 0' }} />
        <div style={{ fontFamily: F.title, fontWeight: 600, fontSize: 58, color: outro ? C.ink : C.ink2, marginTop: 34, opacity: tag, textAlign: 'center', lineHeight: 1.2 }}>
          {outro ? (
            <>
              Free · Android
              <br />
              Windows · macOS · Linux
            </>
          ) : (
            'Play and study Go.'
          )}
        </div>
        {outro ? (
          <div style={{ fontFamily: F.ui, fontWeight: 700, fontSize: 44, color: C.rose, marginTop: 28, opacity: url }}>buddhacodes.github.io/BadukDana</div>
        ) : null}
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- the phone, one continuous shot

const H = 1440;
const SCREEN_H = H - 28;
const SCREEN_W = (SCREEN_H * 824) / 1784;
const W = SCREEN_W + 28;
const MOVES = 30;
const STEP = 5;
const START = 36;
const SLANT = SCREEN_H * Math.tan((15 * Math.PI) / 180);

// phone timeline (relative to the phone shot)
const P = { confirm: 210, analysis: 290, sweep: 360 };

const PhoneShot: React.FC = () => {
  const frame = useCurrentFrame();
  const rise = interpolate(frame, [0, 28], [0, 1], { ...CLAMP, easing: LAND });
  const raw = (frame - START) / STEP;
  const i = Math.max(0, Math.min(MOVES, Math.floor(raw)));
  const fadeNew = i < MOVES ? interpolate(raw - i, [0, 0.5], [0, 1], CLAMP) : 0;
  // push towards the confirm bar, then back
  const push = interpolate(frame, [P.confirm, P.confirm + 24, P.analysis - 6, P.analysis + 14], [0, 1, 1, 0], { ...CLAMP, easing: CAM });
  const confirmIn = interpolate(frame, [P.confirm - 4, P.confirm + 4], [0, 1], CLAMP);
  const analysisIn = interpolate(frame, [P.analysis, P.analysis + 12], [0, 1], CLAMP);
  // one theme sweep (theme-switch A on the phone): Mochi → Kuro mochi, same move, same layout
  const p = interpolate(frame, [P.sweep + 6, P.sweep + 44], [-20, SCREEN_W + SLANT + 40], { ...CLAMP, easing: (x) => 1 - Math.pow(1 - x, 3) });
  const lineOp = interpolate(frame, [P.sweep + 6, P.sweep + 10, P.sweep + 40, P.sweep + 46], [0, 1, 1, 0], CLAMP);
  const sweepOn = frame >= P.sweep;
  const scale = 1 + 0.42 * push;
  const layer = (src: string, op: number) => <Img src={staticFile(src)} style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', opacity: op }} />;
  return (
    <AbsoluteFill>
      <Clouds />
      <div
        style={{
          position: 'absolute', left: 0, top: 0, width: 1080, height: 1920,
          transform: `translateY(${(1 - rise) * 1500}px) scale(${scale})`, transformOrigin: '50% 86%',
        }}
      >
        <Phone src={`shots/phone-seq-mochi/${pad3(i)}.png`} height={H} style={{ left: 540 - W / 2, top: 330 }}>
          {fadeNew > 0 && frame < P.confirm ? layer(`shots/phone-seq-mochi/${pad3(i + 1)}.png`, fadeNew) : null}
          {frame >= P.confirm - 4 && frame < P.sweep ? layer('shots/phone/confirm-mochi.png', confirmIn) : null}
          {frame >= P.analysis && frame < P.sweep ? layer('shots/phone/analysis-mochi.png', analysisIn) : null}
          {sweepOn ? layer('shots/phone-seq-mochi/030.png', 1) : null}
          {sweepOn ? (
            <div style={{ position: 'absolute', inset: 0, clipPath: `polygon(0 0, ${p}px 0, ${p - SLANT}px ${SCREEN_H}px, 0 ${SCREEN_H}px)` }}>
              {layer('shots/phone-seq-kuromochi/030.png', 1)}
            </div>
          ) : null}
          {sweepOn && lineOp > 0 ? (
            <div
              style={{
                position: 'absolute', left: p - SLANT / 2 - 2, top: -100, width: 4, height: SCREEN_H + 200, background: '#fff',
                boxShadow: `0 0 18px 4px ${C.pink}`, transform: 'rotate(15deg)', opacity: lineOp,
              }}
            />
          ) : null}
        </Phone>
      </div>
      <Caption text="A real board in your hand." from={34} to={P.confirm - 6} top={110} />
      <Caption text="Tap to aim, tap again to play." from={P.confirm + 4} to={P.analysis - 2} top={110} />
      <Caption text="KataGo reviews every move." from={P.analysis + 6} to={P.sweep - 2} top={110} />
      <Caption text="Ten themes, light or dark." from={P.sweep + 4} to={LEN.phone - 2} top={110} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- the desktop coda

const Desk: React.FC = () => {
  const frame = useCurrentFrame();
  const t = interpolate(frame, [0, 22], [0, 1], { ...CLAMP, easing: LAND });
  const keys: Key[] = [
    { f: 0, cx: 1234, cy: 712, z: 1.85 },
    { f: LEN.desk, cx: 1234, cy: 712, z: 2.0 },
  ];
  return (
    <AbsoluteFill>
      <Clouds />
      <AbsoluteFill style={{ transform: `translateY(${(1 - t) * 300}px)`, opacity: t }}>
        <Shot src="shots/desk/review-mochi.png" w={2560} h={1440} keys={keys} />
      </AbsoluteFill>
      <Caption text="And on your desktop." from={10} to={LEN.desk - 2} top={300} />
    </AbsoluteFill>
  );
};

// ---------------------------------------------------------------- sound

type Sfx = { from: number; src: string; volume: number; dur?: number };
const STONES = ['stone_1.wav', 'stone_2.wav', 'stone_3.wav'];
const V = VSHOTS;
const VSFX: Sfx[] = [
  { from: V.open.from + 22, src: 'stone_firm_1.wav', volume: 0.55 },
  { from: V.open.from + 30, src: 'stone_firm_2.wav', volume: 0.45 },
  { from: V.open.from + 34, src: 'transition-soft.mp3', volume: 0.25 },
  { from: V.t1.from, src: 'swoosh-quick.mp3', volume: 0.32 },
  { from: V.phone.from, src: 'transition-soft.mp3', volume: 0.26 },
  ...Array.from({ length: MOVES }, (_, m) => ({ from: V.phone.from + START + m * STEP + 2, src: STONES[m % 3], volume: 0.72 - 0.012 * m })),
  { from: V.phone.from + P.confirm, src: 'whoosh-fast.mp3', volume: 0.16 },
  { from: V.phone.from + P.analysis, src: 'transition-soft.mp3', volume: 0.2 },
  { from: V.phone.from + P.sweep + 4, src: 'whoosh-big.mp3', volume: 0.3 },
  { from: V.desk.from, src: 'whoosh-fast.mp3', volume: 0.2 },
  { from: V.outro.from - 30, src: 'riser-cine.mp3', volume: 0.28, dur: 64 },
  { from: V.outro.from + 34, src: 'impact-deep-whoosh.mp3', volume: 0.6 },
  { from: V.outro.from + 62, src: 'sparkle.mp3', volume: 0.3 },
];

const DUCKS: [number, number][] = [
  [V.phone.from + START, V.phone.from + START + MOVES * STEP + 6],
  [V.outro.from + 30, V.outro.from + 90],
];
const bgmVolume = (f: number) => {
  const env = interpolate(f, [0, 30, VTOTAL - 50, VTOTAL], [0, 0.42, 0.42, 0], CLAMP);
  const duck = Math.max(...DUCKS.map(([a, b]) => interpolate(f, [a - 10, a + 4, b - 4, b + 10], [0, 1, 1, 0], CLAMP)));
  return env * (1 - 0.45 * duck);
};

export const HoshiPromoVertical: React.FC<{ bgm: boolean }> = ({ bgm }) => {
  useFonts();
  const S = (n: Name, el: React.ReactNode) => (
    <Sequence key={n} from={V[n].from} durationInFrames={V[n].dur}>
      {el}
    </Sequence>
  );
  return (
    <AbsoluteFill style={{ background: C.cream }}>
      {S('open', <Open len={LEN.open} />)}
      {S('t1', <TitleCard words={['Go,', 'in', 'your', 'pocket.']} accent={3} size={128} />)}
      {S('phone', <PhoneShot />)}
      {S('desk', <Desk />)}
      {S('outro', <Open len={LEN.outro} outro />)}
      {bgm ? <Audio src={staticFile('audio/lofi.wav')} volume={(f) => bgmVolume(f)} /> : null}
      {VSFX.map((s, i) => (
        <Sequence key={`sfx-${i}`} from={s.from} durationInFrames={s.dur ?? 150}>
          <Audio src={staticFile(`audio/${s.src}`)} volume={s.volume} />
        </Sequence>
      ))}
    </AbsoluteFill>
  );
};
