using Ara3D.Utils;

namespace Ara3D.StepParser;

public class StepNode
{
  public readonly StepGraph Graph;
  public readonly StepInstance Entity;

  public StepNode(StepGraph g, StepInstance e)
  {
    Graph = g;
    Entity = e;
  }

  public List<StepNode> Nodes { get; } = new();

  private void AddNodes(StepValue value)
  {
    if (value is StepId id)
    {
      var n = Graph.GetNode(id.Id);
      Nodes.Add(n);
    }
    else if (value is StepList agg)
    {
      foreach (var v in agg.Values)
        AddNodes(v);
    }
  }

  public void Init()
  {
    foreach (var a in Entity.AttributeValues)
      AddNodes(a);
  }

  public override string ToString() => Entity.ToString();

  public string ToGraph(HashSet<StepNode> prev = null)
  {
    prev ??= new HashSet<StepNode>();
    if (prev.Contains(this))
      return "_";
    var nodeStr = Nodes.Select(n => n.ToGraph(prev)).JoinStringsWithComma();
    return $"{EntityType}({nodeStr})";
  }

  public string EntityType => Entity.EntityType;

  public string QuickHash() => $"{EntityType}:{Nodes.Count}";
}
