import io
from pathlib import Path
import tempfile
import unittest
import zipfile

from tools.patch_kingdom_rush import (
    CompatibilityError,
    WRAPPER,
    build_patched_image,
    install,
    sha256,
    uninstall,
    verify,
)


PREFIX = b"synthetic-love-prefix"
LUA = b"return { bootstrap=function() end, after_main=function() end }"


def original_image() -> bytes:
    archive = io.BytesIO()
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as target:
        target.writestr("main.lua", b"return 'original main'")
        target.writestr("data/keep.txt", b"unchanged")
    return PREFIX + archive.getvalue()


class ReverseGoldPatcherTests(unittest.TestCase):
    def test_build_preserves_original_main_and_unrelated_entries(self):
        original = original_image()
        result = build_patched_image(original, LUA, sha256(original))
        with zipfile.ZipFile(io.BytesIO(result.image)) as archive:
            self.assertEqual("main.lua", archive.namelist()[-2])
            self.assertEqual("reverse_gold.lua", archive.namelist()[-1])
            self.assertEqual(WRAPPER, archive.read("main.lua"))
            self.assertEqual(b"return 'original main'", archive.read("_reverse_gold_main_orig.lua"))
            self.assertEqual(LUA, archive.read("reverse_gold.lua"))
            self.assertEqual(b"unchanged", archive.read("data/keep.txt"))
            self.assertEqual(len(archive.namelist()), len(set(archive.namelist())))
            self.assertIsNone(archive.testzip())

    def test_wrong_hash_and_already_patched_archives_are_rejected(self):
        original = original_image()
        with self.assertRaises(CompatibilityError):
            build_patched_image(original, LUA, "0" * 64)
        patched = build_patched_image(original, LUA, sha256(original)).image
        with self.assertRaises(CompatibilityError):
            build_patched_image(patched, LUA, sha256(patched))

    def test_install_verify_and_uninstall_restore_exact_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            game = root / "game"
            saves = root / "saves"
            game.mkdir()
            saves.mkdir()
            (saves / "slot.lua").write_text("save", encoding="utf-8")
            exe = game / "Kingdom Rush.exe"
            source = root / "reverse_gold.lua"
            manifest = game / "reverse_gold_manifest.json"
            original = original_image()
            exe.write_bytes(original)
            source.write_bytes(LUA)
            installed = install(exe, source, saves, manifest, sha256(original), "test-build")
            self.assertEqual("ok", verify(exe, manifest)["status"])
            self.assertEqual(installed["patched_sha256"], sha256(exe.read_bytes()))
            self.assertEqual("uninstalled", uninstall(exe, manifest)["status"])
            self.assertEqual(original, exe.read_bytes())
