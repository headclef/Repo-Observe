# Observe

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **R.E.P.O.** that reveals nearby valuables at the press of a key.

## What This Mod Does

Press **F** (configurable) to instantly reveal every valuable item within **10 meters** of you — even through walls. Each revealed item pops the game's own discovery indicator (the same one you see when you look directly at a valuable), so it feels completely native. The ability then goes on a short **cooldown**.

- Reveals **all** valuables in range — whether or not you've already discovered them.
- Uses the game's **own** discovery graphic, so the reveal looks and lasts exactly like the vanilla effect.
- **Client-side only** — only you see your reveal; it sends no network traffic and costs nothing to other players.

## Usage

Press **F** during a level. Valuables within range pop their value for a moment, then the cooldown starts.

## Configuration

Settings are in `BepInEx/config/headclef.Observe.cfg` or the in-game mod config menu:

| Key | Default | Range | Description |
|-----|---------|-------|-------------|
| Observe Key | `F` | any key | Key to reveal nearby valuables |
| Radius | `10` | 1–50 | Reveal radius in meters |
| Cooldown | `10` | 0–60 | Seconds between uses |

## Requirements

- [BepInEx 5.x](https://github.com/BepInEx/BepInEx) installed for R.E.P.O.

## Installation

1. Install via **Thunderstore** (recommended).
2. Or manually: place `Observe.dll` into your `BepInEx/plugins` folder.
3. Launch the game — the config file is generated on first run.

## Multiplayer

- Runs **per-client** — only you see your own reveals.
- Sends no RPCs and does not affect other players or the host.

## Development

### Project Structure

```text
├── Observe.cs                     # Plugin entry point & config
├── Patches/
│   └── ObservePatch.cs            # Key polling, cooldown, and valuable reveal
└── README.md
```

### Building

This project is standalone (BepInEx only) and part of the `Repo.slnx` solution:

```bash
dotnet build ../Repo.slnx
```

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
