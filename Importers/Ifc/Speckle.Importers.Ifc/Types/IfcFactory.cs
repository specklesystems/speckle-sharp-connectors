using Speckle.InterfaceGenerator;

namespace Speckle.Importers.Ifc.Types;

[GenerateAutoInterface]
public sealed class IfcFactory : IIfcFactory
{
  //probably never disposing this
  private static readonly IntPtr _ptr = Importers.Ifc.Native.WebIfc.InitializeApi();

  public IfcModel Open(string fullPath)
  {
    if (!File.Exists(fullPath))
    {
      throw new ArgumentException($"File does not exist: {fullPath}");
    }
    return new(Importers.Ifc.Native.WebIfc.LoadModel(_ptr, fullPath));
  }

  public string Version => Importers.Ifc.Native.WebIfc.GetVersion();
}
