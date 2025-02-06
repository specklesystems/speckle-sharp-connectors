namespace Speckle.Converters.TeklaShared.ToSpeckle.Helpers;

public class PropertiesExtractor
{
  private readonly ReportPropertyExtractor _reportPropertyExtractor;
  private readonly UserDefinedAttributesExtractor _userDefinedAttributesExtractor;

  public PropertiesExtractor(
    ReportPropertyExtractor reportPropertyExtractor,
    UserDefinedAttributesExtractor userDefinedAttributesExtractor
  )
  {
    _reportPropertyExtractor = reportPropertyExtractor;
    _userDefinedAttributesExtractor = userDefinedAttributesExtractor;
  }

  public Dictionary<string, object?> GetProperties(TSM.ModelObject modelObject)
  {
    Dictionary<string, object?> properties = new();

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
