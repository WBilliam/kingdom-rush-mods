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

