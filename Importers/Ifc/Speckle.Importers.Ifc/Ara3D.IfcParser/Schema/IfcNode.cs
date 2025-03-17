using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;

public class IfcNode : IfcEntity
{
  public IfcNode(IfcGraph graph, StepInstance lineData)
    : base(graph, lineData) { }
}
