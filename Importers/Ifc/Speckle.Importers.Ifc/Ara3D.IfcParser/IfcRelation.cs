﻿using Speckle.Importers.Ifc.Ara3D.IfcParser.Schema;
using Speckle.Importers.Ifc.Ara3D.StepParser;

namespace Speckle.Importers.Ifc.Ara3D.IfcParser;

/// <summary>
/// Always express a 1-to-many relation
/// </summary>
public class IfcRelation : IfcEntity
{
  public StepId From { get; }
  public StepList To { get; }

  public IfcRelation(IfcGraph graph, StepInstance lineData, StepId from, StepList to)
    : base(graph, lineData)
  {
    if (!IsIfcRoot)
      throw new SpeckleIfcException("Expected relation to be an IFC root entity");
    From = from;
    To = to;
  }

  public IEnumerable<uint> GetRelatedIds() => To.Values.Select(v => v.AsId());

  public IEnumerable<IfcNode> GetRelatedNodes() => Graph.GetNodes(GetRelatedIds());
}
