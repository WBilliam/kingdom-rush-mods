from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import tempfile
import zipfile


MOD_ID = "kingdom-rush-four-hero"
MOD_VERSION = "0.1.3"
GAME_VERSION = "kr1-desktop-6.4.46"
EXPECTED_REVERSE_GOLD_SHA256 = "74fae2529719612080f6353da7f268a09b298dbc1ec8db6a7cd0eb015517f55e"
REQUIRED_REVERSE_GOLD_ENTRIES = {
    "main.lua", "reverse_gold.lua", "_reverse_gold_main_orig.lua"
}


class PatcherError(RuntimeError):
    pass


class CompatibilityError(PatcherError):
    pass


class AlreadyInstalledError(PatcherError):
    pass


@dataclass(frozen=True)
class BuildResult:
    image: bytes
    original_sha256: str
    patched_sha256: str


WRAPPER = b'''local reverse_chunk, reverse_error = love.filesystem.load("reverse_gold.lua")
if not reverse_chunk then error("Reverse Gold: " .. tostring(reverse_error)) end
local reverse_ok, reverse_runtime = pcall(reverse_chunk)
if not reverse_ok then error("Reverse Gold: " .. tostring(reverse_runtime)) end
reverse_runtime.bootstrap()

local multi_chunk, multi_error = love.filesystem.load("multi_hero.lua")
if not multi_chunk then error("Four Hero: " .. tostring(multi_error)) end
local multi_ok, multi_runtime = pcall(multi_chunk)
if not multi_ok then error("Four Hero: " .. tostring(multi_runtime)) end
multi_runtime.bootstrap()

local original, original_error = love.filesystem.load("_reverse_gold_main_orig.lua")
if not original then error("Original main: " .. tostring(original_error)) end
local loaded, game_error = pcall(original)
if not loaded then error("Original main: " .. tostring(game_error)) end

reverse_runtime.after_main()
multi_runtime.after_main()
'''


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _validate_reverse_gold(archive: zipfile.ZipFile) -> None:
    names = archive.namelist()
    if len(names) != len(set(names)):
        raise CompatibilityError("executable archive contains duplicate entries")
    missing = REQUIRED_REVERSE_GOLD_ENTRIES.difference(names)
    if missing:
        raise CompatibilityError(f"Reverse Gold entries missing: {sorted(missing)}")
    wrapper = archive.read("main.lua")
    required = (b'reverse_gold.lua', b'_reverse_gold_main_orig.lua', b'runtime.bootstrap()', b'runtime.after_main()')
    if not all(token in wrapper for token in required):
        raise CompatibilityError("main.lua is not the expected Reverse Gold wrapper")


def _copy_info(info: zipfile.ZipInfo) -> zipfile.ZipInfo:
    clone = zipfile.ZipInfo(info.filename, info.date_time)
    for attr in ("compress_type", "comment", "extra", "create_system", "create_version",
                 "extract_version", "reserved", "flag_bits", "volume", "internal_attr",
                 "external_attr"):
        setattr(clone, attr, getattr(info, attr))
    return clone


def build_patched_image(image: bytes, lua_module: bytes, expected_sha256: str) -> BuildResult:
    actual = sha256(image)
    if actual.lower() != expected_sha256.lower():
        raise CompatibilityError(f"executable hash mismatch: expected {expected_sha256}, got {actual}")
    source = io.BytesIO(image)
    try:
        archive = zipfile.ZipFile(source, "r")
    except zipfile.BadZipFile as error:
        raise CompatibilityError("executable has no readable fused archive") from error
    with archive:
        _validate_reverse_gold(archive)
        infos = archive.infolist()
        prefix_end = min(info.header_offset for info in infos)
        prefix = image[:prefix_end]
        comment = archive.comment
        entries = [(info, archive.read(info.filename)) for info in infos]
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w") as target:
        for info, data in entries:
            if info.filename == "main.lua":
                data = WRAPPER
            target.writestr(_copy_info(info), data)
        target.writestr("multi_hero.lua", lua_module, compress_type=zipfile.ZIP_DEFLATED)
        target.comment = comment
    patched = prefix + output.getvalue()
    with zipfile.ZipFile(io.BytesIO(patched), "r") as check:
        if check.testzip() is not None:
            raise PatcherError("patched archive failed CRC verification")
        if check.read("multi_hero.lua") != lua_module or check.read("main.lua") != WRAPPER:
            raise PatcherError("patched archive content verification failed")
        _validate_reverse_gold_compatible(check)
    return BuildResult(patched, actual, sha256(patched))


