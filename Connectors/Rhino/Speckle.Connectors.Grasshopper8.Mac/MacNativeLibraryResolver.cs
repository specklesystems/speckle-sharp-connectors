using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Speckle.Connectors.GrasshopperShared;

/// <summary>
/// Resolves bare <c>[DllImport]</c> names (e_sqlite3, nironcompress) to the plug-in's
/// <c>runtimes/osx-{arch}/native/</c> folder.
/// </summary>
/// <remarks>
/// Inside Rhino 8 Mac <c>RuntimeInformation.RuntimeIdentifier</c> is <c>"unknown"</c>, so the runtime's RID-based
/// probing of <c>runtimes/</c> never runs and every native the SDK depends on fails with DllNotFoundException
/// (rhino-mac-connector spec, tickets 08/09). A module initializer registers the resolver before any component
/// (and therefore before PriorityLoad builds the DI container) touches SQLite or Parquet. Scoped to the assemblies
/// that own the imports so no other plug-in's resolver is pre-empted.
/// </remarks>
internal static class MacNativeLibraryResolver
{
  private static readonly string[] s_nativeConsumers = ["SQLitePCLRaw.provider.e_sqlite3", "IronCompress"];
  private static string? s_nativeDir;

  // CA2255: intended for application code — this assembly is the plug-in entry point Rhino loads, and the resolver
  // must be in place before GH constructs any component (which is before PriorityLoad runs).
#pragma warning disable CA2255
  [ModuleInitializer]
#pragma warning restore CA2255
  internal static void Register()
  {
    if (!OperatingSystem.IsMacOS())
    {
      return;
    }
    var pluginDir = Path.GetDirectoryName(typeof(MacNativeLibraryResolver).Assembly.Location) ?? string.Empty;
    var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    s_nativeDir = Path.Combine(pluginDir, "runtimes", $"osx-{arch}", "native");

    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
      Attach(assembly);
    }
    AppDomain.CurrentDomain.AssemblyLoad += (_, e) => Attach(e.LoadedAssembly);
  }

  private static void Attach(Assembly assembly)
  {
    var name = assembly.GetName().Name;
    if (name is null || Array.IndexOf(s_nativeConsumers, name) < 0)
    {
      return;
    }
    try
    {
      NativeLibrary.SetDllImportResolver(assembly, Resolve);
    }
    catch (InvalidOperationException)
    {
      // a resolver is already registered for this assembly — leave it
    }
  }

  private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
  {
    if (s_nativeDir is null)
    {
      return IntPtr.Zero;
    }
    foreach (var candidate in Candidates(libraryName))
    {
      var path = Path.Combine(s_nativeDir, candidate);
      if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
      {
        return handle;
      }
    }
    return IntPtr.Zero; // fall back to default probing
  }

  private static IEnumerable<string> Candidates(string libraryName)
  {
    var stem = libraryName.EndsWith(".dylib", StringComparison.Ordinal) ? libraryName[..^6] : libraryName;
    stem = stem.StartsWith("lib", StringComparison.Ordinal) ? stem[3..] : stem;
    yield return $"lib{stem}.dylib";
    yield return $"{stem}.dylib";
    yield return libraryName;
  }
}
