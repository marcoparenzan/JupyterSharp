using JupyterSharp.Kernels.Abstractions;

namespace JupyterSharp.Kernels.PowerShell;

/// <summary>The <c>powershell</c> kernel as a plugin: PowerShell, one runspace per session. A host loads it from its <c>kernels/powershell</c> folder like any other kernel,
/// or an embedding application passes an instance to <c>NotebookSession</c>.</summary>
public sealed class PowerShellKernelPlugin : IKernelPlugin
{
    public string KernelId => "powershell";

    public IKernel CreateKernel(INotebookHost notebookHost) => new PowerShellKernel();
}
