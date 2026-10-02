# Hoshi's board art

`generate.py` renders every goban, stone set and background in `src/Hoshi.App/Assets/Art/` from code
(numpy + Pillow, fixed seeds). It is Hoshi's own art: no photos or third-party images.

```
pip install numpy pillow
python3 tools/art/generate.py                    # everything
python3 tools/art/generate.py --only stones      # one kind
python3 tools/art/generate.py --sheet sheet.png  # plus a contact sheet
```

Adding a skin: add a renderer to `BOARDS`, `STONES` or `BACKGROUNDS`, run the script, then list it in
`src/Hoshi.App/Themes/Skins.cs` (with its line colours / variant counts) and add its name to `Strings.App.cs`.
