using JupyterSharp.Kernels.Abstractions;

namespace JupyterSharp.Kernels.FSharp;

/// <summary>The <c>fsharp</c> kernel as a plugin: F#, a long-lived FSI session (FSharp.Compiler.Service). A host loads it from its <c>kernels/fsharp</c> folder like any other kernel,
/// or an embedding application passes an instance to <c>NotebookSession</c>.</summary>
public sealed class FSharpKernelPlugin : IKernelPlugin
{
    public string KernelId => "fsharp";

    public IKernel CreateKernel(INotebookHost notebookHost) => new FSharpKernel();
}
