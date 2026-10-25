"""Decode the reference inputs with Pillow and record what Pillow produced.

Run only inside the isolated environment (see README.md in this folder):

    tools\\reference\\.venv\\Scripts\\python.exe -I tools\\reference\\make_references.py <inputs> <output>

For every <inputs>/*.tga this writes <output>/pillow/<name>.rgba (Pillow's pixels converted to RGBA,
top row first, 4 bytes per pixel) and records the outcome in <output>/pillow-manifest.json, with the
SHA-256 of the input so the tests can prove the outputs belong to those exact bytes. A file Pillow
cannot read is recorded with its error message. Nothing else is read or written.
"""

import hashlib
import json
import pathlib
import sys

import PIL
from PIL import Image


def main(inputs: pathlib.Path, output: pathlib.Path) -> None:
    pixel_folder = output / "pillow"
    pixel_folder.mkdir(parents=True, exist_ok=True)
    for stale in pixel_folder.glob("*.rgba"):
        stale.unlink()

    cases = []
    for path in sorted(inputs.glob("*.tga")):
        data = path.read_bytes()
        case = {"file": path.name, "sha256": hashlib.sha256(data).hexdigest()}
        try:
            with Image.open(path, formats=["TGA"]) as image:
                image.load()
                case["mode"] = image.mode
                case["width"], case["height"] = image.size
                rgba = image.convert("RGBA").tobytes()
            pixels = pixel_folder / (path.stem + ".rgba")
            pixels.write_bytes(rgba)
            case["status"] = "decoded"
            case["pixels"] = "pillow/" + pixels.name
        except Exception as error:  # Any failure is a result to record, not a reason to stop.
            case["status"] = "rejected"
            case["error"] = f"{type(error).__name__}: {error}"
        cases.append(case)

    manifest = {
        "decoder": f"Pillow {PIL.__version__}",
        "python": sys.version.split()[0],
        "cases": cases,
    }
    (output / "pillow-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    decoded = sum(case["status"] == "decoded" for case in cases)
    print(f"{decoded} of {len(cases)} files decoded by Pillow {PIL.__version__}; outputs in {output}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("usage: make_references.py <input folder> <output folder>")
    main(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]))
