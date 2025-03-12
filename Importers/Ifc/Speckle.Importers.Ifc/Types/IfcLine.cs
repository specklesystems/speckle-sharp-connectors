namespace Speckle.Importers.Ifc.Types;

public sealed class IfcLine(IntPtr line)
{
  public uint Id => Importers.Ifc.Native.WebIfc.GetLineId(line);
  public IfcSchemaType Type => (IfcSchemaType)Importers.Ifc.Native.WebIfc.GetLineType(line);

  public string Arguments() => Importers.Ifc.Native.WebIfc.GetLineArguments(line);
}
