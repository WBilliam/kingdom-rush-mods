from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
import tempfile
import zipfile


ENTRY_NAME = "reverse_gold.lua"


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def export_reverse_gold_payload(
    source_exe: Path,
    destination: Path,
    expected_source_sha256: str,
    expected_payload_sha256: str,
) -> None:
    image = source_exe.read_bytes()
    actual_source_hash = _sha256(image)
    if actual_source_hash.lower() != expected_source_sha256.lower():
        raise ValueError(
            "source executable SHA-256 mismatch: "
            f"expected {expected_source_sha256}, got {actual_source_hash}"
        )

    with zipfile.ZipFile(source_exe, "r") as archive:
        names = archive.namelist()
        normalized = [name.casefold() for name in names]
        if len(normalized) != len(set(normalized)):
            raise ValueError("duplicate archive entries")
        if ENTRY_NAME not in names:
            raise ValueError(f"missing archive entry: {ENTRY_NAME}")
        payload = archive.read(ENTRY_NAME)

    actual_payload_hash = _sha256(payload)
    if actual_payload_hash.lower() != expected_payload_sha256.lower():
        raise ValueError(
            "Reverse Gold payload SHA-256 mismatch: "
            f"expected {expected_payload_sha256}, got {actual_payload_hash}"
        )

    destination.parent.mkdir(parents=True, exist_ok=True)
    handle, temporary_name = tempfile.mkstemp(
        prefix=destination.name + ".", suffix=".tmp", dir=destination.parent
    )
    temporary = Path(temporary_name)
    try:
        with os.fdopen(handle, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, destination)
    finally:
        if temporary.exists():
            temporary.unlink()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Export the author-owned Reverse Gold module from a verified artifact"
    )
    parser.add_argument("source_exe", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--expected-source-sha256", required=True)
    parser.add_argument("--expected-payload-sha256", required=True)
    args = parser.parse_args()
    try:
        export_reverse_gold_payload(
            args.source_exe,
            args.destination,
            args.expected_source_sha256,
            args.expected_payload_sha256,
        )
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        parser.exit(2, f"error: {error}\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
