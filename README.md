# Arno's A2 Toolkit

An unofficial companion app for **AION 2**, for Linux and Windows. All its tools share one
passive capture program:

- **Pets:** ranks, souls, live map and farming spots.
- **DPS Meter:** damage, skill breakdown, boss view and a fight log.
- **Resource Tracker:** where gathering nodes are, by type, with the closest of each.
- **Hidden Cube Tracker:** every place a cube can appear, ranked, skipping spots you checked.

All tools share one mini-map: a tool's Mini-map button adds its ring and markers.

Built on Linux (CachyOS, KDE Wayland), where the game runs through Steam and Proton. The
Windows version is built from the same code and has been tested on a Windows PC.

## Account risk

Nothing is sent to the game and game memory is not touched: the toolkit reads the game's
incoming network traffic and the pixels of the in-game minimap. Even so, using third-party
tools can put your account at risk, and no publisher has approved this one. Read
[LEGAL.md](LEGAL.md) before you use it.

## Install

Downloads are on the [releases page](https://github.com/arnosantgaming-jpg/arno-a2-toolkit/releases).

### Windows

1. Install [Npcap](https://npcap.com). Leave "Restrict Npcap driver's access to Administrators
   only" unticked. Npcap's licence doesn't allow bundling it, so it is a separate install.
2. Download `arno-a2-toolkit-<version>-windows-x64.exe` and run it. It is portable, so there is
   nothing to install, and it unpacks itself the first time, which takes a moment.
3. Run AION 2 in borderless windowed mode, and keep other windows off the game's minimap, so
   the toolkit can read it.

The exe isn't signed, so Windows SmartScreen or your antivirus may warn about it. Settings are
saved in `%LOCALAPPDATA%\arno-a2-toolkit`.

### Linux

Needs `libpcap`, `libcap` and an X11 or XWayland session.

```bash
tar -xzf arno-a2-toolkit-<version>-linux-x64.tar.gz
cd arno-a2-toolkit-<version>-linux-x64
bash tools/install.sh
```

The toolkit asks for your password once, to give the capture program permission to read
network packets. Add `--autostart` to the install to start at login. Settings are saved in
`~/.local/share/arno-a2-toolkit`.

## Build from source

Needs the .NET 10 SDK. Both versions build on Linux:

```bash
bash tools/build.sh
bash tools/build.sh win-x64-portable
```

## Layout

Linux and Windows share nearly all their code. Only what differs has its own folder.

- `src/Protocol`: packet decoding, shared by every tool.
- `src/Capture`: the capture program, with `Linux` and `Windows` folders.
- `src/Modules`: one engine per tool, which do not use each other.
- `src/App`: the main window and each tool's windows, with `Platform/Linux` and
  `Platform/Windows` folders.

## Licence and data

The code is GPL-3.0-only (see [LICENSE](LICENSE) and [NOTICE](NOTICE)). Game art and game data
are not covered by that licence. Node and cube positions are AION 2 game data and belong to
NCSOFT. See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
