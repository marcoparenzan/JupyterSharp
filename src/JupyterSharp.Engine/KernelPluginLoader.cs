using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using JupyterSharp.Kernels.Abstractions;

namespace JupyterSharp.Engine;

/// <summary>
/// Discovers and loads kernel plugins. Every kernel is one: the four shipped with JupyterSharp
/// (csharp, fsharp, powershell, powerfx), which JupyterSharp.Host and the CLI publish into their own
/// <c>kernels/&lt;id&gt;</c> folders, and those each library implements in its own repo (PySharp, MatSharp,
/// NDSharp, ...). The search always covers <c>&lt;host base dir&gt;/kernels/*</c>, then the directories of
/// <paramref name="kernelPathsOverride"/> if given (e.g. the CLI's <c>--kernel-paths</c>), else of
/// <c>JUPYTERSHARP_KERNEL_PATHS</c> (an OS path-list, one directory per plugin — each expected to be that
/// plugin project's own <c>dotnet publish</c> output). A later directory wins over an earlier one with the
/// same kernel id, so a configured path can replace a bundled kernel during development.
///
/// Every plugin loads into the default load context via <see cref="Assembly.LoadFrom"/>, which —
/// specifically for this case — comes with .NET's own "LoadFrom context" dependency probing: an
/// assembly that can't otherwise be resolved is looked for beside the assembly that requested it,
/// so a plugin's own private dependencies (everything <c>dotnet publish</c> copied into its
/// folder) are found automatically with no code here.
///
/// An earlier version of this loader gave each plugin its own collectible
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> for stronger version isolation between
/// plugins (relevant since e.g. the Ralf plugin drags in Azure SDK/Speech SDK versions). That
/// produced a reproducible failure instead: running a builtin kernel (C# *or* F#, both of which do
/// their own dynamic/JIT compilation) before a plugin's first use made that plugin's own
/// same-folder dependency resolution start failing with "operation is not legal in the current
/// state" — evidence of the two dynamic-compilation engines interacting with custom
/// AssemblyLoadContexts in a way not fully understood, not something to ship. A single shared load
/// context is the simpler, correct-first choice for what this actually is: a handful of known plugins
/// on one person's machine, not an untrusted multi-tenant plugin host — version conflicts between
/// them are a real but much smaller risk than a host that intermittently fails to load a kernel.
/// </summary>
public static class KernelPluginLoader
{
    public static IReadOnlyDictionary<string, IKernelPlugin> DiscoverPlugins(TextWriter warnings, IEnumerable<string>? kernelPathsOverride = null)
    {
        var plugins = new Dictionary<string, IKernelPlugin>();
        foreach (var directory in ResolvePluginDirectories(kernelPathsOverride))
        {
            try
            {
                var plugin = LoadPlugin(directory);
                if (plugin is null)
                {
                    warnings.WriteLine($"JupyterSharp: no IKernelPlugin found in '{directory}', skipping.");
                    continue;
                }
                plugins[plugin.KernelId] = plugin;
            }
            catch (Exception ex)
            {
                warnings.WriteLine($"JupyterSharp: failed to load kernel plugin from '{directory}': {ex.Message}");
            }
        }
        return plugins;
    }

    private static IEnumerable<string> ResolvePluginDirectories(IEnumerable<string>? overridePaths)
    {
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "kernels");
        if (Directory.Exists(bundledRoot))
            foreach (var directory in Directory.GetDirectories(bundledRoot).Order(StringComparer.Ordinal)) yield return directory;

        IEnumerable<string> configured = overridePaths
            ?? (Environment.GetEnvironmentVariable("JUPYTERSHARP_KERNEL_PATHS") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var path in configured)
            if (Directory.Exists(path)) yield return path;
    }

    private static IKernelPlugin? LoadPlugin(string directory)
    {
        // A `dotnet publish` output has exactly one <name>.deps.json, next to its matching main
        // DLL; every other DLL in the folder is a dependency and has no deps.json of its own.
        var mainAssemblyPath = Directory.GetFiles(directory, "*.dll")
            .FirstOrDefault(dll => File.Exists(Path.ChangeExtension(dll, ".deps.json")));
        if (mainAssemblyPath is null) return null;

        RegisterNativeProbing(directory);
        RegisterDependencyResolver(mainAssemblyPath);
        var assembly = Assembly.LoadFrom(mainAssemblyPath);
        var pluginType = assembly.GetTypes().FirstOrDefault(t =>
            typeof(IKernelPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
        return pluginType is null ? null : (IKernelPlugin)Activator.CreateInstance(pluginType)!;
    }

    private static readonly List<AssemblyDependencyResolver> Resolvers = new();
    private static bool _resolvingHooked;

    /// <summary>LoadFrom finds a plugin's managed dependencies only when they sit next to it. Assemblies that a package
    /// ships per platform (<c>runtimes/&lt;rid&gt;/lib/...</c>, e.g. PowerShell's System.Management.Automation) are only known
    /// to the plugin's own <c>.deps.json</c>: resolve them through it, still in the default load context.</summary>
    private static void RegisterDependencyResolver(string mainAssemblyPath)
    {
        lock (Resolvers)
        {
            Resolvers.Add(new AssemblyDependencyResolver(mainAssemblyPath));
            if (_resolvingHooked) return;
            _resolvingHooked = true;
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                AssemblyDependencyResolver[] resolvers;
                lock (Resolvers) resolvers = Resolvers.ToArray();
                foreach (var resolver in resolvers)
                    if (resolver.ResolveAssemblyToPath(name) is { } path && File.Exists(path)) return context.LoadFromAssemblyPath(path);
                return null;
            };
        }
    }

    private static readonly List<string> NativeDirectories = new();
    private static bool _nativeHooked;

    /// <summary>LoadFrom resolves managed dependencies next to the plugin but not native ones (SkiaSharp, OpenCvSharp, ...),
    /// so probe the plugin's <c>runtimes/&lt;rid&gt;/native</c> folder and its root for them.</summary>
    private static void RegisterNativeProbing(string directory)
    {
        lock (NativeDirectories)
        {
            var rid = RuntimeInformation.RuntimeIdentifier;
            var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            NativeDirectories.Add(Path.Combine(directory, "runtimes", $"{os}-{arch}", "native"));
            NativeDirectories.Add(Path.Combine(directory, "runtimes", rid, "native"));
            NativeDirectories.Add(directory);
            if (_nativeHooked) return;
            _nativeHooked = true;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
            {
                string[] candidates = OperatingSystem.IsWindows() ? new[] { name, name + ".dll" }
                    : OperatingSystem.IsMacOS() ? new[] { name, name + ".dylib", "lib" + name + ".dylib" }
                    : new[] { name, name + ".so", "lib" + name + ".so" };
                string[] dirs;
                lock (NativeDirectories) dirs = NativeDirectories.ToArray();
                foreach (var dir in dirs)
                    foreach (var c in candidates)
                    {
                        var path = Path.Combine(dir, c);
                        if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle)) return handle;
                    }
                return IntPtr.Zero;
            };
        }
    }
}
