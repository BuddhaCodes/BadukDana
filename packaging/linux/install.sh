#!/bin/sh
# Installs Hoshi for the current user: ~/.local/share/hoshi, a launcher in ~/.local/bin and a menu entry.
set -e
here="$(cd "$(dirname "$0")" && pwd)"
dest="${XDG_DATA_HOME:-$HOME/.local/share}/hoshi"
mkdir -p "$dest" "$HOME/.local/bin" "${XDG_DATA_HOME:-$HOME/.local/share}/applications" "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/512x512/apps"
cp -R "$here"/. "$dest"/
chmod +x "$dest/Hoshi"
ln -sf "$dest/Hoshi" "$HOME/.local/bin/Hoshi"
cp "$here/hoshi.png" "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/512x512/apps/hoshi.png"
sed "s#^Exec=Hoshi#Exec=$dest/Hoshi#" "$here/hoshi.desktop" > "${XDG_DATA_HOME:-$HOME/.local/share}/applications/hoshi.desktop"
echo "Hoshi installed. Start it from your applications menu or run: $dest/Hoshi"
