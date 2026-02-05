using Tekla.Structures.Datatype;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Helpers;

public class ReportPropertyExtractor
{
  private static readonly Dictionary<Type, string[]> s_typeSpecificProperties =
    new()
    {
      {
        typeof(TSM.Beam),
        new[]
        {
          "VOLUME",
          "WEIGHT",
          "LENGTH",
          "HEIGHT",
          "WIDTH",
          "AREA",
          "PROFILE_TYPE",
          "MATERIAL_TYPE",
          "CLASS",
          "PREFIX",
          "ASSEMBLY_POS",
          "ASSEMBLY_NAME",
          "PHASE",
        }
      },
      {
        typeof(TSM.ContourPlate),
        new[]
        {
          "VOLUME",
          "WEIGHT",
          "AREA",
          "PROFILE_TYPE",
          "MATERIAL_TYPE",
          "CLASS",
          "PREFIX",
          "ASSEMBLY_POS",
          "PHASE",
        }
      },
      { typeof(TSM.RebarGroup), new[] { "NUMBER_OF_REBARS", "TOTAL_LENGTH", "WEIGHT", "SIZE", "GRADE", "CLASS" } },
      { typeof(TSM.SingleRebar), new[] { "LENGTH", "WEIGHT", "SIZE", "GRADE", "CLASS" } },
      { typeof(TSM.BoltArray), new[] { "BOLT_SIZE", "NUMBER_OF_BOLTS", "BOLT_STANDARD", "BOLT_TYPE", "LENGTH" } },
    };

  public Dictionary<string, object?> GetReportProperties(TSM.ModelObject modelObject)
  {
    var reportProperties = new Dictionary<string, object?>();

    if (!s_typeSpecificProperties.TryGetValue(modelObject.GetType(), out var propertyNames))
    {
      // NOTE: Return empty dictionary if no specific properties defined
      return reportProperties;
    }

    foreach (string propertyName in propertyNames)
    {
      TryGetReportProperty(modelObject, propertyName, reportProperties);
    }

    return reportProperties;
  }

  private void TryGetReportProperty(
    TSM.ModelObject modelObject,
    string propertyName,
    Dictionary<string, object?> properties
  )
  {
    var reportProperty = new Dictionary<string, object?> { ["name"] = propertyName };

    // NOTE: ModelObject.GetReportProperty has specific overloads (not generic), we need to try each overload
    double doubleValue = 0.0;
    int intValue = 0;
    string stringValue = string.Empty;

    if (modelObject.GetReportProperty(propertyName, ref doubleValue))
    {
      // NOTE: It seems default is millimeter https://developer.tekla.com/doc/tekla-structures/2023/millimeters-property-12484#
      reportProperty["value"] = doubleValue;
      reportProperty["units"] = propertyName switch
      {
        "LENGTH" or "WIDTH" or "HEIGHT" => Distance.MILLIMETERS, // NOTE: This is horrible, I know! Waiting on response from Tekla
        "VOLUME" => $"Cubic {Distance.MILLIMETERS.ToString().ToLower()}",
        "AREA" => $"Square {Distance.MILLIMETERS.ToString().ToLower()}", // NOTE: Weird number, but corresponds with generated report
        "WEIGHT" => "Kilograms",
        _ => null, // NOTE: No units appended for other parameters
      };
    }
    else if (modelObject.GetReportProperty(propertyName, ref intValue))
    {
      reportProperty["value"] = intValue;
    }
    else if (modelObject.GetReportProperty(propertyName, ref stringValue) && !string.IsNullOrEmpty(stringValue))
    {
      reportProperty["value"] = stringValue;
    }

    // NOTE: Only assign if it actually contains a value
    if (reportProperty.ContainsKey("value"))
    {
      properties[propertyName] = reportProperty;
    }
  }
}
