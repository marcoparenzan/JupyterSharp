<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="branding/logo-dark.svg">
    <img alt="JupyterSharp" src="branding/logo.svg" height="72">
  </picture>
</p>

# JupyterSharp

> **Part of [ScienceSharp](../ScienceSharp)** — the Science suite. Targets **.NET 11**. Status: preview, not published. License: MIT.
> **Form:** global tool `jupytersharp` (CLI) plus VS Code extension `marcoparenzan.jupytersharp-notebook`.

| [README](README.md) | [USAGE](USAGE.md) (install first) | [ARCHITECTURE](ARCHITECTURE.md) | [ROADMAP](ROADMAP.md) | [OPENPOINTS](OPENPOINTS.md) |
| --- | --- | --- | --- | --- |

A kernel host for **.NET notebooks in VS Code** — the itch [dotnet/interactive](https://github.com/dotnet/interactive)
left unscratched once it stopped being developed. JupyterSharp is a small **plugin host**: it knows no kernel by
name. **Every kernel is a plugin** (`IKernelPlugin`), loaded from a `kernels/<id>/` folder. JupyterSharp ships
four ".NET languages" as plugins of its own (C#, F#, PowerShell, Power Fx), published next to the host and the CLI;
every library implements its own kernel in its own repo (`<Library>.JupyterSharp.Kernel`: PySharp, MatSharp,
NDSharp), matching the library's release cycle rather than JupyterSharp's.

| Cell language | Backed by |
| --- | --- |
| `csharp` | Roslyn scripting (`Microsoft.CodeAnalysis.CSharp.Scripting`) — shipped plugin, a REPL-style C#, variables persist across cells. |
| `fsharp` | `FSharp.Compiler.Service`'s `FsiEvaluationSession` — shipped plugin, a real long-lived FSI session, so state just persists on its own. |
| `powershell` | `Microsoft.PowerShell.SDK` — shipped plugin, a real embedded PowerShell 7+ `Runspace`, so state persists the same way. |
| `powerfx` | `Microsoft.PowerFx.Interpreter`'s `RecalcEngine` — shipped plugin, a single formula engine per session; a cell is one formula, same shape as a Power Apps formula bar. |
| `pysharp` | Plugin `PySharp.JupyterSharp.Kernel` (PySharp repo) — a Python 3 interpreter written from scratch in C#, embedded via `PySharpLib.PyEngine`. |
| `matsharp` | Plugin `MatSharp.JupyterSharp.Kernel` (MatSharp repo) — MATLAB/Octave on `MatEngine`; figures are PNG outputs. |
| `ndsharp` | Plugin `NDSharp.JupyterSharp.Kernel` (NDSharp repo) — C# with NDSharp and MathemaSharp already referenced and imported (`np.Sum(...)` with no `#r`). |

Run code in cells, get HTML output, keep state between cells — the parts of dotnet/interactive
that made it worth using, on top of tools already living under `D:\dev`. JupyterSharp is not only a
VS Code extension: `JupyterSharp.Engine` (nbformat read/write, kernel dispatch, plugin discovery,
variable injection) is a standalone library, so a `.ipynb` can also be run headlessly from a
**CLI** (`jupytersharp run notebook.ipynb`) or **embedded directly in a .NET application**, which can
inject live objects into a kernel's context — see [samples/EmbeddingSample](samples/EmbeddingSample).

## Layout

- `src/JupyterSharp.Protocol` — the NDJSON message contracts shared by the host and the extension.
- `src/JupyterSharp.Kernels.Abstractions` — `IKernel`/`IKernelOutputSink`/`INotebookHost`/
  `IKernelPlugin`/`IVariableInjectable`, the entire contract a kernel implements. The Engine references only this project.
- `src/JupyterSharp.Kernels.{CSharp,FSharp,PowerShell,PowerFx}` — the four kernels JupyterSharp ships, each with a `*KernelPlugin` class; `build/BundledKernels.targets` publishes them into `kernels/<id>/` next to the host and the CLI.
- `src/JupyterSharp.Engine` — the reusable core: `NotebookDocument` (a C# nbformat reader/writer),
  `NotebookSession` (kernel dispatch, plugin discovery, variable injection) — what `JupyterSharp.Host`,
  `JupyterSharp.Cli`, and any embedding application all build on.
- `src/JupyterSharp.Host` — the process the VS Code extension spawns: wraps `NotebookSession` in the
  NDJSON stdin/stdout protocol.
- `src/JupyterSharp.Cli` — `jupytersharp run <notebook.ipynb>`, headless execution for CI/scripting.
- `vscode-extension/` — the VS Code extension: notebook type `jupytersharp-notebook` for `*.ipynb`
  files (real Jupyter nbformat, offered as an option rather than hijacking every notebook), one
  multi-language `NotebookController`, an HTML output renderer.
- `samples/tour.ipynb` — a notebook exercising the seven kernels of the first release (see the per-language samples; `ontly` and `ralf` do not load, J-3). `samples/` also has one
  per-language sample notebook (`csharp.ipynb`, `fsharp.ipynb`, `powershell.ipynb`,
  `powerfx.ipynb`, `pysharp.ipynb`, `ontly.ipynb`, `ralf.ipynb`), each focused on one kernel's own
  features. `samples/EmbeddingSample` — a console app embedding `JupyterSharp.Engine` directly and
  injecting a live object into a cell.
- `tests/JupyterSharp.Tests` — xUnit coverage for the nbformat reader/writer, the four shipped kernels
  (including variable injection), the plugin contract (the Engine references no kernel) and `NotebookSession`'s dispatch/error handling.
- `docs/protocol.md` — the wire protocol. [ARCHITECTURE.md](ARCHITECTURE.md) — how it all fits
  together, the plugin-loading design, and known limits. [USAGE.md](USAGE.md) — install and run
  it.
  [docs/pandas-in-notebooks.md](docs/pandas-in-notebooks.md) and
  [docs/cvintro-course.md](docs/cvintro-course.md) — running pandas/numpy/OpenCV/torch-backed
  PySharp notebooks, including the full 61-notebook cvintro course.

The library kernel plugins live beside the library they wrap, not in this repo: `PySharp/src/PySharp.JupyterSharp.Kernel`,
`MatSharp/src/MatSharp.JupyterSharp.Kernel`, `NDSharp/src/NDSharp.JupyterSharp.Kernel`. The Ontly and Ralf kernels (other repos) are not migrated yet (J-3).

## Quick start

```powershell
# build everything (host, CLI, engine, the four shipped kernels, tests, embedding sample)
dotnet build JupyterSharp.slnx
dotnet test tests/JupyterSharp.Tests

# headless: run a notebook's cells top to bottom from the CLI
dotnet run --project src/JupyterSharp.Cli -- run samples/tour.ipynb

# or install it as a real `jupytersharp` command (see Publishing below), then just:
jupytersharp run samples/tour.ipynb

# smoke-test the host directly (see docs/protocol.md for the message shapes)
echo '{"id":"1","method":"execute","params":{"kernel":"csharp","code":"21 * 2"}}' | dotnet run --project src/JupyterSharp.Host

# the VS Code extension (needs Node.js — see USAGE.md)
cd vscode-extension
npm install
npm run compile
# then F5 in VS Code to launch an Extension Development Host, and open samples/tour.ipynb
```

The four shipped kernels need nothing more. To exercise the library kernels (`pysharp`, `matsharp`, `ndsharp`) in the
CLI, the host, or the extension, publish them once — see [USAGE.md](USAGE.md) for the exact commands and where the
extension/CLI look for them (`jupytersharp.kernelPaths`/`--kernel-paths`). `jupytersharp kernels` lists what is installed.

## Publishing

`build/pack.ps1` packs the projects that live in this repo (`JupyterSharp.Protocol`,
`Kernels.Abstractions`, `Kernels.CSharp`, `Kernels.FSharp`, `Kernels.PowerShell`, `Kernels.PowerFx`,
`Engine`, `Host`, `Cli`) into
`D:\dev\NuGetLocalFeed`. `build/package-extension.ps1` publishes `JupyterSharp.Host` (with the four shipped
kernels in `host/kernels/`) *and* the library kernel plugins (PySharp, MatSharp, NDSharp) into `vscode-extension/host/`, then packages the extension as a `.vsix`.

`JupyterSharp.Cli` packs as a real `dotnet tool` (`jupytersharp` on PATH), the same pattern PySharp
uses for `pysharp`:

```powershell
./build/pack.ps1
dotnet tool install --global --add-source D:\dev\NuGetLocalFeed JupyterSharp.Cli   # first time
dotnet tool update  --global --add-source D:\dev\NuGetLocalFeed JupyterSharp.Cli   # after bumping <Version>
```

`dotnet tool update` only notices a *version bump* — re-`pack`ing the exact same `<Version>` (this
repo hasn't bumped it yet) leaves it reporting "already installed" and running the old bits, the
same NuGet global-package-cache gotcha Ontly's own `_rules.md` documents. Without a version bump,
force a real refresh instead:

```powershell
dotnet tool uninstall --global JupyterSharp.Cli
dotnet tool install --global --add-source D:\dev\NuGetLocalFeed JupyterSharp.Cli
```

## A note on licensing

This is a development exercise, not a commercial effort and not a way to take anything from anyone. It tries to show that .NET and C# can do the same kind of
scientific work as Python, in the hope that someone will try it. Its licensing, including the third-party material it adapts or credits, was reviewed with the help
of generative AI, with the aim of the greatest respect for the rights of the owners. That is not legal advice, and some points are still open: they are listed in
[LICENSING.md](../ScienceSharp/LICENSING.md). If you own material that is used here and have a concern, please open an issue: it will be discussed and, if needed, fixed or removed.
