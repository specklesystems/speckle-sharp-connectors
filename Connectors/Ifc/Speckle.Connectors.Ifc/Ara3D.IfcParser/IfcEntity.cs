﻿using Ara3D.StepParser;

namespace Ara3D.IfcParser;

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
  public uint Id => (uint)LineData.Id;
  public string Type => LineData?.EntityType ?? "";

  public IfcEntity(IfcGraph graph, StepInstance lineData)
  {
    Graph = graph;
    LineData = lineData;
  }

  public override bool Equals(object obj)
  {
    if (obj is IfcEntity other)
      return Id == other.Id;
    return false;
  }

  public override int GetHashCode() => (int)Id;

  public override string ToString() => $"{Type}#{Id}";

  public bool IsIfcRoot =>
    Count >= 4 && this[0] is StepString str && (this[1] is StepId) || (this[1] is StepUnassigned);

  // Modern IFC files conform to this, but older ones have been observed to have different length IDs.
  // Leaving as a comment for now.
  //&& str.Value.Length == 22;

  public string Guid => IsIfcRoot ? (this[0] as StepString)?.Value.ToString() : null;

  public uint OwnerId => IsIfcRoot ? (this[1] as StepId)?.Id ?? 0 : 0;

  public string Name => IsIfcRoot ? (this[2] as StepString)?.AsString() : null;

  public string Description => IsIfcRoot ? (this[3] as StepString)?.AsString() : null;

  public int Count => LineData.Count;

  public StepValue this[int i] => LineData[i];

  public IReadOnlyList<IfcRelation> GetOutgoingRelations() => Graph.GetRelationsFrom(Id);

  public IEnumerable<IfcNode> GetAggregatedChildren() =>
    GetOutgoingRelations().OfType<IfcRelationAggregate>().SelectMany(r => r.GetRelatedNodes());

  public IEnumerable<IfcNode> GetSpatialChildren() =>
    GetOutgoingRelations().OfType<IfcRelationSpatial>().SelectMany(r => r.GetRelatedNodes());

  public IEnumerable<IfcNode> GetChildren() => GetAggregatedChildren().Concat(GetSpatialChildren());

  public IReadOnlyList<IfcPropSet> GetPropSets() =>
    Graph.PropertySetsByNode.TryGetValue(Id, out var list) ? list : Array.Empty<IfcPropSet>();
}
