# Usage

## Installation

JupyterSharp is a **global tool** (`jupytersharp`) and a **VS Code extension**. Requires the **.NET 11 SDK**.

```powershell
# the tool, from the suite installer or by hand
D:\Dev\Science\ScienceSharp\Install-Suite.ps1 -Tools
dotnet tool install --global JupyterSharp.Cli --add-source D:\Dev\NuGetLocalFeed --prerelease
jupytersharp run notebook.ipynb --output executed.ipynb

# the extension (per-platform package: win32-x64, linux-x64, darwin-arm64)
code --install-extension JupyterSharp\vscode-extension\jupytersharp-notebook-win32-x64-0.2.3.vsix --force
```

Build the extension packages with `build/package-extension.ps1 -Runtime win-x64|linux-x64|osx-arm64` (43-46 MB each). The PySharp plugin in them has no libtorch or OpenCV: install those with `ScienceSharp\Install-Natives.ps1`, or build with `-BundleNatives` for a self-contained package of about 350 MB on Windows. The PySharp kernel plugin is
bundled; the Ontly and Ralf plugins are not (they still use the old JupyterNet package). Only Windows x64 has been run.
Everything below is the detailed setup: prerequisites, building from source, the host protocol, embedding.

## Prerequisites

- .NET SDK 11.
- Node.js + npm, to build the VS Code extension.
- `D:\Dev\NuGetLocalFeed` reachable (it is a source in the machine's NuGet.Config) and containing
  `JupyterSharp.Kernels.Abstractions` (pack it from this repo with `build/pack.ps1`, or pass `-p:Version=…` to `dotnet pack`)
  and `PySharp.Interpreter`.
- The **PySharp kernel plugin** built once. It lives in the PySharp repo (`PySharp/src/PySharp.JupyterSharp.Kernel`):

  ```powershell
  dotnet publish D:\Dev\Science\PySharp\src\PySharp.JupyterSharp.Kernel -c Release -o D:\Dev\Science\PySharp\src\PySharp.JupyterSharp.Kernel\bin\publish -r win-x64 --self-contained false
  ```

  Or build it for the gate with `dotnet run --file D:\Dev\Science\MathemaSharp\ops\BuildPySharpKernel.cs`. The packaged extension
  bundles it, so `jupytersharp.kernelPaths` is empty by default. `pysharp` cells fail with "Unknown kernel" until the plugin is found.
- **Ontly and Ralf plugins are not supported at the moment.** They live in other repos and are still built against the old
  `JupyterNet.Kernels.Abstractions` package, so this host cannot load them (suite open point S4). The notes below that mention
  `ontly` and `ralf` describe the design, not what runs today.
- For `ralf` cells, once the plugin is migrated: an existing RalfAI configuration — either `RALFAI_CONFIG_PATH`
  pointing at a working `config.json`, or an `appsettings.json` next to `JupyterSharp.Host.dll` with the same
  `AIProvider`/`OpenAI`|`Anthropic`|`AzureAIFoundry` shape.

## Build, test, and smoke-test the host

```powershell
dotnet build JupyterSharp.slnx
dotnet test tests/JupyterSharp.Tests
```

`JupyterSharp.Host` reads NDJSON requests on stdin and writes NDJSON events on stdout — see
[docs/protocol.md](docs/protocol.md). To try it directly without VS Code (`csharp`/`fsharp`/`powershell`/`powerfx` work
with no further setup; `pysharp`/`ontly`/`ralf` need the plugin publish step above and
`JUPYTERSHARP_KERNEL_PATHS` pointed at those three `bin/publish` folders, `;`-separated):

```powershell
'{"id":"1","method":"execute","params":{"kernel":"csharp","code":"21 * 2"}}' | dotnet run --project src/JupyterSharp.Host
```

## Run a notebook headlessly (CLI)

No VS Code, no editor — `jupytersharp run` executes a `.ipynb`'s cells top to bottom and exits `0`
only if every one of them ran without error (handy in CI, or just from a terminal). Install it once
as a real command (same pattern as `pysharp`/`ontly`/`ralf`), or use `dotnet run` straight from the
repo while developing:

```powershell
# once, from the repo root:
./build/pack.ps1
dotnet tool install --global --add-source D:\dev\NuGetLocalFeed JupyterSharp.Cli
# after changing the CLI/Engine/a builtin kernel, re-pack then (see README.md's Publishing
# section for why `dotnet tool update` alone isn't enough unless <Version> was bumped):
dotnet tool uninstall --global JupyterSharp.Cli
dotnet tool install --global --add-source D:\dev\NuGetLocalFeed JupyterSharp.Cli

jupytersharp run samples/tour.ipynb
# or, without installing: dotnet run --project src/JupyterSharp.Cli -- run samples/tour.ipynb

# pysharp/ontly/ralf need the same plugin paths as the host/extension — either pass them every time:
jupytersharp run samples/tour.ipynb --kernel-paths "D:\dev\2026\repos\PySharp\src\PySharp.JupyterSharp.Kernel\bin\publish;D:\dev\2026\repos\ontly\src\JupyterSharp.Kernels.Ontly\bin\publish;D:\dev\MarcoParenzan\RalfAI\src\JupyterSharp.Kernels.Ralf\bin\publish"

# --output writes the executed notebook (with real outputs) elsewhere; --in-place overwrites the input
jupytersharp run samples/tour.ipynb --output executed.ipynb
```

`--fail-fast` stops at the first cell whose output is an error instead of running the rest.

**To avoid typing `--kernel-paths` every time**, set it once as a persistent user environment
variable instead — every `jupytersharp`/`JupyterSharp.Host` invocation falls back to it automatically:

```powershell
[Environment]::SetEnvironmentVariable('JUPYTERSHARP_KERNEL_PATHS',
  'D:\dev\2026\repos\PySharp\src\PySharp.JupyterSharp.Kernel\bin\publish;D:\dev\2026\repos\ontly\src\JupyterSharp.Kernels.Ontly\bin\publish;D:\dev\MarcoParenzan\RalfAI\src\JupyterSharp.Kernels.Ralf\bin\publish',
  'User')
```

Windows only applies this to *new* processes — it won't reach a terminal that was already open
when you set it (a real quirk hit while writing this: setting it and testing in the same shell
still showed "Unknown kernel"). Open a new terminal (or restart VS Code, for the extension) before
expecting it to take effect. Already set this way on this machine.
Without `--kernel-paths`, it falls back to `JUPYTERSHARP_KERNEL_PATHS`/the same `kernels/` default
directory the host uses.

## Embed a notebook in a .NET application

[samples/EmbeddingSample](samples/EmbeddingSample) is the whole story in one file: reference
`JupyterSharp.Engine`, create a `NotebookSession`, inject a live object into a kernel's context, run
some code that uses it.

```csharp
using JupyterSharp.Engine;

var session = new NotebookSession();
await session.SetVariableAsync("csharp", "weather", new Weather());   // a plain .NET object
await session.ExecuteAsync("csharp", "weather.City", sink, default);  // -> "Trento"
```

```powershell
dotnet run --project samples/EmbeddingSample
```

`SetVariableAsync` only works on a kernel that implements `IVariableInjectable` — today that's
`csharp`, `fsharp`, `powershell` and `powerfx` (the four builtin kernels); a kernel that doesn't throws `NotSupportedException`
rather than silently doing nothing. See ARCHITECTURE.md's "Engine, CLI and embedding" section for
how each of the four actually makes an injected object usable from cell code (PowerFx's own version
only exposes an injected object's properties, not its methods).

## Install the VS Code extension

```powershell
cd vscode-extension
npm install
npm run compile
```

Then, in VS Code, open the `vscode-extension` folder and press F5 (`launch.json` is already set
up — it runs `npm: compile` first and opens `samples/tour.ipynb` in the new window). In that
window:

- `.ipynb` is registered with `priority: "option"` (see [ARCHITECTURE.md](ARCHITECTURE.md)) —
  JupyterSharp is offered as a choice, not forced as the default, so if another extension (e.g. Jupyter)
  also handles `.ipynb` you may need **right-click → Open With... → JupyterSharp Notebook** the first
  time, or set `"workbench.editorAssociations": { "*.ipynb": "jupytersharp-notebook" }` to make it the
  default for every `.ipynb` in that workspace.
- Run **JupyterSharp: New Notebook**, or open `samples/tour.ipynb` from the repo root.
- Each cell picks its language from VS Code's usual cell-language picker: `csharp`, `fsharp`,
  `powershell`, `powerfx`, `pysharp`, `ontly` or `ralf`.
- Run a cell with the usual ▷ button/`Ctrl+Enter`. The first cell of a session starts a
  `JupyterSharp.Host` process for that notebook (visible, if needed, in the **JupyterSharp** output channel —
  stderr from the host lands there, including a warning per kernel plugin that failed to load);
  it stays alive, keeping every kernel's state, until the notebook is closed or you run
  **JupyterSharp: Restart Kernel Host**.

The extension looks for the host in this order: the `jupytersharp.hostDll` setting if you set one; else
`<extension folder>/host/JupyterSharp.Host.dll` (produced by `build/package-extension.ps1`, see below);
else it falls back automatically to `src/JupyterSharp.Host/bin/{Debug,Release}/net11.0/JupyterSharp.Host.dll`
in the repo, so a plain `dotnet build` is enough while developing — no setting to configure.

`jupytersharp.kernelPaths` (an array setting) is where the `pysharp`/`ontly`/`ralf` plugins are found —
forwarded to the host as `JUPYTERSHARP_KERNEL_PATHS`. It defaults to this machine's three plugin
repos' own `bin/publish` output (see Prerequisites); override it if those repos live elsewhere, or
point it at `<extension folder>/host/kernels/*` if you installed a packaged `.vsix` that bundled
them (see Packaging).

## Cell examples

```csharp
var greeting = "hello";
Display.Html($"<b>{greeting}</b>");
greeting.Length
```

Notebooks written for .NET Interactive / Polyglot Notebooks also run: the C# kernel provides
`display(value)` and `HTML(string)`, so `display(HTML("<b>hi</b>"))` shows HTML, `display(bytes)`
shows a PNG, and `display(x)` shows `x.ToString()`. Null shows nothing. A cell can still define its
own `display`, which then takes precedence.

**NuGet packages.** `#r "nuget: Id, Version"` works in C# cells, as in Polyglot Notebooks (a `#r "nuget: Id"` without a version takes the newest, prereleases
included). The directive lines are removed from the cell (replaced by empty lines, so error line numbers stay right), the packages are restored with
`dotnet restore` into a per-request folder under `%TEMP%\jupytersharp-nuget` (set `JUPYTERSHARP_NUGET_WORK` to move it), using the machine's NuGet.Config
sources, which include the local feed `D:\Dev\NuGetLocalFeed`. The packages' assemblies are then referenced for this cell and every later one, and native
libraries (SkiaSharp, OpenCV, libtorch) are found in the restored packages. A restore that fails shows the `dotnet restore` output in the cell.
The first cell that asks for a package pays for the restore; the same request is cached.

**`using var` at the top level.** Roslyn scripting rejects `using var x = ...;` in a cell, so a declaration that starts in column 0 is rewritten to
`var x = ...;`. The object is therefore **not disposed** when the cell ends, only when the session ends. Indented `using var` (inside a method or block) and
the block form `using (...) { }` are unchanged and keep their disposal.

```fsharp
let greeting = "hello from F#"
printfn "%s" greeting
```

```powershell
$greeting = "hello from PowerShell"
Write-Host $greeting
```

```powerfx
Set(greeting, "hello from PowerFx")
```

```pysharp
message = "hello from PySharp"
print(message)
display_html(f"<i>{message}</i>")
```

```ontly
context: tour
namespace: JupyterSharp.Tour
scalars:
  Email:
    base: string
    maxLength: 200
  Name:
    base: string
    maxLength: 100
types:
  Person:
    kind: dto
    attributes:
      name: Name
      email: Email
```

```ralf
List the cells in this notebook, then run the first PySharp cell and tell me what it printed.
```

## Packaging

```powershell
./build/pack.ps1               # dotnet pack this repo's own projects (incl. Engine, Cli) -> D:\dev\NuGetLocalFeed
./build/package-extension.ps1  # publish JupyterSharp.Host + the 3 plugins into vscode-extension/host + build a .vsix
```

`package-extension.ps1` bundles the three plugins into `vscode-extension/host/kernels/<name>/` so
a packaged `.vsix` works standalone, without the PySharp/ontly/RalfAI repos present — that bundled
path is exactly `KernelPluginLoader`'s fallback (`<host base dir>/kernels/*`), used automatically
whenever `jupytersharp.kernelPaths` isn't set to something else.
