using EmbeddingSample;
using JupyterSharp.Engine;
using JupyterSharp.Kernels.Abstractions;

// The whole "embed a notebook in an application" story in one file: no process, no NDJSON, no VS
// Code — just a NotebookSession, a plain .NET object injected into it, and a few cells that use
// that object directly. The engine knows no kernel: an embedding application picks the ones it wants,
// here csharp and fsharp, by referencing their packages and passing their plugins, so this runs standalone
// with nothing else to publish first (any other kernel — pysharp, ndsharp, matsharp — would come from a
// folder in JUPYTERSHARP_KERNEL_PATHS, same as the CLI/host — see USAGE.md).
var session = new NotebookSession(plugins: [new JupyterSharp.Kernels.CSharp.CSharpKernelPlugin(), new JupyterSharp.Kernels.FSharp.FSharpKernelPlugin()]);
var sink = new ConsolePrintingSink();

Console.WriteLine("--- csharp: injecting a Weather instance ---");
await session.SetVariableAsync("csharp", "weather", new Weather());
await session.ExecuteAsync("csharp", "weather.City", sink, default);
await session.ExecuteAsync("csharp", "weather.TempC(3)", sink, default);
await session.ExecuteAsync("csharp", "foreach (var day in weather.Forecast) Display.Text(day);", sink, default);
await session.ExecuteAsync("csharp", "weather.City = \"Bolzano\"; weather.City", sink, default);

Console.WriteLine();
Console.WriteLine("--- fsharp: the same object, a different kernel ---");
await session.SetVariableAsync("fsharp", "weather", new Weather());
await session.ExecuteAsync("fsharp", "weather.City", sink, default);
await session.ExecuteAsync("fsharp", "weather.TempC(3)", sink, default);
await session.ExecuteAsync("fsharp", "for day in weather.Forecast do printfn \"%s\" day", sink, default);

Console.WriteLine();
Console.WriteLine("--- an unknown/unavailable kernel is a clear error, not a crash ---");
try
{
    // "pysharp" isn't builtin and no JUPYTERSHARP_KERNEL_PATHS plugin provides it here — the same
    // InvalidOperationException a kernel plugin that fails to load would produce (see USAGE.md for
    // running pysharp/ontly/ralf, which need those three repos built). A kernel that *is* available
    // but simply doesn't implement IVariableInjectable throws NotSupportedException instead — both
    // are ordinary exceptions from SetVariableAsync, not something that takes the process down.
    await session.SetVariableAsync("pysharp", "weather", new Weather());
}
catch (Exception ex)
{
    Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
}

/// <summary>Prints straight to the console, unlike JupyterSharp.Host/the CLI's sinks which have somewhere structured to put the output.</summary>
internal sealed class ConsolePrintingSink : IKernelOutputSink
{
    public void WriteText(string text) => Console.WriteLine(text);
    public void WriteHtml(string html) => Console.WriteLine(html);
    public void WriteError(string message, string? stackTrace = null) => Console.Error.WriteLine(message);
}
