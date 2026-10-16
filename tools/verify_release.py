from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import re


ARTIFACT_PATTERN = re.compile(
    r"KingdomRush-ModPack-Setup-[0-9]+\.[0-9]+\.[0-9]+-win-x64\.exe"
)
CHECKSUM_PATTERN = re.compile(r"([0-9a-f]{64})  ([^\r\n]+)\r?\n?\Z")


def verify_release(executable: Path, checksum_file: Path) -> None:
    if not ARTIFACT_PATTERN.fullmatch(executable.name):
        raise ValueError(f"invalid artifact filename: {executable.name}")
    if not executable.is_file():
        raise ValueError(f"artifact does not exist: {executable}")
    if checksum_file.name != executable.name + ".sha256" or not checksum_file.is_file():
        raise ValueError(f"checksum file does not exist or has the wrong name: {checksum_file}")

    allowed = {executable.name, checksum_file.name}
    extras = sorted(path.name for path in executable.parent.iterdir() if path.name not in allowed)
    if extras:
        raise ValueError(f"unexpected release file: {extras[0]}")

    try:
        checksum_text = checksum_file.read_text(encoding="ascii")
    except UnicodeDecodeError as error:
        raise ValueError("invalid checksum format") from error
    match = CHECKSUM_PATTERN.fullmatch(checksum_text)
    if not match or match.group(2) != executable.name:
        raise ValueError("invalid checksum format")
    actual = hashlib.sha256(executable.read_bytes()).hexdigest()
    if actual != match.group(1):
        raise ValueError(f"SHA-256 mismatch: expected {match.group(1)}, got {actual}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate a staged Kingdom Rush patcher release")
    parser.add_argument("executable", type=Path)
    parser.add_argument("checksum_file", type=Path)
    args = parser.parse_args()
    try:
        verify_release(args.executable, args.checksum_file)
    except (OSError, ValueError) as error:
        parser.exit(2, f"error: {error}\n")
    print(f"verified {args.executable.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
