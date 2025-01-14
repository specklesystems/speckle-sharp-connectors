using Ara3D.Utils;
using Speckle.Sdk.Common;

namespace Speckle.Importers.Ifc.Ara3D.StepParser;

public class StepGraph
{
  public StepDocument Document { get; }

  public readonly Dictionary<uint, StepNode> Lookup = new();

  public StepNode GetNode(uint id) => Lookup[id];

  public IEnumerable<StepNode> Nodes => Lookup.Values;

  public StepGraph(StepDocument doc)
  {
    Document = doc;

    foreach (var e in doc.GetInstances())
    {
      var node = new StepNode(this, e);
      Lookup.Add(node.Entity.Id, node);
    }

    foreach (var n in Nodes)
      n.Init();
  }

  public static StepGraph Create(StepDocument doc) => new(doc);

  public string ToValString(StepNode node, int depth) => ToValString(node.Entity.Entity, depth - 1);

  public string ToValString(StepValue value, int depth)
  {
    if (value == null)
      return "";

    switch (value)
    {
      case StepList stepAggregate:
        return $"({stepAggregate.Values.Select(v => ToValString(v, depth)).JoinStringsWithComma()})";

      case StepEntity stepEntity:
        return $"{stepEntity.EntityType}{ToValString(stepEntity.Attributes, depth)}";

      case StepId stepId:
        return depth <= 0 ? "#" : ToValString(GetNode(stepId.Id), depth - 1);

      case StepNumber stepNumber:
      case StepRedeclared stepRedeclared:
      case StepString stepString:
      case StepSymbol stepSymbol:
      case StepUnassigned stepUnassigned:
      default:
        return value.ToString().NotNull();
    }
  }
}
