namespace Speckle.Converters.TeklaShared.ToSpeckle.Helpers;

public class ClassPropertyExtractor
{
  public ClassPropertyExtractor() { }

  public Dictionary<string, object?> GetProperties(TSM.ModelObject modelObject)
  {
    Dictionary<string, object?> properties = new();

    switch (modelObject)
    {
      // includes beams and contour plates
      case TSM.Part part:
        AddPartProperties(part, properties);
        break;
      case TSM.BoltArray boltArray:
        AddBoltArrayProperties(boltArray, properties);
        break;
      case TSM.SingleRebar singleRebar:
        AddSingleRebarProperties(singleRebar, properties);
        break;
      case TSM.RebarMesh rebarMesh:
        AddRebarMeshProperties(rebarMesh, properties);
        break;
      case TSM.RebarGroup rebarGroup:
        AddRebarGroupProperties(rebarGroup, properties);
        break;
    }

    return properties;
  }

  private void AddPartProperties(TSM.Part part, Dictionary<string, object?> properties)
  {
    properties["profile"] = part.Profile.ProfileString;
    properties["material"] = part.Material.MaterialString;
  }

  private void AddBoltArrayProperties(TSM.BoltArray boltArray, Dictionary<string, object?> properties)
  {
    properties["boltSize"] = boltArray.BoltSize.ToString();
    properties["boltCount"] = boltArray.BoltPositions.Count.ToString();
    properties["boltStandard"] = boltArray.BoltStandard;
  }

  private void AddSingleRebarProperties(TSM.SingleRebar singleRebar, Dictionary<string, object?> properties)
  {
    properties["grade"] = singleRebar.Grade;
    properties["size"] = singleRebar.Size;
  }

  private void AddRebarMeshProperties(TSM.RebarMesh rebarMesh, Dictionary<string, object?> properties)
  {
    properties["grade"] = rebarMesh.Grade;
  }

  private void AddRebarGroupProperties(TSM.RebarGroup rebarGroup, Dictionary<string, object?> properties)
  {
    properties["grade"] = rebarGroup.Grade;
    properties["size"] = rebarGroup.Size;
  }
}
