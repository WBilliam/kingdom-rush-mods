# Kingdom Rush Combined Mod Patcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and release a self-contained .NET 8 WinForms executable that safely installs, verifies, and uninstalls Reverse Gold 1.0.1 plus Four-Hero 0.1.4 on the supported Steam build of Kingdom Rush.

**Architecture:** A UI-independent core library owns fused-archive parsing, compatibility validation, transactions, recovery, Steam discovery, and manifests. A thin WinForms project embeds the two author-owned Lua payloads, calls the core asynchronously, and presents progress and actionable errors. Tests operate only on synthetic executables and temporary directories; the installed game is never modified by automated tests.

**Tech Stack:** C# 12, .NET 8 (`net8.0` / `net8.0-windows`), WinForms, `System.IO.Compression`, `System.Text.Json`, Windows Registry APIs, xUnit, Python `unittest`, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-16-windows-combined-mod-patcher-design.md`

## Global Constraints

- Target Windows 10/11 x64 and publish a self-contained, single-file `win-x64` executable.
- Support only Steam build `24662480` / `kr1-desktop-6.4.46` with clean executable SHA-256 `ebb4a3a9fd4fa6a30fb98e2b39fb32f0eff8630d47a3951b658339c26ce91e7b`.
- Bundle only the author-owned `reverse_gold.lua` and `multi_hero.lua`; never commit or ship a Kingdom Rush executable, original game script, extracted asset, or decompiled game code.
- Pin Reverse Gold 1.0.1 payload SHA-256 to `cd624da2eadfb16888c413fa4514fa66b3a6579a10dbf74c290aca5343df947b` and preserve Four-Hero version `0.1.4`.
- Do not expose a force mode, hash override, compatibility bypass, runtime download, unexpected elevation, or automatic save restore.
- Back up and verify the executable and `%APPDATA%\Kingdom Rush` before replacement; use same-volume temporary files and atomic replacement.
- Automated tests must use synthetic fused executables and temporary directories only.

## Review Focus

- A PE prefix containing misleading `PK` bytes must not fool ZIP-boundary detection; Task 2 adds a synthetic-prefix regression test.
- Archive names that differ only by case must be rejected to prevent shadowed `main.lua` or payload entries; Task 2 adds an ordinal-ignore-case duplicate test.
- Truncated or malformed pending-manifest JSON must stop with a recovery-specific error without changing the executable; Task 3 adds this recovery test.
- A locked executable or insufficient destination free space must fail during preflight before any backup or manifest is created; Task 3 adds both preflight tests through injected filesystem capabilities.
- Invalid or partially written `libraryfolders.vdf` data must be skipped without crashing while manual Browse remains available; Task 4 adds malformed-VDF discovery tests.

---

## File Structure

- `KingdomRush.ModPatcher.sln` — solution entry point.
- `Directory.Build.props` — shared nullable, warnings, language-version, and deterministic-build settings.
- `installer/src/KingdomRush.ModPatcher.Core/` — focused core types organized by compatibility, archives, transactions, discovery, and diagnostics.
- `installer/src/KingdomRush.ModPatcher.WinForms/` — application entry point, one main form, embedded payload loader, and UI presenter.
- `installer/tests/KingdomRush.ModPatcher.Core.Tests/` — synthetic-image and temporary-filesystem tests.
- `src/reverse_gold.lua` — author-owned Reverse Gold 1.0.1 source, imported from the verified local artifact and hash-pinned.
- `src/multi_hero.lua` — existing Four-Hero 0.1.4 source.
- `.github/workflows/windows-release.yml` — test, publish, checksum, artifact, and tagged-release workflow.
- `README.md` and `MODLOG.md` — player instructions, release notes, provenance, and implementation record.

### Task 1: Establish the .NET solution and verified payload contract

