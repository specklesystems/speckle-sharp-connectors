namespace Speckle.Connectors.Ifc.Types;

public class IfcLine(IntPtr line)
{
  public uint Id => WebIfc.WebIfc.GetLineId(line);
  public IfcSchemaType Type => (IfcSchemaType)WebIfc.WebIfc.GetLineType(line);

  public string Arguments() => WebIfc.WebIfc.GetLineArguments(line);
}
