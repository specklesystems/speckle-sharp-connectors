namespace Speckle.Converters.TeklaShared.ToSpeckle.Helpers;

public class PropertiesExtractor
{
  private readonly ReportPropertyExtractor _reportPropertyExtractor;
  private readonly UserDefinedAttributesExtractor _userDefinedAttributesExtractor;
  private readonly ClassPropertyExtractor _classPropertyExtractor;

  public PropertiesExtractor(
    ReportPropertyExtractor reportPropertyExtractor,
    UserDefinedAttributesExtractor userDefinedAttributesExtractor,
    ClassPropertyExtractor classPropertyExtractor
  )
  {
    _reportPropertyExtractor = reportPropertyExtractor;
    _userDefinedAttributesExtractor = userDefinedAttributesExtractor;
    _classPropertyExtractor = classPropertyExtractor;
  }

  public Dictionary<string, object?> GetProperties(TSM.ModelObject modelObject)
  {
    // get the top level class properties first
    Dictionary<string, object?> properties = _classPropertyExtractor.GetProperties(modelObject);

    Dictionary<string, object?> report = _reportPropertyExtractor.GetReportProperties(modelObject);
    if (report.Count > 0)
    {
      properties.Add("Report", report);
    }

    Dictionary<string, object?> userDefinedAttributes = _userDefinedAttributesExtractor.GetUserDefinedAttributes(
      modelObject
    );
    if (userDefinedAttributes.Count > 0)
    {
      properties.Add("User Defined Attributes", userDefinedAttributes);
    }

    return properties;
  }
}
