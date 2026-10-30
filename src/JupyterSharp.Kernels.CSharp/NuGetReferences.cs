using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JupyterSharp.Kernels.CSharp;

/// <summary>A NuGet package asked for with <c>#r "nuget: Id, Version"</c>. A null <see cref="Version"/> means the newest, prereleases included.</summary>
public sealed record PackageRequest(string Id, string? Version);

/// <summary>The files a restored set of packages contributes: assemblies to reference and load, and folders that hold native libraries.</summary>
public sealed record ResolvedPackages(IReadOnlyList<string> Assemblies, IReadOnlyList<string> NativeDirectories);

/// <summary>
/// Support for the Polyglot-Notebooks style directive <c>#r "nuget: Id, Version"</c>, which Roslyn scripting does not understand.
/// The directive lines are cut out of the cell (replaced by blank lines, so error line numbers stay right), the packages are restored
/// with <c>dotnet restore</c> into a per-request folder (the machine's NuGet.Config decides the sources), and the resulting
/// assemblies are added to the script options.
/// </summary>
public static class NuGetReferences
{
    private static readonly Regex Directive = new(
        @"^[ \t]*#r[ \t]+""nuget:[ \t]*(?<id>[A-Za-z0-9_.\-]+)[ \t]*(?:,[ \t]*(?<version>[^""]+?))?[ \t]*""[ \t]*(?://.*)?$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Removes the nuget directives from <paramref name="code"/> and returns them. Every removed line is kept as an empty line.</summary>
    public static (string Code, IReadOnlyList<PackageRequest> Requests) Extract(string code)
    {
        var requests = new List<PackageRequest>();
        var cleaned = Directive.Replace(code, m =>
        {
            var version = m.Groups["version"].Success ? m.Groups["version"].Value.Trim() : null;
            if (version is { Length: 0 }) version = null;
            requests.Add(new PackageRequest(m.Groups["id"].Value, version));
            return "";
        });
        return (cleaned, requests);
    }

    /// <summary>The project that restores the packages. Floating <c>*-*</c> asks for the newest version including prereleases.</summary>
    public static string BuildProject(IEnumerable<PackageRequest> requests)
    {
        var items = string.Join("\n    ", requests.Select(r =>
            $"<PackageReference Include=\"{System.Security.SecurityElement.Escape(r.Id)}\" Version=\"{System.Security.SecurityElement.Escape(r.Version ?? "*-*")}\" />"));
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <DisableImplicitFrameworkReferences>false</DisableImplicitFrameworkReferences>
              </PropertyGroup>
              <ItemGroup>
                {items}
              </ItemGroup>
            </Project>
            """;
    }

    /// <summary>
    /// Reads <c>obj/project.assets.json</c>. Runtime assemblies are preferred over reference-only ones, so a package is referenced once;
    /// native libraries of the current runtime identifier add their folder to <see cref="ResolvedPackages.NativeDirectories"/>.
    /// </summary>
    public static ResolvedPackages ReadAssets(string assetsJson, string runtimeIdentifier)
    {
        using var doc = JsonDocument.Parse(assetsJson);
        var root = doc.RootElement;
        var folders = root.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name).ToList();
        var libraries = root.GetProperty("libraries");
        var assemblies = new List<string>();
        var natives = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? Locate(string library, string relative)
        {
            if (!libraries.TryGetProperty(library, out var lib) || !lib.TryGetProperty("path", out var p)) return null;
            foreach (var folder in folders)
            {
                var full = Path.GetFullPath(Path.Combine(folder, p.GetString()!, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(full)) return full;
            }
            return null;
        }

        foreach (var target in root.GetProperty("targets").EnumerateObject())
        {
            foreach (var package in target.Value.EnumerateObject())
            {
                if (package.Value.TryGetProperty("type", out var type) && type.GetString() != "package") continue;
                var set = package.Value.TryGetProperty("runtime", out var rt) ? rt
                        : package.Value.TryGetProperty("compile", out var cp) ? cp : default;
                if (set.ValueKind == JsonValueKind.Object)
                    foreach (var asset in set.EnumerateObject())
                    {
                        if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || asset.Name.EndsWith("_._", StringComparison.Ordinal)) continue;
                        var path = Locate(package.Name, asset.Name);
                        if (path is not null && seenNames.Add(Path.GetFileName(path))) assemblies.Add(path);
                    }
                if (package.Value.TryGetProperty("runtimeTargets", out var targets))
                    foreach (var asset in targets.EnumerateObject())
                    {
                        if (!asset.Value.TryGetProperty("rid", out var rid) || rid.GetString() != runtimeIdentifier) continue;
                        if (!asset.Value.TryGetProperty("assetType", out var kind) || kind.GetString() != "native") continue;
                        var path = Locate(package.Name, asset.Name);
                        if (path is not null) natives.Add(Path.GetDirectoryName(path)!);
                    }
            }
        }
        return new ResolvedPackages(assemblies, natives.ToList());
    }

    /// <summary>The runtime identifier native libraries are looked up for (win-x64, linux-x64, osx-arm64, ...).</summary>
    public static string CurrentRuntimeIdentifier { get; } =
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" +
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    /// <summary>Restores the requested packages and returns the files to reference. Throws <see cref="InvalidOperationException"/> with the restore output on failure.</summary>
    public static async Task<ResolvedPackages> ResolveAsync(IReadOnlyList<PackageRequest> requests, string? workRoot = null, CancellationToken cancellationToken = default)
    {
        var project = BuildProject(requests);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project)))[..16].ToLowerInvariant();
        var folder = Path.Combine(workRoot ?? DefaultWorkRoot(), key);
        Directory.CreateDirectory(folder);
        var projectFile = Path.Combine(folder, "restore.csproj");
        File.WriteAllText(projectFile, project);

        var assets = Path.Combine(folder, "obj", "project.assets.json");
        if (!File.Exists(assets))
        {
            var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { "restore", projectFile, "--verbosity", "minimal" }) psi.ArgumentList.Add(a);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start 'dotnet restore'.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(assets))
                throw new InvalidOperationException("NuGet restore failed for "
                    + string.Join(", ", requests.Select(r => r.Version is null ? r.Id : $"{r.Id} {r.Version}")) + ":\n" + (await stdout + await stderr).Trim());
        }
        return ReadAssets(File.ReadAllText(assets), CurrentRuntimeIdentifier);
    }

    private static string DefaultWorkRoot() =>
        Environment.GetEnvironmentVariable("JUPYTERSHARP_NUGET_WORK") is { Length: > 0 } custom ? custom
        : Path.Combine(Path.GetTempPath(), "jupytersharp-nuget");
}
