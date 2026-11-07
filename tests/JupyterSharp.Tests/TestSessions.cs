using JupyterSharp.Engine;
using JupyterSharp.Kernels.Abstractions;
using JupyterSharp.Kernels.CSharp;
using JupyterSharp.Kernels.FSharp;
using JupyterSharp.Kernels.PowerFx;
using JupyterSharp.Kernels.PowerShell;

namespace JupyterSharp.Tests;

/// <summary>Sessions with the four kernels JupyterSharp ships, given in code: the engine itself knows no kernel.</summary>
internal static class TestSessions
{
    public static IKernelPlugin[] BundledPlugins() => [new CSharpKernelPlugin(), new FSharpKernelPlugin(), new PowerShellKernelPlugin(), new PowerFxKernelPlugin()];

    // An empty kernel-path list keeps JUPYTERSHARP_KERNEL_PATHS on the developer's machine out of the tests.
    public static NotebookSession WithBundledKernels(string? notebookPath = null) =>
        new(kernelPathsOverride: [], notebookPath: notebookPath, plugins: BundledPlugins());
}
