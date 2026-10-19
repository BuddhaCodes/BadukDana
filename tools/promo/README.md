# Hoshi promo video (Remotion)

Source of the promotional videos (16:9 `HoshiPromo`, 9:16 `HoshiPromoVertical`).

1. Footage: `HOSHI_PROMO=1 HOSHI_PROMO_OUT=<promo>/public/shots dotnet test tests/Hoshi.App.Tests --filter PromoFootage`
   (renders the real Avalonia UI headlessly; the analysis numbers come from a scripted stand-in engine, not KataGo).
2. Music: `python3 tools/lofi.py public/audio/lofi.wav 54` (synthesized, ours).
3. `public/fonts` = Hoshi's own OFL fonts; `public/audio` = Hoshi's stone sounds plus Mixkit SFX (Mixkit free licence; not committed).
4. `npm ci && npx remotion render src/index.ts HoshiPromo out/hoshi-promo.mp4`
   (add `--props=props-nobgm.json` for the version without music).
