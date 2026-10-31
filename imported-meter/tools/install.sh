#!/usr/bin/env bash
# Install the build for this user: app menu entry, `arno-dps-meter` command and
# (with --autostart) start at login. Re-run after rebuilding to update.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
build="$root/build/linux-x64"
autostart=no
[[ "${1:-}" == "--autostart" ]] && autostart=yes
[[ -x "$build/arno-dps-meter" && -x "$build/capture/arno-dps-capture" ]] || { echo "No build found. Run: bash tools/build.sh" >&2; exit 1; }

id=arno-dps-meter
app="$HOME/.local/opt/$id"
bin="$HOME/.local/bin/$id"
share="${XDG_DATA_HOME:-$HOME/.local/share}"
config="${XDG_CONFIG_HOME:-$HOME/.config}"

pkill -f "$app/arno-dps-meter" 2>/dev/null || true
pkill -f "$app/capture/arno-dps-capture" 2>/dev/null || true
mkdir -p "$(dirname "$app")" "$(dirname "$bin")" "$share/applications" "$share/icons/hicolor/256x256/apps"
rm -rf -- "$app.new"
cp -a -- "$build" "$app.new"
# An unchanged capture program keeps its permission: move the installed copy (with its
# file capability) into the new tree instead of the fresh, unprivileged copy.
if [[ -f "$app/capture/arno-dps-capture" ]] && cmp -s "$app/capture/arno-dps-capture" "$app.new/capture/arno-dps-capture" && diff -rq "$app/capture" "$app.new/capture" >/dev/null 2>&1; then
    rm -rf -- "$app.new/capture"
    mv -- "$app/capture" "$app.new/capture"
fi
rm -rf -- "$app"
mv -- "$app.new" "$app"
cp -- "$app/icon.png" "$share/icons/hicolor/256x256/apps/$id.png"

cat > "$bin" <<LAUNCH
#!/usr/bin/env bash
# Starts Arno's DPS Meter. Only the capture program needs a permission (cap_net_raw);
# if an update removed it, the desktop's password dialog asks for it once.
capture="$app/capture/arno-dps-capture"
if ! getcap "\$capture" 2>/dev/null | grep -q cap_net_raw; then
    pkexec /usr/bin/setcap cap_net_raw=ep "\$capture" || notify-send "Arno's DPS Meter" "Capture permission was not granted; use ⚙ → Grant capture permission." 2>/dev/null || true
fi
exec "$app/arno-dps-meter" "\$@"
LAUNCH
chmod +x "$bin"

entry="[Desktop Entry]
Type=Application
Name=Arno's DPS Meter
GenericName=AION 2 damage meter
Comment=Damage per second for AION 2
Exec=$bin
Icon=$id
Terminal=false
Categories=Game;
StartupWMClass=arno-dps-meter"
printf '%s\n' "$entry" > "$share/applications/$id.desktop"
if [[ "$autostart" == yes ]]; then
    mkdir -p "$config/autostart"
    printf '%s\nX-KDE-autostart-after=panel\n' "$entry" > "$config/autostart/$id.desktop"
else
    rm -f -- "$config/autostart/$id.desktop"
fi
command -v update-desktop-database >/dev/null && update-desktop-database "$share/applications" 2>/dev/null || true

if ! getcap "$app/capture/arno-dps-capture" | grep -q cap_net_raw; then
    echo "Granting packet-capture permission to the capture program only:"
    sudo setcap cap_net_raw=ep "$app/capture/arno-dps-capture"
fi

printf "\nInstalled to %s\nMenu: \"Arno's DPS Meter\"   Command: %s   Start at login: %s\n" "$app" "$id" "$autostart"
