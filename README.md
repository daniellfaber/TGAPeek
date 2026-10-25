# TGAPeek

A lightweight, native Windows viewer for TGA (Truevision Targa) images, made for inspecting game and 3D textures. It shows pixel and alpha values exactly as stored, opens a wide range of TGA variants, and explains clearly when a file is damaged or unsupported. It never changes, re-saves or uploads anything.

**Status:** Version 1 is complete, including the self-contained release build, which needs no .NET installation. See [Building a release](#building-a-release).

## Running it

- **The release build:** `artifacts\publish\win-x64-self-contained\TGAPeek.exe`. It carries everything it needs, including .NET, so it runs on any 64-bit Windows 10 or 11 PC. A smaller development build that uses the installed .NET 10 is in `artifacts\publish\win-x64-framework-dependent\`.
- **From the command line:**
  ```
  TGAPeek.exe "C:\textures\brick wall.tga"
  TGAPeek.exe first.tga second.tga third.tga
  ```
  With several files, the first one opens and Left/Right step through the files in the order given. With one file, Left/Right step through the TGA files in its folder, in Explorer-style name order. Every launch opens its own window.
- **Other ways to open files:** Ctrl+O, dragging files (or a folder) onto the window, or Explorer's "Open with".

## Using the viewer

| Action | Keys and mouse |
|---|---|
| Open | Ctrl+O, or drop files onto the window |
| Previous / next image | Left / Right, Page Up / Page Down (Home / End for first / last) |
| Fit to window | Ctrl+0, or double-click |
| Actual pixels (100%) | Ctrl+1, or double-click when fitted |
| Zoom | Mouse wheel (zooms around the pointer), + / −, Ctrl++ / Ctrl+− |
| Pan | Drag with the left mouse button, or Ctrl+arrow keys |
| RGBA (alpha applied) / RGB (alpha not applied) | Ctrl+2 / Ctrl+3 |
| Red / green / blue channel as greyscale | Ctrl+4 / Ctrl+5 / Ctrl+6 |
| Alpha channel as greyscale | Ctrl+7 |
| Alpha: Auto / Use / Ignore | Ctrl+Shift+1 / 2 / 3 |
| Nearest-neighbour / smooth scaling | Ctrl+Shift+N / Ctrl+Shift+S |
| Checkerboard on or off | Ctrl+B (solid colours: View ▸ Background) |
| Information panel | F9 |
| Inspect a pixel | Point at it (values appear in the status bar); click to pin it; Esc releases it |

The **status bar** shows:
- the size, pixel format and compression;
- how alpha is being shown (point at it for the full reason);
- the zoom level;
- the values of the pixel under the pointer;
- any warnings (click them for details).

The **information panel** (F9) shows:
- every header field, and the TGA 2.0 metadata (author, software, gamma, developer directory, colour-correction table, postage stamp, scan-line table);
- all warnings;
- a detailed pixel inspector: stored values, 5-bit values for 16-bit pixels, palette indices, and normalised 0–1 values.

The window size and position, scaling, background and panel width are remembered in `%LOCALAPPDATA%\TGAPeek\settings.json`.

## How images are displayed (default display policy)

- **No colour changes:** pixel values are shown as stored. There is no colour management, gamma correction, sRGB conversion, sharpening or "enhancement". The TGA 2.0 gamma value and colour-correction table are shown in the information panel but never applied.
- **Pixel inspector:** values always come from the decoded file, never from the screen.
- **Scaling:** nearest-neighbour by default (crisp pixels for inspection); smooth scaling is available. At 100% zoom, one image pixel is one physical screen pixel. The viewer is per-monitor DPI aware, so this should also hold with Windows display scaling. So far it has only been tested at 100% display scaling.
- **Transparency** is drawn over a checkerboard by default, so transparent pixels can't be confused with black ones. A solid background colour can be chosen instead.
- **Alpha, Auto mode:** decided from what the file declares, never just from its pixel size:
  1. If the file has a TGA 2.0 attributes type, it is followed:
     - 0, 1 and 2 (no alpha, or undefined alpha): shown opaque, with the data kept and visible in the Alpha view;
     - 3: straight alpha;
     - 4: premultiplied alpha.
  2. Otherwise alpha is applied only if the header declares alpha bits. A 32-bit file that declares none is shown opaque, with a notice.
  3. If every alpha value is 0, the image is shown opaque with a notice, because the channel is almost certainly unused.

  View ▸ Alpha ▸ *Use alpha* or *Ignore alpha* overrides any of these. The status bar always says which rule applied.
- **Premultiplied alpha** is composited as premultiplied, and the stored values are never converted. Pixels whose colour exceeds their alpha are invalid premultiplied data. They are clamped for display only, counted in the information panel, and flagged by the inspector.
- **Damaged or unusual files:**
  - Missing pixels in a truncated file have **magenta stripes**.
  - Pixels with invalid palette indices have **orange stripes**.
  - A banner explains both.
  - In the Alpha view, a file without alpha shows a grey pattern and a message rather than a misleading white image.

## Supported TGA formats

| Area | Supported |
|---|---|
| Image types | Colour-mapped, true-colour and greyscale, each uncompressed (types 1, 2, 3) or run-length encoded (types 9, 10, 11) |
| True-colour depths | 15, 16 (with attribute bit), 24 and 32 bits |
| Greyscale | 8-bit, and 16-bit with alpha |
| Colour maps | 8- and 16-bit indices; palette entries of 15, 16, 24 and 32 bits; non-zero first index |
| Layout | All four origins; two-way and four-way interleaving; RLE packets that cross rows |
| Metadata | Image ID; TGA 2.0 footer, extension area, developer directory, colour-correction table, postage stamp, scan-line table; extra data after the pixels |

**Not supported:**
- Huffman/delta compressed types 32 and 33;
- the reserved interleaving value;
- reserved colour-map types that carry data;
- pixel depths outside those listed (for example 8-bit true-colour).

Each is reported with a clear message. [docs/FORMAT_COVERAGE.md](docs/FORMAT_COVERAGE.md) lists exactly what has been tested and how.

## Setting TGAPeek as the default for .tga files

Windows does this itself; the viewer never changes file associations on its own.

1. Put `TGAPeek.exe` somewhere permanent, for example `%LOCALAPPDATA%\Programs\TGAPeek\`. If you move it later, repeat these steps.
2. Right-click any `.tga` file and choose **Open with ▸ Choose another app**.
3. Choose **Choose an app on your PC** (Windows 11) or **More apps ▸ Look for another app on this PC** (Windows 10), and select `TGAPeek.exe`.
4. Select **TGAPeek**, then click **Always** (Windows 11), or tick **Always use this app to open .tga files** and click OK (Windows 10).

You can also set it later in **Settings ▸ Apps ▸ Default apps**: search for `.tga` and choose TGAPeek.

## Installing, updating and removing

- **Install:** copy `TGAPeek.exe` (the self-contained release build) into a folder of your choice. No installer and no Administrator rights are needed. When giving it to someone else, include `LICENSE.txt`, `THIRD-PARTY-NOTICES.md` and the `licenses` folder from the same release folder. The MIT licence and the .NET licences require their notices to go with it.
- **Update:** close the viewer and replace `TGAPeek.exe` with the new version at the same path. The file association keeps working.
- **Remove:** delete `TGAPeek.exe` and the `%LOCALAPPDATA%\TGAPeek` folder, and choose another default app for `.tga` if you set one.

## Memory

- **The file is streamed:** it is read in small pieces and never held in memory as a whole.
- **Decoded image:** 4 bytes per pixel (for example 64 MB for 4096 × 4096). Colour-mapped images add 1–2 bytes per pixel for their palette indices.
- **Damaged images:** truncated images, or images with invalid indices, add 1 bit per pixel for each marking.
- **Display:** about 8 bytes per pixel of the *window*, not of the image: the rendered view plus Windows' back buffer. That is about 16 MB at 1920 × 1080 and 66 MB at 3840 × 2160. Only the visible part of the image is processed, so zooming, panning and resizing don't depend on the image size.
- **While loading:** the previous image stays on screen until the new one is ready, so a failed load never leaves you with nothing. If memory is short, the previous image is closed first, and the viewer tells you.
- **Limits:**
  - Each image may use up to 4 GB by default. To change it, set `MaxDecodeMemoryMB` in the settings file.
  - Before decoding, the viewer also checks that Windows has enough memory available.
  - If either check fails, the viewer explains why instead of trying.

## Privacy and safety

- No network access, telemetry, analytics, crash uploads or automatic updates.
- Runs with normal user rights; never asks for Administrator rights.
- Files are opened read-only and never locked, so other programs can still save or delete them.
- Every file is treated as untrusted. Sizes and offsets are validated before use, and malformed files produce explanations rather than crashes.

## Supported Windows versions

TGAPeek targets 64-bit Windows 10 and 11 on .NET 10, as far as [.NET 10's supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) allow:
- **Windows 11:** current versions are supported.
- **Windows 10:** Microsoft's list includes it only in versions 1607, 1809 and 21H2 marked "(E)", plus 21H2 IoT. The ordinary Windows 10 22H2 (Home and Pro) is not on that list. The viewer will very likely run there, but that is not officially supported and hasn't been tested.

## Building from source

Requires the .NET SDK 10.0.402 or a later 10.0 feature band (pinned in `global.json`).

```
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/TGAPeek.App -- "path\to\file.tga"
```

Development tools:
```
dotnet run --project tools/TGAPeek.SampleGenerator -- samples/generated          # sample files (add --large for 8192 × 8192)
dotnet publish src/TGAPeek.App -p:PublishProfile=win-x64-framework-dependent    # small build using the installed .NET
powershell -ExecutionPolicy Bypass -File tools/SmokeTest.ps1                         # end-to-end check (opens viewer windows; -Exe picks the build)
powershell -ExecutionPolicy Bypass -File tools/MakeIcon.ps1                          # redraws the application icon
```

| Path | Purpose |
|---|---|
| `src/TGAPeek.Core` | TGA decoder, display renderer, alpha policy, inspector. No UI code, no dependencies. |
| `src/TGAPeek.App` | Windows Forms viewer (`TGAPeek.exe`). No dependencies. |
| `tests/TGAPeek.Core.Tests` | 420 automated tests (xUnit), including a comparison with the independent decoder Pillow. |
| `tests/TGAPeek.TestSupport` | Test-only TGA writer and sample catalogues. |
| `tools/` | Sample generator, smoke test, icon script, and the Pillow reference script ([tools/reference/README.md](tools/reference/README.md)). |
| `docs/` | [Project brief](docs/PROJECT_BRIEF.md), [implementation plan](docs/IMPLEMENTATION_PLAN.md), [format coverage](docs/FORMAT_COVERAGE.md), [decoder evaluation](docs/DECODER_EVALUATION.md). |

## Building a release

The distribution build is a single self-contained `TGAPeek.exe` for 64-bit Windows, which needs no .NET installation:

```
dotnet publish src/TGAPeek.App -p:PublishProfile=win-x64-self-contained
```

The output goes to `artifacts\publish\win-x64-self-contained\`:
- `TGAPeek.exe` (107 MB);
- `LICENSE.txt`, TGAPeek's own MIT licence;
- `THIRD-PARTY-NOTICES.md`;
- a `licenses` folder holding the .NET runtime's licences and notices, copied from the runtime packs at publish time.

Zipped for sharing, the folder is 44 MB. These figures were measured on this PC on 2026-10-25:
- **Downloads:** the first publish downloads the win-x64 .NET runtime packs (version 10.0.13) from nuget.org into the NuGet cache. That happened on 2026-10-25, with the owner's approval. Later publishes use the cache. A newer .NET SDK may need newer packs, which means another download.
- **Start-up:** the release build shows an image about 216 ms after launch and uses about 18 MB of private memory, the same as the build that uses the installed .NET. It loads nothing from an installed .NET and extracts nothing to disk.
- **Not compressed:** compression shrinks the exe to 49 MB, but every start then takes about 80 ms longer and uses 44 MB more memory. A viewer is opened often, so the release build is uncompressed. Zipping gives an even smaller download. The compressed build is still available with `-p:PublishProfile=win-x64-self-contained-compressed`.
- **Not trimmed:** the .NET SDK refuses to trim Windows Forms apps (`TrimmingWindowsFormsIsNotSupported`). Forced through an internal switch as an experiment, trimming gave a 22 MB exe. That exe started more slowly, never saved its settings, and failed two display checks.
- **English only:** Windows Forms' own translated messages are left out, which saves 9 MB. The viewer's interface is in English.
- **SmartScreen:** the exe is not code-signed, so Windows may warn the first time it runs on another PC.
- **Security updates:** the bundled runtime doesn't receive .NET security updates through Windows Update. Rebuild with an updated SDK to pick them up.

## Licences

TGAPeek is released under the [MIT License](LICENSE), copyright (c) 2026 Daniel Faber: anyone may use, copy, modify and distribute it, provided the copyright and licence notice are kept. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
