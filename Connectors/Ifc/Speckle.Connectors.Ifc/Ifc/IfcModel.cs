namespace Speckle.WebIfc.Importer.Ifc;

public class IfcModel(IntPtr model)
{
  public int GetNumGeometries() => WebIfc.GetNumGeometries(model);

  public IfcGeometry? GetGeometry(uint id)
  {
    var geometry = WebIfc.GetGeometryFromId(model, id);
    return geometry == IntPtr.Zero ? null : new IfcGeometry(geometry);
  }

  public IEnumerable<IfcGeometry> GetGeometries()
  {
    var numGeometries = WebIfc.GetNumGeometries(model);
    for (int i = 0; i < numGeometries; ++i)
    {
      var gPtr = WebIfc.GetGeometryFromIndex(model, i);
      if (gPtr != IntPtr.Zero)
      {
        yield return new IfcGeometry(gPtr);
      }
    }
  }

  public uint GetMaxId() => WebIfc.GetMaxId(model);

  public IfcLine? GetLine(uint id)
  {
    var line = WebIfc.GetLineFromModel(model, id);
    return line == IntPtr.Zero ? null : new IfcLine(line);
  }
}
