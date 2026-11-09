using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Speckle.Sdk;

namespace Speckle.Connectors.Common.Operations;

/// <summary>
/// Pre-loads IronCompress's native <c>nironcompress.dll</c> so Parquet.Net's Zstd codec resolves on .NET Framework.
/// </summary>
/// <remarks>
/// Parquet.Net Zstd-(de)compresses row groups via IronCompress, which P/Invokes a bare <c>[DllImport("nironcompress")]</c>.
/// On .NET Framework that resolves against the host process (e.g. <c>Revit.exe</c>) directory — NOT the plugin folder —
/// so the co-deployed native isn't found. We pre-load it by full path once (Windows then matches the bare import to the
/// already-loaded module by base name); the native is located next to this assembly, which every connector deploys into
/// its plugin folder alongside <c>nironcompress.dll</c> (see the connectors' <c>Net48.IronCompress.props</c>). Call at
/// plugin startup so BOTH the artefact send AND receive paths have it — receive reads the parquet bundle (in
/// <see cref="ArtifactReceiver"/>) before the host builder runs, so a receive-first session would otherwise hit
/// "No compression codec for Zstd is available". net8+ resolves the native via its RID, so this is a no-op there.
/// </remarks>
public static class ZstdNativeLoader
{
#if NETFRAMEWORK
  [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static extern IntPtr LoadLibrary(string lpFileName);

  private static int s_preloaded;

  public static void Ensure(ILogger? logger = null)
  {
    if (Interlocked.Exchange(ref s_preloaded, 1) == 1)
    {
      return;
    }
    try
    {
      var dir = Path.GetDirectoryName(typeof(ZstdNativeLoader).Assembly.Location);
      var native = Path.Combine(dir ?? string.Empty, "nironcompress.dll");
      if (File.Exists(native))
      {
        if (LoadLibrary(native) == IntPtr.Zero)
        {
          logger?.LogWarning("Failed to pre-load native {Native} (parquet Zstd compression may fail)", native);
        }
      }
      else
      {
        logger?.LogWarning("Native {Native} not found next to the plugin; parquet Zstd compression may fail", native);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger?.LogWarning(ex, "Could not pre-load the IronCompress native for parquet Zstd compression");
    }
  }
#else
  // net8+ resolves the native via its RID-specific runtimes/ deployment, except inside Rhino 8 Mac, where
  // RuntimeInformation.RuntimeIdentifier is "unknown" so IronCompress's own probe finds nothing (FEA-124). Other
  // platforms keep the no-op.
  private static int s_preloaded;

  public static void Ensure(ILogger? logger = null)
  {
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || Interlocked.Exchange(ref s_preloaded, 1) == 1)
    {
      return;
    }
    try
    {
      var dir = Path.GetDirectoryName(typeof(ZstdNativeLoader).Assembly.Location) ?? string.Empty;
      var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
      var native = Path.Combine(dir, "runtimes", $"osx-{arch}", "native", "libnironcompress.dylib");
      if (!File.Exists(native))
      {
        return;
      }
      if (!NativeLibrary.TryLoad(native, out _))
      {
        logger?.LogWarning("Failed to pre-load native {Native} (parquet Zstd compression may fail)", native);
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger?.LogWarning(ex, "Could not pre-load the IronCompress native for parquet Zstd compression");
    }
  }
#endif
}
