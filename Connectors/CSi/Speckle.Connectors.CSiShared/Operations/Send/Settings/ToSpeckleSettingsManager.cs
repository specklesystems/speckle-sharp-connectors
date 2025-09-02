using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.InterfaceGenerator;
using Speckle.Newtonsoft.Json.Linq;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.CSiShared.Operations.Send.Settings;

[GenerateAutoInterface]
public class ToSpeckleSettingsManager : IToSpeckleSettingsManager
{
  private readonly ISendConversionCache _sendConversionCache;
  private readonly Dictionary<string, List<string>?> _loadCaseCombinationCache = new();
  private readonly Dictionary<string, List<string>?> _resultTypeCache = new();

  public ToSpeckleSettingsManager(ISendConversionCache sendConversionCache)
  {
    _sendConversionCache = sendConversionCache;
  }

  public List<string> GetLoadCasesAndCombinations(SenderModelCard modelCard)
  {
    var setting = modelCard.Settings?.FirstOrDefault(s => s.Id == "loadCasesAndCombinations");
    var returnValue = (setting?.Value as JArray)?.Select(x => x.ToString()).ToList() ?? [];

    if (_loadCaseCombinationCache.TryGetValue(modelCard.ModelCardId.NotNull(), out List<string>? previousValue))
    {
      if (!AreListsEqual(previousValue, returnValue))
      {
        EvictCacheForModelCard(modelCard);
      }
    }
    _loadCaseCombinationCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  public List<string> GetResultTypes(SenderModelCard modelCard)
  {
    var setting = modelCard.Settings?.FirstOrDefault(s => s.Id == "resultTypes");
    var returnValue = (setting?.Value as JArray)?.Select(x => x.ToString()).ToList() ?? [];

    if (_resultTypeCache.TryGetValue(modelCard.ModelCardId.NotNull(), out List<string>? previousValue))
    {
      if (!AreListsEqual(previousValue, returnValue))
      {
        EvictCacheForModelCard(modelCard);
      }
    }
    _resultTypeCache[modelCard.ModelCardId] = returnValue;
    return returnValue;
  }

  private static bool AreListsEqual(List<string>? list1, List<string>? list2)
  {
    if (list1 == null && list2 == null)
    {
      return true;
    }

    if (list1 == null || list2 == null)
    {
      return false;
    }

    return list1.Count == list2.Count && list1.OrderBy(x => x).SequenceEqual(list2.OrderBy(x => x));
  }

  private void EvictCacheForModelCard(SenderModelCard modelCard)
  {
    var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.NotNull().RefreshObjectIds() : [];
    _sendConversionCache.EvictObjects(objectIds);
  }
}
