# Kingdom Rush Combined Mod Patcher — Design Specification

## Purpose

Create a player-facing Windows graphical patcher that installs Reverse Gold 1.0.1 and Four-Hero together into a player-owned Steam copy of Kingdom Rush. The patcher must be downloadable from GitHub as a single executable, require neither Python nor a separately installed .NET runtime, preserve user data, reject unsupported game builds, verify its work, and restore the exact pre-install executable on uninstall.

The mod author owns both bundled mods. The release may therefore include their original Lua modules. It must not include Kingdom Rush executables, original game scripts, extracted game assets, or any other Ironhide-owned game file.

## Supported environment

- Windows 10 and Windows 11, x64.
- Steam edition of Kingdom Rush.
- Game build `24662480` / `kr1-desktop-6.4.46` initially.
- A clean supported game executable as the installation input.
- The existing Reverse Gold-patched executable remains useful as a migration source during development, but the public combined installer will patch the player's clean executable directly.

Support is allowlisted by SHA-256. Adding another game build requires an explicit new compatibility entry and tests; the installer must never offer a force-patch or skip-hash option.

## Deliverables

1. A .NET 8 WinForms application published as one self-contained `win-x64` executable.
2. A reusable patching library independent of WinForms.
3. Original Reverse Gold and Four-Hero Lua payloads embedded as installer resources.
4. Automated unit and integration tests using synthetic game images and temporary directories.
5. A GitHub Actions workflow that runs Python and .NET tests and produces release artifacts plus a SHA-256 checksum.
6. Updated player and maintainer documentation.

The release asset will use a clear versioned name such as `KingdomRush-ModPack-Setup-0.1.0-win-x64.exe`, accompanied by `KingdomRush-ModPack-Setup-0.1.0-win-x64.exe.sha256`.

## Architecture

### Projects

The solution will separate patching behavior from presentation:

- `KingdomRush.ModPatcher.Core`: archive inspection, compatibility checks, patch generation, backups, manifests, recovery, verification, uninstall, and Steam discovery.
- `KingdomRush.ModPatcher.WinForms`: the graphical interface and user interaction only.
- `KingdomRush.ModPatcher.Tests`: synthetic archive, filesystem, transaction, recovery, and discovery tests.

The core library will expose explicit operations and structured progress events. It will not depend on WinForms, registry dialogs, or global UI state. All filesystem paths and compatibility data used by tests will be injectable.

### Payload model

The installer embeds only mod-owned files:

- `reverse_gold.lua`
- `multi_hero.lua`
- a combined bootstrap `main.lua` template generated from installer-owned source

During installation, the core reads the original `main.lua` from the player's own executable and stores it inside the patched archive as `_modpack_main_orig.lua`. That original script is never placed in the repository or release artifact.

The combined bootstrap loads the modules in this order:

1. Load Reverse Gold and run `bootstrap()`.
2. Load Four-Hero and run `bootstrap()`.
3. Load and execute `_modpack_main_orig.lua`.
4. Run Reverse Gold `after_main()`.
5. Run Four-Hero `after_main()`.

This preserves the ordering already proven by the existing Four-Hero patcher while allowing installation from a clean supported executable.

### Game discovery

The application attempts discovery in this order:

1. Steam installation path from the Windows registry.
2. Steam's `libraryfolders.vdf` entries.
3. The standard Steam library location.
4. A player-selected directory through a folder picker.

A discovered directory is not trusted merely because it contains `Kingdom Rush.exe`; the executable must pass the compatibility hash and archive-structure checks.

## User interface

The main window contains:

- Product name and installer version.
- Detected game directory with a **Browse** action.
- Detected game/version status.
- Separate Reverse Gold and Four-Hero installation status indicators.
- **Install Both Mods**, **Verify**, and **Uninstall / Restore** buttons.
- A scrollable activity log.
- A progress indicator used for discovery, backup, patching, and verification.

Long-running work runs off the UI thread. Buttons are disabled while an operation is active, and closing the window is blocked only during the short atomic replacement stage. The application will not launch the game or take control of mouse/keyboard input.

The UI shows concise player-facing messages and keeps technical details in the activity log. Errors identify the failed stage and tell the player what to do next, such as restoring a supported Steam build, closing the game, selecting the correct folder, or retaining a backup for support.

## Installation transaction

### Preflight

1. Resolve and normalize the selected game directory.
2. Ensure the executable is accessible for exclusive write, which also detects a running game in the normal case.
3. Hash the executable and require an allowlisted clean build.
4. Open the fused ZIP archive and reject unreadable archives, duplicate names, missing `main.lua`, or an existing unknown modification.
5. Ensure the save directory `%APPDATA%\Kingdom Rush` exists and can be backed up.
6. Ensure adequate free disk space for the executable backup, save backup, temporary image, and safety margin.

### Prepare

1. Copy the exact executable to a hash-named backup in the game directory.
2. Verify the executable backup's SHA-256.
3. Create a timestamped ZIP backup of the save directory and verify its CRC, file list, and SHA-256.
4. Build the patched executable in the game directory under a unique temporary name.
5. Preserve all unrelated ZIP entries and their metadata.
6. Move original `main.lua` bytes to `_modpack_main_orig.lua` in the new archive.
7. Add both embedded mod modules and the combined bootstrap.
8. Reopen the temporary image, verify its ZIP CRC and exact payload bytes, and compute its final SHA-256.
9. Atomically write a pending transaction manifest containing paths, versions, hashes, backup metadata, and state `prepared`.

