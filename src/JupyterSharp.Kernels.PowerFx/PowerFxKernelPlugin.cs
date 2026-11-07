using JupyterSharp.Kernels.Abstractions;

namespace JupyterSharp.Kernels.PowerFx;

/// <summary>The <c>powerfx</c> kernel as a plugin: Power Fx formulas. A host loads it from its <c>kernels/powerfx</c> folder like any other kernel,
/// or an embedding application passes an instance to <c>NotebookSession</c>.</summary>
public sealed class PowerFxKernelPlugin : IKernelPlugin
{
    public string KernelId => "powerfx";

    public IKernel CreateKernel(INotebookHost notebookHost) => new PowerFxKernel();
}
