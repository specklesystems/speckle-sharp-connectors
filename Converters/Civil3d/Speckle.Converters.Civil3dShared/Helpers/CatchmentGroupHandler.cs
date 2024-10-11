using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Common;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Converters.Civil3dShared.Helpers;

public sealed class CatchmentGroupHandler
{
  /// <summary>
  /// Keeps track of all catchment groups used by catchments in the current send operation.
  /// (catchmentGroup objectId, catchmentGroupProxy).
  /// This should be added to the root commit object post conversion.
  /// </summary>
  /// POC: Using group proxies for now
  public Dictionary<ADB.ObjectId, GroupProxy> CatchmentGroupProxies { get; } = new();

  private readonly IConverterSettingsStore<Civil3dConversionSettings> _converterSettings;

  public CatchmentGroupHandler(IConverterSettingsStore<Civil3dConversionSettings> converterSettings)
  {
    _converterSettings = converterSettings;
  }

  /// <summary>
  /// Extracts the Catchment group from a catchment and stores in <see cref="CatchmentGroupProxies"/> the appId of the catchment.
  /// </summary>
  /// <param name="catchment"></param>
  /// <returns></returns>
  public void HandleCatchmentGroup(CDB.Catchment catchment)
  {
    ADB.ObjectId catchmentGroupId = catchment.ContainingGroupId;

    if (catchmentGroupId == ADB.ObjectId.Null)
    {
      return;
    }

    string catchmentApplicationId = catchment.GetSpeckleApplicationId();
    if (CatchmentGroupProxies.TryGetValue(catchmentGroupId, out GroupProxy? value))
    {
      value.objects.Add(catchmentApplicationId);
    }
    else
    {
      using (var tr = _converterSettings.Current.Document.Database.TransactionManager.StartTransaction())
      {
        var catchmentGroup = (CDB.CatchmentGroup)tr.GetObject(catchmentGroupId, ADB.OpenMode.ForRead);

        CatchmentGroupProxies[catchmentGroupId] = new()
        {
          name = catchmentGroup.Name,
          objects = new() { catchmentApplicationId },
          applicationId = catchmentGroup.Handle.Value.ToString()
        };

        tr.Commit();
      }
    }
    return;
  }
}
