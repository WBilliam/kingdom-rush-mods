# Kingdom Rush Four-Hero Mod

Current version: 0.1.4. This release adds a Hero Room `RANDOM x4` button that replaces the party with four distinct currently available heroes.

A reversible runtime extension for the owned Steam edition of **Kingdom Rush**, build `24662480` / `kr1-desktop-6.4.46`. It extends an existing Reverse Gold 1.0.1 installation and supports ordered parties of zero to four distinct unlocked heroes.

## Features

- Select 0–4 heroes in the Hero Room by clicking unlocked portraits.
- Use `RANDOM x4` to select up to four distinct unlocked and progression-available heroes at once.
- See each selected portrait's assigned key and manage the four-slot strip with remove/left/right controls.
- Use `3`, `4`, `5`, and `6` for party slots 1–4 in battle. `1` and `2` remain power keys; Space no longer selects a hero.
- Spawn the party in a small formation in campaign, Heroic, Iron, and endless modes.
- Preserve the first party member as vanilla `main_hero`, while independently tracking every party member.
- Persist each hero's highest observed XP unless the game enables ephemeral XP.
- Retain Reverse Gold and every unrelated archive entry.

The mod contains original Lua and patching code only. It does not ship the game executable, extracted assets, or decompiled game source.

## Compatibility

The installer intentionally accepts only this exact starting state:

- Steam build: `24662480`
- Game version: `kr1-desktop-6.4.46`
- Reverse Gold: `1.0.1`
- Patched executable SHA-256: `74fae2529719612080f6353da7f268a09b298dbc1ec8db6a7cd0eb015517f55e`

An update or unknown executable is rejected. Do not bypass the check without reconfirming the new build.

## Install and recovery

Close the game first. From this directory, run:

```powershell
python tools\patch_kingdom_rush.py install
python tools\patch_kingdom_rush.py verify
```

Installation creates, before replacement:

- A hash-named backup of the Reverse Gold executable.
- A ZIP backup of `%APPDATA%\Kingdom Rush`.
- `multi_hero_manifest.json` with hashes and restore paths.
- An entry in the game directory's `MODLOG.md`.

To restore the exact Reverse Gold executable:

```powershell
python tools\patch_kingdom_rush.py uninstall
```

Uninstall deliberately leaves current saves untouched. The save-backup path and hash remain in the archived manifest so a user can restore it manually if desired.

Use `--game-dir` and `--save-dir` only for an explicitly reconfirmed installation location. `--expected-sha256` exists for controlled validation and should never be used to guess around a mismatch.

## Development verification

```powershell
python -m unittest discover -s tests -v
```

The Lua behavior tests execute `src/multi_hero.lua` through the target installation's own `lua51.dll`. Archive tests use synthetic fused executables and temporary save directories. No automated test writes to the installed game.

## Troubleshooting

Runtime diagnostics are written to `%APPDATA%\Kingdom Rush\multi_hero.log`. If a level fails to load, include that log and a screenshot of the error when reporting the problem.

## Credits and disclosure

- Runtime structure reference: [ReBBBorn/Kingdom-Rush](https://github.com/ReBBBorn/Kingdom-Rush).
- Requires and preserves the separately published Reverse Gold 1.0.1 mod.
- Developed with OpenAI Codex. No generated artwork, audio, extracted game assets, executable, or decompiled game source is included.
- Original mod code is released under the MIT License; see `LICENSE`.

## In-game acceptance checklist

After installation, test parties of 0, 1, and 4 heroes in campaign, Heroic, Iron, and endless modes. Confirm formation spawn, independent movement/combat, death and respawn, portrait clicks, keys `3`–`6`, 2×2 HUD layout, Hero Room add/remove/reorder, XP after every exit path, and Reverse Gold behavior. Test the reference resolution plus ultrawide and the narrowest supported aspect ratio.

