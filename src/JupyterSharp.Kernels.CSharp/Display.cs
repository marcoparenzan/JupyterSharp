namespace JupyterSharp.Kernels.CSharp;

/// <summary>
/// Lets a C# cell push rich output explicitly, e.g. <c>Display.Html("&lt;b&gt;hi&lt;/b&gt;")</c>.
/// Backed by an <see cref="AsyncLocal{T}"/> so it works without threading a sink parameter through
/// user code — <see cref="CSharpKernel"/> sets it for the duration of one cell's execution.
/// </summary>
public static class Display
{
    internal static readonly AsyncLocal<Abstractions.IKernelOutputSink?> CurrentSink = new();

    public static void Html(string html) => CurrentSink.Value?.WriteHtml(html);

    public static void Text(string text) => CurrentSink.Value?.WriteText(text);

    /// <summary>Shows an image, e.g. <c>Display.Image(File.ReadAllBytes("a.png"))</c>.</summary>
    public static void Image(byte[] data, string mimeType = "image/png") => CurrentSink.Value?.WriteImage(mimeType, data);
}

/// <summary>Markup that <c>display</c> sends as HTML output; created with the cell helper <c>HTML("…")</c>.</summary>
public sealed record HtmlString(string Value)
{
    public override string ToString() => Value;
}
