using JupyterSharp.Kernels.Abstractions;

namespace JupyterSharp.Engine;

/// <summary>
/// The reusable engine behind every JupyterSharp host: owns the kernels of one notebook session, dispatches cell execution to them, and
/// is itself the <see cref="INotebookHost"/> a Ralf-style kernel's tools call against. Used by
/// <c>JupyterSharp.Host</c> (wrapping it in the NDJSON protocol), the <c>jupytersharp</c> CLI (wrapping
/// it in a headless run loop), and directly by any embedding application — see
/// <see cref="SetVariableAsync"/> for the embedding entry point.
/// </summary>
public sealed class NotebookSession : INotebookHost
{
    private readonly Dictionary<string, IKernel> _kernels = new();
    private readonly Dictionary<string, IKernelPlugin> _plugins;
    private IReadOnlyList<NotebookCellInfo> _cells = [];

    /// <summary>Raised when a kernel (Ralf's <c>Notebook_SetCellCode</c> tool, in practice) asks to overwrite a cell's source.
    /// JupyterSharp.Host turns this into an <c>editCell</c> NDJSON event for the extension to apply; a headless host
    /// (the CLI, an embedding app) can instead apply it directly to its own in-memory <see cref="NotebookDocument"/>.</summary>
    public event Action<int, string>? CellEditRequested;

    /// <summary>
    /// The session knows no kernel by name: every kernel is an <see cref="IKernelPlugin"/>, discovered in the host's
    /// <c>kernels/</c> folder and the configured kernel paths (see <see cref="KernelPluginLoader"/>), or passed in
    /// <paramref name="plugins"/> by an application that references a kernel package directly. A plugin in
    /// <paramref name="plugins"/> wins over a discovered one with the same id.
    /// </summary>
    /// <param name="pluginWarnings">Where plugin-discovery warnings go (a missing/broken kernel plugin directory) — defaults to discarding them.</param>
    /// <param name="kernelPathsOverride">Explicit plugin search directories, used instead of <c>JUPYTERSHARP_KERNEL_PATHS</c> (the CLI's <c>--kernel-paths</c>); the host's own <c>kernels/</c> folder is searched in any case.</param>
    /// <param name="notebookPath">The notebook's file, if it has one — see <see cref="NotebookPath"/>.</param>
    /// <param name="plugins">Kernels given in code instead of discovered from folders.</param>
    public NotebookSession(TextWriter? pluginWarnings = null, IEnumerable<string>? kernelPathsOverride = null, string? notebookPath = null,
                           IEnumerable<IKernelPlugin>? plugins = null)
    {
        _plugins = new Dictionary<string, IKernelPlugin>(KernelPluginLoader.DiscoverPlugins(pluginWarnings ?? TextWriter.Null, kernelPathsOverride));
        foreach (var plugin in plugins ?? []) _plugins[plugin.KernelId] = plugin;
        NotebookPath = notebookPath;
    }

    /// <summary>The ids of the kernels this session can run, in alphabetical order: what a notebook cell's language can be.</summary>
    public IReadOnlyList<string> KernelIds => _plugins.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The notebook's file path. The <c>csharp</c> kernel compiles its cells as if they lived in this file, so relative
    /// <c>#r</c>/<c>#load</c> resolve against the notebook's folder. Read when that kernel is created (its first cell):
    /// setting it later does not affect an already-created kernel. JupyterSharp.Host fills it from the first
    /// <c>execute</c> request that carries <c>notebookPath</c>.
    /// </summary>
    public string? NotebookPath { get; set; }

    public IReadOnlyList<NotebookCellInfo> Cells => _cells;

    /// <summary>Refreshes the session's notebook-wide cell cache — what Ralf's <c>Notebook_ListCells</c>/<c>GetCell</c>/<c>RunCell</c> tools see.</summary>
    public void UpdateCells(IReadOnlyList<NotebookCellInfo> cells) => _cells = cells;

    /// <summary>
    /// Gets (creating and caching on first use) the kernel for <paramref name="kernelId"/> from its plugin.
    /// Throws if no plugin serves that id; callers executing
    /// a cell should prefer <see cref="ExecuteAsync"/>, which turns that into a normal
    /// <see cref="IKernelOutputSink.WriteError"/>.
    /// </summary>
    public IKernel GetOrCreateKernel(string kernelId)
    {
        if (_kernels.TryGetValue(kernelId, out var existing)) return existing;
        if (!_plugins.TryGetValue(kernelId, out var plugin))
            throw new InvalidOperationException(_plugins.Count == 0
                ? $"Unknown kernel '{kernelId}': no kernel is installed (no kernels/ folder next to the host and no kernel paths)."
                : $"Unknown kernel '{kernelId}'. Installed kernels: {string.Join(", ", KernelIds)}.");
        return _kernels[kernelId] = plugin.CreateKernel(this);
    }

