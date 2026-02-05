using Speckle.Converters.Autocad;
using Speckle.Converters.Autocad.Extensions;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Autocad2023.ToHost.Raw;

public class AutocadPolycurveToHostPolylineRawConverter : ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline>
{
  private readonly ITypedConverter<SOG.Vector, AG.Vector3d> _vectorConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public AutocadPolycurveToHostPolylineRawConverter(
    ITypedConverter<SOG.Vector, AG.Vector3d> vectorConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _vectorConverter = vectorConverter;
    _settingsStore = settingsStore;
  }

  public ADB.Polyline Convert(SOG.Autocad.AutocadPolycurve target)
  {
    if (target.normal is null || target.elevation is null)
    {
      throw new System.ArgumentException(
        "Autocad polycurve of type light did not have a valid normal and/or elevation"
      );
    }

    double f = Units.GetConversionFactor(target.units, _settingsStore.Current.SpeckleUnits);
    List<AG.Point2d> points2d = target.value.ConvertToPoint2d(f);

    ADB.Polyline polyline =
      new()
      {
        Normal = _vectorConverter.Convert(target.normal),
        Elevation = (double)target.elevation * f,
        Closed = target.closed,
      };

    for (int i = 0; i < points2d.Count; i++)
    {
      var bulge = target.bulges is null ? 0 : target.bulges[i];
      polyline.AddVertexAt(i, points2d[i], bulge, 0, 0);
    }

    return polyline;
  }
}
