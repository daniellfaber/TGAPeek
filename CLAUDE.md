# TGA Viewer — Project Guide for Claude

## Read first — before any implementation change
1. docs/PROJECT_BRIEF.md: the authoritative product specification (requirements and
   intended behaviour). Never edit it without the user's explicit approval.
2. docs/IMPLEMENTATION_PLAN.md: phases, architecture, technical decisions, known
   limitations, verification strategy; §14 records what Version 1 actually built.
3. docs/FORMAT_COVERAGE.md: what is tested and how. docs/DECODER_EVALUATION.md: why the
   decoder is custom.
4. This file: concise working rules.
If these documents disagree, do not guess or silently pick one: report the conflict and
ask the user. The brief wins over the plan; the user's explicit decisions win over both.

## Goals (priority order)
1. Defined, tested behaviour on all inputs (see Decoder guarantees).
2. Exact display of stored pixel/alpha values — no silent changes.
3. Broad real-world TGA compatibility.  4. Small, fast, native.

## Stack & layout
.NET 10 (SDK pinned in global.json), C#, Windows Forms. Solution TgaViewer.slnx:
- src/TgaViewer.Core — decoder, DisplayRenderer, AlphaPolicy, ViewMath, inspector/report
  text. net10.0. No UI refs. NO packages. Warnings are errors.
- src/TgaViewer.App — WinForms exe (TgaViewer.exe). net10.0-windows, PerMonitorV2,
  asInvoker manifest. NO packages.
- tests/TgaViewer.Core.Tests — xUnit; the only project allowed (approved) packages.
- tests/TgaViewer.TestSupport — test-only TGA writer + sample catalogue. Never shipped.
- tools/ — TgaViewer.SampleGenerator, SmokeTest.ps1 (real UI), MakeIcon.ps1,
  reference/ (Pillow reference script + README; its .venv is git-ignored).
v1 scope: viewing and inspection only. NO export/save/edit features.

## Commands
- Build / test: dotnet build -c Release; dotnet test -c Release
- Samples: dotnet run --project tools/TgaViewer.SampleGenerator -- samples/generated
- Local exe (downloads nothing): dotnet publish src/TgaViewer.App -p:PublishProfile=win-x64-framework-dependent
- Smoke test (opens windows on the user's desktop; sends keys only while the viewer is in
  front, else SKIPs; restores the settings file; -Exe picks the build):
  powershell -ExecutionPolicy Bypass -File tools/SmokeTest.ps1
- Release (the shipped build: uncompressed, not trimmed; see plan §14.3 for why):
  dotnet publish src/TgaViewer.App -p:PublishProfile=win-x64-self-contained
  Runtime packs 10.0.13 are in the NuGet cache (approved 2026-10-25). A newer SDK needing
  newer packs means a new download: ask first.
- To prove a restore downloads nothing, add: --source <empty folder>

## Safety rules — ask the user first, every time
- No installing software, SDKs, workloads, tools, or Python packages (venvs included).
- No new NuGet packages or other dependencies.
  Approved packages: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk (tests only).
- No downloads (sample files, references, runtime packs, docs into the repo).
- No deleting/overwriting/bulk-rewriting existing files without confirmation.
- No system settings, registry, environment variables, or file associations;
  the app must never register itself as a .tga handler.
- No commits, pushes, branch changes, or history rewrites unless asked.

## Format rules (verified against the TGA 2.0 spec and netpbm tgatoppm)
- Types 1,2,3,9,10,11 supported. 0 → Unsupported(NoImageData). 32/33 → Unsupported.
  Others → Invalid. Width or height 0 → Invalid.
- Depths: true-colour 15/16/24/32; grey 8, and 16 = grey byte then alpha byte;
  colour-mapped 8/16-bit indices with 15/16/24/32-bit entries; honour colorMapStart.
  Anything else → Unsupported.
- Descriptor: bits 0-3 alpha bits; bit 4 right-to-left; bit 5 top-to-bottom;
  bits 6-7 interleave (TGA 1.0/netpbm): 00 none, 01 two-way, 10 four-way, 11 → Unsupported.
  Row order: logical rows 0,s,2s,… then 1,1+s,…; origin flip applied after.
  (ImageMagick reads these bits differently — documented, not followed.)
- RLE: one continuous stream; packets crossing rows accepted (note); overrun clipped (warning).
- 5→8 bit expansion: (v << 3) | (v >> 2). Inspector shows the stored 5-bit values.
- Colour map in a true-colour/grey file: skipped. Colour-map type ≥ 2: ignored if its
  fields are zero, otherwise Unsupported. Type 0 with fields set: fields ignored (warning).
- Invalid palette index: stored as 0, marked in InvalidIndexPixels, hatched, warned.
- Footer: last 26 bytes = ext offset, dev-dir offset, "TRUEVISION-XFILE", '.', 0x00.
  Pixel reading never extends into a valid footer.
