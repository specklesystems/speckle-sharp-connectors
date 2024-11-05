namespace Speckle.Converter.Tekla2024.ToSpeckle.Helpers;

public class PropertyExtractor
{
  public PropertyExtractor() { }

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
      case TSM.BoltGroup boltGroup:
        AddBoltGroupProperties(boltGroup, properties);
        break;
    }

    return properties;
  }

  private void AddBeamProperties(TSM.Beam beam, Dictionary<string, object?> properties)
  {
    properties["profile"] = beam.Profile.ProfileString;
    properties["material"] = beam.Material.MaterialString;
    properties["class"] = beam.Class;
  }

  private void AddContourPlateProperties(TSM.ContourPlate plate, Dictionary<string, object?> properties)
  {
    properties["profile"] = plate.Profile.ProfileString;
    properties["material"] = plate.Material.MaterialString;
    properties["class"] = plate.Class;
  }

  private void AddBoltGroupProperties(TSM.BoltGroup boltGroup, Dictionary<string, object?> properties)
  {
    properties["boltSize"] = boltGroup.BoltSize;
    properties["bolt"] = boltGroup.Bolt;
  }
}