**Files:**
- Create: `KingdomRush.ModPatcher.sln`
- Create: `Directory.Build.props`
- Create: `installer/src/KingdomRush.ModPatcher.Core/KingdomRush.ModPatcher.Core.csproj`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Compatibility/SupportedBuild.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Payloads/ModPayloadSet.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Payloads/EmbeddedPayloadProvider.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Payloads/combined_main.lua`
- Create: `installer/tests/KingdomRush.ModPatcher.Core.Tests/KingdomRush.ModPatcher.Core.Tests.csproj`
- Create: `installer/tests/KingdomRush.ModPatcher.Core.Tests/PayloadTests.cs`
- Create: `tools/export_reverse_gold_payload.py`
- Create: `tests/test_reverse_gold_payload_export.py`
- Create: `src/reverse_gold.lua`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `SupportedBuild(string BuildId, string GameVersion, string ExecutableSha256, string OriginalMainSha256)` and `SupportedBuild.KingdomRushSteam24662480`.
- Produces: `ModPayloadSet(byte[] ReverseGold, byte[] FourHero, byte[] Bootstrap)` with `ValidateHashes()`.
- Produces: `EmbeddedPayloadProvider.Load() -> ModPayloadSet` for later UI composition.
- Consumes: the local author-owned Reverse Gold artifact only through the explicit export tool; no game-owned entry is exported.

- [ ] **Step 1: Write failing Python export tests**

Add tests named `test_exports_only_reverse_gold_module_from_verified_artifact`, `test_rejects_wrong_source_executable_hash`, and `test_rejects_wrong_payload_hash`. Build a synthetic source containing `main.lua`, `reverse_gold.lua`, and a game-owned sentinel, then assert that only the requested mod module is written and that the exact source and payload hashes are required.

- [ ] **Step 2: Run the export tests and verify failure**

Run: `py -3 -m unittest tests.test_reverse_gold_payload_export -v`

Expected: FAIL because `tools.export_reverse_gold_payload` does not exist.

- [ ] **Step 3: Implement the narrow payload export tool**

Implement `export_reverse_gold_payload(source_exe: Path, destination: Path, expected_source_sha256: str, expected_payload_sha256: str) -> None` in `tools/export_reverse_gold_payload.py`. It must read only `reverse_gold.lua`, reject duplicate archive names, verify both hashes, and atomically write the destination; its CLI must require all paths/hashes explicitly.

- [ ] **Step 4: Run the export tests**

Run: `py -3 -m unittest tests.test_reverse_gold_payload_export -v`

Expected: all export tests PASS.

- [ ] **Step 5: Import and verify the owned Reverse Gold payload**

Run the export tool against `D:\Steam\steamapps\common\Kingdom Rush\Kingdom Rush.exe.four-hero.74fae2529719.bak`, requiring source hash `74fae2529719612080f6353da7f268a09b298dbc1ec8db6a7cd0eb015517f55e` and payload hash `cd624da2eadfb16888c413fa4514fa66b3a6579a10dbf74c290aca5343df947b`, writing `src/reverse_gold.lua`. Re-run `Get-FileHash src\reverse_gold.lua -Algorithm SHA256` and require the pinned payload hash.

- [ ] **Step 6: Create the solution, projects, shared build settings, and failing payload tests**

Target `net8.0` for Core and Tests. Enable nullable references, implicit usings, deterministic builds, and warnings as errors for project code. Add xUnit tests asserting the exact supported build values, exact Reverse Gold hash, non-empty Four-Hero payload, required bootstrap filenames, and bootstrap order.

- [ ] **Step 7: Run the .NET tests and verify failure**

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release --filter FullyQualifiedName~PayloadTests`

Expected: FAIL because the compatibility and payload types are not implemented.

- [ ] **Step 8: Implement the minimal compatibility and payload types**

Implement the interfaces above. Embed `src/reverse_gold.lua`, `src/multi_hero.lua`, and installer-owned `Payloads/combined_main.lua` with stable logical resource names. `ValidateHashes()` must use fixed-time, case-insensitive hex comparison and report which mod payload mismatched.

- [ ] **Step 9: Run all baseline tests**

