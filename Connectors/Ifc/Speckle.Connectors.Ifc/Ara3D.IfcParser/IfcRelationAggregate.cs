﻿using Ara3D.StepParser;

namespace Ara3D.IfcParser;

public class IfcRelationAggregate : IfcRelation
{
  public IfcRelationAggregate(IfcGraph graph, StepInstance lineData, StepId from, StepList to)
    : base(graph, lineData, from, to) { }
}