def _validate_reverse_gold_compatible(archive: zipfile.ZipFile) -> None:
    names = archive.namelist()
    for name in ("reverse_gold.lua", "_reverse_gold_main_orig.lua", "multi_hero.lua"):
        if name not in names:
            raise CompatibilityError(f"patched archive entry missing: {name}")
    if len(names) != len(set(names)):
        raise CompatibilityError("patched archive contains duplicate entries")


def _timestamp() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def _backup_saves(save_dir: Path, destination: Path) -> tuple[str | None, list[str]]:
    if not save_dir.is_dir():
        return None, []
    files: list[str] = []
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(save_dir.rglob("*")):
            if path.is_file():
                relative = path.relative_to(save_dir).as_posix()
                archive.write(path, relative)
                files.append(relative)
    return sha256(destination.read_bytes()), files


def _replace_transactionally(path: Path, data: bytes) -> None:
    handle, temporary_name = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    temporary = Path(temporary_name)
    try:
        with os.fdopen(handle, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        with zipfile.ZipFile(temporary, "r") as archive:
            if archive.testzip() is not None:
                raise PatcherError("temporary archive failed CRC verification")
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


def pending_manifest_path(manifest_path: Path) -> Path:
    return manifest_path.with_name(manifest_path.stem + ".pending.json")


def _atomic_write_json(path: Path, data: dict) -> None:
    payload = (json.dumps(data, indent=2) + "\n").encode("utf-8")
    handle, temporary_name = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    temporary = Path(temporary_name)
    try:
        with os.fdopen(handle, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


def _recover_pending(exe_path: Path, manifest_path: Path) -> bool:
    pending = pending_manifest_path(manifest_path)
    if not pending.exists():
        return False
    journal = json.loads(pending.read_text(encoding="utf-8"))
    current_hash = sha256(exe_path.read_bytes())
    if current_hash == journal.get("patched_sha256"):
        journal["transaction_state"] = "installed"
        _atomic_write_json(manifest_path, journal)
        pending.unlink()
        return True
    if current_hash == journal.get("original_sha256"):
        pending.unlink()
        return False
    raise PatcherError("pending install journal does not match the current executable")


def install(exe_path: Path, lua_path: Path, save_dir: Path, manifest_path: Path,
            expected_sha256: str = EXPECTED_REVERSE_GOLD_SHA256,
            game_version: str = GAME_VERSION) -> dict:
    for value in (exe_path, lua_path, save_dir, manifest_path):
        if not isinstance(value, Path):
            raise TypeError("install paths must be pathlib.Path values")
    if _recover_pending(exe_path, manifest_path):
        raise AlreadyInstalledError("recovered a completed install from its pending journal")
    if manifest_path.exists():
        raise AlreadyInstalledError(f"manifest already exists: {manifest_path}")
    if not save_dir.is_dir():
        raise PatcherError(f"save directory is missing; refusing installation without a backup: {save_dir}")
    original = exe_path.read_bytes()
    result = build_patched_image(original, lua_path.read_bytes(), expected_sha256)
    backup_path = exe_path.with_name(f"{exe_path.name}.four-hero.{result.original_sha256[:12]}.bak")
    if backup_path.exists() and sha256(backup_path.read_bytes()) != result.original_sha256:
        raise PatcherError(f"existing backup hash mismatch: {backup_path}")
    if not backup_path.exists():
        shutil.copy2(exe_path, backup_path)
    if sha256(backup_path.read_bytes()) != result.original_sha256:
        raise PatcherError("new executable backup failed hash verification")
    save_backup_path = exe_path.parent / f"multi_hero_user_data_backup_{_timestamp()}.zip"
    save_backup_sha256, save_backup_files = _backup_saves(save_dir, save_backup_path)
    installed_at = datetime.now(timezone.utc).isoformat()
    manifest = {
        "mod_id": MOD_ID, "mod_version": MOD_VERSION,
        "installed_at_utc": installed_at, "game_version": game_version,
        "exe_path": str(exe_path), "backup_path": str(backup_path),
        "original_sha256": result.original_sha256,
        "patched_sha256": result.patched_sha256,
        "save_backup_path": str(save_backup_path) if save_backup_sha256 else None,
        "save_backup_sha256": save_backup_sha256,
        "save_backup_files": save_backup_files,
        "transaction_state": "prepared",
    }
    pending = pending_manifest_path(manifest_path)
    _atomic_write_json(pending, manifest)
    try:
        _replace_transactionally(exe_path, result.image)
        if sha256(exe_path.read_bytes()) != result.patched_sha256:
            raise PatcherError("installed executable failed post-replacement hash verification")
        manifest["transaction_state"] = "installed"
        _atomic_write_json(manifest_path, manifest)
        pending.unlink()
    except Exception:
        if sha256(exe_path.read_bytes()) == result.patched_sha256:
            _replace_transactionally(exe_path, original)
        if manifest_path.exists():
            manifest_path.unlink()
        if pending.exists():
            pending.unlink()
        raise
    try:
        log_path = exe_path.parent / "MODLOG.md"
        with log_path.open("a", encoding="utf-8") as log:
            log.write(f"\n## {installed_at} — Four Hero {MOD_VERSION}\n\n")
            log.write(f"- Executable backup: `{backup_path}` ({result.original_sha256})\n")
            log.write(f"- Save backup: `{save_backup_path}` ({save_backup_sha256})\n")
            log.write(f"- Patched executable: `{exe_path}` ({result.patched_sha256})\n")
    except OSError:
        pass
    return manifest


def verify(exe_path: Path, manifest_path: Path) -> dict:
    _recover_pending(exe_path, manifest_path)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    actual = sha256(exe_path.read_bytes())
    if actual != manifest.get("patched_sha256"):
        raise PatcherError(f"patched executable hash mismatch: {actual}")
    backup = Path(manifest["backup_path"])
    if sha256(backup.read_bytes()) != manifest.get("original_sha256"):
        raise PatcherError("executable backup hash mismatch")
    save_backup_path = manifest.get("save_backup_path")
    save_backup_hash = manifest.get("save_backup_sha256")
    if save_backup_path or save_backup_hash:
        if not save_backup_path or not save_backup_hash:
            raise PatcherError("save backup metadata is incomplete")
        save_backup = Path(save_backup_path)
        if not save_backup.is_file() or sha256(save_backup.read_bytes()) != save_backup_hash:
            raise PatcherError("save backup is missing or has a hash mismatch")
        try:
            with zipfile.ZipFile(save_backup, "r") as archive:
                if archive.testzip() is not None:
                    raise PatcherError("save backup failed CRC verification")
                if archive.namelist() != manifest.get("save_backup_files", []):
                    raise PatcherError("save backup file list differs from the manifest")
        except zipfile.BadZipFile as error:
            raise PatcherError("save backup is not a readable ZIP archive") from error
    with zipfile.ZipFile(exe_path, "r") as archive:
        if archive.testzip() is not None:
            raise PatcherError("archive CRC verification failed")
        _validate_reverse_gold_compatible(archive)
    return {"status": "ok", "patched_sha256": actual}


def uninstall(exe_path: Path, manifest_path: Path) -> dict:
    _recover_pending(exe_path, manifest_path)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    current_hash = sha256(exe_path.read_bytes())
    if current_hash not in (manifest.get("patched_sha256"), manifest.get("original_sha256")):
        raise PatcherError("refusing uninstall because the installed executable changed")
    backup = Path(manifest["backup_path"])
    original = backup.read_bytes()
    if sha256(original) != manifest.get("original_sha256"):
        raise PatcherError("refusing uninstall because the backup hash changed")
    if current_hash != manifest.get("original_sha256"):
        _replace_transactionally(exe_path, original)
    archived = manifest_path.with_name("multi_hero_manifest.uninstalled.json")
    os.replace(manifest_path, archived)
    return {"status": "uninstalled", "restored_sha256": sha256(exe_path.read_bytes())}


def main() -> int:
    parser = argparse.ArgumentParser(description="Transactional Kingdom Rush Four Hero patcher")
    parser.add_argument("command", choices=("install", "verify", "uninstall"))
    parser.add_argument("--game-dir", type=Path, default=Path(r"D:\Steam\steamapps\common\Kingdom Rush"))
    parser.add_argument("--save-dir", type=Path, default=Path(os.environ.get("APPDATA", "")) / "Kingdom Rush")
    parser.add_argument("--expected-sha256", default=EXPECTED_REVERSE_GOLD_SHA256)
    args = parser.parse_args()
    exe = args.game_dir / "Kingdom Rush.exe"
    manifest = args.game_dir / "multi_hero_manifest.json"
    source = Path(__file__).resolve().parents[1] / "src" / "multi_hero.lua"
    try:
        if args.command == "install":
            result = install(exe, source, args.save_dir, manifest, args.expected_sha256, GAME_VERSION)
        elif args.command == "verify":
            result = verify(exe, manifest)
        else:
            result = uninstall(exe, manifest)
    except (OSError, PatcherError, ValueError, zipfile.BadZipFile) as error:
        parser.exit(2, f"error: {error}\n")
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

