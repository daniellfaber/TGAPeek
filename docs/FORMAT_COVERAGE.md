# TGA format coverage and test evidence

This page records which TGA variants Version 1 supports, how each one has been verified, and what is still unverified (brief §9: "Keep a record of which format combinations have actually been tested and which remain unverified").

## How things were verified

| Code | Kind of evidence |
|---|---|
| **H** | Hand-written test file: bytes written from the TGA specification, expected pixels typed by hand (`GoldenTests`, `MalformedInputTests`). Independent of both the decoder and the test builder. |
| **G** | Generated combinations: random images written by the test builder, with expected values from separate test code (`FormatMatrixTests`, `Tga2MetadataTests`). The builder is itself checked against **H** files. |
| **E2E** | Seen in the running app: `tools/SmokeTest.ps1` opens the file in the real viewer, captures the window and checks the colours and layout of the test pattern. |
| **R** | Compared with an independent decoder, Pillow 13.0.0 (`ReferenceTests`), on 35 files with random pixels. Each file has a stated agreement level: exact, colour only, stored 5-bit colour, or excluded where Pillow cannot read the variant or reads it differently ([tools/reference/README.md](../tools/reference/README.md)). "R excluded" below means Pillow could not serve as a reference for that variant. |

Every combination below was decoded without error and matched its expected pixels exactly. All 420 automated tests last passed on 2026-10-25. Against the self-contained release build, every smoke-test check passed in at least one of six runs; see IMPLEMENTATION_PLAN.md §14.3.

## Image types

| Type | Meaning | Status | Evidence |
|---|---|---|---|
| 1 | Colour-mapped | Supported | H, G, E2E, R |
| 2 | True-colour | Supported | H, G, E2E, R |
| 3 | Greyscale | Supported | H, G, R |
| 9 | RLE colour-mapped | Supported | H, G, E2E, R |
| 10 | RLE true-colour | Supported | H, G, R |
| 11 | RLE greyscale | Supported | H, G, R |
| 0 | No image data | Reported as "no image data" (unsupported) | H |
| 32, 33 | Huffman/delta compressed colour-mapped | **Not supported**, reported clearly | H, E2E |
| Any other | Not defined by TGA | Reported as an invalid file | H |

## Pixel formats

| Format | Status | Evidence |
|---|---|---|
| True-colour 15-bit (5-5-5) | Supported. Bit 15 is not used. | H, G (R excluded: Pillow cannot read 15-bit images) |
| True-colour 16-bit (5-5-5 + attribute bit) | Supported | H, G, R (stored 5-bit colour; Pillow reads the attribute bit as inverted alpha) |
| True-colour 24-bit (BGR) | Supported | H, G, E2E, R |
| True-colour 32-bit (BGRA) | Supported | H, G, E2E (RGB and alpha views at 100%), R |
| Greyscale 8-bit | Supported | H, G, R |
| Greyscale 16-bit (grey byte, then alpha byte) | Supported | H, G, R (Pillow reads the same byte order) |
| Colour-mapped, 8-bit indices | Supported | H, G, E2E, R |
| Colour-mapped, 16-bit indices (indices above 255) | Supported | H, G (R excluded: Pillow cannot read 16-bit indices) |
| Palette entries 15, 16, 24 and 32 bits | Supported | H, G, R for 24 and 32 bits, R (stored 5-bit colour) for 16 bits; 15 bits R excluded |
| Palette starting at a non-zero index | Supported | H (2, 300), G (7, 1000), E2E (16), R (16) |
| Palette index outside the colour map | Marked: orange stripes, a warning and a count | H, E2E |
| Other depths (for example 8-bit true-colour, 24-bit greyscale, 24-bit indices, 8-bit palette entries) | **Not supported**, reported clearly | H |

**How 5-bit channels become 8-bit:** they are expanded by bit replication, `(v << 3) | (v >> 2)`. This maps 0 to 0 and 31 to 255, and the inspector always shows the original 5-bit value. Other decoders may differ by ±1 here; for example, stb_image and Pillow use `v * 255 / 31`, truncated (confirmed for Pillow 13.0.0), so the reference comparison checks the stored 5-bit values. Pillow also reads the 16-bit attribute bit the other way round (a set bit means transparent); the viewer, GIMP and ImageMagick treat a set bit as opaque.

## Orientation, interleaving and compression

