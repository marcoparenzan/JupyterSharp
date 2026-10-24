using System.Text;
using JupyterNet.Kernels.Abstractions;

namespace JupyterNet.Kernels.CSharp;

/// <summary>
/// For the duration of exactly one C# cell, points the process-wide <c>Console.Out</c>/<c>Error</c> at a buffer and
/// wraps the cell's sink — the same scoped redirect <c>FSharpKernel</c> does for <c>printfn</c>, plus two things the
/// C# kernel needs because it also has <see cref="Display"/>:
/// <list type="bullet">
/// <item>Ordering: buffered console text is flushed as one text output right before any other output (a
/// <c>Display.Html</c>, the cell's result, an error) and at the end, so console text and rich output appear in
/// the order the cell produced them.</item>
/// <item>No feedback loop: while the wrapped sink is handling an output, console writes pass straight through to
/// the previous writer. The CLI's sink echoes every output to the console; without this the echo would be
/// captured back as cell text (and the host's NDJSON sink never writes to <c>Console.Out</c>, so it is unaffected).</item>
/// </list>
/// Like FSharpKernel this assumes cells run one at a time per process, which every JupyterNet host guarantees.
/// </summary>
internal sealed class ConsoleCapture : IKernelOutputSink, IDisposable
{
    private readonly IKernelOutputSink _inner;
    private readonly TextWriter _previousOut;
    private readonly TextWriter _previousError;
    private readonly StringBuilder _pending = new();
    private readonly object _gate = new();
    private bool _inSink;

    public ConsoleCapture(IKernelOutputSink inner)
    {
        _inner = inner;
        _previousOut = Console.Out;
        _previousError = Console.Error;
        var writer = new CaptureWriter(this);
        Console.SetOut(writer);
        Console.SetError(writer);
    }

    public void WriteText(string text) => Forward(() => _inner.WriteText(text));
    public void WriteHtml(string html) => Forward(() => _inner.WriteHtml(html));
    public void WriteImage(string mimeType, byte[] data) => Forward(() => _inner.WriteImage(mimeType, data));
    public void WriteError(string message, string? stackTrace = null) => Forward(() => _inner.WriteError(message, stackTrace));

    public void Dispose()
    {
        FlushPending();
        Console.SetOut(_previousOut);
        Console.SetError(_previousError);
    }

    private void Forward(Action write)
    {
        FlushPending();
        Passthrough(write);
    }

    private void FlushPending()
    {
        string text;
        lock (_gate)
        {
            if (_pending.Length == 0) return;
            text = _pending.ToString().TrimEnd('\r', '\n');
            _pending.Clear();
        }
        Passthrough(() => _inner.WriteText(text));
    }

    private void Passthrough(Action write)
    {
        var previous = _inSink;
        _inSink = true;
        try { write(); }
        finally { _inSink = previous; }
    }

    private void Append(char value)
    {
        if (_inSink) { _previousOut.Write(value); return; }
        lock (_gate) _pending.Append(value);
    }

    private void Append(string? value)
    {
        if (value is null) return;
        if (_inSink) { _previousOut.Write(value); return; }
        lock (_gate) _pending.Append(value);
    }

    private sealed class CaptureWriter(ConsoleCapture owner) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => owner.Append(value);
        public override void Write(string? value) => owner.Append(value);
    }
}
