using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Rhino.Operations.Send.Settings;

public class ToSpeckleSettingsManager
{
  private readonly ISendConversionCache _sendConversionCache;
  private readonly Dictionary<string, bool?> _addVisualizationPropertiesCache = [];

  public ToSpeckleSettingsManager(ISendConversionCache sendConversionCache)
  {
    _sendConversionCache = sendConversionCache;
  }

  public bool GetAddVisualizationPropertiesSetting(SenderModelCard modelCard)
  {
    var value = modelCard.Settings?.FirstOrDefault(s => s.Id == "addVisualizationProperties")?.Value as bool?;
    var returnValue = value != null && value.NotNull();
    if (_addVisualizationPropertiesCache.TryGetValue(modelCard.ModelCardId.NotNull(), out bool? previousValue))
    {
      if (previousValue != returnValue)
      {
        EvictCacheForModelCard(modelCard);
      }
    }

    _addVisualizationPropertiesCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  private void EvictCacheForModelCard(SenderModelCard modelCard)
  {
    var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.NotNull().SelectedObjectIds : [];
    _sendConversionCache.EvictObjects(objectIds);
  }
}
