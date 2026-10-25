<!--
  docs/PROJECT_BRIEF.md: the authoritative product specification for TGAPeek.
  Do not change the text between the BEGIN and END markers without the project
  owner's explicit approval.
-->

> **About this file** *(added when the brief was saved; not part of the brief)*
>
> - **Status:** the authoritative source for product requirements and intended behaviour. [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) describes how the brief will be delivered, and [CLAUDE.md](../CLAUDE.md) holds the working rules. Where either disagrees with this brief, the conflict is reported to the project owner, not resolved silently.
> - **Source:** pasted by the project owner into the Claude Code session on 2026-10-25. The message had no separate start/end markers, so the whole message was saved, from "Project: Lightweight, High-Compatibility Windows TGA Viewer" to "…handles unsupported or malformed files safely and transparently."
> - **Fidelity:** the text between the markers was copied by script from the session transcript, so every word, punctuation mark, line and list item is exactly as received and in the original order. The only additions are formatting: `#` heading markers on the title, the 11 numbered section titles and the 9 sub-headings, plus blank lines between paragraphs. Without those blank lines, Markdown would merge some lines into the list item above them.
> - **Check value:** 240 non-blank lines; SHA-256 of those lines (heading markers removed, joined with `\n`, UTF-8): `7d696487139ae7bd461ac7c82c56785292f60cee628ac6aa1be7cac47d895655`.

<!-- BEGIN PROJECT BRIEF (verbatim) -->

# Project: Lightweight, High-Compatibility Windows TGA Viewer

Build a production-quality, extremely lightweight native Windows desktop application for viewing TGA (Truevision TARGA) images, designed primarily for a professional 3D texture-art workflow.

The application must prioritise decoding correctness, image fidelity, compatibility, fast startup, low memory overhead, and reliability.

Do not begin by implementing only the common TGA formats. First research the TGA specification, identify its variants and edge cases, propose a comprehensive implementation plan, and then build and test the application.

## 1. Technology and architecture

- Use C# and Windows Forms.
- Target Windows 10 and Windows 11, subject to the chosen .NET runtime's supported operating systems.
- Use a modern, supported .NET version.
- Publish a self-contained Windows x64 executable so the target computer does not need a separately installed .NET runtime.
- Keep the executable, dependencies, memory consumption and startup time as small as reasonably possible.
- Avoid Electron, Chromium, web interfaces, heavyweight UI frameworks, unnecessary background services and external network dependencies at runtime.
- Keep the image decoder separate from the UI so it can be tested independently.
- Use a maintained, well-tested image-decoding library if it provides the required TGA coverage and fidelity. Otherwise, implement a dedicated decoder with comprehensive tests.
- Before choosing a library, inspect its licence, maintenance status, supported formats, known limitations and dependency footprint.
- Do not write an entire custom decoder unnecessarily if a suitable lightweight library provides demonstrably better compatibility.
- Do not sacrifice correct decoding merely to reduce a small amount of code or executable size.

## 2. TGA format coverage

Aim to support every valid, standard TGA variant that can reasonably be decoded, including legacy TGA files and TGA 2.0 files.

Support the following image classes:

- Colour-mapped / indexed-colour images.
- True-colour images.
- Grayscale images.
- Uncompressed image data.
- Run-length encoded (RLE) image data.

Implement or verify support for all applicable standard combinations of image type, pixel depth, colour-map entry depth and descriptor attributes.

Explicitly investigate and test:

- Indexed images with 8-bit and 16-bit colour-map indices where valid.
- Colour maps with nonzero first-entry indices.
- 15-bit, 16-bit, 24-bit and 32-bit true-colour data where valid.
- 8-bit grayscale and 16-bit grayscale-plus-alpha data.
- Colour-map entries with supported 15-bit, 16-bit, 24-bit and 32-bit encodings.
- 16-bit pixel interpretation, including 5-5-5-1 layouts and correct handling of attribute bits where specified.
- Image ID fields of varying lengths.
- Optional colour maps and all applicable header fields.
- TGA 2.0 footer and extension-area metadata.
- Developer directories and optional metadata structures.
- Gamma information, colour-correction tables, postage-stamp images, scanline tables and attribute-type information where present.
- Legacy files without a TGA 2.0 footer.
- Valid files containing additional data after the primary pixel data.

