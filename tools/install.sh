#!/usr/bin/env bash
# Install the build for this user: app menu entry and `arno-a2-toolkit` command; with
# --autostart, also start at login (the toolkit has a switch for that too). Re-run after rebuilding to update.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
build="$root/build/linux-x64"
autostart=no
[[ "${1:-}" == "--autostart" ]] && autostart=yes
[[ -x "$build/arno-a2-toolkit" && -x "$build/capture/arno-a2-capture" ]] || { echo "No build found. Run: bash tools/build.sh" >&2; exit 1; }

id=arno-a2-toolkit
app="$HOME/.local/opt/$id"
bin="$HOME/.local/bin/$id"
share="${XDG_DATA_HOME:-$HOME/.local/share}"
config="${XDG_CONFIG_HOME:-$HOME/.config}"

pkill -f "$app/arno-a2-toolkit" 2>/dev/null || true
pkill -f "$app/capture/arno-a2-capture" 2>/dev/null || true
mkdir -p "$(dirname "$app")" "$(dirname "$bin")" "$share/applications" "$share/icons/hicolor/256x256/apps"
rm -rf -- "$app.new"
cp -a -- "$build" "$app.new"
# An unchanged capture program keeps its permission: move the installed copy (with its
# file capability) into the new tree instead of the fresh, unprivileged copy.
if [[ -f "$app/capture/arno-a2-capture" ]] && cmp -s "$app/capture/arno-a2-capture" "$app.new/capture/arno-a2-capture" && diff -rq "$app/capture" "$app.new/capture" >/dev/null 2>&1; then
    rm -rf -- "$app.new/capture"
    mv -- "$app/capture" "$app.new/capture"
fi
rm -rf -- "$app"
mv -- "$app.new" "$app"
cp -- "$app/icon.png" "$share/icons/hicolor/256x256/apps/$id.png"

cat > "$bin" <<LAUNCH
#!/usr/bin/env bash
# Starts Arno's A2 Toolkit. Only the capture program needs a permission (cap_net_raw);
# if an update removed it, the desktop's password dialog asks for it once.
capture="$app/capture/arno-a2-capture"
if ! getcap "\$capture" 2>/dev/null | grep -q cap_net_raw; then
    pkexec /usr/bin/setcap cap_net_raw=ep "\$capture" || notify-send "Arno's A2 Toolkit" "Capture permission was not granted; use Settings → Grant capture permission." 2>/dev/null || true
fi
exec "$app/arno-a2-toolkit" "\$@"
LAUNCH
chmod +x "$bin"

entry="[Desktop Entry]
Type=Application
Name=Arno's A2 Toolkit
GenericName=AION 2 toolkit
Comment=Pets, DPS meter and farming tools for AION 2
Exec=$bin
Icon=$id
Terminal=false
Categories=Game;
StartupWMClass=arno-a2-toolkit"
printf '%s\n' "$entry" > "$share/applications/$id.desktop"
if [[ "$autostart" == yes ]]; then
    mkdir -p "$config/autostart"
    printf '%s\nX-KDE-autostart-after=panel\n' "$entry" > "$config/autostart/$id.desktop"
fi
command -v update-desktop-database >/dev/null && update-desktop-database "$share/applications" 2>/dev/null || true

if ! getcap "$app/capture/arno-a2-capture" | grep -q cap_net_raw; then
    echo "Granting packet-capture permission to the capture program only:"
    sudo setcap cap_net_raw=ep "$app/capture/arno-a2-capture"
fi

printf "\nInstalled to %s\nMenu: \"Arno's A2 Toolkit\"   Command: %s\n" "$app" "$id"
if [[ -f "$config/autostart/arnos-a2-pet-tracker.desktop" ]]; then
    printf '\nNote: the older "Arno'"'"'s A2 Pet Tracker" also starts at login. Run only one of them:\n  rm %s\n' "$config/autostart/arnos-a2-pet-tracker.desktop"
fi
