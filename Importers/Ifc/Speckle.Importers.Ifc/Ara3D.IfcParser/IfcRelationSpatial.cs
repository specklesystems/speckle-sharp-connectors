using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser;

public class IfcRelationSpatial : IfcRelation
{
  public IfcRelationSpatial(IfcGraph graph, StepInstance lineData, StepId from, StepList to)
    : base(graph, lineData, from, to) { }
}
