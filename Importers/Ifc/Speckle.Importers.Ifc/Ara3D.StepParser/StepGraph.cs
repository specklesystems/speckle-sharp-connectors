using Ara3D.Utils;
using Speckle.Sdk.Common;

namespace Speckle.Importers.Ifc.Ara3D.StepParser;

public class StepGraph
{
  public StepDocument Document { get; }

  private readonly Dictionary<uint, StepNode> _lookup = new();

  public StepNode GetNode(uint id) => _lookup[id];

  public IEnumerable<StepNode> Nodes => _lookup.Values;

  public StepGraph(StepDocument doc)
  {
    Document = doc;

    foreach (var e in doc.GetInstances())
    {
      var node = new StepNode(this, e);
      _lookup.Add(node.Entity.Id, node);
    }

    foreach (var n in Nodes)
    {
      n.Init();
    }
  }

  public static StepGraph Create(StepDocument doc) => new(doc);

  public string ToValString(StepNode node, int depth) => ToValString(node.Entity.Entity, depth - 1);

  public string ToValString(StepValue? value, int depth)
  {
    return value switch
    {
      null => "",
      StepList stepAggregate => $"({stepAggregate.Values.Select(v => ToValString(v, depth)).JoinStringsWithComma()})",
      StepEntity stepEntity => $"{stepEntity.EntityType}{ToValString(stepEntity.Attributes, depth)}",
      StepId stepId => depth <= 0 ? "#" : ToValString(GetNode(stepId.Id), depth - 1),
      _ => value.ToString().NotNull(),
    };
  }
}
