namespace Speckle.Converters.TeklaShared.ToSpeckle.Helpers;

public class PropertiesExtractor
{
  private readonly ReportPropertyExtractor _reportPropertyExtractor;

  public PropertiesExtractor(ReportPropertyExtractor reportPropertyExtractor)
  {
    _reportPropertyExtractor = reportPropertyExtractor;
  }

  public Dictionary<string, object?> GetProperties(TSM.ModelObject modelObject)
  {
    Dictionary<string, object?> properties = new();

    Dictionary<string, object?> report = _reportPropertyExtractor.GetReportProperties(modelObject);
    if (report.Count > 0)
    {
      properties.Add("Report", report);
    }

    return properties;
  }
}
