"""Check built assets against editable sources; requires UnityPy and Pillow."""
from pathlib import Path
import hashlib
import json
import UnityPy
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
BUNDLE = ROOT / "build/bundle/rtlc.bundle"
env = UnityPy.load(str(BUNDLE))
expected = {}
font_manifest = json.loads((ROOT / "tools/bundle-fonts.json").read_text(encoding="utf-8"))
assert len(font_manifest) == len(set(name.lower() for name in font_manifest)), "Duplicate font manifest entry"
for name in font_manifest:
    p = ROOT / "Fonts" / name
    assert p.is_file(), f"Missing source font: {name}"
    expected["fonts/" + name.lower()] = ("Font", p)
for p in (ROOT / "Textures").glob("*.png"):
    expected["textures/" + p.stem.lower()] = ("Texture2D", p)
expected["translation"] = ("TextAsset", ROOT / "Translations/translation.txt")
seen = set()
for name, obj in env.container.items():
    assert name in expected, f"Unexpected asset: {name}"
    kind, source = expected[name]
    # UnityPy exposes font material/atlas dependencies under the same address.
    if obj.type.name != kind:
        continue
    assert name not in seen, f"Duplicate asset: {name}"
    seen.add(name)
    data = obj.read()
    if kind == "Font":
        assert bytes(data.m_FontData) == source.read_bytes(), name
    elif kind == "Texture2D":
        original = Image.open(source).convert("RGBA")
        decoded = data.image.convert("RGBA")
        assert decoded.size == original.size == (2048, 999), name
        assert decoded.tobytes() == original.tobytes(), name
    else:
        assert data.m_Script.encode("utf-8", errors="surrogateescape") == source.read_bytes(), name
assert seen == set(expected), f"Missing assets: {set(expected) - seen}"
report = {
    "bundle_sha256": hashlib.sha256(BUNDLE.read_bytes()).hexdigest(),
    "translation_sha256": hashlib.sha256((ROOT / "Translations/translation.txt").read_bytes()).hexdigest(),
    "asset_count": len(seen),
    "font_bytes_exact": sum(kind == "Font" for kind, _ in expected.values()),
    "logo_pixels_exact": sum(kind == "Texture2D" for kind, _ in expected.values()),
    "assets": sorted(seen),
}
destination = ROOT / "build/bundle/validation.json"
destination.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({k: v for k, v in report.items() if k != "assets"}, indent=2))
