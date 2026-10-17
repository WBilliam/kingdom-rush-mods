# Mod Development Log

## 2026-10-12 — Four Hero 0.1.0 workspace build

- Target locked to Steam build `24662480` / `kr1-desktop-6.4.46` with Reverse Gold 1.0.1.
- Implemented ordered 0–4 hero party state, migration, validation, spawning, controls, XP synchronization, Hero Room extensions, and compact battle HUD hooks.
- Added transactional install/verify/uninstall tooling and automated Lua/archive tests.
- The installed game and saves have not been modified. Live installation and acceptance testing require explicit approval.

## 2026-10-12 — Installed after explicit approval

- Patched executable SHA-256: `1777a58a927d74b512f910675b5da19603b9c8e2e4f524efb378ecc458309b0e`.
- The transactional installer created verified executable and save backups in the game directory.
- Post-install verification returned `status: ok`; archive has 1,479 unique entries and no CRC failures.
- Game launch and acceptance testing were left to the user as requested.

## 2026-10-12 — Four Hero 0.1.1

- Fixed campaign entry crashing when fallback hero insertion ran before level exits were initialized.
- Added a regression test covering delayed initialization.
- Reinstalled transactionally and verified the resulting archive.

## 2026-10-12 — Four Hero 0.1.2

- Fixed party slots 2–4 rendering only health bars by queueing and tracking each extra hero's texture and sound groups during the native game transition.
- Fixed portraits 3 and 4 overlapping the second portrait by assigning the party slot before the native hero-added GUI callback.
- Added focused regression coverage for resource loading and the compact 2×2 battle HUD positions.

## 2026-10-12 — Four Hero 0.1.3

- User acceptance confirmed campaign tests 1–7: visible sprites, correct behavior, XP gain, hotkeys, and level re-entry.
- Fixed Heroic, Iron, and endless party insertion by bypassing the native `locked_hero` restriction only during each party insertion and restoring it immediately afterward.
- Reworked the 2×2 battle HUD to use standalone frames, hide the native stacked separators, and space portraits on an 84×82 grid at the existing 78% scale.
- Added regression coverage for locked-mode insertion/restoration and clean four-portrait HUD geometry.

## 2026-10-13 — Four Hero 0.1.4

- Added a `RANDOM x4` Hero Room button above the party strip.
- Random selection samples without replacement from unlocked and progression-available heroes, selects up to four, mirrors party slot 1, saves once, and refreshes the room immediately.
- Added deterministic tests for candidate filtering, distinct selection, the four-hero limit, button wiring, save behavior, and UI refresh.

## 2026-10-17 — Combined Windows patcher 0.1.0

- Added a self-contained .NET 8 WinForms installer for Reverse Gold 1.0.1 and Four-Hero 0.1.4.
- The installer detects Steam libraries, validates the exact clean build, patches the player's own fused archive, and ships no game files.
- Added verified executable/save backups, pending-transaction recovery, Verify, and byte-exact Uninstall / Restore.
- Added synthetic archive, lifecycle, Steam discovery, and presenter tests plus a Windows GitHub release workflow and SHA-256 artifact validation.
- End-to-end acceptance now proves both embedded payloads install together, corruption is detected, saves remain unchanged, and uninstall restores the original executable byte-for-byte.
- Final review moved filesystem work off the WinForms UI thread and tightened verification of installer versions, manifest paths, and exact embedded payload bytes.

## 2026-10-17 — Combined Windows patcher 0.1.1

- Fixed save discovery to use the game's actual `%APPDATA%\Kingdom_rush` directory instead of the incorrect space-separated path.
- Added regression coverage for the real save directory name and updated player documentation.

