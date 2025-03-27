using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;

public sealed class IfcUnitAssignment(IfcGraph graph, StepInstance lineData) : IfcNode(graph, lineData)
{
  public IEnumerable<IfcNode> Units
  {
    get
    {
      if (LineData[0] is StepList units)
      {
        foreach (var stepValue in units.Values)
        {
          var id = (StepId)stepValue;
          var unit = Graph.GetNode(id);
          yield return unit;
        }
      }
    }
  }
}
