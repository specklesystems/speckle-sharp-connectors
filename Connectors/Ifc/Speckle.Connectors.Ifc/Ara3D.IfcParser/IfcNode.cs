using Speckle.Connectors.Ifc.Ara3D.StepParser;

namespace Speckle.Connectors.Ifc.Ara3D.IfcParser;

public class IfcNode : IfcEntity
{
  public IfcNode(IfcGraph graph, StepInstance lineData)
    : base(graph, lineData) { }
}
