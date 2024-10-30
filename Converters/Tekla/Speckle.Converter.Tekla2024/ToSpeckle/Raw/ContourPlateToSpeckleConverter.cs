using Speckle.Converter.Tekla2024.ToSpeckle.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

public class ContourPlateToSpeckleConverter : ITypedConverter<TSM.ContourPlate, Base>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.Point, SOG.Point> _pointConverter;
  private readonly DisplayValueExtractor _displayValueExtractor;

  public ContourPlateToSpeckleConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.Point, SOG.Point> pointConverter,
    DisplayValueExtractor displayValueExtractor
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
    _displayValueExtractor = displayValueExtractor;
  }

  public Base Convert(TSM.ContourPlate target)
  {
    var plateObject = new Base
    {
      ["profile"] = target.Profile.ProfileString,
      ["material"] = target.Material.MaterialString,
      ["class"] = target.Class
    };

    var contourPoints = new List<SOG.Point>();
    foreach (TG.Point point in target.Contour.ContourPoints)
    {
      contourPoints.Add(_pointConverter.Convert(point));
    }
    plateObject["contourPoints"] = contourPoints;

    var displayValue = _displayValueExtractor.GetDisplayValue(target).ToList();
    if (displayValue.Count != 0)
    {
      plateObject["displayValue"] = displayValue;
    }

    return plateObject;
  }
}
