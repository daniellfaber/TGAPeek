# Decoder choice

**Decision:** TGAPeek uses its own decoder (`src/TGAPeek.Core`). None of the libraries evaluated below covers the TGA features and error handling the [project brief](PROJECT_BRIEF.md) requires (§1–3, §8), and adopting one would also add a dependency. The brief says to use a maintained library if one is good enough, and otherwise to write a dedicated decoder with comprehensive tests (§1). This page records why the second option applies.

Evaluated on 2026-10-25 by reading each project's source code or documentation online. Nothing was downloaded or installed.

## What a decoder had to handle

These requirements come from the brief:
- Two-way and four-way interleaving, and safe handling of the reserved value.
- All four origins, including right-to-left.
- 8- and 16-bit palette indices, a non-zero first palette index, and invalid indices rejected visibly.
- TGA 2.0 alpha semantics: alpha must not be inferred from a 32-bit layout alone, and the attributes type must be respected.
- TGA 2.0 footer, extension area, developer directory, colour-correction table, postage stamp and scan-line table.
- Access to stored values (such as palette indices) for the pixel inspector.
- Truncated and malformed data reported as errors, never crashes or silent substitutions, and no unbounded allocations.

## Candidates

| Library | Licence | Footprint | Findings | Verdict |
|---|---|---|---|---|
| [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) | Six Labors Split License 1.0: Apache 2.0 for open-source use and for companies under US$1M revenue; otherwise a paid licence | Large managed imaging library | See below | Not suitable |
| [Magick.NET](https://github.com/dlemstra/Magick.NET) (ImageMagick `coders/tga.c`) | Apache 2.0 (Magick.NET), ImageMagick licence | Native ImageMagick library | See below | Not suitable |
| [StbImageSharp](https://github.com/StbSharp/StbImageSharp) (port of stb_image) | Public domain or MIT | Small, managed | See below | Not suitable |
| [Pfim](https://github.com/nickbabcock/Pfim) | MIT | Small, managed | Its documentation doesn't describe orientation, 16-bit, interleaving or TGA 2.0 handling, and no releases are listed on the project page. | No evidence of the required coverage; not pursued |
| Windows Imaging Component | Part of Windows | None | Has no TGA codec. | Not available |

**ImageSharp findings:**
- Interleaving bits are ignored, so interleaved files would decode wrongly without any error.
- No 16-bit palette indices.
- The first palette index isn't applied to lookups.
- No TGA 2.0 footer, extension area or attributes type.
- 32-bit pixels are always treated as having alpha.
- Some end-of-file reads are unchecked.

**ImageMagick findings:**
- Only two-way interleaving is handled, and only when the interleave bits equal 2. TGA 1.0 and netpbm define 1 as two-way and 2 as four-way, so the two disagree.
- Four-way interleaving isn't handled.
- Alpha is enabled for all 32-bit data.
- The attributes type is used only to clear alpha when it isn't 3.

**StbImageSharp findings:**
- Right-to-left order and interleaving are ignored.
- The first palette index is treated as a byte skip.
- Out-of-range indices are silently replaced with entry 0.
- The 16-bit attribute bit is ignored.
- No TGA 2.0 metadata.
- Short reads of raw pixel data aren't reported.

## Consequences

- **Testing:** the decoder needs strong tests of its own, described in [FORMAT_COVERAGE.md](FORMAT_COVERAGE.md). Independent reference decoders are still wanted for testing (brief §9). Pillow is planned, and installing it is waiting on the owner's approval.
- **Interleaving:** it follows TGA 1.0 and [netpbm](https://stuff.mit.edu/afs/sipb.mit.edu/user/kenta/netpbm-10.32/converter/ppm/tga.h) (`TGA_IL_Two = 1`, `TGA_IL_Four = 2`). Files written to ImageMagick's convention would display differently. No real interleaved files were available to check against.
