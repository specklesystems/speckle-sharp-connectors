namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class PropertiesExtractor
{
  private readonly CsiGeneralPropertiesExtractor _generalPropertyExtractor;
  private readonly IClassPropertyExtractor _classPropertyExtractor; // This will be product specific extractor (i.e. EtabsClassPropertiesExtractor)

  public PropertiesExtractor(
    CsiGeneralPropertiesExtractor generalPropertyExtractor,
    IClassPropertyExtractor classPropertyExtractor
  )
  {
    _generalPropertyExtractor = generalPropertyExtractor;
    _classPropertyExtractor = classPropertyExtractor;
  }

  public Dictionary<string, object?> GetProperties(ICsiWrapper wrapper)
  {
    var properties = new Dictionary<string, object?>();

    var generalProperties = _generalPropertyExtractor.ExtractProperties(wrapper); // Csi common properties
    {
      properties["General Properties"] = generalProperties;
    }
    if (generalProperties != null) { }
    var classProperties = _classPropertyExtractor.ExtractProperties(wrapper); // Verticals specific properties
    if (classProperties != null)
    {
      properties["Class Properties"] = classProperties;
    }

    return properties;
  }
}
