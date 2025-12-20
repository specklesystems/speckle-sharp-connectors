using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Plant3dShared;

public class Plant3dRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<Plant3dConversionSettings> _settingsStore;

  public Plant3dRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target)
  {
    if (target is not ADB.DBObject dbObject)
    {
      return null;
    }

    var settings = _settingsStore.GetSettings();
    var converted = _toSpeckle.Convert(dbObject, settings.Document, settings.SpeckleUnits);

    return converted;
  }
}

