# Changelog

## 0.3.1-net11-preview.6 — every kernel is a plugin (not packed, not published)

- `JupyterSharp.Engine` no longer references any kernel project: `NotebookSession` creates kernels only from `IKernelPlugin`s. The four kernels
  JupyterSharp ships got a plugin class each (`CSharpKernelPlugin`, `FSharpKernelPlugin`, `PowerShellKernelPlugin`, `PowerFxKernelPlugin`) and are
  published into `kernels/<id>/` next to the host and the CLI by the new `build/BundledKernels.targets`.
- `KernelPluginLoader` always searches `<host>/kernels/*`, then `--kernel-paths` / `JUPYTERSHARP_KERNEL_PATHS` (they no longer replace the bundled folder);
  a later directory wins on the same id. Plugin dependencies that a package ships per platform are resolved through the plugin's `.deps.json`.
- `NotebookSession(..., plugins:)` for kernels given in code, `NotebookSession.KernelIds`, and an "unknown kernel" error that lists the installed ones.
  `INotebookHost.NotebookPath` (default member) so the C# plugin keeps resolving `#r`/`#load` against the notebook's folder.
- `jupytersharp kernels` and `JupyterSharp.Host --list-kernels` print the installed ids.
- Removed `JupyterSharp.Protocol.KernelIds` and the extension's `KernelIds`; the extension reads its notebook languages from `package.json`, which
  `build/package-extension.ps1` regenerates from the packaged host. The package script also publishes `matsharp` and `ndsharp` (not run yet).
- Plugin dependencies that a package ships per platform are resolved through the plugin's `.deps.json`, then by looking in the plugin folders (the second step is
  needed because the handler runs before .NET's LoadFrom probing; without it PySharp's plot kernel failed to find `MathemaSharp.Geometry`).
