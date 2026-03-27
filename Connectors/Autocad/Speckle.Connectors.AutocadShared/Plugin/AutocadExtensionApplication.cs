//using System.Reflection;

using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Speckle.Connectors.Common;

namespace Speckle.Connectors.Autocad.Plugin;

public class AutocadExtensionApplication : IExtensionApplication
{
  public void Initialize()
  {
    AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.OnAssemblyResolve<AutocadExtensionApplication>;
#if PLANT3D
    AppDomain.CurrentDomain.AssemblyResolve += OnPlant3dAssemblyResolve;
#endif

    AutocadCommand autocadCommand = new();
    AutocadRibbon ribbon = new(autocadCommand);
    ribbon.CreateRibbon();
  }

  public void Terminate() { }

#if PLANT3D
  /// <summary>
  /// Resolves AEC and Plant3D assemblies from AutoCAD install subfolders (ACA, PLNT3D)
  /// that aren't on the default probing path.
  /// </summary>
  private static Assembly? OnPlant3dAssemblyResolve(object? sender, ResolveEventArgs args)
  {
    string name = args.Name.Split(',')[0];
    string acadDir =
      Path.GetDirectoryName(typeof(Autodesk.AutoCAD.Runtime.CommandMethodAttribute).Assembly.Location) ?? string.Empty;

    // Probe subfolders where AEC/Plant3D DLLs live
    string[] probePaths =
    [
      Path.Combine(acadDir, "ACA", name + ".dll"),
      Path.Combine(acadDir, "PLNT3D", name + ".dll"),
    ];

    foreach (string probePath in probePaths)
    {
      if (File.Exists(probePath))
      {
        return Assembly.LoadFrom(probePath);
      }
    }

    return null;
  }
#endif
}
