using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;

/// <summary>
/// Types like IfcSite, IfcBuilding, IfcBuildingStorey
/// </summary>
/// <param name="graph"></param>
/// <param name="lineData"></param>
public class IfcSpatialStructureElement(IfcGraph graph, StepInstance lineData) : IfcNode(graph, lineData)
{
  public string? ObjectType => (LineData[5] as StepString)?.AsString();
  public string? LongName => (LineData[8] as StepString)?.AsString();
  public string? CompositionType => (LineData[9] as StepString)?.AsString();
}
