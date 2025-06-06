namespace Speckle.Importers.Ifc.Ara3D.StepParser;

public class StepInstance
{
  public StepEntity Entity { get; }
  public uint Id { get; }

  public List<StepValue> AttributeValues => Entity.Attributes.Values;

  public string EntityType => Entity?.EntityType.ToString() ?? "";

  public StepInstance(uint id, StepEntity entity)
  {
    Id = id;
    Entity = entity;
  }

  public bool IsEntityType(string str) => EntityType == str;

  public override string ToString() => $"#{Id}={Entity};";

  public int Count => AttributeValues.Count;

  public StepValue this[int i] => AttributeValues[i];
}
