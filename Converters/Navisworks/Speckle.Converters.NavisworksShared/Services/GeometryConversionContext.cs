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

  public int PrimeBatch(IReadOnlyList<NAV.ModelItem> modelItems, Action<double, int>? onProgress = null)
  {
    Clear();

    if (settingsStore.Current.User.GeometryDetailLevel == GeometryDetailLevel.Lite)
    {
      return 0;
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
      return 0;
    }

    var batchedResults = geometryConverter.ConvertBatch(geometryItems, onProgress);
    if (batchedResults.Count != geometryItems.Count)
    {
      // Fall back to legacy per-item extraction if batch mapping is ambiguous.
      return geometryItems.Count;
    }

    for (var i = 0; i < geometryItems.Count; i++)
    {
      _batchedDisplayValues[geometryItems[i]] = batchedResults[i];
    }

    return geometryItems.Count;
  }

  public bool TryGetDisplayValue(NAV.ModelItem modelItem, out List<Base> displayValue) =>
    _batchedDisplayValues.TryGetValue(modelItem, out displayValue!);

  public void Clear() => _batchedDisplayValues.Clear();
}
