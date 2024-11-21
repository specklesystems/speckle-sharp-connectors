namespace Speckle.Converter.Tekla2024.ToSpeckle.Helpers;

public class ClassPropertyExtractor
{
  public ClassPropertyExtractor() { }

  public Dictionary<string, object?> GetProperties(TSM.ModelObject modelObject)
  {
    Dictionary<string, object?> properties = new();

    switch (modelObject)
    {
      case TSM.Beam beam:
        AddBeamProperties(beam, properties);
        break;
      case TSM.ContourPlate plate:
        AddContourPlateProperties(plate, properties);
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

  private void AddBeamProperties(TSM.Beam beam, Dictionary<string, object?> properties)
  {
    properties["name"] = beam.Name;
    properties["profile"] = beam.Profile.ProfileString;
    properties["material"] = beam.Material.MaterialString;
  }

  private void AddContourPlateProperties(TSM.ContourPlate plate, Dictionary<string, object?> properties)
  {
    properties["name"] = plate.Name;
    properties["profile"] = plate.Profile.ProfileString;
    properties["material"] = plate.Material.MaterialString;
  }

  private void AddBoltArrayProperties(TSM.BoltArray boltArray, Dictionary<string, object?> properties)
  {
    properties["boltSize"] = boltArray.BoltSize.ToString();
    properties["boltCount"] = boltArray.BoltPositions.Count.ToString();
    properties["boltStandard"] = boltArray.BoltStandard;
  }

  private void AddSingleRebarProperties(TSM.SingleRebar singleRebar, Dictionary<string, object?> properties)
  {
    properties["name"] = singleRebar.Name;
    properties["grade"] = singleRebar.Grade;
    properties["size"] = singleRebar.Size;
  }

  private void AddRebarMeshProperties(TSM.RebarMesh rebarMesh, Dictionary<string, object?> properties)
  {
    properties["name"] = rebarMesh.Name;
    properties["grade"] = rebarMesh.Grade;
  }

  private void AddRebarGroupProperties(TSM.RebarGroup rebarGroup, Dictionary<string, object?> properties)
  {
    properties["name"] = rebarGroup.Name;
    properties["grade"] = rebarGroup.Grade;
    properties["size"] = rebarGroup.Size;
  }
}
