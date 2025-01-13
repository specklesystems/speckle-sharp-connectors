using Ara3D.StepParser;

namespace Ara3D.IfcParser;

public class IfcProp : IfcNode
{
  public readonly StepValue Value;

  public new string Name => this[0].AsString();
  public new string Description => this[1].AsString();

  public IfcProp(IfcGraph graph, StepInstance lineData, StepValue value)
    : base(graph, lineData)
  {
    if (lineData.Count < 2)
      throw new System.Exception("Expected at least two values in the line data");
    if (!(lineData[0] is StepString))
      throw new System.Exception("Expected the first value to be a string (Name)");
    Value = value;
  }
}
