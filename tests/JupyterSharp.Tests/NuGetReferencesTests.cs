using JupyterSharp.Kernels.CSharp;

namespace JupyterSharp.Tests;

/// <summary>A fact that is skipped, with the reason shown in the test report, when the machine's local feed is not there.</summary>
public sealed class FactIfLocalFeedAttribute : FactAttribute
{
    public FactIfLocalFeedAttribute()
    {
        if (!Directory.Exists(@"D:\Dev\NuGetLocalFeed")) Skip = @"The local NuGet feed D:\Dev\NuGetLocalFeed does not exist on this machine.";
    }
}

public class NuGetReferencesTests
{
    [Fact]
    public void ExtractReadsIdAndVersionAndBlanksTheLine()
    {
        var (code, requests) = NuGetReferences.Extract("#r \"nuget: Example.Package, 1.2.3\"\nvar x = 1;\n#r \"nuget:PlotSharp\"\nx");
        Assert.Equal([new PackageRequest("Example.Package", "1.2.3"), new PackageRequest("PlotSharp", null)], requests);
        Assert.Equal("\nvar x = 1;\n\nx", code); // line numbers of the remaining code are unchanged
    }

    [Fact]
    public void ExtractAcceptsPrereleaseVersionsAndTrailingComments()
    {
        var (_, requests) = NuGetReferences.Extract("  #r   \"nuget: MatSharp.Interpreter,  0.1.1-net11-preview.5 \"  // the interpreter");
        Assert.Equal([new PackageRequest("MatSharp.Interpreter", "0.1.1-net11-preview.5")], requests);
    }

    [Theory]
    [InlineData("#r \"System.Xml.dll\"")]
    [InlineData("// #r \"nuget: X, 1.0\"")]
    [InlineData("var s = \"#r nuget: X, 1.0\";")]
    [InlineData("#r \"C:\\libs\\a.dll\"")]
    public void ExtractLeavesOtherCodeAlone(string code)
    {
        var (cleaned, requests) = NuGetReferences.Extract(code);
        Assert.Empty(requests);
        Assert.Equal(code, cleaned);
    }

    [Fact]
    public void BuildProjectListsEveryPackageAndFloatsAMissingVersion()
    {
        var project = NuGetReferences.BuildProject([new("A.B", "1.2.3"), new("C", null)]);
        Assert.Contains("<PackageReference Include=\"A.B\" Version=\"1.2.3\" />", project);
        Assert.Contains("<PackageReference Include=\"C\" Version=\"*-*\" />", project);
        Assert.Contains("<TargetFramework>net11.0</TargetFramework>", project);
    }

