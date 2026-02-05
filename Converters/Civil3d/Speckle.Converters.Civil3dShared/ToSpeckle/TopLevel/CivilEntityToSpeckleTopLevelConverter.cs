using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(CDB.Entity), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CivilEntityToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly BaseCurveExtractor _baseCurveExtractor;
  private readonly ClassPropertiesExtractor _classPropertiesExtractor;
  private readonly PropertiesExtractor _propertiesExtractor;
  private readonly CorridorHandler _corridorHandler;

  public CivilEntityToSpeckleTopLevelConverter(
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    BaseCurveExtractor baseCurveExtractor,
    ClassPropertiesExtractor classPropertiesExtractor,
    PropertiesExtractor propertiesExtractor,
    CorridorHandler corridorHandler
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _baseCurveExtractor = baseCurveExtractor;
    _classPropertiesExtractor = classPropertiesExtractor;
    _propertiesExtractor = propertiesExtractor;
    _corridorHandler = corridorHandler;
  }

  public Base Convert(object target) => Convert((CDB.Entity)target);

  public Civil3dObject Convert(CDB.Entity target)
  {
    PropertyHandler propHandler = new();
    string name = propHandler.TryGetValue(() => target.Name, out string? tarName)
      ? tarName ?? target.DisplayName
      : target.DisplayName; // this will throw for some entities like labels

    // get basecurve
    List<ICurve>? baseCurves = _baseCurveExtractor.GetBaseCurves(target);

    // extract display value.
    // If object has no display but has basecurves, use basecurves for display instead (for viewer selection)
    List<Base> displayValue = _displayValueExtractor.GetDisplayValue(target).ToList();
    if (displayValue.Count == 0)
    {
      displayValue = _displayValueExtractor.ProcessICurvesForDisplay(baseCurves).ToList();
    }

    // get properties
    Dictionary<string, object?> properties = _propertiesExtractor.GetProperties(target);

    // determine if this entity has any children elements that need to be converted.
    // this is a bespoke method by class type.
    var children = GetEntityChildren(target).ToList();

    Civil3dObject civilObject =
      new()
      {
        name = name,
        type = target.GetType().ToString().Split('.').Last(),
        baseCurves = baseCurves,
        elements = children,
        displayValue = displayValue,
        properties = properties,
        units = _settingsStore.Current.SpeckleUnits,
        applicationId = target.GetSpeckleApplicationId(),
      };

    return civilObject;
  }

  private IEnumerable<Base> GetEntityChildren(CDB.Entity entity)
  {
    switch (entity)
    {
      case CDB.Alignment alignment:
        var alignmentChildren = GetAlignmentChildren(alignment);
        foreach (var child in alignmentChildren)
        {
          yield return child;
        }
        break;

      case CDB.Site site:
        var siteChildren = GetSiteChildren(site).ToList();
        foreach (var child in siteChildren)
        {
          yield return child;
        }
        break;

      case CDB.Corridor corridor:
        var corridorChildren = _corridorHandler.GetCorridorChildren(corridor);
        foreach (var child in corridorChildren)
        {
          yield return child;
        }
        break;
    }
  }

  private IEnumerable<Civil3dObject> GetSiteChildren(CDB.Site site)
  {
    using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
    {
      foreach (ADB.ObjectId parcelId in site.GetParcelIds())
      {
        var parcel = (CDB.Parcel)tr.GetObject(parcelId, ADB.OpenMode.ForRead);
        yield return Convert(parcel);
      }

      tr.Commit();
    }
  }

  private IEnumerable<Civil3dObject> GetAlignmentChildren(CDB.Alignment alignment)
  {
    using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
    {
      foreach (ADB.ObjectId profileId in alignment.GetProfileIds())
      {
        var profile = (CDB.Profile)tr.GetObject(profileId, ADB.OpenMode.ForRead);
        yield return Convert(profile);
      }

      tr.Commit();
    }
  }
}
