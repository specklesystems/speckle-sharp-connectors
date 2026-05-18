using Speckle.Converter.Navisworks.Settings;
using Speckle.Converter.Navisworks.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Navisworks.Services;

public class GeometryConversionContext(
  GeometryToSpeckleConverter geometryConverter,
  IConverterSettingsStore<NavisworksConversionSettings> settingsStore,
  IElementSelectionService elementSelectionService
)
{
  private readonly Dictionary<NAV.ModelItem, List<Base>> _batchedDisplayValues = new();

  public void PrimeBatch(IReadOnlyList<NAV.ModelItem> modelItems)
  {
    Clear();

    if (settingsStore.Current.User.GeometryDetailLevel == GeometryDetailLevel.Lite)
    {
      return;
    }

    var geometryItems = new List<NAV.ModelItem>(modelItems.Count);
    foreach (var item in modelItems)
    {
      if (item.HasGeometry && elementSelectionService.IsVisible(item))
      {
        geometryItems.Add(item);
      }
    }

    if (geometryItems.Count == 0)
    {
      return;
    }

    var batchedResults = geometryConverter.ConvertBatch(geometryItems);
    if (batchedResults.Count != geometryItems.Count)
    {
      // Fall back to legacy per-item extraction if batch mapping is ambiguous.
      return;
    }

    for (var i = 0; i < geometryItems.Count; i++)
    {
      _batchedDisplayValues[geometryItems[i]] = batchedResults[i];
    }
  }

  public bool TryGetDisplayValue(NAV.ModelItem modelItem, out List<Base> displayValue) =>
    _batchedDisplayValues.TryGetValue(modelItem, out displayValue!);

  public void Clear() => _batchedDisplayValues.Clear();
}
