using JupyterSharp.Kernels.CSharp;

namespace JupyterSharp.Tests;

public class CSharpKernelTests
{
    [Fact]
    public async Task SessionStatePersistsAcrossCells()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("var x = 21;", sink, default);
        await kernel.ExecuteAsync("x * 2", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("42", sink.Texts);
    }

    [Fact]
    public async Task DisplayHtmlWritesHtmlOutput()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("Display.Html(\"<b>hi</b>\");", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("<b>hi</b>", sink.Htmls);
    }

    [Fact]
    public async Task LowercaseDisplayOfHtmlWritesHtmlOutput()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("display(HTML(\"<svg/>\"));", sink, default);

        Assert.False(sink.HasError);
        Assert.Equal(["<svg/>"], sink.Htmls);
        Assert.Empty(sink.Texts);
    }

    [Fact]
    public async Task LowercaseDisplayOfOtherValuesWritesTextAndImages()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("display(42); display(null); display(new byte[] { 1, 2, 3 });", sink, default);

        Assert.False(sink.HasError);
        Assert.Equal(["42"], sink.Texts);
        var image = Assert.Single(sink.Images);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(new byte[] { 1, 2, 3 }, image.Data);
    }

    [Fact]
    public async Task ACellCanStillDefineItsOwnDisplay()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("void display(object o) => Console.WriteLine(\"mine\");", sink, default);
        await kernel.ExecuteAsync("display(1);", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("mine", sink.Texts);
    }

    [Fact]
    public async Task CompileErrorBecomesWriteError()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("this is not valid C#;;;", sink, default);

        Assert.True(sink.HasError);
    }

    [Fact]
    public async Task RuntimeExceptionBecomesWriteError()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("throw new InvalidOperationException(\"boom\");", sink, default);

        Assert.True(sink.HasError);
        Assert.Contains("boom", sink.Errors[0].Message);
    }

    [Fact]
    public async Task InjectedVariableIsUsableInALaterCell()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.SetVariableAsync("weather", new Weather(), default);
        await kernel.ExecuteAsync("weather.City", sink, default);
        await kernel.ExecuteAsync("weather.TempC(3)", sink, default);

        Assert.False(sink.HasError);
        Assert.Contains("Trento", sink.Texts);
        Assert.Contains("23", sink.Texts);
    }

    [Fact]
    public async Task InjectingAnInvalidIdentifierThrows()
    {
        var kernel = new CSharpKernel();
        await Assert.ThrowsAsync<ArgumentException>(() => kernel.SetVariableAsync("not a valid name", 1, default));
    }
}

/// <summary>
/// Must be a top-level `public` type, not a nested/private one: injected objects are accessed via
/// `dynamic` from a separately-compiled script submission assembly, and the DLR binder enforces
/// real accessibility — a private nested class would throw a RuntimeBinderException at the call
/// site, not just fail to compile.
/// </summary>
public sealed class Weather
{
    public string City { get; set; } = "Trento";
    public double TempC(int day) => 20.0 + day;
}
