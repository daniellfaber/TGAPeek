# TGAPeek — Implementation Plan

> **Document status** *(2026-10-25)*
> - **Version 1 is implemented** (see §14). The owner asked for the complete Version 1 without phase-by-phase approval, with routine technical decisions made within the brief. **§14 records what was actually built, the decisions taken on the §13 issues, test results and what remains blocked.** Where §1–§11 differ from §14, §14 is current.
> - **Authority:** [PROJECT_BRIEF.md](PROJECT_BRIEF.md) is the authoritative product specification. This plan describes how the brief is delivered. Working rules are in [CLAUDE.md](../CLAUDE.md); test evidence is in [FORMAT_COVERAGE.md](FORMAT_COVERAGE.md).
> - **History:** this is Revision 3 (approved in principle), copied into the repository with a status note, the Phase 0 result (§9), a pointer in place of the old CLAUDE.md draft (§10), notes on two unverified claims and decision D6 (§8, §12), §13 (open issues against the brief) and §14 (the Version 1 record).

**Revision 3 changes:**
- Exact compositing in our own renderer, so GDI+ only ever receives opaque buffers.
- Allocation checks for every buffer.
- Pillow and sample-file approval gates spelled out.
- Decisions D4–D6 resolved.
- D7 decided by measurement in Phase 6.

## Context

You want a lightweight, native Windows viewer for TGA (Truevision Targa) images that:
- opens as many real-world TGA variants as possible,
- shows pixel and alpha data exactly as stored, and
- handles malformed files with defined, tested behaviour.

Revision 2 adds your eight requirements. These are:
- correct TGA 2.0 alpha semantics,
- the advanced TGA 2.0 structures,
- interleaving,
- inspection views,
- a self-contained single-file build,
- independent reference testing,
- realistic decoder guarantees, and
- unchanged scope and permissions.