- Extension size 495 (larger: read 495 + note). Attributes type: 0 no alpha,
  1 undefined-ignore, 2 undefined-retain, 3 useful alpha, 4 premultiplied; 5+ → ignored.
- Postage stamp: image's pixel format, uncompressed, 1-byte w/h, image's origin bits (assumed).
- Colour-correction table: 256×ARGB SHORTs. Gamma, aspect ratio: shown, NEVER applied.
- Scan-line table: checked against the data, never used for decoding.
- Optional structures must lie between the end of pixel data and the footer; each is
  parsed in isolation and can never change decoded pixels.

## Raw-data and display rules
- Decoded values are stored exactly (0xAARRGGBB rows); never modified by heuristics.
  Premultiplied data stays premultiplied.
- Alpha interpretation happens ONLY in AlphaPolicy/DisplayRenderer (Auto / Use / Ignore).
  Auto applies alpha only when declared: attributes type 3/4, else header bits > 0.
  A 32-bit layout alone never implies alpha (brief §3). All-zero alpha → shown opaque
  (display only, indicated). Every Auto decision is explained in the UI.
- DisplayRenderer composites into an OPAQUE viewport buffer drawn 1:1. Never pass alpha
  buffers to GDI+ and never let GDI+ scale or interpolate image pixels.
- Compositing (Div255(x) = (x + 127) / 255):
  straight      out = Div255(c*a + bg*(255-a))
  premultiplied c' = min(c, a) per colour channel; out = c' + Div255(bg*(255-a))
  Invalid premultiplied pixels (any channel > A) are clamped for DISPLAY ONLY, counted,
  flagged; a=0 with RGB>0 displays exactly bg. Channel views show stored values.
- Scaling: nearest by default; Smooth = bilinear up / box down on premultiplied values,
  display only; nearest is used while zooming/panning, smooth once settled.
- No colour management, gamma or CCT application. Inspector reads TgaImage, never the screen.

## Untrusted input and memory rules
- Bounds-check every read; 64-bit arithmetic for sizes; no unsafe code.
- The memory budget is the governing limit (TgaDecodeOptions.MaxDecodeMemoryBytes, default
  4 GiB; App setting MaxDecodeMemoryMB). Width/height/pixel-count limits default to the
  format maximum — do not add arbitrary format restrictions (brief §7).
- TgaMemoryEstimator bounds the whole decode; checked BEFORE allocating; a test keeps
  actual allocations <= the estimate. The App also checks available commit memory and
  accounts for the viewport and the previous image.
- Files are streamed (TgaFile.Open: read-only, FileShare ReadWrite|Delete, unbuffered);
  never read a whole file into memory.
- Commit-on-success loading: decode + first render before swapping; stale loads are
  discarded; on failure the previous image stays (unless memory forced its release, which
  the user is told).
- The decoder throws only TgaException subtypes for content problems (Invalid / Unsupported /
  LimitExceeded, with a ReasonCode). I/O errors and cancellation propagate as themselves.
- Truncated pixel data: keep decoded pixels (file order), mark the rest in MissingPixels,
  flag Partial; strict mode (AllowPartial=false) → Invalid.

## Decoder guarantees (each must stay covered by tests)
G1 result-or-TgaException for any content; G2 bounded memory (estimate checked first);
G3 bounded time (linear in file + pixels); G4 no out-of-bounds reads;
G5 metadata corruption never alters pixels; G6 deterministic, reads only the given stream.
Not guaranteed — never claim otherwise: that every input below the limits can be
loaded or displayed (memory/OS/GDI+ may fail); ambiguous-spec interpretations.

## Testing rules
- Every decoder change needs tests; add a regression test for every decoding bug found.
- Golden tests are hand-written from the spec and must stay independent of the decoder
  and the TestSupport builder.
- Keep truncate-at-every-byte, malformed, metadata-isolation, memory and fuzz tests green.
- Compositing tests use exact values (semi-transparent, a=0 with RGB, invalid premultiplied,
  boundary c=a, checkerboard and solid backgrounds) and assert stored pixels unchanged.
- Pillow 13.0.0 references are committed (ReferenceTests, tools/reference/README.md). Set each
  file's agreement level (Exact / Colour / FiveBitColour / Excluded) from how Pillow reads the
  variant, never from whether outputs match. Reinstalling or upgrading Pillow needs explicit
  approval of the exact commands.
- Public sample files: download only from a list the user has explicitly approved.
- Keep docs/FORMAT_COVERAGE.md accurate. Never fabricate results; report skipped,
  blocked and untested items plainly.

## Code style
Nullable enabled; Core builds with warnings as errors. Prefer small explicit code;
cite the TGA spec field number in comments for format quirks.
