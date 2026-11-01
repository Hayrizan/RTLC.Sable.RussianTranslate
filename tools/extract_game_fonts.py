"""Prepare local source fonts from an owned Sable installation; never commit output."""
import argparse
import json
from pathlib import Path
import UnityPy

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game", type=Path, help="Directory containing Sable.exe")
    parser.add_argument("--output", type=Path, default=ROOT / "Fonts/Original")
    args = parser.parse_args()
    source = args.game / "Sable_Data/data.unity3d"
    if not source.is_file():
        parser.error("Sable_Data/data.unity3d not found")
    manifest = json.loads((ROOT / "tools/bundle-fonts.json").read_text(encoding="utf-8"))
    required = {Path(name).stem: Path(name).name for name in manifest if name.startswith("Original/")}
    found = {}
    for obj in UnityPy.load(str(source)).objects:
        if obj.type.name != "Font":
            continue
        data = obj.read()
        if data.m_Name in required:
            raw = bytes(data.m_FontData)
            if not raw:
                raise ValueError("Empty font: " + data.m_Name)
            if data.m_Name in found and found[data.m_Name] != raw:
                raise ValueError("Ambiguous font: " + data.m_Name)
            found[data.m_Name] = raw
    missing = set(required) - set(found)
    if missing:
        raise ValueError("Missing source fonts: " + ", ".join(sorted(missing)))
    # Check every destination before writing any file; preserve local changes.
    for name, raw in found.items():
        target = args.output / required[name]
        if target.exists() and target.read_bytes() != raw:
            raise FileExistsError("Different font already exists: " + str(target))
    args.output.mkdir(parents=True, exist_ok=True)
    for name, raw in found.items():
        (args.output / required[name]).write_bytes(raw)
    print(f"Prepared {len(found)} local fonts in {args.output}. Do not commit extracted game assets.")


if __name__ == "__main__":
    main()