**Environment found (read-only checks, before Phase 0):**
| Item | Status |
|---|---|
| Project folder | Empty except `.git`. Nothing to preserve. |
| Git | 2.56. Branch `master`. No commits, no remote. |
| .NET | SDK **10.0.402** (LTS) and 9.0.319. Windows Desktop runtimes 8, 9 and 10. |
| Self-contained runtime packs | **Not present locally.** A self-contained publish must download them from nuget.org, so I'll ask first. |
| Reference decoders on this PC | Python 3.11 is installed, but **without Pillow**. No ImageMagick, ffmpeg, netpbm or GIMP. (`convert.exe` found on PATH is Windows' disk tool, not ImageMagick.) |

**Format facts verified against the TGA 2.0 spec text and the netpbm source**:
- **Attributes Type (extension field 24):**
  - 0 = no alpha data included
  - 1 = undefined data, can be ignored
  - 2 = undefined data, but should be retained
  - 3 = useful alpha
  - 4 = premultiplied alpha
  - 5–127 reserved, 128–255 unassigned
- **Extension size** should be 495.
- **Footer** is 26 bytes: extension offset (4), developer directory offset (4), `TRUEVISION-XFILE` (16), `.`, `0x00`.
- **Developer directory:** a SHORT count, then 10-byte entries (tag SHORT, offset LONG, size LONG).
- **Colour-correction table:** 256 × 4 SHORT values (A,R,G,B, each 0–65535), 2,048 bytes.
- **Postage stamp:** a byte for width, a byte for height, then pixels in the **same format as the main image but without compression**. Truevision recommends no larger than 64×64.
- **Scan-line table:** one 4-byte file offset per scan line, in the order the lines were saved.
- **RLE:** the spec says run-length packets "should not" cross scan lines. Real encoders do it anyway, so we accept it.
- **Interleaving** (descriptor bits 7–6):
  - TGA 1.0 defines 00 = none, 01 = two-way (even/odd), 10 = four-way, 11 = reserved.
  - TGA 2.0 instead says these bits "must be zero".
  - Neither spec describes the actual row layout. The only concrete definition is netpbm's: file rows fill logical rows 0, 2, 4, … then 1, 3, 5, … (four-way: 0, 4, 8, … then 1, 5, …). The top/bottom origin flip is applied afterwards. I'll implement that interpretation and document it.

---

## 1. Architecture (unchanged shape: Core / App / Tests, WinForms, no export)

```
TGAPeek.slnx
global.json                      pin SDK 10.0.402, rollForward latestFeature
src/TGAPeek.Core/              net10.0, no UI references, NO NuGet packages
    TgaHeader.cs                 18-byte header parse + validation
    TgaDecoder.cs                byte[] -> TgaDecodeResult (pure, no file I/O)
    PixelReaders.cs              8/15/16/24/32-bit + palette readers
    RowMapper.cs                 file-order row/column -> image (x,y): origin bits 4/5 + interleave
    Tga2Metadata.cs              footer, extension area, developer dir, CCT, stamp, scan-line table
    TgaImage.cs                  decoded data (see "Data model")
    DisplayRenderer.cs           TgaImage + DisplayOptions -> display buffer (pure, testable)
    TgaDecodeOptions.cs          limits, AllowPartial
    TgaException.cs              Invalid / Unsupported / LimitExceeded (+ ReasonCode)
src/TGAPeek.App/               net10.0-windows WinForms exe, NO NuGet packages
    Program.cs, MainForm.cs, ImageCanvas.cs, PixelInspectorPanel.cs, InfoPanel.cs, BitmapConverter.cs
tests/TGAPeek.Core.Tests/      xUnit (test-only packages)
    Golden/                      hand-authored byte arrays + hand-written expected pixels
    Builder/TgaTestFileBuilder.cs  combinatorial generator (itself checked against Golden/)
    Reference/                   independent-decoder expected outputs (needs approval, see §6)
```

**Why this shape:** all parsing of untrusted bytes lives in one pure function, `byte[] → result`, which we can test thoroughly without a UI. The display logic (`DisplayRenderer`) is also pure and lives in Core, so alpha policies and channel views get unit tests too. The App only draws buffers it is given.

### Data model: raw data is never altered
- `TgaImage.Pixels` is BGRA, 8 bits per channel, top-left origin. Values come straight from the file:
  - 24/32-bit and 8-bit grey are copied unchanged.
  - 15/16-bit channels are expanded with `(v<<3)|(v>>2)`, an exact, reversible mapping.
  - **Premultiplied data stays premultiplied.** It is never converted to straight alpha.
  - Alpha is **never** changed by any heuristic.
- `TgaImage.RawValues` (only where the stored value isn't already an 8-bit channel): palette indices (8/16-bit) and raw 15/16-bit words. The pixel inspector shows them.
- `TgaImage.Palette`: raw entries + expanded BGRA.
- `TgaImage.HasStoredAlpha` records whether the file format actually stores alpha. For 24-bit, the 255 filled in is labelled "synthetic" in the inspector.
- `TgaImage.MissingMask` (truncated files only) marks pixels that were not present in the file.
- `TgaImage.AlphaInfo`: descriptor alpha bits, extension attributes type (if valid), and scan results (all zero / all 255 / mixed).
- `TgaImage.Metadata` (optional structures, each with a Present / Valid / Invalid status) and `TgaImage.Warnings`.

## 2. Alpha: decoded data vs. display policy

The decoder only **records** facts. `DisplayRenderer` decides how to **show** them, and the user can always override it (View ▸ Alpha: **Auto** / **Use alpha** / **Ignore alpha**).

**Auto** policy (the default):
| Situation | Auto display | Notes |
|---|---|---|
| Ext. attributes type 0 (no alpha) | Opaque | Warning if the file's alpha bits are non-zero anyway |
| Type 1 (undefined, ignorable) | Opaque | Data kept. Visible in the inspector and alpha view. |
| Type 2 (undefined, retain) | Opaque | Data kept. "Use alpha" shows it. |
| Type 3 (useful alpha) | Straight alpha | |
| Type 4 (premultiplied) | Premultiplied composite | Uses the premultiplied formula (below). Stored values are never converted. |
| Type 5–255 / no ext. area | See next rows | Unknown types → warning |
| No ext. area, 32bpp or 16bpp with descriptor alpha bits > 0 | Straight alpha | |
| No ext. area, 32bpp, alpha bits = 0 | Straight alpha *(D6, approved)* | Common in real files. Status bar notes it. |
| Any "use alpha" case where **every** alpha value is 0 | **Opaque** (compatibility heuristic) | Display only. Status bar says "alpha all zero — shown opaque" and offers an override. |
| 16bpp attribute bit when alpha bits ≠ 1 | Opaque | The bit is still visible in the inspector |

The all-zero heuristic and every other Auto choice are labelled in the status bar, so nothing is applied silently.

### Exact compositing (no alpha-representation mismatch with GDI+)
- **GDI+ never receives alpha.** `DisplayRenderer` (in Core, pure) renders the **visible viewport** into an **opaque** BGRX buffer:
  - it samples image pixels nearest-neighbour for each screen pixel,
  - interprets each pixel per the active policy, and
  - composites it over the screen-space checkerboard or solid background itself.
- The App wraps that buffer as `Format32bppRgb` (no alpha channel) and draws it **unscaled**. Because GDI+ never scales, blends or premultiplies, what's on screen is exactly what the renderer computed. Viewport-sized buffers also avoid GDI+'s limits on giant bitmaps.
- **Formulas** (8-bit, integer, round half up: `Div255(x) = (x + 127) / 255` for x ≥ 0):
  - **Straight:** `out = Div255(c·a + bg·(255−a))`
  - **Premultiplied:** `c' = min(c, a)` per colour channel, then `out = c' + Div255(bg·(255−a))`
    - **Invalid-data policy:** a premultiplied pixel is *valid* only if R ≤ A, G ≤ A and B ≤ A. If **any** colour channel exceeds alpha, the pixel is invalid. For display only, **each** colour channel is clamped to alpha (`c' = min(c, a)`) before compositing. Valid pixels are unaffected, because `c' = c`.
    - **Stored values are never changed.** The clamp exists only inside the renderer.
    - Invalid pixels are counted, reported as a status-bar warning ("N invalid premultiplied pixels — colour clamped to alpha for display"), and flagged "invalid premultiplied: R > A" (etc.) in the pixel inspector, which still shows the stored values.
    - **Consequence: a = 0 with non-zero RGB is invalid.** It clamps to `c' = 0`, so `out = bg` exactly (fully transparent). This is the same result as straight alpha with a = 0. No additive-light interpretation is used.
    - **No overflow is possible, so the formula needs no extra `min(255, …)`.** Because `c' ≤ a` and `Div255(bg·(255−a)) ≤ 255−a`, the result `out` is at most 255.
    - Channel views (R/G/B/A) show the raw stored values, unclamped.
  - **Ignore alpha / RGB view:** `out = c`
  - **Channel views:** `out = (v, v, v)` from the stored channel value
- **Exact tests** (expected values computed by hand in the tests):
  - semi-transparent pixels (e.g. a = 128, a = 1, a = 254), in straight and premultiplied mode
  - fully transparent pixels with non-zero RGB, in both modes. In premultiplied mode, `a = 0, RGB = (200,100,50)` must give exactly `bg` over every background.
  - invalid premultiplied pixels:
    - one channel > a, all channels > a
    - the boundary case c = a, which is valid
    - a = 255 with any c, which is always valid
  - counting of invalid pixels, and an assertion that the stored `TgaImage.Pixels` bytes are unchanged after rendering
  - over both checkerboard colours and over solid black, white and mid-grey backgrounds
  - checkerboard cell placement at several zoom and pan offsets
  - every channel view
- The optional "smooth when zoomed out" mode (off by default) is also done in our renderer, as a box filter over premultiplied-equivalent values, never through GDI+ interpolation. It gets its own tests.

## 3. Inspection & display features (v1)

- **Channel views** (View ▸ Channel):
  - **RGBA** (composited)
  - **RGB without alpha** (opaque)
  - **Red**, **Green**, **Blue** individually, as greyscale
  - **Alpha** as greyscale

  Channel views show the **stored** values. For premultiplied images, that means the premultiplied values.
- **Pixel inspector:** hover shows the value under the cursor, and a click pins a pixel. It shows:
  - x,y (image coordinates) plus the file-order row/column
  - the raw stored value (hex word, or palette index → palette entry)
  - B, G, R, A as 8-bit values
  - for 15/16-bit pixels, the original 5-bit values
  - whether alpha is stored or synthetic, and "missing" for truncated pixels

  For premultiplied pixels, the inspector also shows a *derived* straight-alpha value, clearly labelled.

  It always reads `TgaImage`, never the screen.
- **Background:** checkerboard (default) or a solid colour (configurable).
- **Zoom/pan:** fit-to-window by default, 10%–3200%, Ctrl+wheel and +/−, drag to pan, Ctrl+0 = 1:1.
  - Nearest-neighbour + `PixelOffsetMode.Half` at **all** zoom levels by default.
  - A "smooth when zoomed out" option exists, **off** by default and shown in the status bar when on.
  - **PerMonitorV2** DPI: 1:1 means one image pixel per physical screen pixel.
- **No** colour management, gamma correction, or colour-correction-table application. Gamma and the CCT appear as metadata only.
- **Info panel:**
  - header fields
  - image ID
  - extension fields (author, comments, date, job, software, key colour, aspect ratio, gamma), with text sanitised
  - developer directory entries (tag/offset/size/valid, and a short hex preview)
  - colour-correction table (present/valid, viewable as a table)
  - postage stamp (shown as a thumbnail if valid)
  - scan-line table (count, valid, and a consistency check)
  - warnings
- **Navigation:**
  - open dialog
  - drag-and-drop
  - command-line path (enables "Open with…")
  - ←/→ for the next/previous `.tga` in the same folder

  Decoding runs off the UI thread. The file is read into memory and closed immediately.
- **Not in v1:** export, save, clipboard image copy, aspect-ratio correction.

## 4. Advanced TGA 2.0 structures: parsing rules

The rules below cover the footer, the extension area, the developer directory, the colour-correction table, the postage stamp and the scan-line table.

- **Pixel decoding never depends on optional structures.** They are parsed **after** the pixels, each in its own isolated parse step that returns `Valid` / `Invalid(reason)` / `Absent`. They never throw out of the decoder. The only way they affect display is through the Auto alpha policy, and only when the extension area fully validates.
- **Footer:**
  - The file must be at least 18 + 26 bytes.
  - The signature `TRUEVISION-XFILE.\0` must be in the last 26 bytes. Otherwise the file is treated as TGA 1.0, with no warning.
- **Every offset/length:**
  - 0 means absent.
  - It must be at least 18, must not overlap the header, ID or colour map, and `offset + length` must fit before the footer.
  - Checked arithmetic is used throughout.
  - Overlap with the decoded pixel-data range (known once decoding finishes) → Invalid + warning.
- **Extension area:**
  - Size field < 495 → Invalid.
  - Size > 495 → read the 495 known bytes and warn.
  - Strings are read up to their fixed length, stopping at NUL, with control characters removed.
- **Developer directory:**
  - Count × 10 + 2 must fit in the file.
  - Each entry's range is validated on its own.
  - Contents are not interpreted, only previewed.
- **Colour-correction table:** exactly 2,048 bytes in range. It is displayed, **never applied**.
- **Postage stamp:**
  - A width and height of 0 are allowed; the image size must fit in the range.
  - Decoded uncompressed in the main image's pixel format, with the main palette for colour-mapped images. I'll assume the same orientation as the main image and document that.
  - Any failure → Invalid, no thumbnail.
- **Scan-line table:**
  - Needs height × 4 bytes in range.
  - It is **not used** to decode.
  - For uncompressed, non-interleaved images, the offsets are cross-checked against the computed row positions. A mismatch → warning.

## 5. Format coverage

- **Image types:**
  - 1, 2, 3 and their RLE versions 9, 10, 11.
  - Type 0 → `Unsupported(NoImageData)` with a friendly message.
  - Types 32/33 (Huffman/Delta/RLE) → `Unsupported`.
  - Other type numbers → `Invalid`.
- **Depths:**
  - true-colour 15/16/24/32
  - grey 8, plus 16 as grey + alpha (a non-standard compatibility variant, labelled as such)
  - colour-mapped with 8- or 16-bit indices; palette entries of 15/16/24/32 bits; non-zero `colorMapStart`
  - An index outside `[start, start+length)` → transparent black, marked in the inspector, plus a warning.
- **Origin:** all four combinations of bit 4 (right-to-left) and bit 5 (top-to-bottom).
- **Interleaving:**
  - None, two-way and four-way are supported, combined with all four origins, with RLE and uncompressed data, and with odd heights (e.g. 5 or 7 rows).
  - Reserved value 11 → `Unsupported` *(decision D5)*.
- **RLE:** raw and run packets, and packets that cross scan lines (allowed, with a note in the warnings).
- **Image ID:** read and shown in the info panel, with text sanitised.

## 6. Testing (three independent tiers)

1. **Hand-authored goldens** (no tools, no downloads):
   - Small files (e.g. 3×2, 4×5) written byte-by-byte in test code, with expected pixels typed out by hand.
   - They are **not** produced by the decoder or by our builder.
   - They cover each type/depth, all 4 origins × {none, 2-way, 4-way} interleave, palette offsets, 16-bit palette indices, RLE (including packets that cross lines), every attributes type, all-zero alpha, and 16-bit attribute bits.
   - For interleave, the expected row order is also cross-checked against a separate re-implementation of netpbm's ~10-line row-sequence algorithm inside the tests.
2. **Combinatorial generated tests:** `TgaTestFileBuilder` produces every type × depth × origin × interleave × RLE combination from random seeded pixels.
   - Generated files must round-trip exactly, and RLE vs. uncompressed must decode identically.
   - The builder is itself checked against the Tier 1 goldens, so a bug shared by builder and decoder is harder to miss.
3. **Independent decoder references** (Pillow approved in principle, **installation needs a separate explicit OK in Phase 4**):
   - **Proposed location:** `tools\reference\.venv\` inside the project, git-ignored. It's an isolated Python environment that touches nothing system-wide and is removed by deleting the folder. It sits in the project, not the session scratchpad, so references can be regenerated in later sessions.
   - **Proposed commands** (exact Pillow version to be stated when I ask):
     1. `py -3.11 -m venv tools\reference\.venv`
     2. `tools\reference\.venv\Scripts\python.exe -m pip install pillow==<version>`. This downloads from PyPI.
     3. `tools\reference\.venv\Scripts\python.exe -I tools\reference\make_references.py <inputs> tests\TGAPeek.Core.Tests\Reference`
   - The script and its output (raw RGBA + JSON) are committed. Tests compare our **decoded data** (not the display output) and run without Python.
   - **Agreement levels are recorded per case in a manifest:**
     - **Exact:** byte-for-byte agreement expected. Plain types 1/2/3/9/10/11 at 8/24/32 bpp, top/bottom origins, palette offsets.
     - **Partial:** compare only the channels where both interpretations agree, e.g. RGB of 15/16-bit pixels with the attribute bit excluded.
     - **Excluded:** no comparison. Right-to-left origin, interleaving, alpha attribute semantics, premultiplied data, grey+alpha, truncated files, out-of-range palette indices, and anything Pillow is found to interpret differently.

     A case is never compared as "exact" when Pillow's interpretation differs. Excluded cases rely on Tiers 1–2 plus the netpbm cross-check.
   - **Public sample corpus:** in Phase 4 I'll propose a list (each file, its source URL and licence). **Nothing is downloaded until you approve that list.**
4. **Malformed-input tests:**
   - truncation at **every** byte offset of several fixtures
   - corrupted header fields
   - palette, RLE and interleave edge cases
   - corrupted footer, extension area, developer directory, CCT, stamp and scan-line table, asserting that the **pixel buffer is identical** to the uncorrupted file
   - limit violations
   - an "invalid vs unsupported vs limit" classification test for each `ReasonCode`
5. **Seeded fuzz test:** a deterministic set of mutation and truncation iterations (a few thousand), each with a per-case timeout, asserting the guarantees in §7.
6. **Display tests:** `DisplayRenderer` output for each alpha policy and channel view on golden images.
7. **Manual UI checklist** (see Verification).

## 7. Error handling & realistic guarantees

**Error categories** (all derive from `TgaException` and carry a `ReasonCode` and a byte offset):
- `TgaInvalidFileException`: the data breaks the format, for example:
  - truncated header
  - unknown image type
  - depth inconsistent with type
  - colour-mapped with no or truncated palette
  - zero width/height
- `TgaUnsupportedFormatException`: the file is valid or well-defined but this viewer doesn't decode it. Covers types 0, 32 and 33, reserved interleave, and unsupported depth combinations.
- `TgaLimitExceededException`: the file is valid but exceeds the configured limits. Defaults: 32,768 px per side, 256 megapixels, 1 GB file.

**Partial decoding for truncated files** (`AllowPartial`: on in the App, testable both ways):
- If the file ends before the end of the header, image ID or colour map → `Invalid`. Pixels can't be interpreted without them.
- If it ends during the pixel data:
  - All complete pixels are decoded **in file order**.
  - In an RLE packet whose header is present but whose data is cut off, pixels with known values are kept and the rest are missing.
  - The remaining pixels are `(0,0,0,0)` and marked in `MissingMask`.
  - The result is flagged **Partial** with "decoded N of M pixels (x%)".
- Because of orientation and interleave, the missing area may appear at the **top** of a bottom-origin image, or as alternating rows in an interleaved one. The UI hatches missing pixels so they are never mistaken for real transparent pixels.
- With `AllowPartial` off, truncation → `Invalid(TruncatedPixelData)`.
- Extra bytes after the pixel data that aren't recognised TGA 2.0 structures are ignored.

**Guarantees the decoder aims to provide (each one is tested):**
- **G1:** For any input within `MaxFileSize`, `Decode` returns a result or throws a `TgaException` subtype. Any other exception escaping is a bug. Tested by the fuzz, truncation and malformed suites.
- **G2:** Memory use is bounded.
  - **Every** buffer size is computed with checked 64-bit arithmetic and checked against the limits **before** allocation:
    - the pixel output, raw values and missing mask
    - the palette (at most 65,535 entries)
    - the developer directory (entry count limited by the file)
    - hex previews (capped at 64 bytes)
    - the postage stamp (at most 255×255)
    - the scan-line table (height × 4)
    - extension strings (fixed sizes)
    - the renderer's viewport buffers (bounded by the screen area)
  - All limits are configurable in `TgaDecodeOptions`: file size, width, height and pixel count.
  - **Whole-operation memory estimate:** `TgaMemoryEstimator` (Core, pure) runs right after the header is parsed and before any large allocation. It returns an upper-bound breakdown, where N = width × height:

    | Component | Bytes |
    |---|---|
    | File bytes (already read, after the file-size check) | F |
    | Decoded BGRA pixels | 4·N |
    | Raw values (palette index / 15–16-bit word) | 0, 1·N or 2·N |
    | Missing-pixel bitmask (allocated only if truncated, always counted) | ⌈N/8⌉ |
    | Palette (raw + expanded) | ≤ 65,535 × 8 ≈ 0.5 MB |
    | Optional metadata (developer directory, stamp ≤ 255²×4, scan-line table h×4, CCT 2 KB, strings, previews) | bounded, ≤ ~2 MB at default limits |
    | Decoder temporaries | none. Pixels are decoded straight into their final position, with no intermediate buffer. |
    | Render buffers (opaque viewport buffer + display bitmap) | 2 × 4 × viewport pixels (≈ 66 MB at 4K) |
    | **Previously displayed image**, kept alive until the new one succeeds | its own `Pixels + RawValues + mask` |
    | Safety margin (GC / large-object-heap overhead) | +10% |

    - **Examples:** a 16,384 × 16,384 32-bit uncompressed file is about 1 GiB file + 1 GiB pixels ≈ 2.2 GiB peak, plus any previous image. A 16-bit file of the same size is about 0.5 + 1 + 0.5 ≈ 2.2 GiB.
    - **Two checks:**
      1. In Core: a configurable `MaxWorkingSetBytes` limit (default 4 GiB) → `TgaLimitExceededException(WorkingSetTooLarge)`.
      2. In the App: compare the estimate with `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`. If it exceeds roughly 80% of that, refuse **before decoding** with a clear message: "This image needs about 2.3 GB of memory; about 1.6 GB is available."
    - **Tests:** the estimator's breakdown is checked for each format. Using `GC.GetAllocatedBytesForCurrentThread()`, a test asserts that the actual bytes allocated by a decode are **≤ the estimate** for representative images. That keeps the estimate a real upper bound.
- **G3:** Running time is bounded, roughly proportional to file size plus pixel count. Every loop advances through input or output. Tested with timeouts.
- **G4:** Reads never go outside the input buffer: explicit bounds checks, backed up by .NET's own array checks, and no `unsafe` code.
- **G5:** Corrupt optional metadata never changes decoded pixels.
- **G6:** The decoder is deterministic and does no file or network I/O.

**What is not guaranteed** (documented, and handled in the App with friendly messages):
- An input under the configured limits **may still fail to load or display**. The machine may lack free memory, or a Windows/GDI+ call may fail.
- Bugs in .NET or Windows themselves are out of our control.
- Interpretations of under-specified features (interleaving, postage-stamp orientation) may differ from other tools.

**Application boundary ("commit on success" loading):**
1. Read the file size, then check it against `MaxFileSize`, and only then read the bytes.
2. Parse the header, then run the estimator. Check `MaxWorkingSetBytes` and the available memory.
3. Decode off the UI thread into a **new** `TgaImage`, with a `CancellationToken` (opening another file cancels the load in progress).
4. Render the first viewport of the new image.
5. **Only then**, on the UI thread, swap the new image in and release the old one.

- If any step fails, the new objects are dropped, the old image, view state, title and status bar stay exactly as they were, and a clear message is shown.
- The App catches `OutOfMemoryException`, `InsufficientMemoryException`, GDI+ `ArgumentException`/`ExternalException`, I/O and access errors, and `TgaException` subtypes, each with its own message.
- After an out-of-memory failure, the App releases references and requests one garbage collection, so the old image remains usable.
- Any other exception is reported as a bug with copyable details.

## 8. Deployment: self-contained single-file win-x64

**Outcome (2026-10-25):** built after the owner approved the download. The release build is an uncompressed single file of 107 MB. Compression and trimming were measured and not used (§14.3). The plan text below is kept as written; corrections are in *(italics)*.

- **Development builds** stay framework-dependent: fast, using your installed .NET 10.
- **Distribution:** `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` via a publish profile. Optionally add `-p:EnableCompressionInSingleFile=true`.
- **Expected size** (I'll measure the real numbers in Phase 6):
  - about 140–190 MB uncompressed
  - about 60–80 MB with compression

  It's large because the whole .NET runtime plus WinForms ships inside the exe. *(Measured: 107 MB uncompressed, 49 MB compressed, with Windows Forms' translations left out.)*
- **Trade-offs:**
  - Runs without installing .NET, but it's large.
  - **Trimming and Native AOT are not supported for WinForms**, so the size can't be cut much further. *(Verified for .NET 10: the SDK rejects trimming Windows Forms apps, and an experiment that forced it broke the settings and two display checks; §14.3.)*
  - Compression gives a smaller file but slightly slower startup. I'll measure both and recommend one.
  - **Security fixes:** the bundled runtime doesn't get .NET security updates through Windows Update. You'd rebuild to pick up runtime patches.
  - `IncludeNativeLibrariesForSelfExtract` gives a true single file, but native DLLs are extracted to a temp folder on first run. Without it, a few DLLs sit beside the exe. *(Measured: this app bundles no native libraries that need extracting, so nothing is written to disk at start-up.)*
  - The exe is unsigned, so SmartScreen may warn on other PCs after a download. Fixing that needs a code-signing certificate, which is out of scope.
  - The first self-contained publish **downloads the win-x64 runtime packs** (tens of MB) from nuget.org. **Not done until I ask you in Phase 6 and you explicitly approve.** *(Approved and done on 2026-10-25: version 10.0.13, about 93 MB. That includes the ASP.NET Core pack, which the SDK fetches although the viewer doesn't use it; the setting that would skip it, `DisableTransitiveFrameworkReferenceDownloads`, breaks Windows Forms builds with `NETSDK1185`.)*
  - **Compression (D7):** in Phase 6 I'll build both the compressed and uncompressed single-file versions, measure file size and cold/warm startup time, report the numbers, and then recommend the default. *(Decided: uncompressed; §14.3.)*
- No installer and no file associations. "Open with…" works through the command-line argument.

## 9. Phased plan (each phase: build + tests green → summary → pause for your review; commits only on request)

0. **Scaffolding: ✅ complete (2026-10-25).**
   - **Result:** build 0 warnings / 0 errors, 2/2 tests pass, and the empty window opens and closes cleanly. Nothing is committed.
   - **Side effect, reported to the owner:** the first `dotnet` run put an untrusted ASP.NET Core HTTPS development certificate into the Windows user certificate store. It is unused by this project and was left in place.
   - Steps carried out:
   1. `git branch -m main`.
   2. `dotnet new gitignore`, then `dotnet new globaljson --sdk-version 10.0.402 --roll-forward latestFeature`. Add a hand-written `.editorconfig`, `Directory.Build.props` (nullable, implicit usings, deterministic builds), `README.md` and `CLAUDE.md`.
   3. Create the solution: `dotnet new sln -n TGAPeek` (.slnx).
   4. Create the projects:
      - `dotnet new classlib` → `src/TGAPeek.Core` (net10.0; Core sets `TreatWarningsAsErrors`)
      - `dotnet new winforms` → `src/TGAPeek.App` (net10.0-windows, PerMonitorV2, empty `MainForm`, references Core)
      - `dotnet new xunit --no-restore` → `tests/TGAPeek.Core.Tests`
   5. In the test project, **remove the template's `coverlet.collector`**. It's an extra dependency nobody approved. Keep only xunit, xunit.runner.visualstudio and Microsoft.NET.Test.Sdk.
   6. Add the projects to the solution. Replace the template placeholders (`Class1`, `Form1`, `UnitTest1`).
   7. Add a minimal `TgaDecodeOptions` (default limits) plus a trivial test of its defaults.
   8. `dotnet restore` (xUnit packages only), `dotnet build`, `dotnet test`.
   9. Report, then **stop for review**. No commit.
1. **Header, exceptions, `TgaMemoryEstimator`, uncompressed true-colour/grey, RowMapper:** all 4 origins and 2-/4-way interleave, with Tier 1 goldens.
2. **Colour-mapped, RLE, 15/16-bit and grey+alpha variants:** builder + Tier 2 tests.
3. **TGA 2.0 structures + alpha model + DisplayRenderer:** footer, extension area, developer directory, CCT, stamp and scan-line table. Isolation tests, policy and channel-view tests.
4. **Robustness + independent references:**
   - limits, partial decoding, the malformed suite, fuzzing
   - **ask you** to approve the exact Pillow install commands
   - **propose** the sample list and wait for your approval
   - then the Tier 3 tests
5. **Viewer UI:** canvas, inspector, channel/alpha menus, info panel, navigation, DPI, async decode, error dialogs.
6. **Deployment:**
   - **ask you** before downloading runtime packs
   - self-contained single-file publish, measure size and startup (compressed vs not), README, manual QA

## 10. Working rules (`CLAUDE.md`)

The draft that used to be here became [CLAUDE.md](../CLAUDE.md) in Phase 0. The file in the repository is now the only copy, so the two can't drift apart. Keep CLAUDE.md short, and record detailed design decisions in this plan.

## 11. Verification

- `dotnet build` (zero warnings in Core) and `dotnet test` all green: goldens, combinatorial, reference, malformed, isolation and fuzz tests.
- **Manual QA:**
  - every channel view and alpha mode
  - inspector values match the hand-authored goldens
  - premultiplied and type-2 files
  - a truncated file shows hatched missing pixels and a warning
  - a renamed non-TGA file → friendly "invalid" message; a type-32 file → "unsupported" message
  - crisp 1:1 at 100% and 150% Windows scaling
  - large image doesn't freeze the UI
  - file can be deleted while open
  - ←/→ navigation and "Open with…" work
- The self-contained exe runs from a folder with no .NET involvement: launch it with `DOTNET_ROOT` pointed at an empty folder, a process-local setting only. I'll report its size and startup time. *(Done more directly: the running viewer's loaded modules were listed, and none came from the installed .NET; §14.3.)*

## 12. Decision status

| # | Decision | Status |
|---|---|---|
| D1 | Pillow in an isolated venv for reference outputs | **Done 2026-10-25.** The owner approved the §6 commands; Pillow 13.0.0 is installed in `tools\reference\.venv` ([tools/reference/README.md](../tools/reference/README.md)). |
| D2 | Public TGA sample corpus | **List to be reviewed and explicitly approved** before any download (Phase 4). *No list has been proposed yet.* |
| D3 | win-x64 runtime packs | **Approved and done 2026-10-25** (version 10.0.13, now in the NuGet cache). |
| D4 | `master` → `main` | Approved. Done in Phase 0. |
| D5 | Interleave value 11 | Approved: reject as Unsupported |
| D6 | 32-bpp, alpha bits 0, no ext. area | Approved: Auto uses alpha (display-only override + all-zero heuristic). **Superseded in Version 1 (§14.1 A2): the brief forbids inferring alpha from a 32-bit layout, so Auto shows these files opaque, with a notice and a one-click override.** |
| D7 | Single-file compression | **Decided 2026-10-25: uncompressed.** Compression saved 58 MB but cost about 80 ms and 44 MB of memory at every start (§14.3). |

**Known limitations:**
- No independent reference covers interleaving (Pillow ignores the bits), and real interleaved files are very rare. Interleaving is covered by hand-authored goldens and the netpbm cross-check only. Right-to-left origins now agree with Pillow.
- Postage-stamp orientation isn't specified, so we assume it matches the main image.
- The self-contained exe is 107 MB (44 MB zipped). Trimming is not supported for Windows Forms in .NET 10 (§14.3).
- It's unsigned, so expect SmartScreen prompts on other PCs.

## 13. Open issues against the project brief (pending the project owner's review)

The plan above was written before [PROJECT_BRIEF.md](PROJECT_BRIEF.md) arrived. This section lists every place found where the plan, CLAUDE.md or the Phase 0 code conflicts with the brief, leaves something out, or depends on an unclear point. **Status: resolved for Version 1. The decision on each item is recorded in §14.1.** Brief references are to its numbered sections.

### 13.1 Conflicts that need a decision

| # | The brief says | The plan / code currently says | Options, and the recommendation |
|---|---|---|---|
| A1 | **Decoder choice (§1, §11 step 3):** use a maintained, well-tested library if it gives the required coverage and fidelity. Check its licence, maintenance status, formats, known limitations and dependency footprint first. Don't write a custom decoder unnecessarily, and explain the choice. | Custom decoder. No documented library evaluation; Revision 1 dismissed libraries in one sentence. | **Recommended:** before Phase 1, a research-only evaluation (web pages only; no installs, downloads or package references) of candidate .NET TGA decoders such as ImageSharp, Magick.NET, Pfim and StbImageSharp. Score them against the brief's §2–3 coverage list (16-bit indices, palette origin, right-to-left origin, interleaving, attributes type, extension and developer areas, raw-value access, malformed-input behaviour). Write it up as `docs/DECODER_EVALUATION.md`, then you decide. A library might also serve as a test reference rather than the production decoder. |
| A2 | **32-bit alpha (§3):** "Do not infer alpha solely from the presence of a 32-bit pixel format." Also: "Do not silently discard alpha or assume every fourth byte contains meaningful transparency", and (§4) report when an image has no meaningful alpha. | Decision **D6** (plan §2, §12; CLAUDE.md): Auto uses alpha for 32-bpp files whose descriptor declares 0 alpha bits and that have no extension area. | (a) Keep D6 as a deliberate, documented exception to the brief. (b) **Recommended:** Auto shows these files opaque, with a status-bar notice ("32-bit file declares no alpha bits; 4th channel not applied") and one-click *Use alpha*. Stored data and the Alpha view are unchanged. (c) Auto uses the 4th channel only if its values vary, with a notice. |
| A3 | **Limits (§7):** "Do not impose arbitrary format restrictions simply to optimise typical files. Use safe limits and explain when a file cannot be displayed because of available resources." | `TgaDecodeOptions` (Phase 0 code) and plan §7 set 32,768 px per side, 256 Mi pixels, a 1 GiB file and a 4 GiB working-set budget. The per-side limit rejects, say, a 40,000 × 16 image that needs under 3 MB. The pixel and file limits are also stricter than the memory budget. | **Recommended:** the memory budget (from `TgaMemoryEstimator`) becomes the limit that actually governs. Width and height default to the format maximum of 65,535. Pixel-count and file-size limits stay only as configurable safety caps that don't undercut the budget. This needs a change to `TgaDecodeOptions` and its test, which hasn't been made. |
| A4 | **Copies (§7):** "Avoid unnecessary full-size image copies and intermediate buffers." | Plan §1/§7: the App reads the whole file into a `byte[]`, then decodes into a second buffer. For uncompressed images that roughly doubles peak memory. | **Recommended:** decode from a seekable, read-only `Stream` with a small read buffer. The file is opened with shared read/delete access and closed right after decoding, and optional TGA 2.0 structures are read through validated seeks. Tests use in-memory streams. Guarantee G6 becomes "deterministic; reads only the stream it is given". |
| A5 | **Previous image (§7):** "Release the previous image and its buffers promptly when opening another file." | Plan §7 and your Phase 0 clarification: commit-on-success keeps the previous image until the new one has loaded, so a failed load leaves it on screen. | The two pull against each other only on peak memory, which briefly holds two images. **Recommended:** keep commit-on-success and release the old image at the moment of the swap. If the estimate shows both images won't fit, release the old one first and say so. In that case a failed load can't bring it back. |
| A6 | **Invalid palette indices (§3):** "Reject invalid indices safely instead of reading arbitrary memory or producing misleading output." §8: don't silently substitute a blank image. | Plan §5: an out-of-range index → transparent black, plus a warning and an inspector flag. Transparent black can be mistaken for real image content. | (a) Fail the decode (`Invalid(PaletteIndexOutOfRange)`). (b) **Recommended:** decode, but mark invalid pixels the way missing pixels are marked (hatched, never transparent black), with a prominent warning and a count. (c) Keep as planned. |
| A7 | **Scaling (§4):** provide both nearest-neighbour scaling for crisp inspection *and* smooth scaling for general viewing. | Plan §2–3: nearest-neighbour at every zoom. Smoothing exists only when zoomed out and is off by default. | **Recommended:** a Nearest / Smooth toggle that works at every zoom. Smooth means bilinear when enlarging and area-average when shrinking, computed in our renderer for display only. **Decide the default.** The recommendation is nearest-neighbour, for texture inspection. |
| A8 | **Size (§1, §10):** "extremely lightweight", the success criterion says "tiny", and output should be as small as reasonably possible. Trimming and other optimisations may be used *after verifying* they don't break anything. The brief also requires C#, WinForms and self-contained publishing. | Plan §8: an estimated 140–190 MB uncompressed, 60–80 MB compressed (not measured). It asserts that WinForms can't be trimmed, without checking. | The size comes from the required stack. **Recommended:** in Phase 6, measure plain, compressed and ReadyToRun builds, and actually test whether .NET 10 can trim this WinForms app (keeping it only if every test and the manual QA still pass). Please confirm that a result in the tens of MB counts as acceptably "tiny" for this stack. |
| A9 | **Windows versions (§1):** Windows 10 and 11, "subject to the chosen .NET runtime's supported operating systems". | README says "Windows 10/11 x64". .NET 10's actual OS support hasn't been checked. Consumer Windows 10 left mainstream support in October 2025, so .NET 10 support may cover only some Windows 10 editions. | Verify .NET 10's supported-OS list and document it before release. A decision is only needed if support turns out narrower than you need. |

### 13.2 Brief requirements the plan leaves out (proposed additions; no conflict)

- **B1 (§5):** Remember window size and position. Proposal: a small settings file in `%LOCALAPPDATA%\TGAPeek`, not the registry.
- **B2 (§6):** Several paths on the command line need defined, documented behaviour. Proposal: open the first file, and ←/→ steps through the given files in order.
- **B3 (§6):** Investigate single-instance operation with file forwarding (Explorer can start one process per selected file). Implement it only if reliable and light.
- **B4 (§4):** Zoom centred on the cursor, and a clear "Reset to fit" action.
- **B5 (§5):** Show the filename, compression type and alpha information, as well as size, depth and image type.
- **B6 (§5):** Accessibility: full keyboard operation, accessible control names, and controls usable at small window sizes and high DPI.
- **B7 (§6):** Tests for paths with spaces and Unicode/non-ASCII characters, and for invalid, missing and inaccessible files.
- **B8 (§11 step 9):** Representative performance tests: decode throughput, viewport render time while panning and zooming, startup time and peak memory.
- **B9 (§4):** The Alpha view of a file with no stored alpha must say so, not show plain white (synthetic 255).
- **B10 (§2):** True-colour and greyscale files that include an unused colour map: skip it correctly (validated) and show it in the info panel. Define the handling of colour-map type values 2–255 (reserved / developer use).
- **B11 (§3):** Alpha semantics for colour-mapped images (alpha in 32-bit palette entries, the attribute bit in 15/16-bit entries). Define and test.
- **B12 (§3):** RLE:
  - Document real-world evidence for packets that cross scan lines before keeping that fallback.
  - Define the policy for a packet that runs past the end of the image: error, or clip plus warning.
- **B13 (§2–3):** Plan §5 and CLAUDE.md label 16-bit greyscale + alpha "non-standard", but the brief lists it as a format to support. Check the spec before labelling it.
- **B14 (§9):** Independently *generated* fixtures. Besides decoding with Pillow, use it (with approval) to *write* fixtures, and ask the owner for real TGA files from their texture tools as representative samples.
- **B15 (§9):** A second independent reference for what Pillow can't check (right-to-left origin, interleaving). Currently these rely on hand-made expectations and the netpbm logic alone. Any new tool needs approval.
- **B16 (§2, §9):** Keep a coverage record, e.g. `docs/FORMAT_COVERAGE.md`. It marks each format combination as checked against a reference, checked against hand-made expectations, untested, or unsupported, and lists intentionally unsupported formats.
- **B17 (§9):** Add a regression test for every decoding bug found. Add this to CLAUDE.md's testing rules.
- **B18 (§10–11):** Release documentation:
  - install, run, update, set as the default app for .tga files, and packaging for another PC
  - build process, dependencies and licence obligations (including the .NET runtime notices bundled into a self-contained exe)
  - supported Windows versions
  - the brief's final hand-over list
- **B19 (§8, §10):** Write down the runtime constraints:
  - no Administrator rights (`asInvoker` manifest)
  - no network access
  - no telemetry, auto-update or crash upload
- **B20 (§6, §10):** The optional installer decision belongs in Phase 6. The exe is currently named `TGAPeek.App.exe`. Proposal: rename it to `TGAPeek.exe` before release, because file associations point at the exe.
- **B21 (§7):** How ordinary users can configure limits. Proposal: entries in the B1 settings file, not exposed in the UI.
- **B22 (§2):** Pixel aspect ratio from the extension area. Proposal: show it as metadata only, keeping texture pixels square.
- **B23 (§4):** Write the default display policy up for users in the README: no gamma or colour-correction application, alpha rules, scaling.

### 13.3 Unclear points

- **C1:** The brief arrived without separate start/end markers, so the whole message was saved as the brief. To be confirmed by the owner.
- **C2 (§5):** "Separate windows or a previous/next workflow": the plan chose previous/next within the same folder. To be confirmed.
- **C3 (§9):** "Zero dimensions": is a header with width or height 0 an invalid file (current plan) or "no image data"?
- **C4 (§11):** The brief's workflow reads as continuous ("Work directly in the project files"), while the agreed process stops for review after every phase. The plan keeps the phase-by-phase reviews unless the owner says otherwise.

### 13.4 Proposed next step

Decide A1–A9, with A1 first because it can change the architecture. Then update this plan to Revision 4, folding in the agreed B-items, and update CLAUDE.md to match. Only after that does Phase 1 start.

*(Superseded on 2026-10-25: the owner authorised independent decisions within the brief and continuous work. See §14.)*

## 14. Version 1 implementation record (2026-10-25)

**Update, same day:** the owner approved the runtime-pack download and the Pillow installation. The release build and the Pillow comparison were then completed (§14.3). The decisions that override earlier choices (A2, A3 and A6) were reported to the owner, and no objection was raised.

**Renamed to TGAPeek (2026-10-25, at the owner's request):**
- **What changed:** the solution, all projects and namespaces, the exe (`TGAPeek.exe`), window and dialog titles, the settings folder (`%LOCALAPPDATA%\TGAPeek`), the tools and the docs.
- **Settings:** settings saved by earlier builds are not carried over.
- **Test data:** four reference inputs embed the generator's name, so they were regenerated and Pillow's outputs refreshed. Pillow's pixels were unchanged.
- **Not renamed:** the brief's own wording, which describes the kind of app, was left as written.

### 14.1 Decisions on the §13 items

| # | Decision taken |
|---|---|
| A1 | Libraries evaluated ([DECODER_EVALUATION.md](DECODER_EVALUATION.md)). None covers the brief: ImageSharp, ImageMagick and stb_image each lack interleaving, palette-offset, TGA 2.0 alpha or error-handling requirements. **The custom decoder is kept.** |
| A2 | **Auto alpha needs a declaration:** attributes type 3 or 4, otherwise header attribute bits above 0. A 32-bit file that declares no alpha is shown opaque, with a notice and a one-click *Use alpha*. The all-zero-alpha rule stays display-only and is always indicated. This replaces D6. |
| A3 | **Width, height and pixel-count limits default to the format maximum** (65,535). The memory budget (4 GiB by default, `MaxDecodeMemoryMB` in the settings) is the limit that normally applies. |
| A4 | **The decoder streams from a `Stream`.** The file is never held in memory as a whole. `TgaFile.Open` is read-only and shares access, so the file is never locked. Guarantee G6 now reads "deterministic, reads only the stream it is given". |
| A5 | **Commit-on-success is kept**, and the old image is released at the swap. If the new image only fits once the current one is gone, the current image is closed first and the user is told. |
| A6 | **Invalid palette indices** are not looked up. They are marked with orange stripes, counted, warned about and flagged in the inspector. |
| A7 | **Nearest/Smooth toggle at every zoom; nearest by default.** Smooth means bilinear (zoom ≥ 1) or a box filter of up to 4 × 4 samples (zoom < 1), on premultiplied values, for display only. Nearest is used while zooming or panning. |
| A8 | **Self-contained release built** (after approval, 2026-10-25): an uncompressed single file with English-only framework resources and the runtime's licence notices beside it. Compression and trimming were measured and rejected (§14.3). |
| A9 | **Checked:** .NET 10's support list includes Windows 10 only in 1607, 1809 and 21H2 "(E)" and 21H2 IoT, not the ordinary 22H2. Documented in the README. |

**B items:**
- **B1:** window size and position are remembered in `%LOCALAPPDATA%\TGAPeek\settings.json`.
- **B2:** with several command-line files, the first is shown and Left/Right follow the given order.
- **B3:** single-instance forwarding was investigated and **not implemented**. It needs inter-process messaging, which adds complexity and failure modes, and each launch already opens a predictable window of its own. This is documented.
- **B4:** zoom around the cursor, and fit with Ctrl+0 or a double-click.
- **B5:** the status bar shows format, compression, alpha decision, zoom, pixel values and warnings.
- **B6:** menus with access keys, shortcuts, accessible names and tooltips, a toolbar that overflows on narrow windows, and a minimum window size. *Not tested with a screen reader.*
- **B7:** tests for paths with spaces and Unicode, missing files, folders and locked files, plus an end-to-end check with a Unicode name.
- **B8:** a performance test (results in §14.3).
- **B9:** the Alpha view of a file without alpha shows a pattern and a message.
- **B10:** an unused colour map is skipped; reserved colour-map types are handled.
- **B11:** palette entries: 32-bit carry 8-bit alpha, 16-bit carry 1-bit alpha.
- **B12:** RLE packets crossing rows are accepted (stb_image decodes the same way); RLE overruns are clipped with a warning.
- **B13:** the "non-standard" label on 16-bit greyscale was removed.
- **B14:** hand-written goldens, the builder and the Pillow comparison are done (§14.3). Real files from the owner's tools are still missing.
- **B15:** *not done*: Pillow is the only reference. Interleaving still relies on hand-made expectations and the netpbm logic; right-to-left origins now agree with Pillow.
- **B16:** [FORMAT_COVERAGE.md](FORMAT_COVERAGE.md).
- **B17:** the regression-test rule is in CLAUDE.md.
- **B18:** README and THIRD-PARTY-NOTICES.
- **B19:** asInvoker manifest; no network, telemetry or update code.
- **B20:** the exe is named `TGAPeek.exe` and has an icon. No installer: Windows' own "Open with ▸ Always" sets the default viewer.
- **B21:** `MaxDecodeMemoryMB` can be set in the settings file.
- **B22:** the pixel aspect ratio is shown, not applied.
- **B23:** the display policy is in the README.

**C items:**
- **C1:** the "no markers" reading of the brief has not been explicitly confirmed by the owner.
- **C2:** previous/next browsing, not separate windows.
- **C3:** a zero width or height means an invalid file.
- **C4:** the owner chose continuous work.

### 14.2 What was built

- **Core** (`src/TGAPeek.Core`):
  - a streaming decoder for every format in [FORMAT_COVERAGE.md](FORMAT_COVERAGE.md), plus an isolated reader for the TGA 2.0 structures;
  - the memory estimator, alpha policy and display renderer (exact compositing, channel views, hatching for missing and invalid pixels, smooth scaling);
  - view maths, the pixel inspector, the information report, file access and Explorer-style name sorting, and plain-language error text.
- **App** (`src/TGAPeek.App`, `TGAPeek.exe`):
  - menus, a compact toolbar, a status bar, and an information panel with a pixel inspector and postage-stamp preview;
  - the canvas: zoom at the cursor, pan, fit, 100% and double-click, all rendered without GDI+ scaling;
  - opening files: drag and drop (files and folders), the command line, previous/next browsing;
  - remembered settings, a memory check before decoding, background loading (stale results discarded; the old image kept until the new one is ready), error dialogs, an icon and the manifest.
- **Tests** (`tests/TGAPeek.Core.Tests`): 420 automated tests:
  - hand-written goldens and a format matrix;
  - a comparison with Pillow on 35 reference files (added after approval);
  - TGA 2.0 metadata and corruption cases;
  - truncation at every byte, classification of malformed files, and fuzzing;
  - memory-estimate bounds, alpha policy, exact compositing, view maths, files and paths, reports;
  - performance.
- **Tools:** a sample generator (27 named samples, large ones, and the 35 reference inputs), `SmokeTest.ps1` (an end-to-end check of the real UI), `MakeIcon.ps1`, and `tools/reference/make_references.py` (Pillow's outputs).
- **Release:** the `win-x64-self-contained` publish profile, plus a publish step that copies the .NET runtime's licence notices beside the exe.

### 14.3 Verification results (2026-10-25, this PC: Windows 11, .NET 10.0.13)

- **Builds:** Debug and Release both have 0 warnings and 0 errors.
- **Automated tests:** 420 passed, 0 failed, 0 skipped, in both Debug and Release. That is the 390 first reported, plus 30 reference tests added after approval.
- **Fuzzing:**
  - 6,000 damaged files: 4,264 decoded, 1,736 rejected with a `TgaException`, slowest 42 ms;
  - plus 4,000 random headers.
  - No other exception occurred.
- **Smoke test** (framework-dependent build): all checks passed:
  - all four origins, colour-mapped, RLE and both interleaving modes show the exact test pattern in the right place;
  - at 100% the quadrants are exactly 759/768/768 pixels (no scaling);
  - the RGB and alpha views work through their shortcuts;
  - the Alpha view of a file without alpha shows the no-alpha pattern;
  - invalid, unsupported and missing files show explanations, and the viewer keeps running;
  - Unicode file names and several command-line files work;
  - the information panel, scaling, zoom and background commands cause no errors;
  - the Right arrow opens the next file;
  - the saved window size is unchanged after a restart.
- **Performance** (Release build, in memory):
  - decode 4096 × 4096 32-bit: 98 ms uncompressed (64 MB), 147 ms RLE;
  - render a 1920 × 1080 view: nearest 3–4 ms, smooth 5–16 ms;
  - memory: 64.2 MB for the decoded 4096² image, plus 15.8 MB of viewport buffers.
- **Comparison with Pillow 13.0.0** (after approval):
  - 35 reference files with random pixels; Pillow decoded 30.
  - All 28 compared files agree at their stated level: 23 exactly, 1 in colour only and 4 in their stored 5-bit colour values.
  - 7 are excluded, because Pillow can't read them or reads them differently.
  - A deliberately wrong classification makes the test fail.
  - Details are in [tools/reference/README.md](../tools/reference/README.md).
- **Release build** (`win-x64-self-contained`, after approval):
  - **File:** `TGAPeek.exe` is 107,241,628 bytes, with SHA-256 `A08719F06C3CACF3A30D5C604971A07B02A8E01303E0146E411C812931635808` (rebuilt after the rename). Zipped with its licence files, it is 44 MB.
  - **No local paths:** Release builds map source paths to `/_/` (`PathMap` in `Directory.Build.props`), so the exe records no folder or user name from the PC that built it.
  - **Independence from installed .NET:** while running, it had loaded no module from the installed .NET (the framework-dependent build loads 40), and it extracted nothing to disk.
  - **Start-up** (time until the image is shown; 8 launches each, median of the last 7):
    - self-contained: 216 ms, 18.2 MB private memory;
    - framework-dependent: 211 ms, 17.6 MB;
    - compressed: 299 ms, 62.3 MB;
    - trimmed: 471 ms, 20.1 MB.
  - **Size:**
    - 116 MB with all framework languages;
    - 107 MB English-only (this is the released build);
    - 49 MB compressed;
    - 22 MB trimmed.
  - **Trimming:** the SDK refuses it for Windows Forms (`TrimmingWindowsFormsIsNotSupported`). As an experiment it was forced through the internal `_SuppressWinFormsTrimError` switch. The trimmed build never saved its settings, because trimming switches off reflection-based JSON, and it failed two display checks. Trimming is not used.
- **Smoke test on the release build:**
  - **Final result:** after the rename, one run with the PC idle passed all 85 checks.
  - **Earlier runs, while the PC was in use:**
    - Keyboard checks were skipped whenever Windows kept another program in front; the logs name VS Code and Chrome.
    - Two runs had visual failures that did not reproduce in 19 isolated captures of the same files. The likely cause is input reaching the viewer windows, for example mouse-wheel scrolling over a window that opened under the pointer.
    - One run stopped when a viewer window disappeared before it could be captured.
  - **Change to the script:** a capture that fails now counts as a failed check instead of stopping the run.
- **Smoke-test tool fixed:**
  - **The problem:** `SendKeys` types into whichever window is in front, and Windows sometimes keeps another program there. In two runs that day, four view shortcuts (Ctrl+1, Ctrl+3, Ctrl+7, Ctrl+2) most likely reached another program instead of the viewer.
  - **The fix:**
    - The test now activates the viewer's main window and sends a key only after confirming the viewer is in front. Otherwise it skips the check and names the program in front.
    - It retries screen captures for up to 3 seconds instead of relying on one fixed wait.
    - It can save the capture behind a failed check (`-CaptureFolder`), and it removes a settings folder it created.

### 14.4 Blocked or not done

- **Public sample corpus:** needs approval of a list. No real-world files from production tools have been tested yet.
- **Not done:** single-instance forwarding (B3, a deliberate decision), screen-reader testing, testing on Windows 10, code signing (out of scope).
- **High-DPI behaviour is untested above 100% display scaling.** This PC runs at 96 DPI, and changing display scaling would be a system-settings change. That includes 1:1 pixels at 125–200% scaling, restoring the window size, and the postage-stamp preview size.

### 14.5 Known limitations

- **Interleaving:** follows TGA 1.0 and netpbm. ImageMagick reads the bits differently, Pillow ignores them, and no real interleaved files were available.
- **Assumptions:** the postage stamp uses the image's own origin bits; this is not checked against a reference. 16-bit greyscale stores grey before alpha; Pillow agrees.
- **5-bit channels:** expanded by bit replication. Pillow and stb_image use `v × 255 / 31`, truncated, so 8-bit values can differ by 1. Pillow also reads the 16-bit attribute bit inverted; the viewer, GIMP and ImageMagick treat a set bit as opaque.
- **Colour management:** the viewer applies none, by design. Windows' own system-wide colour management, where enabled, may still affect what the screen shows.
- **Memory:** images that need more than the budget or the available memory are refused with an explanation. An image under the limits can still fail if memory runs out; the viewer then says so.

