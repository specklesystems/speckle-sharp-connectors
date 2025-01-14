namespace Speckle.Connectors.Ifc.Ifc;

public class IfcLine(IntPtr line)
{
  public uint Id => WebIfc.WebIfc.GetLineId(line);
  public IfcSchemaType Type => (IfcSchemaType)WebIfc.WebIfc.GetLineType(line);

  public string Arguments() => WebIfc.WebIfc.GetLineArguments(line);
}