Run: `py -3 -m unittest discover -s tests -v`

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release`

Expected: all Python and .NET tests PASS.

- [ ] **Step 10: Commit**

```powershell
git add .gitignore Directory.Build.props KingdomRush.ModPatcher.sln installer src/reverse_gold.lua tools/export_reverse_gold_payload.py tests/test_reverse_gold_payload_export.py
git commit -m "build: establish verified mod patcher payloads"
```

### Task 2: Build and validate fused executable images

**Files:**
- Create: `installer/src/KingdomRush.ModPatcher.Core/Archives/ZipLayout.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Archives/FusedArchive.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Archives/PatchImageBuilder.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Archives/PatchImageResult.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Errors/PatcherException.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Errors/CompatibilityException.cs`
- Create: `installer/tests/KingdomRush.ModPatcher.Core.Tests/SyntheticFusedImage.cs`
- Create: `installer/tests/KingdomRush.ModPatcher.Core.Tests/PatchImageBuilderTests.cs`

**Interfaces:**
- Consumes: `SupportedBuild` and `ModPayloadSet` from Task 1.
- Produces: `ZipLayout.Parse(ReadOnlyMemory<byte>) -> ZipLayout` with `PrefixLength` and validated central-directory metadata.
- Produces: `FusedArchive.Read(ReadOnlyMemory<byte>) -> FusedArchive` with case-insensitive unique entries.
- Produces: `PatchImageBuilder.Build(ReadOnlyMemory<byte> original, SupportedBuild build, ModPayloadSet payloads) -> PatchImageResult`.
- Produces: `PatchImageResult(byte[] Image, string OriginalSha256, string PatchedSha256, IReadOnlyDictionary<string,string> EntrySha256)`.

- [ ] **Step 1: Write failing archive and image tests**

Cover: prefix preservation; unrelated entry preservation; original `main.lua` moved to `_modpack_main_orig.lua`; exact combined bootstrap order; both module hashes; wrong executable and original-main hashes; malformed EOCD/central directory; CRC failure; ordinary duplicates; case-only duplicates; an existing `_modpack_main_orig.lua`; and a PE prefix containing fake `PK\x03\x04` bytes before the real ZIP.

- [ ] **Step 2: Run the focused tests and verify failure**

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release --filter FullyQualifiedName~PatchImageBuilderTests`

Expected: FAIL because archive types are missing.

- [ ] **Step 3: Implement strict ZIP layout parsing**

Implement `ZipLayout.Parse` by locating the valid end-of-central-directory record from the end of the image, validating its bounds and single-disk fields, reading the central directory, and deriving the prefix from validated local-header offsets. Do not scan forward for the first `PK` signature.

- [ ] **Step 4: Implement fused archive reading and validation**

Use `ZipArchive` only after `ZipLayout` has validated bounds. Reject duplicate paths with `StringComparer.OrdinalIgnoreCase`, encrypted entries, unsafe/malformed names, unreadable entries, missing `main.lua`, and CRC/read failures.

- [ ] **Step 5: Implement deterministic patch image construction**

Build a new same-format image in memory, preserve prefix bytes and all unrelated entry bytes, copy timestamps/external attributes where supported, rename original `main.lua`, add the two payloads and bootstrap, then reopen and verify every required entry before returning `PatchImageResult`.

- [ ] **Step 6: Run archive tests and the full suite**

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release --filter FullyQualifiedName~PatchImageBuilderTests`

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release`

Expected: all tests PASS.

- [ ] **Step 7: Commit**

```powershell
git add installer/src/KingdomRush.ModPatcher.Core/Archives installer/src/KingdomRush.ModPatcher.Core/Errors installer/tests/KingdomRush.ModPatcher.Core.Tests
git commit -m "feat: build combined fused game image"
```

### Task 3: Implement transactional install, recovery, verify, and uninstall

**Files:**
- Create: `installer/src/KingdomRush.ModPatcher.Core/Transactions/IFileSystemCapabilities.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Transactions/PhysicalFileSystemCapabilities.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Transactions/InstallPaths.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Transactions/InstallManifest.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Transactions/ManifestStore.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Transactions/SaveBackupService.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Transactions/ModPackService.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Transactions/OperationProgress.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Errors/RecoveryException.cs`
- Create: `installer/tests/KingdomRush.ModPatcher.Core.Tests/ModPackServiceTests.cs`
- Create: `installer/tests/KingdomRush.ModPatcher.Core.Tests/ManifestStoreTests.cs`

