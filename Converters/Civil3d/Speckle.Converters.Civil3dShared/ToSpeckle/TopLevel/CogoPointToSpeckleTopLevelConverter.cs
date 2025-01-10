using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.TopLevel;

/// <summary>
/// This top level converter is needed because the namespace of CogoPoint is Autodesk.Civil.DatabaseServices, but the inheritance of CogoPoint is Autodesk.Autocad.Entity
/// This means cogo points will *not* be picked up by the top level civil entity converter.
/// POC: implementing a top level autocad entity converter can probably replace this converter.
/// </summary>
[NameAndRankValue(typeof(CDB.CogoPoint), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CogoPointToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;

  public CogoPointToSpeckleTopLevelConverter(
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
  }

  public Base Convert(object target) => Convert((CDB.CogoPoint)target);

  public Civil3dObject Convert(CDB.CogoPoint target)
  {
    string name = "";
    try
    {
      name = target.PointName;
    }
    catch (Autodesk.Civil.CivilException e) when (!e.IsFatal()) { } // throws if name doesn't exist

    // extract display value as point
    SOG.Point displayPoint = _pointConverter.Convert(target.Location);

    Civil3dObject civilObject =
      new()
      {
        name = name,
        type = target.GetType().ToString().Split('.').Last(),
        baseCurves = null,
        elements = new(),
        displayValue = new() { displayPoint },
        properties = new(),
        units = _settingsStore.Current.SpeckleUnits,
        applicationId = target.Id.GetSpeckleApplicationId()
      };

    // add additional class properties
    civilObject["pointNumber"] = target.PointNumber;
    civilObject["northing"] = target.Northing;
    //civilObject["latitude"] = target.Latitude; // might not be necessary, and also sometimes throws if transforms are not enabled
    //civilObject["longitude"] = target.Longitude; // might not be necessary, and also sometimes throws if transforms are not enabled

    return civilObject;
  }
}