    [Fact]
    public void ReadAssetsPrefersRuntimeAssembliesAndFindsNativeFoldersOfTheCurrentRuntime()
    {
        var root = Directory.CreateTempSubdirectory("assets-").FullName;
        try
        {
            void Touch(string rel) { var p = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, ""); }
            Touch("pkg.a/1.0.0/lib/net11.0/A.dll");
            Touch("pkg.a/1.0.0/ref/net11.0/A.dll");
            Touch("pkg.b/2.0.0/ref/net11.0/B.dll");
            Touch("pkg.b/2.0.0/runtimes/win-x64/native/b_native.dll");
            Touch("pkg.b/2.0.0/runtimes/linux-x64/native/libb_native.so");
            var json = $$"""
            {
              "version": 3,
              "targets": { "net11.0": {
                "Pkg.A/1.0.0": { "type": "package", "compile": { "ref/net11.0/A.dll": {} }, "runtime": { "lib/net11.0/A.dll": {} } },
                "Pkg.B/2.0.0": { "type": "package", "compile": { "ref/net11.0/B.dll": {} },
                  "runtimeTargets": {
                    "runtimes/win-x64/native/b_native.dll": { "assetType": "native", "rid": "win-x64" },
                    "runtimes/linux-x64/native/libb_native.so": { "assetType": "native", "rid": "linux-x64" } } },
                "Some.Project/1.0.0": { "type": "project", "compile": { "bin/placeholder/Some.Project.dll": {} } }
              } },
              "libraries": { "Pkg.A/1.0.0": { "type": "package", "path": "pkg.a/1.0.0" }, "Pkg.B/2.0.0": { "type": "package", "path": "pkg.b/2.0.0" } },
              "packageFolders": { {{System.Text.Json.JsonSerializer.Serialize(root)}}: {} }
            }
            """;

            var win = NuGetReferences.ReadAssets(json, "win-x64");
            var sep = Path.DirectorySeparatorChar;
            Assert.Equal(["A.dll", "B.dll"], win.Assemblies.Select(p => Path.GetFileName(p)!).Order().ToArray());
            Assert.Contains($"lib{sep}net11.0", win.Assemblies.First(a => a.EndsWith("A.dll")));   // runtime beats reference-only
            Assert.Contains($"ref{sep}net11.0", win.Assemblies.First(a => a.EndsWith("B.dll")));   // no runtime asset: reference is used
            Assert.Equal([Path.GetFullPath(Path.Combine(root, "pkg.b", "2.0.0", "runtimes", "win-x64", "native"))], win.NativeDirectories);

            var linux = NuGetReferences.ReadAssets(json, "linux-x64");
            Assert.Equal([Path.GetFullPath(Path.Combine(root, "pkg.b", "2.0.0", "runtimes", "linux-x64", "native"))], linux.NativeDirectories);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ARestoreThatFailsExplainsWhichPackage()
    {
        var work = Directory.CreateTempSubdirectory("nuget-fail-").FullName;
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                NuGetReferences.ResolveAsync([new("Definitely.Not.A.Real.Package.Xyz", "9.9.9")], work));
            Assert.Contains("Definitely.Not.A.Real.Package.Xyz", ex.Message);
        }
        finally { Directory.Delete(work, true); }
    }

    [FactIfLocalFeed]
    public async Task ANugetDirectiveInACellReferencesTheRestoredPackage()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("#r \"nuget: NDSharp.Frame\"\ntypeof(NDSharp.Frame.DataFrame).Name", sink, default);

        Assert.False(sink.HasError, string.Join("\n", sink.Errors.Select(e => e.Message)));
        Assert.Contains("DataFrame", sink.Texts);
    }

    [FactIfLocalFeed]
    public async Task APackageStaysAvailableInLaterCells()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("#r \"nuget: NDSharp.Frame\"", sink, default);
        await kernel.ExecuteAsync("typeof(NDSharp.Frame.Series).Name", sink, default);

        Assert.False(sink.HasError, string.Join("\n", sink.Errors.Select(e => e.Message)));
        Assert.Contains("Series", sink.Texts);
    }
}

public class CellRewritesTests
{
    [Theory]
    [InlineData("using var r = new StringReader(\"x\");", "var r = new StringReader(\"x\");")]
    [InlineData("a;\nusing var r = F();\nb;", "a;\nvar r = F();\nb;")]
    [InlineData("using   var\tr = F();", "var r = F();")]
    public void TopLevelUsingVarBecomesVar(string code, string expected) => Assert.Equal(expected, CellRewrites.ForScripting(code));

    [Theory]
    [InlineData("using (var r = F()) { }")]
    [InlineData("void M() {\n    using var r = F();\n}")]
    [InlineData("using System.IO;")]
    [InlineData("var s = \"using var x\";")]
    public void OtherCodeIsLeftAlone(string code) => Assert.Equal(code, CellRewrites.ForScripting(code));

    [Fact]
    public async Task ACellWithATopLevelUsingVarRuns()
    {
        var kernel = new CSharpKernel();
        var sink = new TestSink();

        await kernel.ExecuteAsync("using var r = new System.IO.StringReader(\"hello\");\nr.ReadLine()", sink, default);

        Assert.False(sink.HasError, string.Join("\n", sink.Errors.Select(e => e.Message)));
        Assert.Contains("hello", sink.Texts);
    }
}
