# Arno's DPS Meter

A Linux-first damage meter for **AION 2**: damage per second for this fight and the
session, ranked per character, in a small always-on-top window.

> **Tested only on CachyOS** (KDE Plasma 6, Wayland session, AION 2 through
> Steam/Proton, NVIDIA GPU). Other distributions, desktops and Wine setups are untested.

Unofficial and not affiliated with NCSOFT. Using third-party tools can put your game
account at risk — read [LEGAL.md](LEGAL.md).

## What it does

- **Fight:** the current fight, or the last one until the next starts. A fight ends
  after 8 seconds without a hit (change it under ⚙).
- **Session:** every fight since the last reset. Time between fights is left out.
- Hover a row for total damage, hits and the biggest hit. If capture data is lost the
  meter says it is incomplete.

- Rows show each character's name and class, with bars in class colours. A class shows
  once that character uses a class skill. Players whose spawn the meter did not see
  (already nearby when it started) show as numbers until they next come into view.

Not yet: damage over time is not counted, so classes that rely on it read low. Summons
show as their own row and are not added to their owner.

## Install (CachyOS / Arch)

```sh
sudo pacman -S --needed git libpcap libcap fontconfig libx11
# .NET SDK 10.0.4xx into your home folder (build tool only):
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --version 10.0.401 --install-dir ~/.dotnet
git clone https://github.com/arnosantgaming-jpg/arno-dps-meter.git
cd arno-dps-meter
bash tools/build.sh
bash tools/install.sh        # asks for your password once; add --autostart to start at login
```

Then start **Arno's DPS Meter** from the menu. On KDE, **⚙ → Keep the meter above the
game** keeps it above a fullscreen game. It runs fine next to Arno's Pet Tracker: each
has its own capture program and socket.

## How it works

| Part | Role | Privileges |
| --- | --- | --- |
| [`src/Capture`](src/Capture) `arno-dps-capture` | Finds the game connection through `/proc`, captures its incoming TCP stream with libpcap, puts it back in order and forwards the bytes over an owner-only Unix socket. No decoding. | `cap_net_raw` (file capability), dropped on the capture thread once the handle is open |
| [`src/Protocol`](src/Protocol) | Framing, LZ4 bundles, decoders for the character and skill hits | none |
| [`src/Meter`](src/Meter) | Fights, sessions, recognising your character, settings | none |
| [`src/App`](src/App) `arno-dps-meter` | The meter window and settings; reads the daemon's socket | none |

It only reads the game's incoming network traffic. It never reads or changes game
memory and never sends anything to the game.

## Build and check

Needs the .NET SDK 10.0.4xx (see `global.json`) and `libpcap`.

```sh
dotnet run --project tests/ProtocolTests
dotnet run --project tests/MeterTests
dotnet run --project tests/CaptureTests
dotnet run --project tests/AppTests -- test-results/app   # renders every window
# With a capture of your own game session (keep it private):
dotnet run --project tools/Replay -c Release -- --meter ~/path/to/recording.pcap
# Live, next to the running game (asks for your password once for cap_net_raw):
bash tools/try-live-capture.sh 60
```

## Credits

The game message formats were worked out by others; this code was written with their
work as reference:

- **[0Fox2Give/PetTracker](https://github.com/0Fox2Give/PetTracker)** (GPL-3.0) —
  framing and identity message layouts.
- **[taengu/A2Tools-DPS-Meter](https://github.com/taengu/A2Tools-DPS-Meter)** (GPL-3.0)
  — the skill hit, player name and class layouts, the class colours, and the KDE
  window-rule approach for overlays above
  fullscreen games.

Details: [NOTICE](NOTICE) and [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
Game names belong to NCSOFT.

## License

GPL-3.0-only for the code — see [LICENSE](LICENSE). Game material is outside that grant.