| Feature | Status | Evidence |
|---|---|---|
| All four origins (descriptor bits 4 and 5) | Supported | H (all four), G (all four for true-colour and greyscale; two for colour-mapped), E2E (all four), R (all four for 24-bit) |
| Two-way interleaving | Supported (netpbm interpretation) | H, G, E2E (R excluded: Pillow ignores the interleave bits) |
| Four-way interleaving | Supported (netpbm interpretation) | H, G, E2E (R excluded, as above) |
| Interleaving combined with RLE and odd heights | Supported | H, G |
| Reserved interleave value (11) | **Not supported**, reported clearly | H |
| RLE run and raw packets | Supported | H, G, R (including 300-pixel rows) |
| RLE packets that continue into the next row | Accepted and noted (see below) | H, G, R for raw packets (Pillow rejects run packets that cross rows) |
| RLE data describing more pixels than the image has | Extra pixels ignored, with a warning | H |

**RLE packets that cross rows:** TGA 2.0 says packets "should not" cross rows, but the data stays unambiguous. stb_image also decodes RLE as one continuous stream. These files are therefore decoded normally, with an informational note.

## Header fields and other structures

| Feature | Status | Evidence |
|---|---|---|
| Image ID of 0, 1 and 255 bytes | Read and shown | G, H, R (pixels after a 39-byte ID) |
| Colour map present in a true-colour or greyscale file | Skipped, with a note | H (R excluded: Pillow fails on such files) |
| Colour-map type 0 with colour-map fields filled in | Fields ignored, with a warning | H |
| Reserved colour-map type with no map data | Ignored, with a warning | H |
| Reserved colour-map type with map data | **Not supported**: the pixel data's position can't be known | H |
| Extra data after the pixels (no footer) | Ignored and shown in the information panel | H |
| TGA 2.0 footer | Read | H, G |
| Extension area: every field, plus attributes types 0–4 | Read and shown | G, R (pixels with types 0, 3 and 4; Pillow forces alpha to 255 for type 0) |
| Extension area larger than 495 bytes | The known 495 bytes are read, with a note | G |
| Undefined attributes type (5–255) | Ignored, with a warning; alpha falls back to the header | G |
| Developer directory and entries (contents previewed, never interpreted) | Read and shown | G |
| Colour-correction table | Read and shown, never applied | G |
| Postage stamp, in all four origins | Read and shown | G |
| Scan-line table, uncompressed and RLE | Read and checked, never used for decoding | G |
| Gamma and pixel aspect ratio | Shown, never applied | G |
| Damaged offsets or sizes in any optional structure | Structure ignored with a warning; pixels unchanged | G (11 cases, plus 800 random damages) |
| Damaged footer signature | Treated as TGA 1.0 | G |

## Alpha

| Situation | How it is shown | Evidence |
|---|---|---|
| Attribute bits 8 (32-bit or 16-bit greyscale) | Alpha applied | G, policy tests |
| 32-bit with attribute bits 0 | Opaque, with a notice; one click applies alpha | Policy tests |
| 16-bit attribute bit, declared 1 | 1-bit alpha applied | H, policy tests |
| Attributes types 0, 1 and 2 | Opaque, with the data kept and visible in the Alpha view | Policy tests |
| Attributes type 3 | Straight alpha | Policy tests |
| Attributes type 4 | Premultiplied compositing; invalid pixels clamped for display only | Policy tests, renderer tests (exact values) |
| Every alpha value 0 | Opaque, with a notice (display only) | Policy tests |
| No alpha stored | Opaque; the Alpha view says there is no alpha channel | Policy tests, renderer tests, E2E |

## Damaged and hostile input

| Check | Evidence |
|---|---|
| Truncation at every byte of four different files, both strict and partial | Automated |
| Partial images keep exactly the pixels present, in file order | H |
| Problems classified as invalid, unsupported or over a limit, each with a reason code (21 cases) | H |
| 10,000 seeded random damages: only the decoder's own errors, each decode under 2 seconds | Fuzz tests |
| Huge headers (65,535 × 65,535) rejected before anything large is allocated | Automated |
| Actual memory allocated stays within the estimate (8 representative files) | Automated |

## Known gaps (unverified)

- **Only one reference decoder (R):** Pillow 13.0.0. Variants it cannot read or reads differently are covered by H and G only: 15-bit images and palettes, 16-bit indices, colour maps in true-colour files, interleaving, and RLE run packets that cross rows. No second reference (for example ImageMagick) has been used.
- **No real-world files:** every test file was generated here. No files from Photoshop, Substance, Blender, GIMP or other production tools have been tested, and no public test corpus has been downloaded (that needs approval).
- **Interleaving:** the row order follows TGA 1.0 and netpbm. ImageMagick reads the interleave bits differently, Pillow ignores them, and no real interleaved files were available.
- **Postage-stamp orientation:** the specification doesn't define it. The image's own origin bits are assumed.
- **16-bit greyscale + alpha:** the byte order (grey, then alpha) agrees with Pillow, but no file from a production tool has confirmed it.
