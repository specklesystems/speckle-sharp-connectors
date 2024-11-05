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
    properties["profile"] = beam.Profile.ProfileString;
    properties["material"] = beam.Material.MaterialString;

    double volume = 0.0;
    beam.GetReportProperty("VOLUME", ref volume);
    double volumeCubic = volume / 1000000000.0; // converting to m3 from mm3

    properties["volume(m3)"] = String.Format("{0:0.0000}", volumeCubic);

    double weight = 0.0;
    beam.GetReportProperty("WEIGHT", ref weight);

    properties["weight(kg)"] = String.Format("{0:0.0000}", weight);
  }

  private void AddContourPlateProperties(TSM.ContourPlate plate, Dictionary<string, object?> properties)
  {
    properties["profile"] = plate.Profile.ProfileString;
    properties["material"] = plate.Material.MaterialString;

    double volume = 0.0;
    plate.GetReportProperty("VOLUME", ref volume);
    double volumeCubic = volume / 1000000000.0; // converting to m3 from mm3

    properties["volume(m3)"] = String.Format("{0:0.0000}", volumeCubic);

    double weight = 0.0;
    plate.GetReportProperty("WEIGHT", ref weight);

    properties["weight(kg)"] = String.Format("{0:0.0000}", weight);
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

    double volume = 0.0;
    singleRebar.GetReportProperty("VOLUME", ref volume);
    properties["volume(m3)"] = String.Format("{0:0.0000}", volume);

    double weight = 0.0;
    singleRebar.GetReportProperty("WEIGHT", ref weight);
    properties["weight(kg)"] = String.Format("{0:0.0000}", weight);
  }

  private void AddRebarMeshProperties(TSM.RebarMesh rebarMesh, Dictionary<string, object?> properties)
  {
    properties["grade"] = rebarMesh.Grade;

    double area = (rebarMesh.Width * rebarMesh.Length) / 100.0; // converting to cm2
    properties["area(cm2)"] = String.Format("{0:0.0000}", area);

    double weight = 0.0;
    rebarMesh.GetReportProperty("WEIGHT", ref weight);
    properties["weight(kg)"] = String.Format("{0:0.0000}", weight);
  }

  private void AddRebarGroupProperties(TSM.RebarGroup rebarGroup, Dictionary<string, object?> properties)
  {
    properties["grade"] = rebarGroup.Grade;
    properties["size"] = rebarGroup.Size;

    double volume = 0.0;
    rebarGroup.GetReportProperty("VOLUME", ref volume);
    properties["volume(m3)"] = String.Format("{0:0.0000}", volume);

    double weight = 0.0;
    rebarGroup.GetReportProperty("WEIGHT", ref weight);
    properties["weight(kg)"] = String.Format("{0:0.0000}", weight);
  }
}
