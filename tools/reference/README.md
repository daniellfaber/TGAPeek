# Reference outputs from Pillow

`tests/TgaViewer.Core.Tests/ReferenceTests.cs` compares the viewer's decoder with an independent one, [Pillow](https://python-pillow.org). Pillow's outputs are committed, so the tests run without Python. This folder holds the script that produced them.

## The isolated environment

Pillow is installed only in `tools\reference\.venv`, a Python virtual environment inside the project. Nothing is installed system-wide. The folder is git-ignored, and deleting it removes everything.

It was created on 2026-10-25 (approved by the project owner) with Python 3.11.0 and pip 22.3:

```
py -3.11 -m venv tools\reference\.venv
tools\reference\.venv\Scripts\python.exe -m pip install pillow==13.0.0 --only-binary=:all: --disable-pip-version-check
```

The second command downloads one file from PyPI: `pillow-13.0.0-cp311-cp311-win_amd64.whl`, 7.3 MB. `--only-binary=:all:` stops pip from building anything from source. Pillow is licensed under MIT-CMU. It is a development tool only and is never shipped with the viewer.

## Regenerating the references

Only needed after changing `TgaReferenceSamples` (in `tests/TgaViewer.TestSupport`):

```
dotnet run --project tools/TgaViewer.SampleGenerator -- tests/TgaViewer.Core.Tests/Reference/inputs --reference
tools\reference\.venv\Scripts\python.exe -I tools\reference\make_references.py tests\TgaViewer.Core.Tests\Reference\inputs tests\TgaViewer.Core.Tests\Reference
dotnet test -c Release --filter FullyQualifiedName~ReferenceTests
```

- **Inputs:** 35 small files (mostly 23 × 17) with seeded random pixels. Random values exercise every bit of every channel, and the odd sizes catch row-stride mistakes.
- **Outputs:** `make_references.py` writes Pillow's pixels as raw RGBA (`Reference/pillow/*.rgba`, top row first). It also writes `Reference/pillow-manifest.json`, which records each input's SHA-256, so the tests can prove the outputs belong to those exact bytes.
- **Different Pillow version:** if the outputs come from another Pillow version, a test fails until the agreement levels below have been reviewed for that version.

## How Pillow 13.0.0 reads TGA, and what is compared

The agreement levels come from Pillow's source (`PIL/TgaImagePlugin.py`) and from probing its pixel unpacker with known values. They never come from whether the outputs happen to match.

| Level | Meaning | Files |
|---|---|---|
| Exact | Every channel of every pixel must be identical | 23 files: 24-bit true-colour in all four origins, 32-bit true-colour with and without declared alpha bits, an image ID, 8-bit and 16-bit greyscale, 8-bit indices with 24- and 32-bit palettes (including a palette starting at index 16), RLE within and across rows (including 300-pixel rows), and TGA 2.0 files with attributes types 3 and 4 |
| Colour | Red, green and blue identical; alpha not compared | `tga2-attributes-0`: Pillow forces alpha to 255 when the extension area declares no alpha. The viewer keeps the stored values and decides only when displaying. |
| 5-bit colour | The stored 5-bit red, green and blue values must be identical; alpha not compared | Four 16-bit files. Pillow expands 5-bit values as `v × 255 / 31`, truncated (the viewer replicates bits, so 8-bit values differ by up to 1). It also reads the attribute bit as *inverted* alpha (rawmode `BGRA;15Z`: a set bit becomes transparent). GIMP and ImageMagick read a set bit as opaque, as the viewer does (checked in their source on 2026-10-25: GIMP `plug-ins/common/file-tga.c`, ImageMagick `coders/tga.c`). |
| Excluded | Not compared | 15-bit images and 15-bit palette entries, and 16-bit palette indices (Pillow cannot read them). True-colour files carrying a colour map (Pillow fails: "unrecognized image mode"). Interleaved files (Pillow ignores the interleave bits). An RLE file with a *run* packet crossing a row (Pillow rejects it: "buffer overrun"; it accepts *raw* packets that cross rows). |

**Result (2026-10-25):**
- Pillow decoded 30 of the 35 files.
- All 28 compared files agree at their stated level.
- A deliberately wrong classification makes the test fail, which shows the comparison is not vacuous.
