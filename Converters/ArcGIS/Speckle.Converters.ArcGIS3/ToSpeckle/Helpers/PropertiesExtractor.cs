using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Helpers;

public sealed class PropertiesExtractor
{
  public PropertiesExtractor() { }

  public Dictionary<string, object?> GetProperties(AC.CoreObjectsBase coreObjectsBase)
  {
    switch (coreObjectsBase)
    {
      case ACD.Row row:
        return GetRowFields(row);
    }

    return new();
  }

  public Dictionary<string, object?> GetRowFields(ACD.Row row, Dictionary<string, string> fields) { }
}