### Commit and finalize

1. Atomically replace `Kingdom Rush.exe` with the prepared image.
2. Verify its SHA-256 and archive contents from disk.
3. Change the manifest state to `installed`, write it atomically, and remove the pending manifest.
4. Report success only after all verification passes.

If an exception occurs before replacement, the original executable remains untouched. If replacement succeeds but finalization fails, the operation restores the verified backup when safe. A later application run inspects the pending manifest and reconciles it against the current executable hashes rather than guessing.

## Manifest and compatibility state

The installed manifest records at least:

- Schema version.
- Installer and mod-pack versions.
- Reverse Gold and Four-Hero versions.
- Supported game build identifier.
- Installation timestamp in UTC.
- Executable, executable-backup, and save-backup paths.
- Original and patched executable SHA-256 values.
- Save-backup SHA-256 and archived file list.
- Embedded payload SHA-256 values.
- Transaction state.

The manifest lives beside the game executable under a mod-pack-specific name. A separately named pending manifest provides crash recovery. Archived uninstall manifests are retained for support and auditability.

## Verify operation

Verification checks:

- The current executable matches the installed hash in the manifest.
- The executable backup exists and matches the original hash.
- The save backup exists, is a readable ZIP, matches its recorded hash, and contains the recorded file list.
- The fused archive has no duplicate entries or CRC errors.
- The combined bootstrap and both modules exist and match the embedded payload hashes.
- `_modpack_main_orig.lua` exists.
- Manifest identities and versions match this installer.

Verification is read-only except for safe pending-transaction reconciliation.

## Uninstall operation

Uninstall requires a valid installed manifest and verified executable backup. The current executable must match either the recorded patched hash or the recorded original hash; any other value is rejected to avoid overwriting an unknown player modification.

The verified original executable is restored atomically. The active manifest is renamed to an archived uninstall manifest. Current saves are not overwritten automatically, and save backups are retained so progress is never silently rolled back.

## Error handling and security

- There is no force mode, hash override, or compatibility bypass in the public GUI.
- Paths are canonicalized and checked before writes.
- ZIP entry names are treated as data; the installer does not extract the game archive to disk.
- Duplicate archive entries are rejected.
- Temporary files are created in the destination directory to preserve same-volume atomic replacement semantics.
- The installer performs no network requests at runtime.
- Logs avoid secrets and do not include save-file contents.
- The application does not request administrator privileges by default. If Steam is installed under a protected directory, Windows may require the player to relaunch with sufficient rights; the installer reports this rather than self-elevating unexpectedly.
- No game executable, original game script, decompiled code, or extracted game asset is committed or shipped.

## Testing strategy

The test suite will build synthetic PE-prefix-plus-ZIP images and use temporary directories. It will cover:

- Successful dual-mod installation while preserving unrelated entries and prefix bytes.
- Exact bootstrap ordering.
- Correct preservation of original `main.lua` under the mod-pack name.
- Wrong hashes, missing entries, malformed ZIPs, duplicate names, and existing unknown modifications.
- Missing or inaccessible save directories.
- Executable and save backup creation and validation.
- Installation idempotency and refusal to overwrite an existing installation.
- Rollback when manifest writes, replacement, or post-write verification fail.
- Recovery when interrupted before and after replacement.
- Read-only verification and corruption detection.
- Byte-exact uninstall and safe uninstall retry.
- Refusal to uninstall over an unknown changed executable.
- Steam registry/VDF discovery and manual-path fallback.
- WinForms presentation logic through testable view-model/presenter boundaries where useful.

The existing Python tests remain in CI during the transition so the new implementation can be checked against established patching behavior.

## Build and GitHub release

The Windows workflow will:

1. Check out the repository.
2. Install supported Python and .NET 8 SDK versions.
3. Run the existing Python test suite.
4. Restore, build, and test the .NET solution in Release mode.
5. Publish WinForms for `win-x64` as self-contained and single-file.
6. Compute the release executable's SHA-256.
7. Upload the executable and checksum as workflow artifacts and attach them to tagged GitHub releases.

The executable will initially be unsigned. Documentation will explain Windows SmartScreen warnings honestly and direct players to verify the published SHA-256. Code signing can be added later without changing the patching architecture.

## Documentation

The README will explain:

- What both mods add.
- Supported Windows, Steam, game, and mod-pack versions.
- Download and checksum verification.
- Installation, verification, and uninstall steps.
- Backup locations and recovery behavior.
- Troubleshooting and log location.
- Compatibility limitations and conflicts with other executable mods.
- Ownership and AI-development disclosure.
- That the package includes only author-owned mod code and patches the player's own game locally.

## Acceptance criteria

The design is complete when:

1. A clean Windows machine can run the published executable without Python or a separately installed .NET runtime.
2. The GUI finds a normal Steam installation or accepts a manually selected one.
3. One Install action applies both mods only to the allowlisted clean game build.
4. The operation creates and verifies executable and save backups before replacement.
5. Verify reports a healthy installation and detects deliberate corruption.
6. Uninstall restores the exact original executable bytes without overwriting current saves.
7. Synthetic automated tests exercise successful, rejected, interrupted, corrupted, and rollback paths.
8. The GitHub release contains the versioned executable and matching SHA-256 file.
9. The release contains no original Kingdom Rush files or assets.

