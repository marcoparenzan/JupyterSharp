using JupyterSharp.Kernels.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace JupyterSharp.Kernels.CSharp;

/// <summary>
/// Runs C# cells with Roslyn scripting. Each cell continues the previous
/// <see cref="ScriptState{T}"/>, so top-level variables declared in one cell are visible in the
/// next — the same "REPL" feel dotnet-interactive's C# kernel has.
/// </summary>
public sealed class CSharpKernel : IKernel, IVariableInjectable
{
    public string Id => "csharp";

    private ScriptState<object?>? _state;
    private readonly ScriptGlobals _globals = new();

    // Microsoft.CSharp.dll (the DLR runtime binder) is required for `dynamic` to compile in a
    // script submission — only ever exercised by SetVariableAsync's hidden "unwrap" interaction.
    private static readonly ScriptOptions BaseOptions = ScriptOptions.Default
        .WithReferences(typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(Display).Assembly,
            typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly)
        .WithImports("System", "System.Linq", "System.Collections.Generic", "JupyterSharp.Kernels.CSharp");

    private ScriptOptions _options;
    private readonly HashSet<string> _referenced = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="notebookPath">
    /// The notebook's file path, when there is one. Every cell is compiled as if it lived in that file
    /// (<see cref="ScriptOptions.WithFilePath"/>, the same thing Screen9's CsxRunner does for a .csx): relative
    /// <c>#r</c>/<c>#load</c> resolve against the notebook's folder — and inside a <c>#load</c>ed file, against that
    /// file's folder — instead of failing; diagnostics name the notebook. Without it, only absolute paths resolve.
    /// </param>
    public CSharpKernel(string? notebookPath = null)
    {
        _options = notebookPath is null ? BaseOptions : BaseOptions.WithFilePath(Path.GetFullPath(notebookPath));
    }

    public async Task ExecuteAsync(string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        // Console.WriteLine inside a cell would otherwise reach the real process stdout — lost from the CLI's saved
        // notebook and, in JupyterSharp.Host, written straight into the NDJSON channel. See ConsoleCapture.
        using var capture = new ConsoleCapture(sink);
        Display.CurrentSink.Value = capture;
        try
        {
            // `#r "nuget: Id, Version"` is not Roslyn syntax: restore the packages, reference them, run the rest of the cell.
            var (cleaned, requests) = NuGetReferences.Extract(code);
            if (requests.Count > 0) await AddPackagesAsync(requests, cancellationToken);

            _state = await RunAsync(CellRewrites.ForScripting(cleaned), cancellationToken);

            if (_state.ReturnValue is not null)
                capture.WriteText(_state.ReturnValue is string s ? s : _state.ReturnValue.ToString() ?? "");
        }
        catch (CompilationErrorException ex)
        {
            capture.WriteError(string.Join(Environment.NewLine, ex.Diagnostics));
        }
        catch (Exception ex)
        {
            capture.WriteError(ex.Message, ex.StackTrace);
        }
        finally
        {
            Display.CurrentSink.Value = null;
        }
    }

    /// <summary>
    /// The embedding entry point (<c>NotebookSession.SetVariableAsync</c>) reaches here. Roslyn
    /// scripting has no runtime-extensible "globals" story — the globals *type* is fixed on the
    /// very first <see cref="CSharpScript.RunAsync"/> call — but a real, script-visible binding is
    /// still achievable: the globals object carries one <see cref="ScriptGlobals.Injected"/>
    /// dictionary (a public member, so script code can reach it by bare name like any other global),
    /// and each newly-injected name gets a one-line hidden interaction —
    /// <c>dynamic weather = Injected["weather"];</c> — run exactly like a real cell would be, so it
    /// becomes an ordinary top-level binding for every cell after it, indistinguishable from
    /// something the notebook itself declared. `dynamic` sidesteps needing the injected object's
    /// exact (possibly non-public) type name at the call site; real member access/calls on it still
    /// resolve via the DLR when a later cell actually runs.
    /// </summary>
    public async Task SetVariableAsync(string name, object? value, CancellationToken cancellationToken)
    {
        if (!SyntaxFacts.IsValidIdentifier(name))
            throw new ArgumentException($"'{name}' is not a valid C# identifier.", nameof(name));

        _globals.Injected[name] = value;
        _state = await RunAsync($"dynamic {name} = Injected[\"{name}\"];", cancellationToken);
    }

    private async Task AddPackagesAsync(IReadOnlyList<PackageRequest> requests, CancellationToken cancellationToken)
    {
        var resolved = await NuGetReferences.ResolveAsync(requests, cancellationToken: cancellationToken);
        var fresh = resolved.Assemblies.Where(_referenced.Add).ToList();
        if (fresh.Count > 0) _options = _options.AddReferences(fresh.Select(p => MetadataReference.CreateFromFile(p)));
        NativeLibraries.Add(resolved.NativeDirectories);
    }

    private Task<ScriptState<object?>> RunAsync(string code, CancellationToken cancellationToken) => _state is null
        ? CSharpScript.RunAsync(code, _options, _globals, cancellationToken: cancellationToken)
        : _state.ContinueWithAsync(code, _options, cancellationToken: cancellationToken);
}

/// <summary>The Roslyn scripting "globals" object for every C# cell in a session — its public members are reachable from script code by bare name.</summary>
public sealed class ScriptGlobals
{
    public Dictionary<string, object?> Injected { get; } = new();

    // Cell helpers with the lowercase names notebooks written for .NET Interactive / Polyglot Notebooks use:
    // `display(HTML(svg))`. They are members of the globals object, so a cell can call them by bare name.

    /// <summary>Wraps markup so that <see cref="display"/> shows it as HTML.</summary>
    public HtmlString HTML(string html) => new(html);

    /// <summary>Shows a value as cell output: <see cref="HtmlString"/> as HTML, a byte array as a PNG image, anything else as text. Null shows nothing.</summary>
    public void display(object? value)
    {
        switch (value)
        {
            case null: break;
            case HtmlString h: Display.Html(h.Value); break;
            case byte[] png: Display.Image(png); break;
            default: Display.Text(value.ToString() ?? ""); break;
        }
    }
}
