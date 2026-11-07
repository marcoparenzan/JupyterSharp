using JupyterSharp.Engine;
using JupyterSharp.Kernels.Abstractions;

namespace JupyterSharp.Tests;

/// <summary>Every kernel is a plugin: the engine references none of them and knows no kernel id.</summary>
public class KernelPluginTests
{
    [Fact]
    public void TheEngineReferencesNoKernelAssembly()
    {
        var referenced = typeof(NotebookSession).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.Contains("JupyterSharp.Kernels.Abstractions", referenced);
        Assert.DoesNotContain(referenced, name => name.StartsWith("JupyterSharp.Kernels.", StringComparison.Ordinal) && name != "JupyterSharp.Kernels.Abstractions");
    }

    [Fact]
    public void EachBundledPluginCreatesAKernelWithItsOwnId()
    {
        var session = TestSessions.WithBundledKernels();
        foreach (var plugin in TestSessions.BundledPlugins())
        {
            var kernel = plugin.CreateKernel(session);
            Assert.Equal(plugin.KernelId, kernel.Id);
            (kernel as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ASessionListsExactlyTheKernelsItWasGiven()
    {
        Assert.Equal(["csharp", "fsharp", "powerfx", "powershell"], TestSessions.WithBundledKernels().KernelIds);
        Assert.Empty(new NotebookSession(kernelPathsOverride: []).KernelIds);
    }

    [Fact]
    public async Task WithoutKernelsEvenCSharpIsUnknown()
    {
        var session = new NotebookSession(kernelPathsOverride: []);
        var sink = new TestSink();
        Assert.False(await session.ExecuteAsync("csharp", "1 + 1", sink, default));
        Assert.Contains("no kernel is installed", sink.Errors.Single().Message);
    }

    [Fact]
    public async Task AnUnknownKernelErrorNamesTheInstalledOnes()
    {
        var sink = new TestSink();
        Assert.False(await TestSessions.WithBundledKernels().ExecuteAsync("cobol", "x", sink, default));
        Assert.Contains("Installed kernels: csharp, fsharp, powerfx, powershell", sink.Errors.Single().Message);
    }

    [Fact]
    public async Task TheCSharpPluginTakesTheNotebookPathFromTheHost()
    {
        var folder = Directory.CreateTempSubdirectory("jupytersharp-plugin-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "helper.csx"), "var fromHelper = 41;");
            var session = TestSessions.WithBundledKernels(notebookPath: Path.Combine(folder.FullName, "notebook.ipynb"));
            var sink = new TestSink();
            Assert.True(await session.ExecuteAsync("csharp", "#load \"helper.csx\"\nfromHelper + 1", sink, default));
            Assert.Contains("42", sink.Texts);
        }
        finally { folder.Delete(recursive: true); }
    }
}
