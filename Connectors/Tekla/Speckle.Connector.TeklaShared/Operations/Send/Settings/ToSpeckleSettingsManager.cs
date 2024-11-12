using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connector.Tekla2024.Operations.Send.Settings;

[GenerateAutoInterface]
public class ToSpeckleSettingsManager : IToSpeckleSettingsManager
{
  private readonly ISendConversionCache _sendConversionCache;
  private readonly Dictionary<string, bool?> _sendRebarsAsSolidCache = new();

  public ToSpeckleSettingsManager(ISendConversionCache sendConversionCache)
  {
    _sendConversionCache = sendConversionCache;
  }

  public bool GetSendRebarsAsSolid(SenderModelCard modelCard)
  {
    var value = modelCard.Settings?.First(s => s.Id == "sendRebarsAsSolid").Value as bool?;
    var returnValue = value != null && value.NotNull();
    if (_sendRebarsAsSolidCache.TryGetValue(modelCard.ModelCardId.NotNull(), out bool? previousValue))
    {
      if (previousValue != returnValue)
      {
        EvictCacheForModelCard(modelCard);
      }
    }
    _sendRebarsAsSolidCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  private void EvictCacheForModelCard(SenderModelCard modelCard)
  {
    var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.NotNull().RefreshObjectIds() : [];
    _sendConversionCache.EvictObjects(objectIds);
  }
}
