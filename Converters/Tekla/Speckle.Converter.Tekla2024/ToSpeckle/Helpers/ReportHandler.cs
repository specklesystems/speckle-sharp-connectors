namespace Speckle.Converter.Tekla2024.ToSpeckle.Helpers;

public class ReportPropertyHandler
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
          "PHASE"
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
          "PHASE"
        }
      },
      { typeof(TSM.RebarGroup), new[] { "NUMBER_OF_REBARS", "TOTAL_LENGTH", "WEIGHT", "SIZE", "GRADE", "CLASS" } },
      { typeof(TSM.SingleRebar), new[] { "LENGTH", "WEIGHT", "SIZE", "GRADE", "CLASS" } },
      { typeof(TSM.BoltArray), new[] { "BOLT_SIZE", "NUMBER_OF_BOLTS", "BOLT_STANDARD", "BOLT_TYPE", "LENGTH" } }
    };

  public Dictionary<string, object?> GetProperties(TSM.ModelObject modelObject)
  {
    var properties = new Dictionary<string, object?>();

    if (!s_typeSpecificProperties.TryGetValue(modelObject.GetType(), out var propertyNames))
    {
      // if no specific properties defined, return empty dictionary
      return properties;
    }

    foreach (string propertyName in propertyNames)
    {
      TryGetReportProperty(modelObject, propertyName, properties);
    }

    return properties;
  }

  private void TryGetReportProperty(
    TSM.ModelObject modelObject,
    string propertyName,
    Dictionary<string, object?> properties
  )
  {
    double doubleValue = 0.0;
    int intValue = 0;
    string stringValue = "";

    if (modelObject.GetReportProperty(propertyName, ref doubleValue))
    {
      properties[propertyName] = doubleValue;
    }
    else if (modelObject.GetReportProperty(propertyName, ref intValue))
    {
      properties[propertyName] = intValue;
    }
    else if (modelObject.GetReportProperty(propertyName, ref stringValue) && !string.IsNullOrEmpty(stringValue))
    {
      properties[propertyName] = stringValue;
    }
  }
}
