# Kingdom Rush Four-Hero Mod Implementation

- [x] Specify and test the pure Lua party model: migration, validation, ordering, XP, and hotkeys.
- [x] Implement defensive runtime hooks for storage, spawning, battle controls, and both UI surfaces.
- [x] Specify and test fused-executable archive rewriting and Reverse Gold compatibility.
- [x] Implement transactional install, verify, and uninstall with executable/save backups and manifests.
- [x] Add operator documentation and perform automated verification.
- [x] Stop for explicit approval before changing the installed game or driving it for acceptance testing.

Installed with explicit approval on 2026-10-12. Automated `verify` returned `status: ok`; manual in-game acceptance remains user-owned.

The workspace began as an empty, non-Git directory. It is itself the isolated project location, so no secondary worktree is needed. Git metadata is intentionally absent because the managed environment reserves `.git` as read-only.

