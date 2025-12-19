using System.Diagnostics.CodeAnalysis;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Civil3dShared.Operations.Send.Settings;

[GenerateAutoInterface]
public class ToSpeckleSettingsManagerCivil3d : IToSpeckleSettingsManagerCivil3d
{
  private readonly ISendConversionCache _sendConversionCache;
  private readonly Dictionary<string, bool?> _revitCategoryMappingCache = [];

  public ToSpeckleSettingsManagerCivil3d(ISendConversionCache sendConversionCache)
  {
    _sendConversionCache = sendConversionCache;
  }

  public bool GetMappingToRevitCategories([NotNull] SenderModelCard modelCard)
  {
    var value = modelCard.Settings?.FirstOrDefault(s => s.Id == "mappingToRevitCategories")?.Value as bool?;

    var returnValue = value != null && value.NotNull();
    if (_revitCategoryMappingCache.TryGetValue(modelCard.ModelCardId.NotNull(), out var previousValue))
    {
      if (previousValue != returnValue)
      {
        EvictCacheForModelCard(modelCard);
      }
    }

    _revitCategoryMappingCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  private void EvictCacheForModelCard(SenderModelCard modelCard)
  {
    var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.NotNull().SelectedObjectIds : [];
    _sendConversionCache.EvictObjects(objectIds);
  }
}