- Tests: 73/73 (7 new: the Engine references no kernel assembly, each plugin's id, the kernel list, unknown-kernel messages, the notebook path through the plugin).

## 0.3.1-net11-preview.4 — `#r "nuget:"` and top-level `using var` (packed to the local feed only, not published)

Found while running the 14 NDSharp notebooks in JupyterSharp: none could run, because they reference their packages with `#r "nuget: Id, Version"`.

- **`#r "nuget: Id, Version"`** in C# cells. The directive is cut out of the cell, the packages are restored with `dotnet restore` into
  `%TEMP%\jupytersharp-nuget\<hash>` (override with `JUPYTERSHARP_NUGET_WORK`), their assemblies are added to the script options and their native
  libraries are found through a per-assembly DllImport resolver. New files: `NuGetReferences.cs`, `NativeLibraries.cs`.
- **Top-level `using var x = ...;`** is rewritten to `var x = ...;` (Roslyn scripting rejects it). The object is not disposed at the end of the cell.
  New file: `CellRewrites.cs`.
- Tests: 66/66 (19 new: directive parsing, project generation, assets reader, a failing restore, two real restores from the local feed, the `using var` rewrite).
- Result: 13 of the 14 NDSharp notebooks ran clean, including the SkiaSharp plot and the OpenCV image notebooks. `12-heatmap` failed because it uses
  `Figure.AddColorbar`, which the old `NDSharp.Plot 2.5.0` pin did not have. After the notebook pins moved to the current feed versions all 14 run.
- All project versions are now `0.3.1-net11-preview.4` (the project files had said `.2` while `.3` was packed with a command-line override).


## 0.3.1-net11-preview.3 — `display` and `HTML` cell helpers (packed to the local feed only, not published)

Found while running the NavierStokes article notebook (`Articoli/2026.10 NavierStokes`) in JupyterSharp.

- **The C# kernel now provides `display(object)` and `HTML(string)`**, the lowercase helpers used by
  notebooks written for .NET Interactive / Polyglot Notebooks. Before, `display(HTML(svg))` failed
  to compile (`CS0103`), so every later cell that depended on a variable from such a cell failed
  too. `HTML` wraps markup in the new `HtmlString`; `display` sends it as HTML, a `byte[]` as a PNG
  image, anything else as text, and null as nothing. They are members of `ScriptGlobals`, so a cell
  can define its own `display` and it wins. Covered by three tests in `CSharpKernelTests`.

## 0.7.0 — C# console capture, notebook-relative paths (NuGet packages 0.3.0)

Found while moving the Numbers repo's notebooks (`D:\Dev\2027\repos\Numbers`) to JupyterSharp.

- **`Console.Out`/`Error` in C# cells are now captured**, the way `FSharpKernel` already did for
  `printfn`. Before, a C# cell's `Console.WriteLine` went to the real process stdout: missing from
  the CLI's saved notebook, and — in `JupyterSharp.Host` — written raw into the NDJSON channel. The
  new `ConsoleCapture` swaps the console for exactly one cell and also wraps the sink, so (1)
  console text is flushed as one text output right before any `Display.*` output, the result or an
  error, keeping the order the cell produced them in, and (2) writes a *sink* makes while handling
  an output (the CLI echoes every output to the console) pass straight through instead of being
  captured back as a duplicate.
- **Relative `#r`/`#load` resolve against the notebook's folder.** `CSharpKernel(notebookPath)`
  compiles every cell with `ScriptOptions.WithFilePath(notebookPath)` — the same thing Screen9's
  `CsxRunner` does for a `.csx` — so Roslyn resolves relative directives against the notebook, and
  inside a `#load`ed file against that file's own folder. Before, only absolute paths worked, so
  notebooks were tied to one machine's layout. Plumbing: `NotebookSession(notebookPath:)` /
  `NotebookSession.NotebookPath`; the CLI passes the notebook it runs; the protocol's
  `ExecuteParams` gains an optional `notebookPath` that the extension fills with the document's
  file path (absent for untitled notebooks) and the host applies before the `csharp` kernel is
  created. Old extensions/hosts interoperate: the field is optional on both sides.
- NuGet package versions bumped 0.2.0 → 0.3.0 (they had never followed this changelog's numbering;
  the bump is what makes `dotnet tool update` pick the new CLI up from the local feed).

## 0.6.0 — PowerFx kernel, per-language samples

- New builtin **`powerfx`** kernel (`JupyterSharp.Kernels.PowerFx`, `Microsoft.PowerFx.Interpreter`):
  one `RecalcEngine` per session; a cell is one formula, the same grain a Power Apps formula bar
  works at. `ParserOptions.Culture` is pinned to the invariant culture — the host machine's own
  `it-IT` culture otherwise expects `;` instead of `,` as the argument separator, breaking every
  formula outright.
- `Set(name, expr)` cells are handled by the kernel itself rather than delegated to Power Fx's own
  `Set` function: Power Fx's native `Set` can only update a name whose type it already knows, and
  pre-declaring a new name as an untyped `Blank` just turns the "unknown name" bind error into a
  later "Invalid argument type (Decimal). Expecting a Blank value instead." type-mismatch error
  once a real value is assigned — a Power Fx variable's type is fixed for good at its first
  `UpdateVariable` call. The kernel instead evaluates `expr` on its own and calls
  `UpdateVariable(name, value)` directly, which both declares a brand-new name (typed from the
  value itself) and updates an existing one, with Power Fx's own `Set` binder never involved.
- Variable injection (`IVariableInjectable.SetVariableAsync`) via `TypeMarshallerCache.Marshal` —
  properties only, not methods, since Power Fx has no concept of calling a CLR instance method
  from a formula. A real, by-design difference from the other three builtin kernels, not a gap.
- Removed `<InvariantGlobalization>true</InvariantGlobalization>` from `JupyterSharp.Host.csproj` and
  `JupyterSharp.Cli.csproj` (leftover, unused boilerplate from this repo's first commit — both publish
  framework-dependent, so it bought nothing). `Microsoft.PowerFx.Core` builds its own error messages
  through a `CultureInfo.CreateSpecificCulture` call that throws outright under that mode, turning
  a clean division-by-zero/syntax-error `WriteError` into a raw .NET exception — found by running a
  PowerFx error cell through the real host, not caught by `dotnet test` since the test project has
  no such setting.
- `samples/tour.ipynb` gained two PowerFx cells. New per-language sample notebooks
  (`samples/{csharp,fsharp,powershell,powerfx,pysharp,ontly,ralf}.ipynb`), one per kernel, each
  demonstrating that kernel's own specific features rather than the single combined tour.

## 0.5.0 — image output

- `IKernelOutputSink.WriteImage(mimeType, bytes)` — kernels can now emit real images. A default
  interface implementation falls back to an inline `<img>` data-URI through `WriteHtml`, so existing
  sinks and plugins keep working unchanged.
- Wire protocol: an `output` event may carry `"encoding": "base64"` with binary `data`
  (`image/png`, `image/jpeg`, ...); the extension turns it into a native `NotebookCellOutputItem`, so
  VS Code renders it without a custom renderer. `.ipynb` round-trips images as nbformat base64
  `image/png` entries (and loads them back as bytes).
- All packages (`Abstractions`, `Protocol`, `Engine`, `Host`, builtin kernels) bumped to 0.2.0.

## 0.4.0 — PowerShell kernel

- New builtin **`powershell`** kernel (`JupyterSharp.Kernels.PowerShell`, `Microsoft.PowerShell.SDK`
  — real PowerShell 7+): one embedded `Runspace` per session, session state persists the same way
  the other builtin kernels' does; `Write-Host`/`Write-Warning` captured via `PowerShell.Streams`
  (no custom `PSHost` needed — PS7+ routes `Write-Host` through the Information stream
  specifically for this); variable injection via `Runspace.SessionStateProxy.SetVariable`, with
  real member/method access on the injected object, no unwrapping.
- Forced a real dependency bump: `Microsoft.PowerShell.SDK` needs
  `Microsoft.CodeAnalysis.CSharp >= 5.0.0`, conflicting with the C# kernel's pinned `4.14.0` via
  `Microsoft.CodeAnalysis.CSharp.Scripting` — moved to `5.9.0`; the C# kernel's own test suite
  re-verified passing after the bump, not just assumed compatible.
- `samples/tour.ipynb` gained two PowerShell cells; `jupytersharp run`/the host/the extension all
  install/dispatch it identically to `csharp`/`fsharp`.

## 0.3.0 — headless CLI, embedding, variable injection, tests

- New `JupyterSharp.Engine`: `NotebookDocument` (a C# nbformat v4 reader/writer — the first one in
  this repo outside the TypeScript extension), `NotebookSession` (kernel dispatch/plugin discovery/
  variable injection, pulled out of `JupyterSharp.Host`'s `Program.cs`). `JupyterSharp.Host` is now a
  thin NDJSON wrapper around it.
- New `JupyterSharp.Kernels.Abstractions.IVariableInjectable`: an opt-in kernel capability for
  injecting a live .NET object into a kernel's context, reached via
  `NotebookSession.SetVariableAsync`. Implemented by both builtin kernels — F#'s via
  `FsiEvaluationSession.AddBoundValue` (real static-ish typing, no unwrapping needed), C#'s via a
  one-line hidden `dynamic x = Injected["x"];` interaction (Roslyn scripting has no runtime-
  extensible globals story otherwise).
- New `JupyterSharp.Cli` (`jupytersharp run <notebook.ipynb> [--kernel-paths] [--output|--in-place]
  [--fail-fast]`) — headless notebook execution, a minimal `nbconvert --execute` equivalent, exit
  code reflecting success.
- New `samples/EmbeddingSample` — a console app embedding `JupyterSharp.Engine` directly (no process,
  no NDJSON) and injecting a `Weather` object into both builtin kernels.
- New `tests/JupyterSharp.Tests` (xUnit): nbformat round-tripping, both builtin kernels (session
  persistence, output capture, error handling, variable injection), `NotebookSession` dispatch.
  Found two real issues while writing them: an injected object's type must be `public` (the DLR
  binder enforces real accessibility from the script's own separately-compiled assembly), and
  `FSharpKernel` is not safe under concurrent/parallel execution (it swaps the process-global
  `Console.Out`/`Error`) — xUnit's default test parallelization raced on it and flaked a test;
  fixed by disabling parallelization for this test assembly, and documented as a real constraint
  in ARCHITECTURE.md, not just a test workaround.

## 0.2.0 — rename to JupyterSharp, plugin split, F#

- Renamed KerNet → **JupyterSharp** throughout (namespaces, `PackageId`s, the extension's id/commands/
  settings, docs) — including the repo folder itself
  (`D:\Dev\2026\repos\KerNet` → `D:\Dev\2026\repos\JupyterSharp`).
- `pysharp`/`ontly`/`ralf` moved out of this repo into their own engine's repo as kernel plugins
  (`JupyterSharp.Kernels.{PySharp,Ontly,Ralf}` under PySharp/ontly/RalfAI's own `src/`), each
  implementing a new `IKernelPlugin` contract in `JupyterSharp.Kernels.Abstractions`.
  `JupyterSharp.Host` discovers and loads them at runtime (`KernelPluginLoader.cs`) from
  `JUPYTERSHARP_KERNEL_PATHS`/`kernels/*`, via `Assembly.LoadFrom` and its own-folder dependency
  probing — see ARCHITECTURE.md for why a per-plugin `AssemblyLoadContext` was tried first and
  reverted after it broke plugin loading whenever a builtin kernel ran first.
- Added a builtin **`fsharp`** kernel (`FSharp.Compiler.Service`'s `FsiEvaluationSession`) — a real
  FSI session, so cross-cell state persistence needs no workaround the way PySharp's does.
  VB.NET was considered and dropped: Roslyn has no scripting/REPL API for VB shaped like
  `CSharpScript`. FSI's own `val x: T = ...` echo is discarded (`TextWriter.Null`) rather than
  shown — a cell's output is exactly what it printed, plus the last expression's value.
- `vscode-extension`: added `jupytersharp.kernelPaths` (forwarded to the host as
  `JUPYTERSHARP_KERNEL_PATHS`), `fsharp` in `supportedLanguages`; `build/package-extension.ps1` now
  also publishes the three plugins into `vscode-extension/host/kernels/*` for a self-contained
  `.vsix`.
- Fixed the notebook HTML renderer failing with "exports is not defined": it was compiled as
  CommonJS like the rest of the extension, but the renderer webview loads its entrypoint as a real
  ES module — split into its own `tsconfig.renderer.json`/`tsc` pass.

## 0.1.0 — initial version

- `JupyterSharp.Host`: NDJSON stdio loop dispatching to four kernels.
- `csharp` kernel: Roslyn scripting, session state across cells, `Display.Html`/`Display.Text`.
- `pysharp` kernel: `PySharpLib.PyEngine`, session state carried forward by hand across cells,
  `display_html` callback.
- `ontly` kernel: `Ontly.Domain.ContractCompiler` against the C# and Python generators, rendered as
  an HTML report; diagnostics rendered as cell errors.
- `ralf` kernel: an in-process RalfAI `AgentEngine` with a `JupyterSharpNotebookContext` exposing
  `Notebook_ListCells`/`GetCell`/`RunCell`/`SetCellCode` tools.
- VS Code extension: `jupytersharp-notebook` type for `*.ipynb` files (real Jupyter nbformat v4, offered
  as an "option" rather than taking over every `.ipynb`), one multi-language `NotebookController`,
  a `text/html` output renderer, `JupyterSharp: New Notebook` / `JupyterSharp: Restart Kernel Host` commands.
- Preliminary change in the RalfAI repo: packaged `RalfAI.Core` and `RalfAI.Abstractions` to
  `D:\dev\NuGetLocalFeed` (metadata-only — `PackageId`/`Version`/`Description` — no logic changes),
  so JupyterSharp could reference them the same way it references PySharp/Ontly.
