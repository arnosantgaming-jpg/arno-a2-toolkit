# Arno's A2 Toolkit

One companion app for **AION 2**, private. Tools share one passive capture program:

- **Pets:** ranks, souls, live map and farming spots.
- **DPS Meter:** damage, skill breakdown, boss view and a fight log.
- **Farming Assist:** planned.

Nothing is sent to the game and game memory is not touched. Using third-party tools can
put your account at risk; see [LEGAL.md](LEGAL.md).

## Layout

- `src/Protocol`: packet decoding, shared by every tool.
- `src/Capture`: the capture program.
- `src/Modules`: one engine per tool, which do not use each other.
- `src/App`: the main window and each tool's windows.

## Build

```bash
bash tools/build.sh
bash tools/install.sh
```

Add `--autostart` to the install to start at login. Saved data lives in
`~/.local/share/arno-a2-toolkit`; the first run copies it from the separate apps.