Do not assume that every .tga file is a conventional uncompressed 24-bit RGB image.

Document any formats or extensions that are intentionally unsupported. Never claim universal compatibility without test evidence.

## 3. Pixel decoding correctness

Pay particular attention to the following:

### Image orientation

Correctly interpret the image-origin bits in the descriptor, including all four combinations of top/bottom origin and left/right origin.

Display the image in the correct visual orientation without modifying the source file.

### Interleaving

Implement supported two-way and four-way interleaving modes, and test their correct reconstruction. Handle reserved or invalid descriptor values safely.

### Channel ordering and pixel layouts

- Correctly interpret BGR and BGRA byte ordering.
- Correctly interpret grayscale and grayscale-plus-alpha layouts.
- Handle 15-bit and 16-bit packed pixels according to the applicable format and attribute-bit semantics.
- Respect the distinction between pixel depth and the descriptor's number of attribute bits.
- Do not infer alpha solely from the presence of a 32-bit pixel format.
- Preserve all supported channel values without accidental swapping, truncation or quantisation.

### Colour maps

- Correctly apply the colour-map origin, length and entry depth.
- Validate palette indices against the actual colour-map range.
- Handle palette indices stored as 16-bit values without assuming they start at zero.
- Reject invalid indices safely instead of reading arbitrary memory or producing misleading output.

### RLE decoding

- Correctly implement run-length and raw-packet decoding.
- Handle packet boundaries, including packets that end at image boundaries.
- Detect packets that would produce more pixels than the declared image dimensions permit.
- Handle truncated packet headers and incomplete pixel payloads.
- Do not confuse a packet's run length with its byte length.
- Follow the specification for row boundaries and interleaving.
- Investigate real-world nonconforming files and decide explicitly whether safe compatibility fallbacks are appropriate.
- Never let malformed packet lengths cause buffer overflows or uncontrolled allocations.

### Alpha semantics

Distinguish between:

- Actual alpha-channel data.
- Unused or undefined attribute bits.
- Useful alpha.
- Premultiplied alpha.
- Files without meaningful alpha.

Respect available TGA 2.0 attribute-type metadata where possible.

Do not silently discard alpha or assume every fourth byte contains meaningful transparency.

## 4. Image fidelity and texture-artist features

This is a technical image viewer, not an image editor.

- Preserve the decoded pixel data without modifying the source.
- Do not save, re-encode, overwrite or otherwise alter the original TGA.
- Provide a checkerboard background for transparency.
- Provide a toggle to view the alpha channel as grayscale.
- Provide a way to show RGB without alpha compositing.
- Provide a way to view individual red, green and blue channels as grayscale.
- Provide a combined RGB view and an alpha-enabled RGBA view.
- Make transparency display distinguishable from black image pixels.
- Provide a pixel-inspection tool that displays coordinates and decoded channel values, including alpha where available.
- Report when an image does not contain meaningful alpha rather than presenting invented transparency information.
- Preserve full decoded precision for the supported source format until display conversion is necessary.

### Colour management

- Do not silently apply artistic colour grading, automatic enhancement, sharpening or contrast adjustment.
- Do not silently premultiply or unpremultiply alpha in a way that changes the underlying pixel values.
- Do not silently apply gamma correction or sRGB transformations that could misrepresent texture values.
- Investigate the TGA 2.0 gamma and colour-correction metadata.
- Establish a clearly documented default display policy suitable for inspecting game textures and masks.
- If metadata-aware colour management is implemented, make its behaviour explicit and provide a way to inspect the untransformed decoded channel values.
- Ensure the pixel inspector reports the decoded source values rather than values sampled from the final screen rendering.

### Scaling and navigation

