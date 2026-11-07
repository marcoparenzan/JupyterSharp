#!/usr/bin/env pwsh
# Publishes JupyterSharp.Host (with the four kernels it ships, in host/kernels) and the library kernel plugins into vscode-extension/host
# (framework-dependent — the target machine needs the .NET 10 runtime, same as every other tool
# in D:\dev), then builds and packages the VS Code extension as a .vsix. Requires Node.js/npm on
# PATH.
#
# The plugins are bundled here (not just left in JUPYTERSHARP_KERNEL_PATHS) so a packaged .vsix is
# self-contained and doesn't need the PySharp/ontly/RalfAI repos present at runtime; the extension
# still defaults `jupytersharp.kernelPaths` to those repos' own publish output for local dev (see
# kernelController.ts) — the bundled copies under host/kernels/ are what a plain install without
# that setting override falls back to instead once the extension resolves `kernels/*` next to
# JupyterSharp.Host (see KernelPluginLoader's default search path).
param(
    [string]$Configuration = "Release",
    # One platform per package: the native libraries (torch, OpenCV, Skia) are most of the size.
    [ValidateSet("win-x64", "linux-x64", "osx-arm64")]
    [string]$Runtime = "win-x64",
    # Bundle libtorch and OpenCV in the PySharp plugin (a package of 350+ MB on Windows) instead of leaving them to Install-Natives.ps1.
    [switch]$BundleNatives
)

# .NET runtime id -> VS Code platform target (vsce package --target).
$vscodeTarget = @{ "win-x64" = "win32-x64"; "linux-x64" = "linux-x64"; "osx-arm64" = "darwin-arm64" }[$Runtime]

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$extensionDir = Join-Path $root "vscode-extension"
$hostOut = Join-Path $extensionDir "host"
$kernelsOut = Join-Path $hostOut "kernels"

# Start from an empty host folder so one platform's natives never leak into another platform's package.
# Clear the contents rather than the folder itself: a running VS Code can hold the folder open.
if (Test-Path $hostOut) { Get-ChildItem $hostOut -Force | Remove-Item -Recurse -Force }

Write-Host "==> dotnet publish JupyterSharp.Host -> $hostOut" -ForegroundColor Cyan
dotnet publish (Join-Path $root "src/JupyterSharp.Host/JupyterSharp.Host.csproj") -c $Configuration -o $hostOut -r $Runtime --self-contained false

$plugins = @(
    # PySharpExternalNatives leaves libtorch and OpenCV out of the package (they are installed by ScienceSharp\Install-Natives.ps1).
    # Pass -BundleNatives to put them back, for a fully self-contained package.
    @{ Name = "pysharp"; Project = (Join-Path $root "../PySharp/src/PySharp.JupyterSharp.Kernel/PySharp.JupyterSharp.Kernel.csproj"); Properties = @($(if ($BundleNatives) { "-p:PySharpExternalNatives=false" } else { "-p:PySharpExternalNatives=true" })) }
    @{ Name = "matsharp"; Project = (Join-Path $root "../MatSharp/src/MatSharp.JupyterSharp.Kernel/MatSharp.JupyterSharp.Kernel.csproj"); Properties = @() }
    @{ Name = "ndsharp"; Project = (Join-Path $root "../NDSharp/src/NDSharp.JupyterSharp.Kernel/NDSharp.JupyterSharp.Kernel.csproj"); Properties = @() }
    # Ontly and Ralf plugins live in repos outside this suite and are still built against JupyterNet.Kernels.Abstractions,
    # so the host cannot load them yet; add them here once they target JupyterSharp.Kernels.Abstractions.
)
foreach ($plugin in $plugins) {
    $pluginOut = Join-Path $kernelsOut $plugin.Name
    Write-Host "==> dotnet publish $($plugin.Name) plugin -> $pluginOut" -ForegroundColor Cyan
    dotnet publish $plugin.Project -c $Configuration -o $pluginOut -r $Runtime --self-contained false @($plugin.Properties)
}

# The notebook languages are the kernels the packaged host actually loads (the four JupyterSharp ships, published by
# the host's own build into host/kernels, plus the plugins above): ask it, and write one contributes.languages entry per
# kernel id. An existing entry keeps its extra fields (csharp's .cs, fsharp's .fs); an id with no kernel is dropped.
Write-Host "==> JupyterSharp.Host --list-kernels" -ForegroundColor Cyan
$kernelIds = @(dotnet (Join-Path $hostOut "JupyterSharp.Host.dll") --list-kernels | Where-Object { $_ -match '^[a-z0-9-]+$' })
if ($kernelIds.Count -eq 0) { throw "The packaged host lists no kernels." }
Write-Host ("    " + ($kernelIds -join ", "))
$packageJsonPath = Join-Path $extensionDir "package.json"
$package = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
$existing = @{}
foreach ($language in $package.contributes.languages) { $existing[$language.id] = $language }
if ((@($package.contributes.languages.id) -join ",") -ne ($kernelIds -join ",")) {
    # Rewritten only when the kernels changed: ConvertTo-Json reformats the whole file.
    Write-Host "    package.json languages updated" -ForegroundColor Yellow
    $package.contributes.languages = @($kernelIds | ForEach-Object { if ($existing.ContainsKey($_)) { $existing[$_] } else { [pscustomobject]@{ id = $_; aliases = @($_) } } })
    $package | ConvertTo-Json -Depth 32 | Set-Content $packageJsonPath -Encoding utf8
}

Push-Location $extensionDir
try {
    Write-Host "==> npm install" -ForegroundColor Cyan
    npm install

    Write-Host "==> npm run compile" -ForegroundColor Cyan
    npm run compile

    Write-Host "==> vsce package" -ForegroundColor Cyan
    npx --yes @vscode/vsce package --target $vscodeTarget
}
finally {
    Pop-Location
}
