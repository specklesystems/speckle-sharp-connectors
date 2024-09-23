using Autodesk.AutoCAD.Runtime;
using Speckle.Autofac;

namespace Speckle.Connectors.Autocad.Plugin;

public class AutocadExtensionApplication : IExtensionApplication
{
  public void Initialize()
  {
    AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.OnAssemblyResolve<AutocadExtensionApplication>;

    AutocadCommand autocadCommand = new();
    AutocadRibbon ribbon = new(autocadCommand);
    ribbon.CreateRibbon();
  }

  public void Terminate() { }
}
