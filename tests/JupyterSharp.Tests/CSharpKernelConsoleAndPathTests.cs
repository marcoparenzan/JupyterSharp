using JupyterSharp.Engine;
using JupyterSharp.Kernels.Abstractions;
using JupyterSharp.Kernels.CSharp;

namespace JupyterSharp.Tests;

public class CSharpKernelConsoleAndPathTests
{
    [Fact]
    public async Task ConsoleWriteLineBecomesOneTextOutput()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("Console.WriteLine(\"one\"); Console.WriteLine(\"two\");", sink, default);

        Assert.False(sink.HasError);
        Assert.Equal(["one" + Environment.NewLine + "two"], sink.Texts);
    }

    [Fact]
    public async Task ConsoleTextKeepsItsOrderWithRichOutputAndTheResult()
    {
        var kernel = new CSharpKernel();
        var sink = new OrderedSink();

        await kernel.ExecuteAsync("Console.WriteLine(\"before\"); Display.Html(\"<b>x</b>\"); Console.WriteLine(\"after\"); 42", sink, default);

        Assert.Equal(["text:before", "html:<b>x</b>", "text:after", "text:42"], sink.Events);
    }

    [Fact]
    public async Task ConsoleErrorIsCapturedToo()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("Console.Error.WriteLine(\"warn\");", sink, default);

        Assert.Equal(["warn"], sink.Texts);
    }

    [Fact]
    public async Task ConsoleIsRestoredAfterTheCellEvenOnError()
    {
        var kernel = new CSharpKernel();
        var before = Console.Out;

        await kernel.ExecuteAsync("Console.WriteLine(\"x\"); throw new Exception(\"boom\");", new TestSink(), default);

        Assert.Same(before, Console.Out);
    }

    [Fact]
    public async Task ASinkThatEchoesToTheConsoleIsNotCapturedBack()
    {
        // The CLI's sink prints every output to the console while handling it: that echo must reach the real console,
        // not become a second, duplicated text output of the cell.
        var kernel = new CSharpKernel();
        var sink = new EchoingSink();

        await kernel.ExecuteAsync("Display.Html(\"<i>chart</i>\"); Console.WriteLine(\"line\");", sink, default);

        Assert.Equal(["html:<i>chart</i>", "text:line"], sink.Events);
    }

    [Fact]
    public async Task RelativeLoadAndReferenceResolveAgainstTheNotebookFolder()
    {
        var folder = Directory.CreateTempSubdirectory("jupytersharp-paths-");
        try
        {
            var shared = Directory.CreateDirectory(Path.Combine(folder.FullName, "shared"));
            // Inside a #load'ed file, relative paths resolve against that file's own folder.
            File.WriteAllText(Path.Combine(shared.FullName, "helper.csx"), "#load \"inner.csx\"\nvar fromHelper = fromInner + 1;");
            File.WriteAllText(Path.Combine(shared.FullName, "inner.csx"), "var fromInner = 40;");
            File.Copy(typeof(Weather).Assembly.Location, Path.Combine(shared.FullName, "Lib.dll"));
            var kernel = new CSharpKernel(Path.Combine(folder.FullName, "notebook.ipynb"));
            var sink = new TestSink();

            await kernel.ExecuteAsync("#load \"shared/helper.csx\"\n#r \"shared/Lib.dll\"\nfromHelper + 1", sink, default);
            await kernel.ExecuteAsync("new JupyterSharp.Tests.Weather().City", sink, default);

            Assert.False(sink.HasError, string.Join(" | ", sink.Errors.Select(e => e.Message)));
            Assert.Equal(["42", "Trento"], sink.Texts);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WithoutANotebookPathRelativeLoadFails()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("#load \"definitely-missing-folder/helper.csx\"", sink, default);

        Assert.True(sink.HasError);
    }

    [Fact]
    public async Task SessionPassesItsNotebookPathToTheCSharpKernel()
    {
        var folder = Directory.CreateTempSubdirectory("jupytersharp-session-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "helper.csx"), "var answer = 42;");
            var session = new NotebookSession(notebookPath: Path.Combine(folder.FullName, "notebook.ipynb"));
            var sink = new TestSink();

            Assert.True(await session.ExecuteAsync("csharp", "#load \"helper.csx\"\nanswer", sink, default));
            Assert.Equal(["42"], sink.Texts);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private sealed class OrderedSink : IKernelOutputSink
    {
        public List<string> Events { get; } = [];
        public void WriteText(string text) => Events.Add("text:" + text);
        public void WriteHtml(string html) => Events.Add("html:" + html);
        public void WriteError(string message, string? stackTrace = null) => Events.Add("error:" + message);
    }

    private sealed class EchoingSink : IKernelOutputSink
    {
        public List<string> Events { get; } = [];
        public void WriteText(string text) { Console.WriteLine(text); Events.Add("text:" + text); }
        public void WriteHtml(string html) { Console.WriteLine(html); Events.Add("html:" + html); }
        public void WriteError(string message, string? stackTrace = null) { Console.Error.WriteLine(message); Events.Add("error:" + message); }
    }
}
