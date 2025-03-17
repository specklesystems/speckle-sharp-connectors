using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;

public class IfcProp : IfcNode
{
  public readonly StepValue Value;

  public new string Name => this[0].AsString();
  public new string Description => this[1].AsString();

  public IfcProp(IfcGraph graph, StepInstance lineData, StepValue value)
    : base(graph, lineData)
  {
    if (lineData.Count < 2)
      throw new SpeckleIfcException("Expected at least two values in the line data");
    if (lineData[0] is not StepString)
      throw new SpeckleIfcException("Expected the first value to be a string (Name)");
    Value = value;
  }
}