    /// <summary>Runs one cell's code through its kernel. Returns false (after a <see cref="IKernelOutputSink.WriteError"/>) instead of throwing for an unknown kernel id or a kernel exception.</summary>
    public async Task<bool> ExecuteAsync(string kernelId, string code, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        var tracking = new ErrorTrackingSink(sink);
        try
        {
            var kernel = GetOrCreateKernel(kernelId);
            await kernel.ExecuteAsync(code, tracking, cancellationToken);
        }
        catch (Exception ex)
        {
            tracking.WriteError(ex.Message, ex.StackTrace);
        }
        return !tracking.HasError;
    }

    /// <summary>Runs the cell at <paramref name="index"/> from the session's own <see cref="Cells"/> cache.</summary>
    public Task<bool> ExecuteCellAsync(int index, IKernelOutputSink sink, CancellationToken cancellationToken)
    {
        var cell = _cells.FirstOrDefault(c => c.Index == index);
        if (cell is null)
        {
            sink.WriteError($"No cell at index {index}.");
            return Task.FromResult(false);
        }
        return ExecuteAsync(cell.Language, cell.Code, sink, cancellationToken);
    }

    /// <summary>
    /// The embedding entry point: injects a live .NET object into <paramref name="kernelId"/>'s
    /// context under <paramref name="name"/>, so notebook cells can use it directly. Throws
    /// <see cref="NotSupportedException"/> if that kernel doesn't implement
    /// <see cref="IVariableInjectable"/> — injection is an opt-in kernel capability, not every
    /// kernel can support it the same way (PySharp's own kernel has an equivalent native
    /// capability, <c>PyEngine.SetVariable</c>, not yet wired to this interface).
    /// </summary>
    public Task SetVariableAsync(string kernelId, string name, object? value, CancellationToken cancellationToken = default)
    {
        var kernel = GetOrCreateKernel(kernelId);
        if (kernel is not IVariableInjectable injectable)
            throw new NotSupportedException($"Kernel '{kernelId}' ({kernel.GetType().Name}) does not support variable injection.");
        return injectable.SetVariableAsync(name, value, cancellationToken);
    }

    async Task<string> INotebookHost.RunCellAsync(int index, CancellationToken cancellationToken)
    {
        var cell = _cells.FirstOrDefault(c => c.Index == index);
        if (cell is null) return $"No cell at index {index}.";

        var sink = new BufferingSink();
        await ExecuteAsync(cell.Language, cell.Code, sink, cancellationToken);
        return sink.ToString();
    }

    void INotebookHost.RequestCellEdit(int index, string newCode) => CellEditRequested?.Invoke(index, newCode);
}

/// <summary>Wraps a sink to track whether <see cref="IKernelOutputSink.WriteError"/> was ever called, regardless of which concrete sink a caller passed in.</summary>
internal sealed class ErrorTrackingSink(IKernelOutputSink inner) : IKernelOutputSink
{
    public bool HasError { get; private set; }
    public void WriteText(string text) => inner.WriteText(text);
    public void WriteHtml(string html) => inner.WriteHtml(html);
    public void WriteImage(string mimeType, byte[] data) => inner.WriteImage(mimeType, data);
    public void WriteError(string message, string? stackTrace = null)
    {
        HasError = true;
        inner.WriteError(message, stackTrace);
    }
}

/// <summary>Collects a kernel's output as plain text instead of forwarding it live — backs <see cref="NotebookSession"/>'s <see cref="INotebookHost.RunCellAsync"/>, so a Ralf-style kernel's "run another cell" tool gets a string result rather than a stream of events.</summary>
internal sealed class BufferingSink : IKernelOutputSink
{
    private readonly System.Text.StringBuilder _text = new();

    public void WriteText(string text) => _text.AppendLine(text);
    public void WriteHtml(string html) => _text.AppendLine(html);
    public void WriteImage(string mimeType, byte[] data) => _text.AppendLine($"[{mimeType}, {data.Length} bytes]");

    public void WriteError(string message, string? stackTrace = null)
    {
        _text.AppendLine("ERROR: " + message);
        if (stackTrace is not null) _text.AppendLine(stackTrace);
    }

    public override string ToString() => _text.Length == 0 ? "(no output)" : _text.ToString();
}
