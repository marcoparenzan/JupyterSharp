# JupyterSharp — Open points

Suite-wide points are in [../ScienceSharp/OPENPOINTS.md](../ScienceSharp/OPENPOINTS.md).

| # | Point | Status |
| --- | --- | --- |
| J-1 | `display`, `HTML`, `#r "nuget:"` and the `using var` rewrite are in the source, in the `preview.4` packages, in the installed tool and in the packaged host (checked over its protocol). The VS Code editor itself was not exercised. | partly done |
| J-2 | The extension (win32-x64) is installed but no notebook was opened with it in VS Code. | open — one manual check |
| J-3 | The Ontly and Ralf kernel plugins (other repos) still reference `JupyterNet.Kernels.Abstractions`, so the host cannot load them. They are not in the bundle. | open (suite S4) |
| J-4 | There is no MatSharp kernel (MatSharp roadmap M4). | open |
| J-5 | Linux x64 and macOS arm64 extension packages are built, never run. | open |
| J-6 | Package size, solved for the native libraries: the notebook extension no longer bundles libtorch and OpenCV (win32-x64 153 MB → 43 MB, linux-x64 and darwin-arm64 43-46 MB). `build/package-extension.ps1 -BundleNatives` puts them back. Linux and macOS packages are built, not run. | done on Windows |
| J-7 | The host bundles the Anthropic, OpenAI and Azure AI client libraries (about 10 MB). Needed only for Ralf, which cannot load. | open |
| J-8 | About 80 uncommitted changes from the JupyterNet → JupyterSharp rename plus this session's edits. | open |
| J-9 | `docs/`, `ARCHITECTURE.md` and `CONVERSATION.md` still contain historical JupyterNet names. | open |
| J-10 | The NavierStokes notebook is written for Polyglot Notebooks (`display(HTML(...))`); it runs in JupyterSharp but was not run in its declared host. | open |
| J-11 | Removed on 2026-10-30: the old `jupyternet` tool and the `marcoparenzan.jupyternet-notebook` extension. VS Code deletes the old extension folder on its next restart. | closed |
| J-12 | Closed 2026-10-30: the gate rewrote the notebooks' saved outputs on every run (random SVG ids made every run differ). PlotSharp `preview.2` derives the ids from the chart, two consecutive gate runs now give identical notebooks, the notebooks are committed with their outputs, and a gate run leaves every repo clean. | closed |
| J-13 | `#r "nuget:"` support is new (`preview.4`): all 14 NDSharp notebooks run (after their pins moved off 2.5.0), and they are a gate step. Not covered: `#r "nuget:"` in F# or PowerShell cells, a package that needs a different target framework than net11.0, offline use with an empty cache, and conflicts when two cells ask for different versions of the same package (the first reference wins). | open |
| J-14 | A top-level `using var` is rewritten to `var`, so the object is not disposed at the end of the cell (Roslyn scripting rejects the original form). | known |
| J-15 | Tool `preview.4` and extension `0.2.1` (bundling host `.4`) were installed on 2026-10-30; the four notebook templates were run with the installed tool. | closed |
