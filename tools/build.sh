#!/usr/bin/env bash
# Build the self-contained app with its capture daemon into build/<target>.
# Usage: bash tools/build.sh [linux-x64|win-x64|win-x64-portable]
# win-x64-portable is one exe that unpacks itself at first start.
set -euo pipefail
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
target="${1:-linux-x64}"
out="$root/build/$target"
rm -rf -- "$out"
if [[ "$target" == *-portable ]]; then
    rid="${target%-portable}" extras="$root/build/$target-extras"
    single=(-c Release -r "$rid" --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true)
    rm -rf -- "$extras"
    dotnet publish "$root/src/Capture/Capture.csproj" "${single[@]}" -o "$extras/capture"
    cp -- "$root/LICENSE" "$root/NOTICE" "$root/THIRD-PARTY-NOTICES.txt" "$root/LEGAL.md" "$extras/"
    cp -R -- "$root/licenses" "$extras/licenses"
    dotnet publish "$root/src/App/App.csproj" "${single[@]}" -p:IncludeAllContentForSelfExtract=true -p:PortableExtras="$extras" -p:ApplicationIcon="$root/assets/icon.ico" -o "$out"
    rm -f -- "$out"/*.pdb
    printf '\nBuilt %s\n' "$out"
    exit 0
fi
dotnet publish "$root/src/App/App.csproj" -c Release -r "$target" --self-contained true -o "$out"
dotnet publish "$root/src/Capture/Capture.csproj" -c Release -r "$target" --self-contained true -o "$out/capture"
cp -- "$root/LICENSE" "$root/NOTICE" "$root/THIRD-PARTY-NOTICES.txt" "$root/LEGAL.md" "$root/assets/icon.png" "$out/"
cp -R -- "$root/licenses" "$out/licenses"
if [[ "$target" == win-* ]]; then
    cp -- "$root/tools/windows-readme.txt" "$out/README.txt"
    printf '\nBuilt %s\n' "$out"
else
    printf '\nBuilt %s\nInstall with: bash tools/install.sh\n' "$out"
fi