**Interfaces:**
- Consumes: `PatchImageBuilder.Build(...)` and Task 1 payload/build contracts.
- Produces: `InstallPaths.ForGameDirectory(string gameDirectory, string appDataDirectory) -> InstallPaths`.
- Produces: `IFileSystemCapabilities.EnsureExclusiveWrite(string path) -> void` and `GetAvailableFreeBytes(string destinationPath) -> long`.
- Produces: `ModPackService.InstallAsync(InstallPaths, IProgress<OperationProgress>?, CancellationToken) -> Task<InstallManifest>`.
- Produces: `ModPackService.VerifyAsync(InstallPaths, CancellationToken) -> Task<VerificationResult>`.
- Produces: `ModPackService.UninstallAsync(InstallPaths, IProgress<OperationProgress>?, CancellationToken) -> Task<UninstallResult>`.
- Produces: `ModPackService.RecoverPendingAsync(InstallPaths, CancellationToken) -> Task<RecoveryResult>`.

- [ ] **Step 1: Write failing manifest and transaction tests**

Cover: manifest JSON round-trip and schema/version validation; successful install/verify/uninstall; exact executable restore; save ZIP hash and sorted file list; already-installed refusal; current executable changed before uninstall; corrupt backup; interrupted-before-replace cleanup; interrupted-after-replace reconciliation; final-manifest failure rollback; truncated/malformed pending JSON refusal; locked executable; insufficient free space; missing save directory; cancellation before commit; and cancellation ignored during atomic commit/finalize.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release --filter "FullyQualifiedName~ModPackServiceTests|FullyQualifiedName~ManifestStoreTests"`

Expected: FAIL because transaction types are missing.

- [ ] **Step 3: Implement paths, manifests, and atomic JSON storage**

Use manifest name `kingdom_rush_modpack_manifest.json`, pending name `kingdom_rush_modpack_manifest.pending.json`, schema version `1`, and archived uninstall name containing a UTC timestamp. JSON writes must flush a same-directory temporary file before atomic replacement.

- [ ] **Step 4: Implement preflight capabilities and save backup service**

`IFileSystemCapabilities` must expose executable-exclusive-open validation and available free bytes so tests can deterministically simulate locks and low disk. Require free space equal to two executable images plus the uncompressed save-directory size plus a 64 MiB margin before creating any backup. `SaveBackupService` must create a timestamped ZIP, reopen it, verify entries, and return its SHA-256 plus a sorted relative path list.

- [ ] **Step 5: Implement install and pending recovery**

Follow the spec's prepare/commit/finalize transaction. Check cancellation only before the commit boundary. After replacement begins, finish verification/finalization or restore the verified executable backup. Recovery must compare current hashes with pending manifest hashes; malformed or contradictory state throws `RecoveryException` without a write.

- [ ] **Step 6: Implement verify and uninstall**

Verify all manifest identities, executable/backup/save hashes, save ZIP entries, required archive entries, and payload hashes. Uninstall only when current executable matches recorded original or patched hash, restore atomically, retain saves/backups, and archive the manifest.

- [ ] **Step 7: Run transaction tests and the full suite**

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release --filter "FullyQualifiedName~ModPackServiceTests|FullyQualifiedName~ManifestStoreTests"`

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release`

Expected: all tests PASS and no test accesses the real Steam or AppData directories.

- [ ] **Step 8: Commit**

```powershell
git add installer/src/KingdomRush.ModPatcher.Core/Transactions installer/src/KingdomRush.ModPatcher.Core/Errors installer/tests/KingdomRush.ModPatcher.Core.Tests
git commit -m "feat: add transactional mod pack lifecycle"
```

### Task 4: Detect Steam libraries and classify installation state

**Files:**
- Create: `installer/src/KingdomRush.ModPatcher.Core/Discovery/IRegistryReader.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Discovery/WindowsRegistryReader.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Discovery/SteamVdfParser.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Discovery/GameDiscoveryService.cs`
- Create: `installer/src/KingdomRush.ModPatcher.Core/Discovery/InstallationStatus.cs`
- Create: `installer/tests/KingdomRush.ModPatcher.Core.Tests/SteamVdfParserTests.cs`
- Create: `installer/tests/KingdomRush.ModPatcher.Core.Tests/GameDiscoveryServiceTests.cs`

**Interfaces:**
- Produces: `IRegistryReader.ReadString(RegistryHive hive, RegistryView view, string subKey, string valueName) -> string?`.
- Produces: `SteamVdfParser.ParseLibraryPaths(string text) -> IReadOnlyList<string>`.
- Produces: `GameDiscoveryService.FindCandidates() -> IReadOnlyList<string>`.
- Produces: `GameDiscoveryService.InspectAsync(string gameDirectory, CancellationToken) -> Task<InstallationStatus>`.
- Produces: `InstallationStatus` states `Missing`, `Unsupported`, `ReadyToInstall`, `InstalledHealthy`, `InstalledCorrupt`, and `RecoveryRequired`, plus player-facing detail.

- [ ] **Step 1: Write failing discovery and classification tests**

Cover both 32-bit and 64-bit Steam registry views, escaped backslashes in VDF, multiple libraries, deduplication, standard-path fallback, malformed/truncated VDF, inaccessible library folders, manual non-Steam directories, the clean supported hash, a healthy installed manifest, corrupt installed image, and pending recovery.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release --filter "FullyQualifiedName~SteamVdfParserTests|FullyQualifiedName~GameDiscoveryServiceTests"`

