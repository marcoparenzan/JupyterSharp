#!/usr/bin/env pwsh
# Packs every JupyterSharp.* library/host into D:\dev\NuGetLocalFeed, the same feed
# PySharp/Ontly/RalfAI already publish to (see NuGet.Config at the repo root).
#
# The three kernel plugins (PySharp/Ontly/Ralf) live in their own repos now — this only packs
# what actually lives here: the protocol, the plugin contract, and the four builtin kernels
# (csharp/fsharp/powershell/powerfx) plus the host itself.
param(
    [string]$Configuration = "Release",
    [string]$OutputFeed = "D:\dev\NuGetLocalFeed"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$projects = @(
    "src/JupyterSharp.Protocol/JupyterSharp.Protocol.csproj",
    "src/JupyterSharp.Kernels.Abstractions/JupyterSharp.Kernels.Abstractions.csproj",
    "src/JupyterSharp.Kernels.CSharp/JupyterSharp.Kernels.CSharp.csproj",
    "src/JupyterSharp.Kernels.FSharp/JupyterSharp.Kernels.FSharp.csproj",
    "src/JupyterSharp.Kernels.PowerShell/JupyterSharp.Kernels.PowerShell.csproj",
    "src/JupyterSharp.Kernels.PowerFx/JupyterSharp.Kernels.PowerFx.csproj",
    "src/JupyterSharp.Engine/JupyterSharp.Engine.csproj",
    "src/JupyterSharp.Host/JupyterSharp.Host.csproj",
    "src/JupyterSharp.Cli/JupyterSharp.Cli.csproj"
)

foreach ($project in $projects) {
    $path = Join-Path $root $project
    Write-Host "==> dotnet pack $project" -ForegroundColor Cyan
    dotnet pack $path -c $Configuration -o $OutputFeed
}
