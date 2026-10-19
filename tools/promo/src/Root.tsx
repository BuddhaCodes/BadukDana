import React from 'react';
import { Composition } from 'remotion';
import { HoshiPromo, TOTAL } from './scenes/Horizontal';
import { HoshiPromoVertical, VTOTAL } from './scenes/Vertical';

export const Root: React.FC = () => (
  <>
    <Composition id="HoshiPromo" component={HoshiPromo} durationInFrames={TOTAL} fps={30} width={1920} height={1080} defaultProps={{ bgm: true }} />
    <Composition id="HoshiPromoVertical" component={HoshiPromoVertical} durationInFrames={VTOTAL} fps={30} width={1080} height={1920} defaultProps={{ bgm: true }} />
  </>
);
