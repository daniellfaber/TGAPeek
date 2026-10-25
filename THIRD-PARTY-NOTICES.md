# Third-party notices

## Shipped with TGA Viewer

The viewer's own code uses no third-party libraries. The **self-contained release build** bundles the .NET runtime, including Windows Forms, which Microsoft and the .NET Foundation publish under the MIT licence:

> The MIT License (MIT)
>
> Copyright (c) .NET Foundation and Contributors
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

The .NET runtime also contains components under other permissive licences. The publish step therefore copies the notices from the runtime packs used for the build (version 10.0.13) into a `licenses` folder beside the exe:
- `licenses\Microsoft.NETCore.App.Runtime.win-x64\`: `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt`;
- `licenses\Microsoft.WindowsDesktop.App.Runtime.win-x64\`: `LICENSE.txt` (this pack has no separate notices file).

Keep that folder, and this file, with `TgaViewer.exe` when giving it to anyone else.

## Used only for development (not shipped)

| Component | Licence | Use |
|---|---|---|
| xunit, xunit.runner.visualstudio | Apache 2.0 | Running the tests |
| Microsoft.NET.Test.Sdk (and its dependencies, including Newtonsoft.Json) | MIT | Running the tests |
| Microsoft.NET.ILLink.Tasks | MIT | Build-time checks during the single-file publish |
| Pillow 13.0.0 | MIT-CMU | Producing the reference outputs for the tests, in the isolated environment `tools\reference\.venv` ([tools/reference/README.md](tools/reference/README.md)) |