Expected: FAIL because discovery types are missing.

- [ ] **Step 3: Implement VDF parsing and candidate discovery**

Parse quoted VDF tokens without an external dependency, tolerate unknown keys/sections, and return no paths for malformed incomplete values rather than throwing. Probe registry views through `IRegistryReader`, add the default Steam path, normalize/deduplicate candidates, and never mutate them.

- [ ] **Step 4: Implement installation inspection**

Classify state from executable hash, active/pending manifest presence, and `ModPackService.VerifyAsync`. Preserve technical exception details for logs while returning actionable player text.

- [ ] **Step 5: Run discovery tests and the full suite**

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release --filter "FullyQualifiedName~SteamVdfParserTests|FullyQualifiedName~GameDiscoveryServiceTests"`

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release`

Expected: all tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add installer/src/KingdomRush.ModPatcher.Core/Discovery installer/tests/KingdomRush.ModPatcher.Core.Tests
git commit -m "feat: discover and inspect Steam installations"
```

### Task 5: Build the WinForms player experience

**Files:**
- Create: `installer/src/KingdomRush.ModPatcher.WinForms/KingdomRush.ModPatcher.WinForms.csproj`
- Create: `installer/src/KingdomRush.ModPatcher.WinForms/Program.cs`
- Create: `installer/src/KingdomRush.ModPatcher.WinForms/MainForm.cs`
- Create: `installer/src/KingdomRush.ModPatcher.WinForms/MainForm.Designer.cs`
- Create: `installer/src/KingdomRush.ModPatcher.WinForms/MainPresenter.cs`
- Create: `installer/src/KingdomRush.ModPatcher.WinForms/IInstallerView.cs`
- Create: `installer/src/KingdomRush.ModPatcher.WinForms/InstallerViewState.cs`
- Create: `installer/src/KingdomRush.ModPatcher.WinForms/InstallerLog.cs`
- Create: `installer/src/KingdomRush.ModPatcher.WinForms/installer.manifest`
- Create: `installer/tests/KingdomRush.ModPatcher.WinForms.Tests/KingdomRush.ModPatcher.WinForms.Tests.csproj`
- Create: `installer/tests/KingdomRush.ModPatcher.WinForms.Tests/MainPresenterTests.cs`
- Modify: `KingdomRush.ModPatcher.sln`

**Interfaces:**
- Consumes: `GameDiscoveryService`, `ModPackService`, `EmbeddedPayloadProvider`, `InstallationStatus`, and `OperationProgress`.
- Produces: `MainPresenter.InitializeAsync`, `BrowseSelectedAsync`, `InstallAsync`, `VerifyAsync`, and `UninstallAsync`.
- Produces: `IInstallerView.Render(InstallerViewState state)`, `AppendLog(string line)`, `ChooseGameDirectory()`, and `Confirm(string message)`.
- Produces: `InstallerLog.Write(string message, Exception? error = null)` writing UTF-8 lines to `%LOCALAPPDATA%\KingdomRushModPatcher\installer.log`.

- [ ] **Step 1: Write failing presenter-state tests**

Create the WinForms and WinForms.Tests projects targeting `net8.0-windows` with `UseWindowsForms=true`, add both to the solution, then cover initial discovery, manual Browse, button enablement for every installation status, operation-in-progress disabling, status refresh after success/failure, actionable compatibility and permission errors, confirmation before install/uninstall, and log messages that contain paths/hashes but never save contents.

- [ ] **Step 2: Run presenter tests and verify failure**

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release --filter FullyQualifiedName~MainPresenterTests`

