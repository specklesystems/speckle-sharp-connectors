using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Speckle.Importers.Ifc.Native;

[SuppressMessage("Globalization", "CA2101:Specify marshaling for P/Invoke string arguments")]
[SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible")]
[SuppressMessage("Security", "CA5393:Do not use unsafe DllImportSearchPath value")]
public static class WebIfc
{
#if WINDOWS
  private const string DllName = "web-ifc.dll";
  private const CharSet Set = CharSet.Ansi;
#else
  private const string DllName = "libweb-ifc.so";
  private const CharSet Set = CharSet.Auto;
#endif

  private const DllImportSearchPath ImportSearchPath = DllImportSearchPath.AssemblyDirectory;

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern IntPtr InitializeApi();

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern void FinalizeApi(IntPtr api);

  [DllImport(DllName, CharSet = Set)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern IntPtr LoadModel(IntPtr api, string fileName);

  [DllImport(DllName, CharSet = Set)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern string GetVersion();

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern IntPtr GetMesh(IntPtr geometry, int index);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern int GetNumMeshes(IntPtr geometry);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern uint GetGeometryType(IntPtr geometry);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern uint GetGeometryId(IntPtr geometry);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern uint GetLineId(IntPtr line);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern uint GetLineType(IntPtr line);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern string GetLineArguments(IntPtr line);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern int GetNumVertices(IntPtr mesh);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern IntPtr GetVertices(IntPtr mesh);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern IntPtr GetTransform(IntPtr mesh);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern int GetNumIndices(IntPtr mesh);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern IntPtr GetIndices(IntPtr mesh);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern IntPtr GetColor(IntPtr mesh);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern IntPtr GetGeometryFromId(IntPtr model, uint id);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern int GetNumGeometries(IntPtr model);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern IntPtr GetGeometryFromIndex(IntPtr model, int index);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern uint GetMaxId(IntPtr model);

  [DllImport(DllName)]
  [DefaultDllImportSearchPaths(ImportSearchPath)]
  public static extern IntPtr GetLineFromModel(IntPtr model, uint id);
}
