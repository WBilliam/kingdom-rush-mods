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

