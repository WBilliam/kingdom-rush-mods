# Kingdom Rush Reverse Gold

Reverse Gold 1.0.1 is a reversible runtime mod for the owned Steam edition of **Kingdom Rush**, build `24662480` / `kr1-desktop-6.4.46`. When enabled, spending gold increases the player's balance by the purchase price instead of decreasing it. Normal income and scripted balance changes retain their native behavior.

## Features

- Adds a **Reverse Gold Spending** checkbox to the options and pause screens.
- Persists the setting in the game's settings data.
- Reverses recognized tower, upgrade, and other native purchase deductions only.
- Can be disabled at any time to restore normal spending behavior.
- Installs and uninstalls transactionally with verified executable and save backups.

The repository contains original Lua and patching code only. It does not include the game executable, extracted assets, or decompiled game source.

## Compatibility

The installer accepts only this exact original starting state:

- Steam build: `24662480`
- Game version: `kr1-desktop-6.4.46`
- Original executable SHA-256: `ebb4a3a9fd4fa6a30fb98e2b39fb32f0eff8630d47a3951b658339c26ce91e7b`

Game updates and unknown executable hashes are rejected rather than patched speculatively. Install Reverse Gold before installing the Four-Hero mod from the repository's `hero-mod` branch.

## Install

Close the game, then run from this directory:

```powershell
python tools\patch_kingdom_rush.py install
python tools\patch_kingdom_rush.py verify
```

The installer backs up `Kingdom Rush.exe` and `%APPDATA%\Kingdom Rush`, records hashes in `reverse_gold_manifest.json`, builds a temporary archive, verifies it, and only then replaces the executable.

## Uninstall

Close the game, then run:

```powershell
python tools\patch_kingdom_rush.py uninstall
```

This restores the exact original executable from its verified backup. Current saves are left untouched; the installation-time save backup remains available for manual recovery.

## Development verification

```powershell
python -m unittest discover -s tests -v
```

## Troubleshooting

Runtime diagnostics are written to `%APPDATA%\Kingdom Rush\reverse_gold.log`. Hash mismatch errors mean the executable is unsupported or already modified; do not bypass the check without reconfirming the game build.

## Credits and disclosure

- Developed with OpenAI Codex.
- No generated art/audio or game-derived assets are included.
- Original mod code is released under the MIT License; see `LICENSE`.
