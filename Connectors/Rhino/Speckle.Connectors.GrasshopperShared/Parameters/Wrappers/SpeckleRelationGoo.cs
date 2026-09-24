using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleRelationGoo : GH_Goo<SpeckleRelation>
{
  public override bool IsValid =>
    Value is not null && !string.IsNullOrEmpty(Value.SourceId) && !string.IsNullOrEmpty(Value.TargetId);
  public override string TypeName => "Speckle Relation";
  public override string TypeDescription => "A typed relation between two Speckle objects, published alongside them";

  public SpeckleRelationGoo(SpeckleRelation value)
  {
    Value = value;
  }

  /// <summary>
  /// Empty constructor should only be used for casting and deserialisation
  /// </summary>
  public SpeckleRelationGoo() { }

  // a relation is immutable, so sharing the value is a faithful duplicate
  public override IGH_Goo Duplicate() => new SpeckleRelationGoo(Value);

  public override string ToString() => Value?.ToString() ?? "Invalid Relation";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleRelation relation:
        Value = relation;
        return true;
      case SpeckleRelationGoo goo:
        Value = goo.Value;
        return true;
    }

    return false;
  }

  public override bool Write(GH_IWriter writer)
  {
    if (Value is null)
    {
      return false;
    }

    writer.SetInt32("type", (int)Value.Type);
    writer.SetString("sourceId", Value.SourceId);
    writer.SetString("targetId", Value.TargetId);
    if (Value.SourceName is not null)
    {
      writer.SetString("sourceName", Value.SourceName);
    }
    if (Value.TargetName is not null)
    {
      writer.SetString("targetName", Value.TargetName);
    }
    return true;
  }

  public override bool Read(GH_IReader reader)
  {
    int type = 0;
    string sourceId = "";
    string targetId = "";
    if (!reader.TryGetInt32("type", ref type) || !reader.TryGetString("sourceId", ref sourceId))
    {
      return false;
    }
    if (!reader.TryGetString("targetId", ref targetId) || !SpeckleRelationTypes.IsAuthorable(type))
    {
      return false;
    }

    string? sourceName = null;
    string? targetName = null;
    if (reader.ItemExists("sourceName"))
    {
      sourceName = reader.GetString("sourceName");
    }
    if (reader.ItemExists("targetName"))
    {
      targetName = reader.GetString("targetName");
    }

    Value = new SpeckleRelation
    {
      Type = (SpeckleRelationType)type,
      SourceId = sourceId,
      TargetId = targetId,
      SourceName = sourceName,
      TargetName = targetName,
    };
    return true;
  }
}
