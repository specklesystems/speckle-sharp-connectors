using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;

public sealed class IfcSiUnit(IfcGraph graph, StepInstance lineData) : IfcNode(graph, lineData)
{
  public string UnitType => ((StepSymbol)LineData[1]).AsString();
  public string? Prefix => (LineData[2] as StepSymbol)?.AsString();

  //Note: This property is actually "Name" in the schema, but right now we have a name property incorrectly on IfcEntity
  public string UnitName => ((StepSymbol)LineData[3]).AsString();
}
