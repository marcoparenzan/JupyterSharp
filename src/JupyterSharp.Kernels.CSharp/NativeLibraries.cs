using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace JupyterSharp.Kernels.CSharp;

/// <summary>
/// Lets packages restored by <c>#r "nuget: ..."</c> load their native libraries (SkiaSharp, OpenCV, libtorch).
/// Roslyn loads the restored assemblies in its own way, so a hook on the default load context alone is not enough: every assembly that
/// loads gets a <see cref="NativeLibrary.SetDllImportResolver"/> that searches the native folders found in the restored packages,
/// and the default load context gets the same search for libraries that no resolver claimed. Registered once per process.
/// </summary>
internal static class NativeLibraries
{
    private static readonly object Gate = new();
    private static readonly List<string> Directories = new();
    private static bool _registered;

    public static void Add(IEnumerable<string> directories)
    {
        lock (Gate)
        {
            foreach (var d in directories) if (!Directories.Contains(d, StringComparer.OrdinalIgnoreCase)) Directories.Add(d);
            if (_registered) return;
            _registered = true;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += (assembly, name) => Find(name);
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => Attach(e.LoadedAssembly);
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies()) Attach(loaded);
        }
    }

    private static void Attach(Assembly assembly)
    {
        if (assembly.IsDynamic) return;
        try { NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => Find(name)); }
        catch (InvalidOperationException) { /* a resolver is already set for this assembly */ }
    }

    private static IntPtr Find(string name)
    {
        string[] directories;
        lock (Gate) directories = Directories.ToArray();
        var ext = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        var names = new List<string> { name };
        if (!name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) names.Add(name + ext);
        if (!OperatingSystem.IsWindows() && !name.StartsWith("lib", StringComparison.Ordinal)) names.Add("lib" + name + ext);
        foreach (var dir in directories)
            foreach (var candidate in names)
            {
                var path = Path.Combine(dir, candidate);
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle)) return handle;
            }
        return IntPtr.Zero;
    }
}
