# Arno's A2 Toolkit

An unofficial companion app for **AION 2**, for Linux and Windows. All its tools share one
passive capture program:

- **Pets:** ranks, souls, live map and farming spots.
- **DPS Meter:** damage, skill breakdown with damage over time, boss view with the boss's HP,
  and a fight log.
- **Resource Tracker:** where gathering nodes are, by type, with the closest of each.
- **Hidden Cube Tracker:** every place a cube can appear, ranked, skipping spots you checked,
  with a sound and an on-screen alert when the game reports a cube (still being tested: it may
  only fire once a cube is being opened). Turn it off in the tool's settings. On KDE the toolkit adds its window rules once, so overlays stay above the game.

The main window lists every tool with its switch; pick one to see what it's doing and its settings,
grouped in cards. **Mini-map** has its own switch: while it's on, each open map tool (pets, resources,
cubes) adds its ring and markers to it. **Server Ping** adds a small always-on-top ping readout you can
drag anywhere. **Session
Gains** adds one for the Kinah, bound Kinah, Abyss Points and Rank Points (net of what a defeat costs)
you've earned since it started, in all and per hour, each in its own colour; its page in the main window
picks which ones show, the text size and how many columns, and starts a new session. **Item
Log** lists the items you obtain, newest on top and in their grade's colour, apart from chat; its page
filters by grade and type, sets how many lines show, or clears it. Right-click either overlay for a
shortcut to its settings. Item names come from
[aion2data.com](https://aion2data.com). **Event
Alerts** in the side menu warns you a few minutes before the Shugo Festival, the Dimensional
Invasion (an Air Raid or one of its two other kinds, picked at random by the game) and the
Spacetime Rift, each switched on separately. Its times come from the published schedule, so after
maintenance the game's own Event menu is the one to trust.

**HP and Mana** adds wide HP, mana and stamina bars you can drag
anywhere, with a pale strip that shows how much a hit took and a blue part for a shield on you
(such as a Templar's Nezekan's Shield), with what it has left. The game sends each value only when it
changes, so a bar is blank until its first change. They dim when you've done nothing for a moment;
their page in the main window sets size, colours and text. On KDE, System Settings → Shortcuts can give **Show or
hide HP bars** a key. With **Hide in game menus** on (Settings), every overlay but alerts
steps aside while a game menu is open, seen from the close X or back arrow in the game's top-right
corner, and lets clicks through to the game while hidden. With **Only while in game** on, overlays
wait until the toolkit sees your character. Both were tested on Linux only so far.

Built on Linux (CachyOS, KDE Wayland), where the game runs through Steam and Proton. The
Windows version is built from the same code and has been tested on a Windows PC.

## Phone map

Open **Phone Map** in the side menu, turn it on and scan the code with a phone on the same
Wi-Fi. The phone shows the live map in its browser, with nothing to install, and can switch
tools, pick spots, mark cube spots as checked and keep its screen on. It controls only the
toolkit, never the game.

It's off by default. The link is private but not encrypted, so use it on your home network
rather than public Wi-Fi. If a firewall blocks the phone, the **Allow phones through the
firewall** button opens port 8766 to your home network only, using the system's own password
prompt.

## Account risk

Nothing is sent to the game and game memory is not touched: the toolkit reads the game's
incoming network traffic and the pixels of the in-game minimap. For the ping it also spots the game's
outgoing ping requests by their size, without reading what's in them. Even so, using third-party
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
