// Hoshi's own fonts (OFL), served from public/fonts; every frame waits until they are loaded.
import { useEffect, useState } from 'react';
import { continueRender, delayRender, staticFile } from 'remotion';

const FACES: [string, string, string][] = [
  ['Fredoka', 'Fredoka_500Medium.ttf', '500'],
  ['Fredoka', 'Fredoka_600SemiBold.ttf', '600'],
  ['Manrope', 'Manrope_500Medium.ttf', '500'],
  ['Manrope', 'Manrope_600SemiBold.ttf', '600'],
  ['Manrope', 'Manrope_700Bold.ttf', '700'],
  ['Shippori', 'ShipporiMincho_700Bold.ttf', '700'],
  ['JetBrains', 'JetBrainsMono_500Medium.ttf', '500'],
];

let loading: Promise<unknown> | null = null;

const load = () => {
  loading ??= Promise.all(
    FACES.map(([family, file, weight]) => {
      const face = new FontFace(family, `url(${staticFile(`fonts/${file}`)})`, { weight });
      document.fonts.add(face);
      return face.load();
    }),
  );
  return loading;
};

/** Holds the frame until the fonts are ready (call at the top of each composition). */
export const useFonts = () => {
  const [handle] = useState(() => delayRender('Hoshi fonts'));
  useEffect(() => {
    load()
      .catch((e) => console.error(e))
      .finally(() => continueRender(handle));
  }, [handle]);
};
