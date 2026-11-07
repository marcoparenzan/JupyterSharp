using JupyterSharp.Kernels.Abstractions;

namespace JupyterSharp.Kernels.CSharp;

/// <summary>The <c>csharp</c> kernel as a plugin: C#, compiled with Roslyn scripting; relative <c>#r</c>/<c>#load</c> resolve against the notebook's folder. A host loads it from its <c>kernels/csharp</c> folder like any other kernel,
/// or an embedding application passes an instance to <c>NotebookSession</c>.</summary>
public sealed class CSharpKernelPlugin : IKernelPlugin
{
    public string KernelId => "csharp";

    public IKernel CreateKernel(INotebookHost notebookHost) => new CSharpKernel(notebookHost.NotebookPath);
}
