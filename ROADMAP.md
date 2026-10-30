# JupyterSharp — Roadmap

JupyterSharp had no roadmap of its own; its plan was spread over [CHANGELOG.md](CHANGELOG.md), [ARCHITECTURE.md](ARCHITECTURE.md) and
the suite plan ([../ScienceSharp/ROADMAP.md](../ScienceSharp/ROADMAP.md), phase P2–P6). This is the project view.

## State (2026-10-30)

- Builtin kernels: `csharp`, `fsharp`, `powershell`, `powerfx`. Plugin kernel: `pysharp`. Tests: 66/66.
- The C# kernel runs notebooks written for .NET Interactive / Polyglot Notebooks: `display(...)`, `HTML(...)`, `#r "nuget: Id, Version"` and top-level `using var`.
- Global tool `jupytersharp` `0.3.1-net11-preview.4` and VS Code extension `0.2.1` (win32-x64, bundling host `.4`) are installed. Both run `#r "nuget:"` notebooks; the packaged host was tested with a nuget cell over its protocol.
- Every notebook of the ecosystem gate runs in JupyterSharp (32 gate steps, all exit 0; the 13 NDSharp notebooks are a step of their own).
- Packaging per platform: `build/package-extension.ps1 -Runtime win-x64|linux-x64|osx-arm64`.

## Next

1. **Click-through in VS Code:** open a template notebook with the installed extension and run a C# and a PySharp cell (J-2). The host inside the package was tested over the protocol, the editor was not.
2. **Align versions** of host, abstractions and the PySharp plugin; pack the plugin into the feed (J-1, suite S6/S7).
3. **MatSharp kernel** as a plugin, following `PySharpKernel` (J-4; MatSharp roadmap M4).
4. **Smaller package:** fetch libtorch and OpenCV on first use instead of bundling (J-6, suite S1).
5. **Linux x64 and macOS arm64:** run the built packages (J-5).
6. **Ontly and Ralf plugins:** migrate to JupyterSharp abstractions or drop from the documentation (J-3).
7. **Clean-up:** historical JupyterNet names in docs, 80 uncommitted changes (J-8, J-9, J-11).
8. **Publication:** only on request (suite D-F3): packages, tool and Marketplace.

## Not planned

- A Jupyter-protocol server for browsers (the host speaks NDJSON to the VS Code extension).
- Debugging support.

Open questions: [OPENPOINTS.md](OPENPOINTS.md).
