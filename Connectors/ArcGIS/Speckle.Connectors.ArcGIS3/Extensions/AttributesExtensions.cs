using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.ArcGIS.Extensions;

public static class AttributesExtensions
{
  /// <summary>
  /// Creates a dictionary from the field descriptions of map member display table.
  /// </summary>
  /// <param name="displayTable"></param>
  /// <returns></returns>
  /// <exception cref="AC.CalledOnWrongThreadException">Throws when this method or property is NOT called within the lambda passed to QueuedTask.Run.</exception>
  public static Dictionary<string, string> GetFieldsAsDictionary(this IDisplayTable displayTable)
  {
    return displayTable.GetFieldDescriptions().ToDictionary(field => field.Name, field => field.Type.ToString());
  }
}
