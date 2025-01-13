using Ara3D.StepParser;

namespace Ara3D.IfcParser;

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
      var r = node as IfcPropSet;
      if (r == null)
        throw new System.Exception($"Expected a property set not {node} from id {From}");
      return r;
    }
  }
}
