# Arno's Pet Tracker

A Linux-first companion for **AION 2** pet farming: your pet ranks and soul totals,
what you are farming right now, and where to go next on a live map.

> **Work in progress.** The core (game-message decoding, capture and tracking) is
> done and tested; the desktop app is being built. Until then, the working tracker is
> [arnos-a2-pet-tracker](https://github.com/arnosantgaming-jpg/arnos-a2-pet-tracker),
> a Linux fork of [0Fox2Give/PetTracker](https://github.com/0Fox2Give/PetTracker).

> **Tested only on CachyOS** (KDE Plasma 6, Wayland session, AION 2 through
> Steam/Proton, NVIDIA GPU). Other distributions, desktops and Wine setups are untested.

Unofficial and not affiliated with NCSOFT. Using third-party tools can put your game
account at risk — read [LEGAL.md](LEGAL.md).

## What it will do

- **Collection:** every pet's rank and souls toward the next rank, exact while the
  game stream is continuous, clearly marked "last known" otherwise.
- **Tracker overlay:** pets you just earned souls for (Recent) or whose creatures are
  around you (Nearby), pinned pets on top, farming goals and session stats.
- **Black Cloud Merchant:** offers worth buying for pets you have not maxed.
- **Live map:** a round mini-map and a full map (Verteron, Altgard) with your
  position, pet spawns and the best farming spots for what you still need.

## How it works

| Part | Role | Privileges |
| --- | --- | --- |
| [`src/Capture`](src/Capture) `arnoa2-capture` | Finds the game connection through `/proc`, captures its incoming TCP stream with libpcap, puts it back in order and forwards the bytes over an owner-only Unix socket. No decoding. | `cap_net_raw` (file capability), dropped on the capture thread once the handle is open |
| [`src/Protocol`](src/Protocol) | Framing, LZ4 bundles, one typed decoder per game message | none |
| [`src/Tracker`](src/Tracker) | Pet ledger, kills, Recent/Nearby, pins, goals, session, merchant, storage | none |
| app *(in progress)* | Overlay, mini-map, map, pet finder, settings | none |

It only reads the game's incoming network traffic (and, for the map, the pixels of
the in-game minimap). It never reads or changes game memory and never sends anything
to the game. The client's own outgoing traffic is encrypted and not used.

**Soul ledger.** Each pet has a rank, a soul balance and a certainty. At login the
game sends the whole collection; after that, balance = login value + gains − spends,
in whatever order the messages arrive, and a rank-2 pet reaching 75 becomes max by
itself. Any capture gap turns balances into "last known" (shown with ≈) until the
next login.

## Build and check

Needs the .NET SDK 10.0.4xx (see `global.json`) and `libpcap`.

```sh
dotnet run --project tests/ProtocolTests
dotnet run --project tests/TrackerTests
dotnet run --project tests/CaptureTests
# With a capture of your own game session (keep it private):
dotnet run --project tools/Replay -c Release -- --tracker ~/path/to/recording.pcap
# Live, next to the running game (asks for your password once for cap_net_raw):
bash tools/try-live-capture.sh 60
```

Status of the checks when the core was written (2026-10-24), against the original
Pet Tracker's decoders and engine on two real recordings (~17,000 frames): identical
frames and messages, and the same final state for all 305 pets (ranks, trusted
balances, kills, session souls). A live 60-second run beside the game: 3,425 frames,
0 faults, 0 gaps, login collection captured.

## Credits

The game message formats were worked out by others; this code was written with their
work as reference:

- **[0Fox2Give/PetTracker](https://github.com/0Fox2Give/PetTracker)** (GPL-3.0) —
  collection, soul, rank, NPC, hit and merchant message layouts; the pet catalog and
  creature, merchant and acquisition data.
- **[SkeeveAN/Aion-DPS-Meter](https://github.com/SkeeveAN/Aion-DPS-Meter)** (MIT) —
  position, zone and movement layouts, map tiles, spawn data and map calibration.
- **[taengu/A2Tools-DPS-Meter](https://github.com/taengu/A2Tools-DPS-Meter)** (GPL-3.0)
  — the KDE window-rule approach for overlays above fullscreen games.

Details: [NOTICE](NOTICE) and [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
Game names and art belong to NCSOFT.

## License

GPL-3.0-only for the code — see [LICENSE](LICENSE). Game material and third-party
data are outside that grant.
