using Speckle.InterfaceGenerator;

namespace Speckle.WebIfc.Importer.Ifc;

[GenerateAutoInterface]
public class IfcFactory : IIfcFactory
{
  //probably never disposing this
  private static readonly IntPtr _ptr = WebIfc.InitializeApi();

  public IfcModel Open(string fullPath)
  {
    if (!File.Exists(fullPath))
    {
      throw new ArgumentException($"File does not exist: {fullPath}");
    }
    return new(WebIfc.LoadModel(_ptr, fullPath));
  }

  public string Version => WebIfc.GetVersion();
}