Expected: FAIL because presenter/view contracts are missing.

- [ ] **Step 3: Implement presenter and immutable view state**

Keep all branching and operation orchestration in `MainPresenter`; marshal only final `Render`/`AppendLog` calls through the WinForms synchronization context. Prevent concurrent operations and block form closure only while `InstallerViewState.IsCommitCritical` is true.

- [ ] **Step 4: Build the WinForms form**

Create a fixed-minimum-size, DPI-aware window with product/version heading, game path textbox, Browse button, game and two-mod status labels, Install Both Mods/Verify/Uninstall buttons, progress bar, and read-only scrollable log. Use system colors/fonts and keyboard-accessible controls. Do not request administrator privileges in `installer.manifest`.

- [ ] **Step 5: Wire composition and global error logging**

`Program.Main` must enable WinForms defaults, load/validate embedded payloads before showing the form, compose physical services, and handle UI-thread exceptions with a concise dialog plus technical details in `InstallerLog`. Log operation stages, paths, versions, and hashes, but never file contents. The application must not perform runtime network requests or launch the game.

- [ ] **Step 6: Run tests and build the GUI**

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release`

Run: `dotnet build installer/src/KingdomRush.ModPatcher.WinForms/KingdomRush.ModPatcher.WinForms.csproj -c Release`

Expected: tests PASS and build succeeds with zero warnings.

- [ ] **Step 7: Perform a synthetic manual UI smoke test**

Launch the Debug build with a development-only command-line game-directory override pointing at a temporary synthetic fixture. Confirm discovery, Ready status, Install, Verify, and Uninstall visually; ensure the override is compiled only under `DEBUG` and is absent from Release.

- [ ] **Step 8: Commit**

```powershell
git add KingdomRush.ModPatcher.sln installer/src/KingdomRush.ModPatcher.WinForms installer/tests/KingdomRush.ModPatcher.WinForms.Tests
git commit -m "feat: add Windows graphical mod pack installer"
```

### Task 6: Add reproducible publishing, documentation, and release validation

**Files:**
- Create: `.github/workflows/windows-release.yml`
- Create: `tools/verify_release.py`
- Create: `tests/test_verify_release.py`
- Modify: `README.md`
- Modify: `MODLOG.md`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: the Release-published WinForms executable and embedded payload/build constants.
- Produces: `verify_release(executable: Path, checksum_file: Path) -> None` for CI artifact validation.
- Produces: workflow artifacts `KingdomRush-ModPack-Setup-<version>-win-x64.exe` and matching `.sha256`.

- [ ] **Step 1: Write failing release-verifier tests**

Cover a valid checksum, a mismatched checksum, malformed checksum text, wrong artifact filename, missing artifact, and scanning the staged release directory for forbidden `.lua` files outside the single-file executable, game executables/backups, manifests, save ZIPs, `.env` files, and obvious secret patterns.

- [ ] **Step 2: Run verifier tests and verify failure**

Run: `py -3 -m unittest tests.test_verify_release -v`

Expected: FAIL because `tools.verify_release` does not exist.

- [ ] **Step 3: Implement release verification**

Implement the interface above with strict SHA-256 parsing and a release-directory allowlist containing only the versioned installer executable and checksum. Return nonzero from the CLI with a precise reason on failure.

- [ ] **Step 4: Add the Windows CI/release workflow**

On pushes and pull requests, install Python and .NET 8, run both test suites, and build Release. On `v*` tags, publish self-contained/single-file `win-x64`, rename it to the versioned asset name, generate SHA-256, run `tools/verify_release.py`, upload both artifacts, and attach them to the GitHub release using the repository token. Keep signing explicitly out of scope.

- [ ] **Step 5: Update player and maintainer documentation**

Replace Python-first player installation with GitHub GUI-installer instructions while retaining a Development section for the legacy tests. Document supported exact build, automatic/manual discovery, backup paths, Verify/Uninstall, conflicts with other executable mods, logs, unsigned SmartScreen warning, checksum verification, no-runtime requirement, ownership/no-game-files statement, and AI disclosure. Add a dated MODLOG entry describing the combined patcher architecture and verification performed.

- [ ] **Step 6: Run all tests and publish locally**

Run: `py -3 -m unittest discover -s tests -v`

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release`

