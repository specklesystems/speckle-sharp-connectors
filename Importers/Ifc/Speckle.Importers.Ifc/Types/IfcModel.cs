namespace Speckle.Importers.Ifc.Types;

public sealed class IfcModel(IntPtr model)
{
  public int GetNumGeometries() => Importers.Ifc.Native.WebIfc.GetNumGeometries(model);

  public IfcGeometry? GetGeometry(uint id)
  {
    var geometry = Importers.Ifc.Native.WebIfc.GetGeometryFromId(model, id);
    return geometry == IntPtr.Zero ? null : new IfcGeometry(geometry);
  }

  public IEnumerable<IfcGeometry> GetGeometries()
  {
    var numGeometries = Importers.Ifc.Native.WebIfc.GetNumGeometries(model);
    for (int i = 0; i < numGeometries; ++i)
    {
      var gPtr = Importers.Ifc.Native.WebIfc.GetGeometryFromIndex(model, i);
      if (gPtr != IntPtr.Zero)
      {
        yield return new IfcGeometry(gPtr);
      }
    }
  }

  public uint GetMaxId() => Importers.Ifc.Native.WebIfc.GetMaxId(model);

  public IfcLine? GetLine(uint id)
  {
    var line = Importers.Ifc.Native.WebIfc.GetLineFromModel(model, id);
    return line == IntPtr.Zero ? null : new IfcLine(line);
  }
}
