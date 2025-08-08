using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Rhino.Operations.Send.Settings;

public class ToSpeckleSettingsManager
{
  private readonly ISendConversionCache _sendConversionCache;
  private readonly Dictionary<string, bool?> _sendVertexNormalsCache = [];
  private readonly Dictionary<string, bool?> _sendTextureCoordinatesCache = [];

  public ToSpeckleSettingsManager(ISendConversionCache sendConversionCache)
  {
    _sendConversionCache = sendConversionCache;
  }

  public bool GetSendVertexNormalsSetting(SenderModelCard modelCard)
  {
    var value = modelCard.Settings?.First(s => s.Id == "sendVertexNormals").Value as bool?;
    var returnValue = value != null && value.NotNull();
    if (_sendVertexNormalsCache.TryGetValue(modelCard.ModelCardId.NotNull(), out bool? previousValue))
    {
      if (previousValue != returnValue)
      {
        EvictCacheForModelCard(modelCard);
      }
    }

    _sendVertexNormalsCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  public bool GetSendTextureCoordinatesSetting(SenderModelCard modelCard)
  {
    var value = modelCard.Settings?.First(s => s.Id == "sendTextureCoordinates").Value as bool?;
    var returnValue = value != null && value.NotNull();
    if (_sendTextureCoordinatesCache.TryGetValue(modelCard.ModelCardId.NotNull(), out bool? previousValue))
    {
      if (previousValue != returnValue)
      {
        EvictCacheForModelCard(modelCard);
      }
    }

    _sendTextureCoordinatesCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  private void EvictCacheForModelCard(SenderModelCard modelCard)
  {
    var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.NotNull().SelectedObjectIds : [];
    _sendConversionCache.EvictObjects(objectIds);
  }
}
