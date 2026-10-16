# Kingdom Rush Reverse Gold + Four-Hero Mod Pack

Mod-pack version: 0.1.0. Downloadable releases provide a self-contained Windows GUI that installs Reverse Gold 1.0.1 and Four-Hero 0.1.4 together.

A reversible runtime extension for the owned Steam edition of **Kingdom Rush**, build `24662480` / `kr1-desktop-6.4.46`. It supports ordered parties of zero to four distinct unlocked heroes.

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

The installer intentionally accepts only this exact clean starting state:

- Steam build: `24662480`
- Game version: `kr1-desktop-6.4.46`
- Clean executable SHA-256: `ebb4a3a9fd4fa6a30fb98e2b39fb32f0eff8630d47a3951b658339c26ce91e7b`

An update or unknown executable is rejected. Do not bypass the check without reconfirming the new build.

## Download, install, and recovery

Download `KingdomRush-ModPack-Setup-<version>-win-x64.exe` and its `.sha256` file from the GitHub Release. The installer is self-contained: players do not need Python or a separately installed .NET runtime.

Optionally verify the published checksum in PowerShell:

```powershell
Get-FileHash .\KingdomRush-ModPack-Setup-0.1.0-win-x64.exe -Algorithm SHA256
```

Close the game, run the installer, confirm the automatically detected Steam directory (or use **Browse**), then select **Install Both Mods**. Use **Verify** at any time to check the patched executable and backups.

The initial executable is unsigned, so Windows SmartScreen may show an unknown-publisher warning. Confirm the filename and published SHA-256 before running it.

Installation creates, before replacement:

- A hash-named backup of the Reverse Gold executable.
- A ZIP backup of `%APPDATA%\Kingdom Rush`.
- `kingdom_rush_modpack_manifest.json` with hashes and restore paths.
- An entry in the game directory's `MODLOG.md`.

Select **Uninstall / Restore** to restore the exact clean executable.

Uninstall deliberately leaves current saves untouched. The save-backup path and hash remain in the archived manifest so a user can restore it manually if desired.

The public GUI has no force mode, alternate expected hash, or compatibility bypass.

## Development verification

```powershell
python -m unittest discover -s tests -v
dotnet test KingdomRush.ModPatcher.sln -c Release
dotnet build installer\src\KingdomRush.ModPatcher.WinForms\KingdomRush.ModPatcher.WinForms.csproj -c Release
```

The Lua behavior tests execute `src/multi_hero.lua` through the target installation's own `lua51.dll`. Archive tests use synthetic fused executables and temporary save directories. No automated test writes to the installed game.

## Troubleshooting

Runtime diagnostics are written to `%APPDATA%\Kingdom Rush\multi_hero.log`. If a level fails to load, include that log and a screenshot of the error when reporting the problem.

Installer diagnostics are written to `%LOCALAPPDATA%\KingdomRushModPatcher\installer.log`. Other executable mods conflict with this patcher and are rejected as unsupported.

## Credits and disclosure

- Runtime structure reference: [ReBBBorn/Kingdom-Rush](https://github.com/ReBBBorn/Kingdom-Rush).
- Includes the author-owned Reverse Gold 1.0.1 module and Four-Hero 0.1.4 module in one installer.
- Developed with OpenAI Codex. No generated artwork, audio, extracted game assets, executable, or decompiled game source is included.
- Original mod code is released under the MIT License; see `LICENSE`.

## In-game acceptance checklist

After installation, test parties of 0, 1, and 4 heroes in campaign, Heroic, Iron, and endless modes. Confirm formation spawn, independent movement/combat, death and respawn, portrait clicks, keys `3`–`6`, 2×2 HUD layout, Hero Room add/remove/reorder, XP after every exit path, and Reverse Gold behavior. Test the reference resolution plus ultrawide and the narrowest supported aspect ratio.