- Fit the image to the window while preserving aspect ratio.
- Provide 100% / native-pixel zoom.
- Support zooming in and out, preferably centred around the cursor position.
- Support panning when zoomed in.
- Provide a clear reset-to-fit action.
- Provide both nearest-neighbour scaling for crisp pixel inspection and smooth scaling for general viewing.
- Avoid unnecessarily resampling the full source image whenever the window changes size.
- Keep zooming, panning, resizing and channel switching responsive.

## 5. Minimal user interface

The application should feel like a fast utility rather than a conventional image-editing package.

- Open directly into the image when a valid file path is supplied.
- Keep the image as the dominant part of the window.
- Use a minimal toolbar or compact menus.
- Include zoom, fit-to-window, transparency, alpha-only, RGB-channel inspection and scaling controls.
- Display the filename, dimensions, pixel depth, image type, compression type and alpha information where applicable.
- Include a useful error message when a file cannot be decoded.
- Support opening a new file through a menu or keyboard shortcut.
- Support drag-and-drop of TGA files onto the viewer.
- Support opening multiple files in separate windows or through a simple previous/next browsing workflow, provided this does not compromise simplicity.
- Remember sensible window size and position settings without introducing a large configuration subsystem.
- Support high-DPI displays and Windows display scaling.
- Ensure keyboard shortcuts, menus and controls remain accessible and usable at different window sizes.

Avoid adding image-editing, exporting, painting or other unrelated features.

## 6. Windows Explorer integration

- Accept a TGA path through command-line arguments.
- Correctly handle paths containing spaces, Unicode characters and non-ASCII characters.
- Support being registered as the default application for .tga files through normal Windows mechanisms.
- Do not modify Windows file associations automatically during development or first launch.
- Provide clear instructions for setting the published application as the default TGA viewer.
- Handle invalid paths, missing files and inaccessible files gracefully.
- If multiple files are passed on the command line, handle them in a predictable and documented way.

Investigate whether single-instance operation with file forwarding is worthwhile. Implement it only if it remains reliable and lightweight.

## 7. Performance and memory management

- Keep the application responsive while loading large images.
- Perform potentially expensive decoding outside the UI thread.
- Prevent stale background loads from overwriting a more recently requested image.
- Avoid unnecessary full-size image copies and intermediate buffers.
- Use checked arithmetic when calculating image dimensions, row sizes, pixel counts, buffer lengths and memory requirements.
- Dispose of image objects, streams, bitmaps and other unmanaged resources deterministically.
- Release the previous image and its buffers promptly when opening another file.
- Avoid retaining every image ever opened in memory.
- Avoid expensive per-pixel UI drawing operations when a more efficient rendering path is available.
- Choose a sensible maximum decoded-image memory budget and handle large files gracefully.
- Make resource limits configurable where appropriate, without exposing unnecessary complexity to ordinary users.
- Explain the memory cost of the chosen rendering approach.

Do not impose arbitrary format restrictions simply to optimise typical files. Use safe limits and explain when a file cannot be displayed because of available resources.

## 8. Robustness and security

Treat every input file as untrusted data.

- Validate headers and all offsets before accessing data.
- Validate dimensions, colour-map ranges, image types, pixel depths and descriptor fields.
- Detect truncated files and inconsistent metadata.
- Prevent integer overflow, out-of-bounds reads, invalid array sizes and uncontrolled memory allocations.
- Validate footer, extension-area and developer-directory offsets against the file boundaries.
- Validate all variable-length structures before parsing them.
- Do not trust metadata lengths or offsets just because they appear in a valid-looking header.
- Avoid unsafe code unless strictly necessary and justified.
- Do not execute content from image files.
- Do not access the network while opening an image.
- Do not require Administrator privileges.
- Never overwrite source images or unrelated project files.
- Return useful diagnostic errors rather than crashing or silently substituting a blank image.

A malformed image must never be able to trigger an unbounded allocation or an out-of-range memory access.

## 9. Automated tests and reference validation

