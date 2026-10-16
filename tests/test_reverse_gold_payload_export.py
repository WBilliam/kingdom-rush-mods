from __future__ import annotations

import hashlib
import io
from pathlib import Path
import tempfile
import unittest
import warnings
import zipfile

from tools.export_reverse_gold_payload import export_reverse_gold_payload


PREFIX = b"MZ" + b"synthetic-prefix" * 4
PAYLOAD = b"return { version = '1.0.1' }\n"


def fused(entries: list[tuple[str, bytes]]) -> bytes:
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, data in entries:
            archive.writestr(name, data)
    return PREFIX + stream.getvalue()


class ReverseGoldPayloadExportTests(unittest.TestCase):
    def setUp(self) -> None:
        self.image = fused([
            ("main.lua", b"game-owned-main"),
            ("reverse_gold.lua", PAYLOAD),
            ("images/game-owned-sentinel.bin", b"must-not-be-exported"),
        ])
        self.source_hash = hashlib.sha256(self.image).hexdigest()
        self.payload_hash = hashlib.sha256(PAYLOAD).hexdigest()

    def test_exports_only_reverse_gold_module_from_verified_artifact(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "owned-mod.exe"
            destination = root / "reverse_gold.lua"
            source.write_bytes(self.image)

            export_reverse_gold_payload(
                source, destination, self.source_hash, self.payload_hash
            )

            self.assertEqual(PAYLOAD, destination.read_bytes())
            self.assertEqual(["owned-mod.exe", "reverse_gold.lua"], sorted(p.name for p in root.iterdir()))

    def test_rejects_wrong_source_executable_hash(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "owned-mod.exe"
            destination = root / "reverse_gold.lua"
            source.write_bytes(self.image)

            with self.assertRaisesRegex(ValueError, "source executable SHA-256 mismatch"):
                export_reverse_gold_payload(source, destination, "0" * 64, self.payload_hash)

            self.assertFalse(destination.exists())

    def test_rejects_wrong_payload_hash(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "owned-mod.exe"
            destination = root / "reverse_gold.lua"
            source.write_bytes(self.image)

            with self.assertRaisesRegex(ValueError, "Reverse Gold payload SHA-256 mismatch"):
                export_reverse_gold_payload(source, destination, self.source_hash, "f" * 64)

            self.assertFalse(destination.exists())

    def test_rejects_duplicate_archive_names(self) -> None:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            image = fused([
                ("reverse_gold.lua", PAYLOAD),
                ("reverse_gold.lua", b"shadowed"),
            ])
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "owned-mod.exe"
            destination = root / "reverse_gold.lua"
            source.write_bytes(image)

            with self.assertRaisesRegex(ValueError, "duplicate archive entries"):
                export_reverse_gold_payload(
                    source,
                    destination,
                    hashlib.sha256(image).hexdigest(),
                    self.payload_hash,
                )

            self.assertFalse(destination.exists())


if __name__ == "__main__":
    unittest.main()
