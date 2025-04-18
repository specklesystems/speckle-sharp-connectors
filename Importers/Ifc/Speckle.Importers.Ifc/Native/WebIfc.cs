using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Speckle.Importers.Ifc.Native;

[SuppressMessage("Globalization", "CA2101:Specify marshaling for P/Invoke string arguments")]
[SuppressMessage("Security", "CA5393:Do not use unsafe DllImportSearchPath value")]
internal static class WebIfc
{
#if WINDOWS
  private const string DLL_NAME = "Native/web-ifc.dll";
  private const CharSet SET = CharSet.Ansi;
#else
  private const string DLL_NAME = "libweb-ifc.so";
  private const CharSet SET = CharSet.Auto;
#endif

  private const DllImportSearchPath IMPORT_SEARCH_PATH = DllImportSearchPath.AssemblyDirectory;

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern IntPtr InitializeApi();

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern void FinalizeApi(IntPtr api);

  [DllImport(DLL_NAME, CharSet = SET)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern IntPtr LoadModel(IntPtr api, string fileName);

  [DllImport(DLL_NAME, CharSet = SET)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern string GetVersion();

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern IntPtr GetMesh(IntPtr geometry, int index);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern int GetNumMeshes(IntPtr geometry);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern uint GetGeometryType(IntPtr geometry);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern uint GetGeometryId(IntPtr geometry);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern uint GetLineId(IntPtr line);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern uint GetLineType(IntPtr line);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern string GetLineArguments(IntPtr line);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern int GetNumVertices(IntPtr mesh);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern IntPtr GetVertices(IntPtr mesh);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern IntPtr GetTransform(IntPtr mesh);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern int GetNumIndices(IntPtr mesh);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern IntPtr GetIndices(IntPtr mesh);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern IntPtr GetColor(IntPtr mesh);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern IntPtr GetGeometryFromId(IntPtr model, uint id);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern int GetNumGeometries(IntPtr model);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern IntPtr GetGeometryFromIndex(IntPtr model, int index);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern uint GetMaxId(IntPtr model);

  [DllImport(DLL_NAME)]
  [DefaultDllImportSearchPaths(IMPORT_SEARCH_PATH)]
  public static extern IntPtr GetLineFromModel(IntPtr model, uint id);
}
