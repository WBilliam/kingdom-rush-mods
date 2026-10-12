from __future__ import annotations

import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import zipfile

from tools import patch_kingdom_rush as patcher


PREFIX = b"MZ" + b"synthetic-pe-prefix" * 8
ORIGINAL_MAIN = b"print('original preserved main')\n"
REVERSE_WRAPPER = b"""local chunk = assert(love.filesystem.load(\"reverse_gold.lua\"))
local runtime = assert(chunk())
runtime.bootstrap()
local original = assert(love.filesystem.load(\"_reverse_gold_main_orig.lua\"))
assert(pcall(original))
runtime.after_main()
"""


def fused(entries: list[tuple[str, bytes]]) -> bytes:
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, data in entries:
            archive.writestr(name, data)
    return PREFIX + stream.getvalue()


class ArchivePatcherTests(unittest.TestCase):
    def setUp(self):
        self.image = fused([
            ("main.lua", REVERSE_WRAPPER),
            ("reverse_gold.lua", b"return {version='1.0.1'}"),
            ("_reverse_gold_main_orig.lua", ORIGINAL_MAIN),
            ("asset.bin", bytes(range(64))),
        ])
        self.expected_hash = hashlib.sha256(self.image).hexdigest()

    def test_build_preserves_reverse_gold_and_all_unrelated_entries(self):
        result = patcher.build_patched_image(
            self.image, b"return {bootstrap=function() end, after_main=function() end}",
            expected_sha256=self.expected_hash,
        )
        with zipfile.ZipFile(io.BytesIO(result.image)) as archive:
            self.assertEqual(b"return {version='1.0.1'}", archive.read("reverse_gold.lua"))
            self.assertEqual(ORIGINAL_MAIN, archive.read("_reverse_gold_main_orig.lua"))
            self.assertEqual(bytes(range(64)), archive.read("asset.bin"))
            self.assertIn("multi_hero.lua", archive.namelist())
            self.assertEqual(len(archive.namelist()), len(set(archive.namelist())))
        self.assertTrue(result.image.startswith(PREFIX))

    def test_wrapper_bootstraps_both_mods_then_main_then_post_hooks(self):
        result = patcher.build_patched_image(self.image, b"return {}", self.expected_hash)
        with zipfile.ZipFile(io.BytesIO(result.image)) as archive:
            wrapper = archive.read("main.lua").decode("utf-8")
        positions = [
            wrapper.index('load("reverse_gold.lua")'),
            wrapper.index("reverse_runtime.bootstrap()"),
            wrapper.index('load("multi_hero.lua")'),
            wrapper.index("multi_runtime.bootstrap()"),
            wrapper.index('load("_reverse_gold_main_orig.lua")'),
            wrapper.index("reverse_runtime.after_main()"),
            wrapper.index("multi_runtime.after_main()"),
        ]
        self.assertEqual(sorted(positions), positions)

    def test_wrong_hash_and_wrong_wrapper_are_rejected(self):
        with self.assertRaises(patcher.CompatibilityError):
            patcher.build_patched_image(self.image, b"return {}", "0" * 64)
        wrong = fused([("main.lua", b"print('vanilla')")])
        with self.assertRaises(patcher.CompatibilityError):
            patcher.build_patched_image(wrong, b"return {}", hashlib.sha256(wrong).hexdigest())

    def test_install_refuses_when_save_directory_cannot_be_backed_up(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            exe = root / "Kingdom Rush.exe"
            source = root / "multi_hero.lua"
            exe.write_bytes(self.image)
            source.write_bytes(b"return {bootstrap=function() end, after_main=function() end}")
            with self.assertRaises(patcher.PatcherError):
                patcher.install(exe, source, root / "missing-saves", root / "manifest.json",
                                self.expected_hash, "test-build")

    def test_install_verify_and_uninstall_restore_exact_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            exe = root / "Kingdom Rush.exe"
            source = root / "multi_hero.lua"
            manifest = root / "multi_hero_manifest.json"
            saves = root / "saves"
            saves.mkdir()
            (saves / "slot.lua").write_text("return {slot=1}", encoding="utf-8")
            exe.write_bytes(self.image)
            source.write_bytes(b"return {bootstrap=function() end, after_main=function() end}")
            installed = patcher.install(exe, source, saves, manifest, self.expected_hash, "test-build")
            self.assertNotEqual(self.image, exe.read_bytes())
            self.assertEqual("ok", patcher.verify(exe, manifest)["status"])
            with self.assertRaises(patcher.AlreadyInstalledError):
                patcher.install(exe, source, saves, manifest, self.expected_hash, "test-build")
            patcher.uninstall(exe, manifest)
            self.assertEqual(self.image, exe.read_bytes())
            archived_path = manifest.with_name("multi_hero_manifest.uninstalled.json")
            archived = json.loads(archived_path.read_text())
            self.assertEqual(installed["patched_sha256"], archived["patched_sha256"])

    def test_verify_rejects_missing_or_corrupt_save_backup(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            exe = root / "Kingdom Rush.exe"
            source = root / "multi_hero.lua"
            manifest = root / "multi_hero_manifest.json"
            saves = root / "saves"
            saves.mkdir()
            (saves / "slot.lua").write_text("slot", encoding="utf-8")
            exe.write_bytes(self.image)
            source.write_bytes(b"return {bootstrap=function() end, after_main=function() end}")
            installed = patcher.install(exe, source, saves, manifest, self.expected_hash, "test-build")
            backup = Path(installed["save_backup_path"])
            backup.write_bytes(b"corrupt")
            with self.assertRaises(patcher.PatcherError):
                patcher.verify(exe, manifest)

    def test_uninstall_retry_finishes_when_executable_is_already_restored(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            exe = root / "Kingdom Rush.exe"
            source = root / "multi_hero.lua"
            manifest = root / "multi_hero_manifest.json"
            saves = root / "saves"
            saves.mkdir()
            exe.write_bytes(self.image)
            source.write_bytes(b"return {bootstrap=function() end, after_main=function() end}")
            installed = patcher.install(exe, source, saves, manifest, self.expected_hash, "test-build")
            exe.write_bytes(Path(installed["backup_path"]).read_bytes())
            result = patcher.uninstall(exe, manifest)
            self.assertEqual("uninstalled", result["status"])
            self.assertEqual(self.image, exe.read_bytes())

    def test_install_rolls_back_executable_if_manifest_cannot_be_written(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            exe = root / "Kingdom Rush.exe"
            source = root / "multi_hero.lua"
            manifest = root / "multi_hero_manifest.json"
            saves = root / "saves"
            saves.mkdir()
            exe.write_bytes(self.image)
            source.write_bytes(b"return {bootstrap=function() end, after_main=function() end}")
            real_atomic_write = patcher._atomic_write_json
            writes = 0
            def fail_final(path, data):
                nonlocal writes
                writes += 1
                if writes == 2:
                    raise OSError("disk full")
                return real_atomic_write(path, data)
            with mock.patch.object(patcher, "_atomic_write_json", side_effect=fail_final):
                with self.assertRaises(OSError):
                    patcher.install(exe, source, saves, manifest, self.expected_hash, "test-build")
            self.assertEqual(self.image, exe.read_bytes())
            self.assertFalse(manifest.exists())

    def test_pending_install_journal_recovers_after_replacement_crash(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            exe = root / "Kingdom Rush.exe"
            source = root / "multi_hero.lua"
            manifest = root / "multi_hero_manifest.json"
            saves = root / "saves"
            saves.mkdir()
            exe.write_bytes(self.image)
            source.write_bytes(b"return {bootstrap=function() end, after_main=function() end}")
            patcher.install(exe, source, saves, manifest, self.expected_hash, "test-build")
            pending = patcher.pending_manifest_path(manifest)
            manifest.replace(pending)
            with self.assertRaises(patcher.AlreadyInstalledError):
                patcher.install(exe, source, saves, manifest, self.expected_hash, "test-build")
            self.assertTrue(manifest.exists())
            self.assertFalse(pending.exists())
            self.assertEqual("ok", patcher.verify(exe, manifest)["status"])


if __name__ == "__main__":
    unittest.main()

