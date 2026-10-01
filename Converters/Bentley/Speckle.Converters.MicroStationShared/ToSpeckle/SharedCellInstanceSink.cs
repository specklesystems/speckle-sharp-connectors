namespace Speckle.Converters.MicroStation.ToSpeckle;

/// <summary>One placement of a shared-cell definition, emitted by the dispatcher for a reference
/// met during recursion (dgnextract's <c>Out.instances</c>): the 16-double row-major master-frame
/// transform (ambient stack composed, global origin removed).</summary>
public sealed record InstanceUse(string DefinitionId, string DefinitionName, List<double> Transform, string Units);

/// <summary>What one element extracts to: display geometry plus shared-cell placements.</summary>
public sealed record ExtractionResult(List<ExtractedGeometry> DisplayValue, List<InstanceUse> Instances);

/// <summary>
/// Per-operation shared-cell definition registry for the dispatcher's instancing path (dgnextract's
/// InstanceMgr). Only the BUNDLE send enables it — the definition geometry becomes DEFINES rows and
/// every reference an INSTANCE placement. The classic v3 Collection builder leaves it disabled and
/// the dispatcher BAKES nested shared cells instead (that pipeline cannot express object-level
/// placements). Scoped per send operation.
/// </summary>
public class SharedCellInstanceSink
{
  /// <summary>Definition geometry, keyed by the definition element id. Built once, in the
  /// definition's LOCAL frame.</summary>
  public Dictionary<string, (string Name, List<ExtractedGeometry> Members)> Definitions { get; } = [];

  /// <summary>False (default): the dispatcher bakes nested shared cells (v3 pipeline). The bundle
  /// builder switches it on for the duration of its collect phase.</summary>
  public bool Enabled { get; set; }

  public bool HasDefinition(string definitionId) => Definitions.ContainsKey(definitionId);

  public void AddDefinition(string definitionId, string name, List<ExtractedGeometry> members) =>
    Definitions[definitionId] = (name, members);
}
