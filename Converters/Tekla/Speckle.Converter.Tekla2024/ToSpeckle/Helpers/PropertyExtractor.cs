using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Helpers;

public class PropertyExtractor
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.Point, SOG.Point> _pointConverter;

  public PropertyExtractor(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.Point, SOG.Point> pointConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
  }

  public Dictionary<string, object?> GetProperties(TSM.ModelObject modelObject)
  {
    Dictionary<string, object?> properties = new();

    switch (modelObject)
    {
      case TSM.Beam beam:
        AddBeamProperties(beam, properties);
        break;
      case TSM.ContourPlate plate:
        AddContourPlateProperties(plate, properties);
        break;
    }

    return properties;
  }

  private void AddBeamProperties(TSM.Beam beam, Dictionary<string, object?> properties)
  {
    properties["profile"] = beam.Profile.ProfileString;
    properties["material"] = beam.Material.MaterialString;
    properties["startPoint"] = _pointConverter.Convert(beam.StartPoint);
    properties["endPoint"] = _pointConverter.Convert(beam.EndPoint);
    properties["class"] = beam.Class;
  }

  private void AddContourPlateProperties(TSM.ContourPlate plate, Dictionary<string, object?> properties)
  {
    properties["profile"] = plate.Profile.ProfileString;
    properties["material"] = plate.Material.MaterialString;
    properties["class"] = plate.Class;
  }
}
