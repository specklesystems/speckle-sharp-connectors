using Speckle.Converters.MicroStation.Services;

namespace Speckle.Converters.MicroStation.Settings;

public interface IMicroStationConversionSettingsFactory
{
  MicroStationConversionSettings Create(bool includeInvisibleElements = false);
}

/// <summary>
/// Creates <see cref="MicroStationConversionSettings"/> from the currently active MicroStation model.
/// The <see cref="Application"/> COM object is injected by the connector's DI registration so the
/// converter project does not need to reference the connector project.
/// </summary>
public class MicroStationConversionSettingsFactory(MicroStationToSpeckleUnitConverter unitConverter, Application app)
  : IMicroStationConversionSettingsFactory
{
  public MicroStationConversionSettings Create(bool includeInvisibleElements = false)
  {
    if (!app.HasActiveModelReference)
    {
      return new MicroStationConversionSettings(SSC.Units.Meters, includeInvisibleElements);
    }

    var model = app.ActiveModelReference;
    var speckleUnits = unitConverter.ConvertOrThrow(model.get_MasterUnit());

    return new MicroStationConversionSettings(speckleUnits, includeInvisibleElements);
  }
}
