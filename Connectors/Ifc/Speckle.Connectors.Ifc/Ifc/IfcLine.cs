namespace Speckle.WebIfc.Importer.Ifc;

public class IfcLine(IntPtr line)
{
  public uint Id => WebIfc.GetLineId(line);
  public IfcSchemaType Type => (IfcSchemaType)WebIfc.GetLineType(line);

  public string Arguments() => WebIfc.GetLineArguments(line);
}
