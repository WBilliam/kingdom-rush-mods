from __future__ import annotations

import hashlib
from pathlib import Path
import tempfile
import unittest

from tools.verify_release import verify_release


NAME = "KingdomRush-ModPack-Setup-0.1.0-win-x64.exe"


class ReleaseVerifierTests(unittest.TestCase):
    def stage(self) -> tuple[tempfile.TemporaryDirectory, Path, Path]:
        temporary = tempfile.TemporaryDirectory()
        root = Path(temporary.name)
        executable = root / NAME
        executable.write_bytes(b"synthetic-windows-executable")
        digest = hashlib.sha256(executable.read_bytes()).hexdigest()
        checksum = root / f"{NAME}.sha256"
        checksum.write_text(f"{digest}  {NAME}\n", encoding="ascii")
        return temporary, executable, checksum

    def test_accepts_valid_executable_and_checksum_pair(self) -> None:
        temporary, executable, checksum = self.stage()
        with temporary:
            verify_release(executable, checksum)

    def test_rejects_mismatched_checksum(self) -> None:
        temporary, executable, checksum = self.stage()
        with temporary:
            checksum.write_text(f"{'0' * 64}  {NAME}\n", encoding="ascii")
            with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                verify_release(executable, checksum)

    def test_rejects_malformed_checksum_text(self) -> None:
        temporary, executable, checksum = self.stage()
        with temporary:
            checksum.write_text("not-a-checksum\n", encoding="ascii")
            with self.assertRaisesRegex(ValueError, "checksum format"):
                verify_release(executable, checksum)

    def test_rejects_wrong_or_missing_artifact(self) -> None:
        temporary, executable, checksum = self.stage()
        with temporary:
            wrong = executable.with_name("setup.exe")
            executable.rename(wrong)
            with self.assertRaisesRegex(ValueError, "artifact filename"):
                verify_release(wrong, checksum)
            with self.assertRaisesRegex(ValueError, "does not exist"):
                verify_release(executable, checksum)

    def test_rejects_any_extra_release_file(self) -> None:
        forbidden_names = [
            "reverse_gold.lua", "Kingdom Rush.exe", "game.bak", "manifest.json",
            "save.zip", ".env", "api_key.txt",
        ]
        for forbidden in forbidden_names:
            with self.subTest(forbidden=forbidden):
                temporary, executable, checksum = self.stage()
                with temporary:
                    (executable.parent / forbidden).write_text("secret=example", encoding="utf-8")
                    with self.assertRaisesRegex(ValueError, "unexpected release file"):
                        verify_release(executable, checksum)


if __name__ == "__main__":
    unittest.main()