Run: `dotnet publish installer/src/KingdomRush.ModPatcher.WinForms/KingdomRush.ModPatcher.WinForms.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false`

Expected: all tests PASS and the publish directory contains one application executable plus only files explicitly required by the single-file publish mode.

- [ ] **Step 7: Validate the staged artifact**

Rename the published executable to `KingdomRush-ModPack-Setup-0.1.0-win-x64.exe`, generate its lowercase SHA-256 file, and run `py -3 tools/verify_release.py <exe> <checksum>`. Inspect the executable version metadata and start it once on Windows to confirm the GUI opens without a separately installed .NET 8 runtime.

- [ ] **Step 8: Run repository hygiene checks**

Run: `git status --short`

Run: `rg -n -i "api[_-]?key|secret|token|password|decompiled with|FUN_[0-9A-F]+|DAT_[0-9A-F]+" --glob '!docs/superpowers/**' --glob '!*.sha256' .`

Run: `git ls-files | rg -i "Kingdom Rush\.exe|\.bak$|user_data_backup|manifest\.json$|\.env$"`

Expected: only intentional source/documentation changes, no secret/decompiler hits requiring action, and no game executable, backup, save archive, installed manifest, or environment file tracked.

- [ ] **Step 9: Commit**

```powershell
git add .github/workflows/windows-release.yml .gitignore README.md MODLOG.md tools/verify_release.py tests/test_verify_release.py
git commit -m "ci: publish verified Windows mod pack installer"
```

### Task 7: End-to-end acceptance without touching the live game

**Files:**
- Create: `installer/tests/KingdomRush.ModPatcher.Core.Tests/EndToEndAcceptanceTests.cs`
- Modify: `MODLOG.md`

**Interfaces:**
- Consumes: the complete Core service, embedded payloads, discovery inspection, and published WinForms artifact.
- Produces: a repeatable synthetic acceptance test proving clean → installed → verified → restored bytes.

- [ ] **Step 1: Write the end-to-end acceptance test**

Construct a synthetic supported image using an injected `SupportedBuild` whose executable/main hashes match the fixture. Run install, inspect the patched archive and manifest, verify, deliberately corrupt a copied installation and assert verify fails, restore the healthy fixture, uninstall, and assert the original executable bytes are identical and current save bytes are unchanged.

- [ ] **Step 2: Run the test and address integration gaps only**

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release --filter FullyQualifiedName~EndToEndAcceptanceTests`

Expected: PASS. If it fails, make the smallest production change that restores the already-specified contracts, then rerun focused and full suites.

- [ ] **Step 3: Run final verification**

Run: `py -3 -m unittest discover -s tests -v`

Run: `dotnet test KingdomRush.ModPatcher.sln -c Release --no-restore`

Run: `dotnet publish installer/src/KingdomRush.ModPatcher.WinForms/KingdomRush.ModPatcher.WinForms.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false --no-restore`

Expected: all tests PASS, publish succeeds, and the staged release verifier accepts the executable/checksum pair.

- [ ] **Step 4: Record acceptance evidence**

Append commands, pass counts, artifact filename, artifact SHA-256, and the statement that no automated test touched `D:\Steam\steamapps\common\Kingdom Rush` or live AppData to `MODLOG.md`.

- [ ] **Step 5: Commit**

```powershell
git add installer/tests/KingdomRush.ModPatcher.Core.Tests/EndToEndAcceptanceTests.cs MODLOG.md
git commit -m "test: verify combined patcher end to end"
```