Do not consider the application complete merely because it builds or opens one ordinary TGA image.

Create a dedicated automated test project for the decoder.

Generate or obtain legitimate test fixtures covering every supported combination of:

- Image type.
- Pixel depth.
- Colour-map entry depth.
- Alpha and attribute-bit interpretation.
- Compression type.
- Image origin.
- Interleaving mode.
- Palette origin and index range.
- Legacy and TGA 2.0 metadata.

Include fixtures for:

- Uncompressed RGB and RGBA images.
- RLE RGB and RGBA images.
- Indexed-colour images with nonzero palette origins.
- Grayscale images with and without alpha.
- 15-bit and 16-bit true-colour images.
- All four image-origin combinations.
- Two-way and four-way interleaved images.
- Different valid image-ID lengths.
- TGA 2.0 extension areas and developer directories.
- Colour-correction and gamma metadata.
- Valid files with optional metadata and trailing structures.
- Zero, extreme and otherwise invalid dimensions where applicable.
- Truncated headers, truncated pixel data and truncated RLE packets.
- Invalid palette indices.
- Incorrect offsets and lengths.
- Corrupt footers and extension structures.
- Files with unsupported or reserved format combinations.

### Independent validation

- Compare decoded output against trusted independent TGA decoders or reference implementations.
- Use independently generated fixtures rather than testing the decoder only against files it generated itself.
- Verify exact pixel values and orientation for lossless formats.
- Include tests that confirm RGB and alpha values are not swapped or modified.
- Include regression tests for every discovered decoding bug.
- Include property-based or fuzz testing of the parser where practical.
- Ensure malformed input does not crash the application, hang indefinitely or consume unreasonable memory.
- Keep a record of which format combinations have actually been tested and which remain unverified.

Do not fabricate test results. Clearly distinguish successful tests, untested cases and known limitations.

## 10. Build and release

- Create a reproducible Release build.
- Publish a self-contained Windows x64 version.
- Use trimming or other publish optimisations only after verifying that they do not break required functionality.
- Keep the published output as small as reasonably possible without sacrificing reliability.
- Include all required runtime dependencies in the published output.
- Provide a straightforward way to launch the application and a documented command-line invocation.
- Provide an optional lightweight installer only if it meaningfully improves installation and file-association setup.
- Do not add automatic updates, telemetry, analytics, crash-report uploads or other background services.
- Do not require a network connection to view images after installation.
- Document the build process, dependencies, licence obligations and supported Windows versions.

## 11. Implementation workflow

Follow this workflow:

1. Research the TGA specification and identify the relevant legacy and TGA 2.0 requirements.
2. Inspect the current project directory before changing anything.
3. Propose an implementation plan and explain the choice of decoder.
4. Identify any format coverage gaps before committing to the architecture.
5. Build the decoder and automated tests before building the UI.
6. Verify decoding correctness with generated fixtures and independent reference implementations.
7. Build the UI and connect it to the tested decoder.
8. Add command-line file opening and drag-and-drop.
9. Run automated tests, Release builds and representative performance tests.
10. Investigate failures and fix them rather than hiding them or weakening tests.
11. Publish the final self-contained application.
12. Verify the published build actually launches and opens representative files.
13. Explain how to install, run, update, and register the application as the default TGA viewer.

Work directly in the project files. Do not just provide code snippets or instructions for me to implement manually.

Ask before destructive operations, installing unfamiliar dependencies, changing system-wide settings or modifying Windows file associations.

At the end, provide:

- The location of the published executable.
- The exact command to run it.
- The steps for setting it as the default .tga viewer.
- A concise summary of supported TGA formats.
- Test results and any known limitations.
- Build and dependency information.
- Any additional steps required to package it for use on another Windows computer.

Success criterion: a tiny, fast, reliable Windows texture viewer that correctly displays as many valid TGA files as practical, preserves texture-channel fidelity, and handles unsupported or malformed files safely and transparently.

<!-- END PROJECT BRIEF (verbatim) -->
