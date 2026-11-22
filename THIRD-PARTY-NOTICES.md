# Third-party notices

Quill includes code adapted from the following projects, and uses the NuGet packages listed at the end.

## Folia

- Source: `.agents/refs/Folia` (reference copy)
- Used in: `src/Quill.Editor.WinUI/Input/TextInputController.cs` and the Q6 prototype `src/Quill.Playground/ImePrototype/ImeInputController.cs`, both adapted from `src/app-winui/editor/interaction/EditorTextInputController.cpp`.

```
MIT License

Copyright (c) 2026 water2004

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## NuGet packages (M6.1, highlighting)

Referenced by `src/Quill.Highlighting` (11-source-mode.md, Q85); not copied into the repository.

- **TextMateSharp** and **TextMateSharp.Grammars** 2.0.4: MIT License, Copyright (c) 2021 Daniel Peñalba. https://github.com/danipen/TextMateSharp
- **Onigwrap** (dependency of TextMateSharp): a wrapper of the Oniguruma regular expression library, which is under the BSD 2-Clause License (Copyright (c) 2002-2021 K.Kosako). https://github.com/kkos/oniguruma
- The grammars and the Light+ / Dark+ themes bundled in TextMateSharp.Grammars come from Visual Studio Code (MIT License, Copyright (c) Microsoft Corporation) and the projects each grammar names in its file.

## Rust crates (M6.3, Mermaid)

Compiled into `quill_native.dll` from `native/quill-native` (Q87); not copied into the repository. `cargo tree` in that folder lists every transitive crate; all are under permissive licenses (MIT, Apache-2.0, BSD-2/3-Clause, Zlib, Unicode-3.0, Unlicense).

- **mermaid-rs-renderer** 0.3.1: MIT License, Copyright (c) 2026 mermaid-rs-renderer contributors. https://github.com/1jehuang/mermaid-rs-renderer
- **usvg** 0.47.0 (with fontdb, rustybuzz, ttf-parser): Apache-2.0 OR MIT, Copyright (c) the resvg authors. https://github.com/linebender/resvg
