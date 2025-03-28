using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;
using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser;

/// <summary>
/// It represents an entity definition. It is usually a single line in a STEP file.
/// Many entity definitions are derived from IfcRoot (including relations).
/// IfcRoot has a GUID, OwnerId, optional Name, and optional Description
/// https://iaiweb.lbl.gov/Resources/IFC_Releases/R2x3_final/ifckernel/lexical/ifcroot.htm
/// </summary>
public class IfcEntity
{
  public StepInstance LineData { get; }
  public IfcGraph Graph { get; }
  public uint Id => LineData.Id;
  public string Type => LineData?.EntityType ?? "";

  public IfcEntity(IfcGraph graph, StepInstance lineData)
  {
    Graph = graph;
    LineData = lineData;
  }

  public override bool Equals(object? obj)
  {
    if (obj is IfcEntity other)
      return Id == other.Id;
    return false;
  }

  public override int GetHashCode() => (int)Id;

  public override string ToString() => $"{Type}#{Id}";

  public bool IsIfcRoot => Count >= 4 && this[0] is StepString && (this[1] is StepId) || (this[1] is StepUnassigned);

  // Modern IFC files conform to this, but older ones have been observed to have different length IDs.
  // Leaving as a comment for now.
  //&& str.Value.Length == 22;

  public string Guid => ((StepString)this[0]).Value.ToString();

  public uint OwnerId => (this[1] as StepId)?.Id ?? 0;

  public string? Name => (this[2] as StepString)?.AsString();

  public string? Description => (this[3] as StepString)?.AsString();

  public int Count => LineData.Count;

  public StepValue this[int i] => LineData[i];

  public IReadOnlyList<IfcRelation> GetOutgoingRelations() => Graph.GetRelationsFrom(Id);

  public IEnumerable<IfcNode> GetAggregatedChildren() =>
    GetOutgoingRelations().OfType<IfcRelationAggregate>().SelectMany(r => r.GetRelatedNodes());

  public IEnumerable<IfcNode> GetSpatialChildren() =>
    GetOutgoingRelations().OfType<IfcRelationSpatial>().SelectMany(r => r.GetRelatedNodes());

  public IEnumerable<IfcNode> GetChildren() => GetAggregatedChildren().Concat(GetSpatialChildren()).Distinct();

  public IReadOnlyList<IfcPropSet> GetPropSets() =>
    Graph.PropertySetsByNode.TryGetValue(Id, out var list) ? list : Array.Empty<IfcPropSet>();
}
