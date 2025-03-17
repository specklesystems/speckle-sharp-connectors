using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;
using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser;

// https://standards.buildingsmart.org/IFC/RELEASE/IFC2x3/TC1/HTML/ifckernel/lexical/ifcreldefinesbyproperties.htm
public class IfcPropSetRelation : IfcRelation
{
  public IfcPropSetRelation(IfcGraph graph, StepInstance lineData, StepId from, StepList to)
    : base(graph, lineData, from, to) { }

  public IfcPropSet PropSet
  {
    get
    {
      var node = Graph.GetNode(From);
      if (node is not IfcPropSet r)
        throw new SpeckleIfcException($"Expected a property set not {node} from id {From}");
      return r;
    }
  }
}
