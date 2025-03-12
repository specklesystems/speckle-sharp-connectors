using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;

public sealed class IfcProject(IfcGraph graph, StepInstance lineData) : IfcNode(graph, lineData)
{
  public string? ObjectType => (LineData[5] as StepString)?.AsString();
  public string? LongName => (LineData[6] as StepString)?.AsString();
  public string? Phase => (LineData[7] as StepString)?.AsString();
}
