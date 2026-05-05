using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.CellElement"/> (named block / group) to a
/// Speckle <see cref="Collection"/> containing the cell's child elements.
/// Children are converted individually by the root converter.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.CellElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CellElementConverter(
  IConverterSettingsStore<MicroStationConversionSettings> settingsStore,
  IRootToSpeckleConverter rootConverter
) : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.CellElement)target);

  private Collection Convert(MSIDGN.CellElement element)
  {
    var s = settingsStore.Current;
    var collection = new Collection
    {
      name = element.Name ?? element.ID.ToString(),
      ["elementType"] = "Cell",
      ["units"] = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };

    var children = element.GetSubElements();
    while (children.MoveNext())
    {
      var child = children.Current;
      if (child == null)
      {
        continue;
      }

      try
      {
        collection.elements.Add(rootConverter.Convert(child));
      }
      catch (Exception ex) when (ex is not OutOfMemoryException)
      {
        // Skip unconvertible children rather than failing the entire cell
        _ = ex;
      }
    }

    return collection;
  }
}
